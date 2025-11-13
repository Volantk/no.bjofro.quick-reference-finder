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
        [Serializable]
        public class SearchResults
        {
            public string SearchText;
            public Object TargetObject;
            public List<Object> Assets = new List<Object>();
            public List<string> NonAssetPaths = new List<string>();
        }

        [MenuItem("Tools/Quick Reference Finder")]
        public static void ShowWindow()
        {
            GetWindow<QuickReferenceFinder>("Quick Reference Finder");
        }

        /// <summary>
        /// Finds all references to the specified asset in the project.
        /// </summary>
        /// <param name="asset">The asset to search for</param>
        /// <returns>SearchResults containing all found references</returns>
        public static SearchResults FindReferencesToAsset(Object asset)
        {
            if (asset == null || !AssetDatabase.Contains(asset))
            {
                Debug.LogError("Invalid asset provided for search!");
                return new SearchResults();
            }

            string selectedPath = AssetDatabase.GetAssetPath(asset);
            string selectedGuid = AssetDatabase.AssetPathToGUID(selectedPath);
            
            return FindReferencesToString(selectedGuid);
        }

        /// <summary>
        /// Finds all references to the specified search text (e.g., GUID) in the project and fills the provided SearchResults object.
        /// </summary>
        /// <param name="searchText"></param>
        /// <param name="resultsToFill"></param>
        public static void FindReferencesToString(string searchText, SearchResults resultsToFill)
        {
            SearchResults results = FindReferencesToString(searchText);
            resultsToFill.SearchText    = results.SearchText;
            resultsToFill.TargetObject  = results.TargetObject;
            resultsToFill.Assets        = results.Assets;
            resultsToFill.NonAssetPaths = results.NonAssetPaths;
        }
        
        /// <summary>
        /// Finds all references to the specified search text (e.g., GUID) in the project.
        /// </summary>
        /// <param name="searchText">The text to search for</param>
        /// <returns>SearchResults containing all found references</returns>
        public static SearchResults FindReferencesToString(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                Debug.LogError("No valid search text provided!");
                return new SearchResults();
            }
            
            if (searchText.Length < 3)
            {
                Debug.LogError("Search too wide, text to find must be at least 3 characters long!");
                return new SearchResults();
            }

            var startTime = DateTime.Now;

            string[] exts = { "prefab", "unity", "mat", "asset", "shader", "cginc", "compute" };
            string projectDir = Application.dataPath.Substring(0, Application.dataPath.Length - 7);

            // Create a temporary instance to access instance methods
            var window = CreateInstance<QuickReferenceFinder>();
            
            IEnumerable<Task<string>> assetTasks = exts.Select(ext => window.RunSearch(searchText, Path.Combine(projectDir, "Assets"), ext));
            IEnumerable<Task<string>> packageTasks = exts.Select(ext => window.RunSearch(searchText, Path.Combine(projectDir, "Packages"), ext));
            IEnumerable<Task<string>> settingsTasks = exts.Select(ext => window.RunSearch(searchText, Path.Combine(projectDir, "ProjectSettings"), ext));

            Task<string>[] tasks = assetTasks.Concat(packageTasks).Concat(settingsTasks).ToArray();
            Task.WaitAll(tasks);

            string combinedOutput = string.Join(Environment.NewLine, tasks.Select(t => t.Result));

            var results = new SearchResults
            {
                SearchText = searchText,
                TargetObject = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(searchText))
            };

            // implement later.
            // var resultsLineNumbers = new Dictionary<Object, List<long>>();

            string[] files = combinedOutput.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in files)
            {
                int colonIndex = path.IndexOf(':', 3);
                if (colonIndex == -1)
                    continue;

                string cleanFilePath = path.Substring(0, colonIndex);
                long fileLineNumber = long.Parse(path.Substring(colonIndex + 1, path.IndexOf(':', colonIndex + 1) - colonIndex - 1));

                // Normalize paths for cross-platform compatibility
                string normalizedFilePath = cleanFilePath.Replace("\\", "/");
                string normalizedDataPath = Application.dataPath.Replace("\\", "/");
                string assetPath = normalizedFilePath.Replace(normalizedDataPath, "Assets");

                Object obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (obj != null)
                {
                    if (!results.Assets.Contains(obj))
                        results.Assets.Add(obj);

                    // if (!resultsLineNumbers.ContainsKey(obj))
                        // resultsLineNumbers[obj] = new List<long>();

                    // resultsLineNumbers[obj].Add(fileLineNumber);
                }
                else
                {
                    if (!results.NonAssetPaths.Contains(path))
                        results.NonAssetPaths.Add(path);
                }

                if (results.Assets.Count + results.NonAssetPaths.Count >= 500)
                {
                    Debug.LogWarning("Reference search aborted: too many results (>500).");
                    break;
                }
            }

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            DestroyImmediate(window);
            
            Debug.Log($"Reference search completed in <b>{duration.TotalSeconds:F2}</b> seconds. Found {results.Assets.Count} asset references and {results.NonAssetPaths.Count} non-asset references.");

            return results;
        }

        [SerializeField] private string searchTargetGuid;
        [SerializeField] private Object searchTargetObject;
        
        [SerializeField] private SearchResults searchResults;
        [SerializeField] private List<SearchResults> allSearchResults = new List<SearchResults>();
        
        private SearchResults _displayedResults;
        
        // [SerializeField] private List<Object> results;
        // [SerializeField] private List<string> nonAssetResults;

        private Vector2 _scroll;
        
        static GUIStyle leftButtonStyle;
        
        private static void InitStyles()
        {
            // if (leftButtonStyle == null)
            {
                leftButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12
                };
            }
        }
        
        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            Selection.selectionChanged += SetTargetOnSelectionChanged;
            
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            Selection.selectionChanged -= SetTargetOnSelectionChanged;
        }

        private void SetTargetOnSelectionChanged()
        {
            if (Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject))
            {
                searchTargetObject = Selection.activeObject;
                Repaint();
            }
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
            SearchResults results = window.FindReferencesToSelectedObject();
            window.AddAndDisplayResults(results);
        }
        
        private void OnGUI()
        {
            InitStyles();
            
            GUILayout.Label("Find all assets containing the search text in /Assets, /Packages and /ProjectSettings", EditorStyles.centeredGreyMiniLabel);
            bool hasValidSelection = Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject);

            GUI.enabled = hasValidSelection;
            GUI.backgroundColor = new Color(0.22f, 0.4f, 0.87f);
            
            string label = hasValidSelection
                ? $"Search for selected asset:\n{Selection.activeObject.name} ({Selection.activeObject.GetType()})"
                : "Select an asset to search for its GUID.";
            
            if (GUILayout.Button(label, GUILayout.Height(40)))
            {
                SearchResults results = FindReferencesToSelectedObject();
                AddAndDisplayResults(results);
            }
            
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 38;

                searchTargetObject = EditorGUILayout.ObjectField("Asset", searchTargetObject, typeof(Object), false);

                GUI.enabled = searchTargetObject;
                GUI.color   = Color.yellow;
                if (GUILayout.Button("Search", GUILayout.Width(90)))
                {
                    AddAndDisplayResults(FindReferencesToAsset(searchTargetObject));
                }
                GUI.color = Color.white;
                GUI.enabled = true;
            }            
            
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 38;

                searchTargetGuid = EditorGUILayout.TextField("Text", searchTargetGuid);

                GUI.enabled = searchTargetGuid.Length >= 3;
                GUI.color   = Color.yellow;
                if (GUILayout.Button(GUI.enabled ? "Search" : "Min 3 chars!", GUILayout.Width(90)))
                {
                    AddAndDisplayResults(FindReferencesToString(searchTargetGuid));
                }
                GUI.color = Color.white;
                GUI.enabled = true;
            }

            EditorGUIUtility.labelWidth = 0;
            
            var leftPanelWidth = allSearchResults.Count > 0 ? 250 : 0;
            
            EditorGUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            
            if (allSearchResults.Count > 0)
            {
                GUILayout.Label("Search History", EditorStyles.boldLabel);
                
                for (var i = 0; i < allSearchResults.Count; i++)
                {
                    var res = allSearchResults[i];

                    string searchLabel = res.TargetObject != null
                        ? $"{res.TargetObject.name} ({res.TargetObject.GetType().Name})"
                        : $"\"{res.SearchText}\"";

                    using (var scope = new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Toggle(allSearchResults.Count > 1 && _displayedResults == res, searchLabel, leftButtonStyle, GUILayout.Width(leftPanelWidth-20)))
                        {
                            _displayedResults = res;
                        }
                        if(GUILayout.Button("X", GUILayout.Width(20)))
                        {
                            allSearchResults.RemoveAt(i);
                            if (_displayedResults == res)
                            {
                                // select next if possible, otherwise previous, otherwise first
                                if (i < allSearchResults.Count)
                                    _displayedResults = allSearchResults[i];
                                else if (i - 1 >= 0)
                                    _displayedResults = allSearchResults[i - 1];
                                else
                                    _displayedResults = allSearchResults.FirstOrDefault();
                            }
                            break;
                        }
                    }
                }
                
                GUI.backgroundColor = Color.gray;
                if (GUILayout.Button("Clear", GUILayout.Width(leftPanelWidth)))
                {
                    allSearchResults.Clear();
                    _displayedResults = null;
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndVertical();
            
            float width = position.width - leftPanelWidth;
            DrawResultsWithScrollAndButtons(_displayedResults, width);
            
            EditorGUILayout.EndHorizontal();
        }

        private void AddAndDisplayResults(SearchResults results)
        {
            allSearchResults.Add(results);
            _displayedResults = results;
            Repaint();
        }

        private void DrawResultsWithScrollAndButtons(SearchResults results, float width)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
            {
                if (results != null && results.Assets.Count + results.NonAssetPaths.Count > 0)
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);
                    DrawResults(results);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.Space();
                    // show help box
                    EditorGUILayout.HelpBox("\nSelect an asset and click 'Search' to find references.\n\nYou can search for any kind of text, not just asset guids.\n", MessageType.Info);
                }
            }
        }

        private void DrawResults(SearchResults results)
        {
            EditorGUIUtility.labelWidth = 85;
            if(results.TargetObject)
                EditorGUILayout.ObjectField("Searched for:", results.TargetObject, typeof(Object), false);
            else
                EditorGUILayout.LabelField("Searched for:", $"\"{results.SearchText}\"");
            
            List<Object> assets          = results.Assets;
            List<string> nonAssetResults = results.NonAssetPaths;

            if (assets.Count + nonAssetResults.Count >= 500)
            {
                EditorGUILayout.HelpBox("Too many results (>500). Please narrow down your search.", MessageType.Warning);
            }
            
            if (assets.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Found {assets.Count} references", GUILayout.Width(120));
                
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Select All", GUILayout.Width(100)))
                {
                    Selection.objects = results.Assets.ToArray();
                }
                EditorGUILayout.EndHorizontal();

                using (var scope = new EditorGUILayout.VerticalScope("box"))
                {
                    foreach (var obj in assets)
                    {
                        EditorGUILayout.ObjectField(obj, obj.GetType(), false);
                    }
                }
            }

            if (nonAssetResults.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {nonAssetResults.Count} non-asset references:");
                foreach (var resultPath in nonAssetResults)
                {
                    using (var scope = new EditorGUILayout.HorizontalScope())
                    {
                        string p = resultPath;
                        p = p.Replace(Application.dataPath.Substring(0, Application.dataPath.Length - 7), ""); // make path relative to project
                        var shortenedPath = "..." + p.Substring(0, Math.Min(80, p.Length)) + "...";
                        GUILayout.Label(new GUIContent(shortenedPath, resultPath));
                        if (GUILayout.Button("Browse", GUILayout.Width(100)))
                        {
                            EditorUtility.RevealInFinder(resultPath);
                        }
                    }
                }
            }
        }

        private bool IsMacOS()
        {
            return Application.platform == RuntimePlatform.OSXEditor;
        }

        private Task<string> RunSearch(string selectedGuid, string dataPath, string ext)
        {
            return Task.Run(() =>
            {
                string processName;
                string arguments;
                
                if (IsMacOS())
                {
                    // macOS: use grep with bash
                    // -r = recursive, -n = line numbers, --include = file pattern
                    processName = "/bin/bash";
                    arguments = $"-c \"grep -rn '{selectedGuid}' --include='*.{ext}' '{dataPath}'\"";
                }
                else
                {
                    // Windows: use findstr with cmd
                    // /S = recursive, /N = line numbers, /C = literal search string
                    processName = "cmd.exe";
                    arguments = $"/C findstr /S /N /C:\"{selectedGuid}\" \"{dataPath}\\*.{ext}\"";
                }
                
                var psi = new System.Diagnostics.ProcessStartInfo(processName, arguments)
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
                        Debug.LogWarning($"Search error: {error}");
                    
                    // normalize line endings to environment
                    output = output.Replace("\r\n", Environment.NewLine).Replace("\n", Environment.NewLine);
                    return output;
                }
            });
        }   
        
        private SearchResults FindReferencesToSelectedObject()
        {
            Object activeObject = Selection.activeObject;
            string selectedPath = AssetDatabase.GetAssetPath(activeObject);
            string selectedGuid = AssetDatabase.AssetPathToGUID(selectedPath);

            var results = FindReferencesToString(selectedGuid);
            results.TargetObject = activeObject;

            return results;
        }
    }
}
