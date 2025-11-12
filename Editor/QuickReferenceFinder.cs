using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bears
{
    public class QuickReferenceFinder : EditorWindow
    {
        [MenuItem("Tools/Quick Reference Finder")]
        public static void ShowWindow()
        {
            GetWindow<QuickReferenceFinder>("Quick Reference Finder");
        }

        [SerializeField] private string searchTargetGuid;
        [SerializeField] private Object searchTargetObject;
        
        [SerializeField] private List<Object> results;
        [SerializeField] private List<string> nonAssetResults;

        private Vector2 _scroll;
        
        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        [MenuItem("Assets/Find References In Project (Quick)", true, 19)]
        private static bool Validate_AssetContextMenuSearch()
        {
            return Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject);
        }      
        
        [MenuItem("Assets/Find References In Project (Quick)", false, 19)]
        private static void AssetContextMenuSearch()
        {
            // Open the window and start search
            QuickReferenceFinder window = GetWindow<QuickReferenceFinder>("Quick Reference Finder");
            window.FindReferencesToSelectedObject();
        }
        
        private void OnGUI()
        {
            GUILayout.Label("Find all assets containing the search text in /Assets, /Packages and /ProjectSettings", EditorStyles.centeredGreyMiniLabel);
            bool hasValidSelection = Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject);

            GUI.enabled = hasValidSelection;
            GUI.backgroundColor = new Color(0.22f, 0.4f, 0.87f);
            
            string label = hasValidSelection
                ? $"Search for guid:\n{Selection.activeObject.name} ({Selection.activeObject.GetType()})"
                : "Select an asset to search for its GUID.";
            
            if (GUILayout.Button(label, GUILayout.Height(40)))
            {
                FindReferencesToSelectedObject();
            }
            
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 38;

                searchTargetGuid = EditorGUILayout.TextField("Text", searchTargetGuid);

                GUI.enabled = searchTargetGuid.Length >= 3;
                GUI.color   = Color.yellow;
                if (GUILayout.Button(GUI.enabled ? "Search" : "Min 3 chars!", GUILayout.Width(90)))
                {
                    SearchForString(searchTargetGuid);
                }
                GUI.color = Color.white;
                GUI.enabled = true;
            }
            
            GUI.enabled = false;
            EditorGUILayout.ObjectField("Asset", searchTargetObject, searchTargetObject.GetType(), false);
            GUI.enabled = true;
            
            EditorGUIUtility.labelWidth = 0;
            
            if (results != null)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                DrawResults();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawResults()
        {
            if (results.Count + nonAssetResults.Count >= 500)
            {
                EditorGUILayout.HelpBox("Too many results (>500). Please narrow down your search.", MessageType.Warning);
            }
            
            if (results.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {results.Count} references:");

                using (var scope = new EditorGUILayout.VerticalScope("box"))
                {
                    foreach (var obj in results)
                    {
                        EditorGUILayout.ObjectField(obj, obj.GetType(), false);
                    }
                }
            }

            if (nonAssetResults.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {nonAssetResults.Count} non-asset references:");
                foreach (var path in nonAssetResults)
                {
                    GUILayout.Label(path);
                }
            }
        }

        private Task<string> RunFindstr(string selectedGuid, string dataPath, string ext)
        {
            return Task.Run(() =>
            {
                // string arguments = $"/C findstr /S /M /C:\"{selectedGuid}\" \"{dataPath}\\*.{ext}\"";
                // include line numbers in output for easier parsing
                string arguments = $"/C findstr /S /N /C:\"{selectedGuid}\" \"{dataPath}\\*.{ext}\"";
                
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit();
                    
                    if (!string.IsNullOrEmpty(error))
                        Debug.LogWarning($"findstr error: {error}");
                    
                    // normalize line endings to environment
                    output = output.Replace("\r\n", Environment.NewLine).Replace("\n", Environment.NewLine);
                    return output;
                }
            });
        }   
        
        private void FindReferencesToSelectedObject()
        {
            Object activeObject = Selection.activeObject;
            string selectedPath = AssetDatabase.GetAssetPath(activeObject);
            string selectedGuid = AssetDatabase.AssetPathToGUID(selectedPath);

            searchTargetGuid = selectedGuid;
            searchTargetObject = activeObject;
            
            SearchForString(selectedGuid);
        }

        private void SearchForString(string target)
        {
            if(string.IsNullOrWhiteSpace(target))
            {
                Debug.LogError("No valid GUID to search for!");
                return;
            }
            
            EditorUtility.DisplayProgressBar("Searching References", "Please wait...", 0.5f);
            
            var startTime = DateTime.Now;

            string[] exts =
            {
                /*"meta", */"prefab", "unity", "mat", "asset"
            };

            string projectDir    = Application.dataPath.Substring(0, Application.dataPath.Length - 7);

            IEnumerable<Task<string>> assetTasks    = exts.Select(ext => RunFindstr(target, Path.Combine(projectDir, "Assets"), ext));
            IEnumerable<Task<string>> packageTasks  = exts.Select(ext => RunFindstr(target, Path.Combine(projectDir, "Packages"), ext));
            IEnumerable<Task<string>> settingsTasks = exts.Select(ext => RunFindstr(target, Path.Combine(projectDir, "ProjectSettings"), ext));

            Task<string>[] tasks = assetTasks.Concat(packageTasks).Concat(settingsTasks).ToArray();
            Task.WaitAll(tasks);
            
            string combinedOutput = string.Join(Environment.NewLine, tasks.Select(t => t.Result));

            results         = new List<Object>();
            nonAssetResults = new List<string>(); // projectsettings and the like

            Dictionary<Object, List<long>> resultsLineNumbers = new Dictionary<Object, List<long>>();

            string[] files = combinedOutput.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in files)
            {
                string cleanFilePath  = path;
                long   fileLineNumber = -1;

                int colonIndex = path.IndexOf(':', 3);
                if(colonIndex == -1)
                    continue;
                
                cleanFilePath  = path.Substring(0, colonIndex);
                fileLineNumber = long.Parse(path.Substring(colonIndex + 1, path.IndexOf(':', colonIndex + 1) - colonIndex - 1));
                
                string assetPath = cleanFilePath.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (obj != null)
                {
                    if (!results.Contains(obj))
                        results.Add(obj);

                    if (!resultsLineNumbers.ContainsKey(obj))
                        resultsLineNumbers[obj] = new List<long>();
                    
                    resultsLineNumbers[obj].Add(fileLineNumber);
                }

                if (!obj)
                {
                    if (!nonAssetResults.Contains(path))
                        nonAssetResults.Add(path);
                }
                
                if (results.Count + nonAssetResults.Count >= 500)
                {
                    Debug.LogWarning("Reference search aborted: too many results (>500). Please narrow down your search.");
                    break;
                }
            }
            
            var endTime  = DateTime.Now;
            var duration = endTime - startTime;

            EditorUtility.ClearProgressBar();
            
            Debug.Log($"Reference search completed in <b>{duration.TotalSeconds:F2}</b> seconds.");
        }
    }
}
