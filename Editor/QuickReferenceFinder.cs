using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BearsEditorTools
{
    public class QuickReferenceFinder : EditorWindow
    {
     
        [MenuItem("Tools/Bears/Quick Reference Finder")]
        public static void ShowWindow()
        {
            GetWindow<QuickReferenceFinder>("Quick Reference Finder");
        }

        [SerializeField]
        private string searchTargetGuid;
        [SerializeField]
        private Object searchTargetObject;
        [SerializeField]
        private List<Object> results;
        [SerializeField]
        private List<string> nonAssetResults;

        private Dictionary<Object, List<long>> resultsFileIDs;
        
        private Vector2 _scroll;
        
        private float _lastSearchTime;

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        [MenuItem("Assets/Find References In Project (Quick)", true, 19)]
        private static bool AssetContextMenuSearchValidate()
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
            GUI.enabled = Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject);

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Find References to Selected Asset:" + "\n" + 
                                 (GUI.enabled
                                     ? (Selection.activeObject.name) + $" ({Selection.activeObject.GetType()})"
                                     : "(Must select an asset)"), GUILayout.Height(40)))
            {
                FindReferencesToSelectedObject();
            }
            GUI.backgroundColor = Color.white;

            GUI.enabled = true;
            
            EditorGUILayout.BeginHorizontal();

            EditorGUIUtility.labelWidth = 38;
            searchTargetGuid = EditorGUILayout.TextField("Guid", searchTargetGuid);
            
            if (GUILayout.Button("Find", GUILayout.Width(40)))
            {
                SearchForGuid(searchTargetGuid);
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (searchTargetObject)
            {
                EditorGUILayout.ObjectField("Asset", searchTargetObject, searchTargetObject.GetType(), false);
            }
            
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
            if (results.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {results.Count} references:");

                using (var scope = new EditorGUILayout.VerticalScope("box"))
                {
                    foreach (var obj in results)
                    {
                        EditorGUILayout.ObjectField(obj, obj.GetType(), false);
   
                        if (resultsFileIDs != null && resultsFileIDs.TryGetValue(obj, out List<long> list))
                        {
                            foreach (long l in list)
                            {
                                EditorGUILayout.BeginHorizontal();
                                GUILayout.Space(15f);
                                bool button = GUILayout.Button($"FileID: {l}", EditorStyles.miniButton);
                                EditorGUILayout.EndHorizontal();
                                if (button)
                                {
                                                 
                                    PropertyInfo inspectorModeInfo =
                                        typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.NonPublic | BindingFlags.Instance);
                                
                                    if (obj is SceneAsset scene)
                                    {
                                        Debug.Log($"LOoking for FileID {l} in scene: " + scene.name);
                                        var rootObjectsInScene = SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(scene)).GetRootGameObjects();
                                        var allComponents      = rootObjectsInScene.SelectMany(ro => ro.GetComponentsInChildren<MonoBehaviour>(true)).ToList();
                                        foreach (var c in allComponents)
                                        {
                                            if (!c)
                                                continue;
                                        
                                            SerializedObject so = new SerializedObject(c);
                                            inspectorModeInfo.SetValue(so, InspectorMode.Debug, null);
                                            SerializedProperty serializedProperty = so.FindProperty("m_LocalIdentfierInFile");
                                        
                                            if(serializedProperty.longValue == l)
                                            {
                                                Selection.activeObject = c.gameObject;
                                                EditorGUIUtility.PingObject(c);
                                                Debug.Log($"Found component {c.GetType().Name} in scene {scene.name} with FileID {l}");
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
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
        
        private Task<string> FindAllLinesWithFileIDs(string file)
        {
            return Task.Run(() =>
            {
                // Normalize path to Windows format with backslashes
                string normalizedPath = Path.GetFullPath(file).Replace("/", "\\");
                string arguments = $"/C findstr /N /C:\"---\" \"{normalizedPath}\"";

                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    WorkingDirectory       = Path.GetDirectoryName(normalizedPath)
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if(!string.IsNullOrEmpty(error))
                        Debug.LogError($"findstr error for {normalizedPath}: {error}");

                    return output;
                }
            });
        }

        private void FindReferencesToSelectedObject()
        {
            
            // use windows findstr command to search for references in the project
            Object activeObject = Selection.activeObject;
            string selectedPath = AssetDatabase.GetAssetPath(activeObject);
            string selectedGuid = AssetDatabase.AssetPathToGUID(selectedPath);

            SearchForGuid(selectedGuid);
        }

        private void SearchForGuid(string selectedGuid)
        {
            EditorUtility.DisplayProgressBar("Searching References", "Please wait...", 0.5f);
            
            searchTargetGuid = selectedGuid;

            string searchPath = AssetDatabase.GUIDToAssetPath(selectedGuid);
            searchTargetObject = AssetDatabase.LoadAssetAtPath<Object>(searchPath);
            if (searchTargetObject == null)
            {
                Debug.LogWarning("Could not load asset at path: " + searchPath);
                EditorUtility.ClearProgressBar();
                
                return;
            }
            

            var startTime = DateTime.Now;

            string[] exts =
            {
                /*"meta", */"prefab", "unity", "mat", "asset"
            };

            string projectDir    = Application.dataPath.Substring(0, Application.dataPath.Length - 7);
            
            var    assetTasks    = exts.Select(ext => RunFindstr(selectedGuid, Path.Combine(projectDir, "Assets"), ext));
            var    packageTasks  = exts.Select(ext => RunFindstr(selectedGuid, Path.Combine(projectDir, "Packages"), ext));
            var    settingsTasks = exts.Select(ext => RunFindstr(selectedGuid, Path.Combine(projectDir, "ProjectSettings"), ext));

            Task<string>[] tasks = assetTasks.Concat(packageTasks).Concat(settingsTasks).ToArray();
            Task.WaitAll(tasks);
            
            string combinedOutput = string.Join(Environment.NewLine, tasks.Select(t => t.Result));

            results         = new List<Object>();
            nonAssetResults = new List<string>();
            resultsFileIDs  = new Dictionary<Object, List<long>>();

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
            }

            // This needs more work so it can find assets in scenes that reference the target object.
            /*
            foreach (Object obj in resultsLineNumbers.Keys)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                
                var fileIDTask = FindAllLinesWithFileIDs(fullPath);
                fileIDTask.Wait();
                
                // Parse file IDs from output
                List<long> fileIDs = new List<long>();
                
                string[] idLinesInFile = fileIDTask.Result.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                string previousLineNumberData = "";

                /*
                foreach (var resultsLineNumber in resultsLineNumbers[obj])
                {
                    Debug.Log(resultsLineNumber);
                }#1#
                
                // extract all line numbers from data strings. Up until colon, but nothing after, per line
                var allNumbers = resultsLineNumbers[obj].ToList();
                allNumbers.Reverse();
                long activeLineNumber = allNumbers.Last();
                
                Debug.Log("All: " + string.Join(", ", allNumbers));
                Debug.Log("Active: " + activeLineNumber);
                
                foreach (string fileIdLine in idLinesInFile)
                {
                    // line format: lineNumber:---
                    int colonIndex = fileIdLine.IndexOf(':');
                    long thisLineNumber = long.Parse(fileIdLine.Substring(0, colonIndex));
                    
                    if (thisLineNumber > activeLineNumber)
                    {
                        Debug.Log("Bingo! Line: " + thisLineNumber);
                        string content = previousLineNumberData.Substring(colonIndex + 1).Trim();
                        
                        string fileIDStr = content.Substring(content.IndexOf('&') + 1).Split(' ')[0];
                        
                        if (long.TryParse(fileIDStr, out long fileID))
                        {
                            fileIDs.Add(fileID);
                        }
                        else
                        {
                            fileIDs.Add(-1);
                        }
                        
                        // Next step
                        allNumbers.RemoveAt(allNumbers.Count - 1);
                        
                        if (allNumbers.Count == 0)
                            break;
                        
                        activeLineNumber = allNumbers.Last();
                    }
                    
                    previousLineNumberData = fileIdLine;
                }
                
                resultsFileIDs[obj] = fileIDs;
            }
            */

            
            var endTime  = DateTime.Now;
            var duration = endTime - startTime;

            _lastSearchTime = (float)duration.TotalSeconds;
            
            EditorUtility.ClearProgressBar();
            
            // Debug.Log($"Reference search completed in <b>{duration.TotalSeconds:F2}</b> seconds.");
        }
    }
}