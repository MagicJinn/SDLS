using BepInEx;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Sunless.Game.Entities;

namespace SDLS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        // Config options
        private const string CONFIG = "SDLS_Config.ini";
        private bool doMerge = true;
        private bool logConflicts = true;
        private bool basegameMerge = true;
        private bool doCleanup = true; // Whether the files created by SDLS should be cleared during shutdown
        private bool logDebugTimers = false;
        private bool fastLoad = true;
        // Config options end

        public static string PersistentDataPath { get; } = Application.persistentDataPath;
        private static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();

        private HashSet<string> componentNames; // Contains the names of all the JSON defaults
        private Dictionary<string, Dictionary<string, object>> componentCache = new(); // Cache for loaded components
        private bool TilesSpecialCase = false; // Variable for the special case of Tiles.json. Check GetAComponent for more info

        private string currentModName; // Variable for tracking which mod is currently being merged. Used for logging conflicts
        private List<string> conflictLog = new(); // List of conflicts
        private Dictionary<string, Stopwatch> DebugTimers = new(); // List of Debug timers, used by DebugTimer()


        private Dictionary<string, Dictionary<int, Dictionary<string, object>>> mergedModsDict = new();
        // Dictionary structure breakdown:
        // - string: Represents the filename or category (eg events.json).
        // - Dictionary<int, Dictionary<string, object>>: 
        //    - int: Id of an entry, either Id or Name or AssociatedQualityId.
        //    - Dictionary<string, object>: The actual data from a JSON object.
        //       - string: The key from the JSON object.
        //       - object: The value, which can be a primitive value or a nested object.


        // Tracks every file SDLS creates. Used during cleanup
        private List<string> createdFiles = new();
        public static Plugin Instance { get; private set; }
        private FastLoad fastLoader;

        private void Awake( /* Run by Unity on game start */ )
        {
            Log($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Instance = this;
            LoadConfig();

            var jsonCompletedEvent = new ManualResetEvent(false); // Track whether JsonInitialization is complete
            ThreadPool.QueueUserWorkItem(state => JsonInitialization(jsonCompletedEvent)); // Start JSON Initialization Async
            if (fastLoad) FastLoadInitialization();
        }

        private void JsonInitialization(ManualResetEvent jsonCompletedEvent)
        {
            InitializationLine();

            DebugTimer("TrashAllJSON");
            try
            {
                TrashAllJSON();
            }
            catch (Exception ex)
            {
                Error($"Exception in TrashAllJSON: {ex.Message}");
                Error($"Stack trace: {ex.StackTrace}");
            }
            DebugTimer("TrashAllJSON");

            // Signal that initialization is complete
            fastLoader.isInitializationComplete = true;
            jsonCompletedEvent.Set();
        }

        private void FastLoadInitialization()
        {
            // Create a new GameObject and attach FastLoad to it
            GameObject fastLoadObject = new GameObject("FastLoad");
            DontDestroyOnLoad(fastLoadObject);
            fastLoader = fastLoadObject.AddComponent<FastLoad>();

            StartCoroutine(fastLoader.WaitForInitAndStartRepositoryManager());
        }

        void OnApplicationQuit( /* Run by Unity on game exit */ )
        {
            if (doCleanup)
            {
                // Removes all files created by SDLS before closing, because otherwise they would cause problems with Vortex
                // So basically, if a user had SDLS installed, and used Vortex to manage their mods, if they wanted to remove
                // a mod, Vortex would only remove the .sdls files, and not the .json files, which would cause the game to
                // still run them, and the user would have no clue why. 
                DebugTimer("Cleanup");
                RemoveDirectory("SDLS_MERGED");
                foreach (string file in createdFiles) JSON.RemoveJSON(file);
                DebugTimer("Cleanup");
            }
        }

        private void TrashAllJSON()
        {
            string[] filePaths = JSON.GetFilePaths(); // List of all possible moddable files
            componentNames = FindComponents(); // list of each default component

            foreach (string modFolder in Directory.GetDirectories(Path.Combine(PersistentDataPath, "addon")))
            {
                foreach (string filePath in filePaths)
                {
                    {
                        try
                        {
                            string modFolderInAddon = Path.Combine("addon", modFolder);
                            string fullRelativePath = Path.Combine(modFolderInAddon, filePath);

                            string fileContent = JSON.ReadGameJson(fullRelativePath + ".sdls"); // Attempt to read the file with ".sdls" extension
                            if (fileContent == null) fileContent = JSON.ReadGameJson(fullRelativePath + "SDLS.json"); // Attempt to read the file with "SDLS.json" extension only if .sdls file is not found

                            if (fileContent != null)
                            {
                                string fileName = GetLastWord(fullRelativePath);

                                DebugTimer("Trash " + fullRelativePath);

                                currentModName = fullRelativePath; // Track current mod to log conflicts

                                string trashedJSON = TrashJSON(fileContent, fileName);
                                DebugTimer("Trash " + fullRelativePath);

                                DebugTimer("Create " + fullRelativePath);
                                JSON.CreateJSON(trashedJSON, fullRelativePath);

                                createdFiles.Add(fullRelativePath);
                                DebugTimer("Create " + fullRelativePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Error($"Error processing file {filePath}: {ex.Message}");
                        }
                    };
                }
            }
        }

        private string TrashJSON(string strObjJoined, string name)
        {
            try
            {
                var strObjList = new List<string>(); // List to store all the JSON strings
                var embeddedData = GetAComponent(name); // Deserialize the default mold data

                foreach (string splitString in JSON.SplitJSON(strObjJoined))
                {
                    var deserializedJSON = JSON.Deserialize(splitString);

                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJSON, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    strObjList.Add(JSON.Serialize(returnData));

                }
                TilesSpecialCase = false;  // Stupid special case for Tiles, check GetAComponent for details

                // Join the processed JSON strings together
                string result = JSON.JoinJSON(strObjList);
                return result;
            }
            catch (Exception ex)
            {
                Error($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return strObjJoined; // Return providedJSON as fallback
            }
        }

        private Dictionary<string, object> ApplyFieldsToMold(
            Dictionary<string, object> tracedJSONObj, // This data will get copied over
            Dictionary<string, object> mergeJSONObj // Data will get merged INTO this object
        )
        {
            // Copy the input dictionary to a new dictionary as a preventitive measure to not repeat the events of 16/05/2023
            var mergeJSONObjCopy = new Dictionary<string, object>(mergeJSONObj);

            foreach (var kvp in tracedJSONObj)
            {
                string tracedKey = kvp.Key; // Name of key, e.g., UseEvent
                var tracedValue = kvp.Value; // Value of key, e.g., 500500

                mergeJSONObjCopy[tracedKey] = /* If */ componentNames.Contains(/* the currently handled */ tracedKey)
                        // Is there a default component for this field?
                        ? HandleComponent(tracedValue, tracedKey) // Yes
                        : HandleDefault(tracedValue); // No
            }

            return mergeJSONObjCopy;
        }

        private object HandleComponent(object tracedValue, string tracedKey)
        {
            if (tracedValue is IEnumerable<object> array)
            {
                var castedArray = array.Cast<Dictionary<string, object>>().ToList();
                return HandleArray(castedArray, tracedKey);
            }
            else
            {
                return HandleObject(tracedValue, tracedKey);
            }
        }

        private object HandleObject(object fieldValue, string fieldName)
        {
            var newMoldItem = GetAComponent(fieldName);

            if (fieldValue is Dictionary<string, object> fieldValueDict)
            {
                return ApplyFieldsToMold(fieldValueDict, newMoldItem);
            }
            else return fieldValue;
        }

        private object HandleDefault(object fieldValue)
        {
            if (fieldValue is Dictionary<string, object> nestedJSON)
            {
                return ApplyFieldsToMold(nestedJSON, new Dictionary<string, object>());
            }
            else if (fieldValue is IEnumerable<object> array)
            {
                return array.Select(item => HandleDefault(item)).ToList();
            }
            else
            {
                return fieldValue;
            }
        }

        private object HandleArray(
                List<Dictionary<string, object>> array, // Provided array
                string fieldName, // Name of the current field (eg. Enhancements) used for GetAComponent
                List<Dictionary<string, object>> arrayToMergeInto = null)
        {
            arrayToMergeInto ??= new List<Dictionary<string, object>>(array.Count); // Set the arrayToMergeInto to an empty array (list) if none are provided.

            foreach (var item in array)
            {
                if (item is Dictionary<string, object> itemDict) // If the item is a dictionary (eg, storylet, quality etc)
                {
                    arrayToMergeInto.Add((Dictionary<string, object>)HandleObject(itemDict, fieldName));
                }
                else arrayToMergeInto.Add(item); // Else, if the item is a value (for example, "SubsurfaceWeather", results in ["value"])
            }
            return arrayToMergeInto;
        }

        private int NameOrId(Dictionary<string, object> JSONObj)
        {
            string primaryKey = FindPrimaryKey(JSONObj);

            return primaryKey == "Name" ?
                JSONObj[primaryKey].GetHashCode() :
                (int)JSONObj[primaryKey];
        }

        private string FindPrimaryKey(Dictionary<string, object> JSONObj)
        {
            string[] keys = { "AssociatedQualityId", "Id", "Name" };

            foreach (string key in keys)
            {
                if (JSONObj.ContainsKey(key))
                {
                    DLog("Chose " + key + " as a primary key");
                    return key;
                }
            }

            throw new ArgumentException($"The provided JSON object does not contain an 'Id', 'AssociatedQualityId', or 'Name' field. Object: {JSON.Serialize(JSONObj)}");
        }


        public static string GetLastWord(string str)
        {
            if (str.IndexOfAny(new char[] { '/', '\\' }) == -1) return str; // No separators found, return the original string

            string result = str.Split(new char[] { '/', '\\' }).Last();

            return result;
        }

        public static string GetParentPath(string filePath)
        {
            int lastIndex = filePath.LastIndexOfAny(new char[] { '/', '\\' });

            // Return the substring from the start of the string up to the last directory separator
            return filePath.Substring(0, lastIndex + 1);
        }

        private void RemoveDirectory(string relativePath) // Removes any directory in addon
        {
            string relativePathDirectory = Path.Combine("addon", relativePath);
            string path = Path.Combine(PersistentDataPath, relativePathDirectory);

            try
            {
                Directory.Delete(path, true);
                Warn("Removing " + relativePathDirectory);
            }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex)
            {
                Error($"Error deleting directory: {ex.Message}");
            }
        }

        private HashSet<string> FindComponents() // Fetches a list of all files (names) in the defaultComponents folder
        {
            string embeddedPath = GetEmbeddedPath("default");
            string[] resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            var components = new HashSet<string>();

            for (int i = 0; i < resourceNames.Length; i++)
            {
                string name = resourceNames[i];
                if (name.StartsWith(embeddedPath))
                {
                    int startIndex = embeddedPath.Length + 1; // +1 to skip the dot
                    int endIndex = name.Length - 5; // -5 to remove ".json"
                    string componentName = name.Substring(startIndex, endIndex - startIndex);
                    components.Add(componentName);
                }
            }

            return components;
        }

        private Dictionary<string, object> GetAComponent(string name)
        {
            string componentName = name;
            if (!componentCache.ContainsKey(componentName))
            {
                string asText = JSON.ReadInternalJson(componentName);

                string referenceString = "REFERENCE=";
                string strippedAsText = asText.Replace(" ", "");
                if (strippedAsText.Contains(referenceString))
                {
                    componentName = strippedAsText.Replace(referenceString, "");
                    var value = GetAComponent(componentName);
                    componentCache[componentName] = value;
                }
                else
                {
                    componentCache[componentName] = JSON.Deserialize(asText);
                }
            }
            else
            {
                // So the deal is, Tiles.json contains a field called Tiles, spelled exactly the same.
                // So if we request the component, there is no way to know which one it requests.
                // We get around this by checking whether we are currently handling the Tiles.json object,
                // And if we are, return TilesTiles instead, since TilesTiles is only used inside of Tiles.json
                // We set TilesSpecialCase to false after we handle the Tiles object.

                if (componentName == "Tiles" && TilesSpecialCase)
                {
                    componentName = "TilesTiles"; // Set the name to TilesTiles to return the correct component
                    if (!componentCache.ContainsKey("TilesTiles")) // If the TilesTiles component hasn't been added, add it.
                    {
                        componentCache[componentName] = JSON.Deserialize(JSON.ReadInternalJson(componentName));
                    }
                }
            }

            if (componentName == "Tiles") TilesSpecialCase = true; // Set special case to true after the first time Tiles has been requested and returned
            return componentCache[componentName];
        }

        public static string GetEmbeddedPath(string folderName = "") // Get the path of embedded resources
        {
            string projectName = Assembly.GetExecutingAssembly().GetName().Name;
            string fullPath = $"{projectName}.{folderName}";
            return fullPath;
        }

        private void LoadConfig(bool loadDefault = false)
        {
            string[] lines;
            if (File.Exists(CONFIG) && !loadDefault)
            {
                lines = File.ReadAllLines(CONFIG);
            }
            else
            {
                Warn("Config not found or corrupt, using default values.");
                string file = ReadTextResource(GetEmbeddedPath() + CONFIG); // Get the default config from the embedded resources

                lines = file.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries); // Split the file into lines
            }

            var optionsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in lines)
                {
                    if (line.Contains('=')) // Check if the line contains an '=' character so it's a valid config line
                    {
                        // Remove all spaces from the line and split it at the first occurrence of '=' into two parts
                        string[] keyValue = line.Replace(" ", "").Split(new[] { '=' }, 2);
                        optionsDict[keyValue[0]] = keyValue[1]; // Add the key and value to the dictionary
                    }
                }

                doMerge = bool.Parse(optionsDict["domerge"]);
                logConflicts = doMerge ? bool.Parse(optionsDict["logmergeconflicts"]) : false;
                basegameMerge = doMerge ? bool.Parse(optionsDict["basegamemerge"]) : false;

                doCleanup = bool.Parse(optionsDict["docleanup"]);

                logDebugTimers = bool.Parse(optionsDict["logdebugtimers"]);

                fastLoad = bool.Parse(optionsDict["fastload"]);
            }
            catch (Exception)
            {
                LoadConfig( /*loadDefault =*/ true); // Load config with default values
            }
        }

        public static string ReadTextResource(string fullResourceName)
        {
            using (Stream stream = Assembly.GetManifestResourceStream(fullResourceName))
            {
                if (stream == null)
                {
                    Instance.Warn("Tried to get resource that doesn't exist: " + fullResourceName);
                    return null; // Return null if the embedded resource doesn't exist
                }

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd(); // Read and return the embedded resource
            }
        }

        public void DebugTimer(string name)
        {
            if (!logDebugTimers) return;

            if (!DebugTimers.TryGetValue(name, out Stopwatch stopwatch))
            { // Start a new timer
                Log(string.Format("Starting process {0}", name));
                stopwatch = new Stopwatch();
                stopwatch.Start();
                DebugTimers[name] = stopwatch;
            }
            else if (stopwatch.IsRunning)
            { // Stop the timer and log the result
                stopwatch.Stop();
                Log(string.Format("Finished process {0}. Took {1:F3} seconds.", name, stopwatch.Elapsed.TotalSeconds));
            }
            else
            { // Removes the timer and starts it again
                DebugTimers.Remove(name);
                DebugTimer(name);
            }
        }

        // private void LogValueOverwritten(string key, object NameOrId, object oldValue, object newValue)
        // {
        //     if (logConflicts)
        //     {
        //         string modToBlame = $"{currentModName} overwrote a value:";
        //         Warn(modToBlame);
        //         conflictLog.Add(modToBlame);

        //         string overwrittenValues = $"Key '{key}' overwritten in Id '{NameOrId}'.\nOld value: {oldValue}\nNew value: {newValue}";
        //         Warn(overwrittenValues);
        //         conflictLog.Add(overwrittenValues);
        //     }
        // }

        // private void LogConflictsToFile()
        // {
        //     if (conflictLog.Count > 0)
        //     {
        //         string fileName = "SDLS_Merge_Conflicts.log";
        //         string writePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        //         using StreamWriter writer = new StreamWriter(writePath, false);
        //         foreach (string str in conflictLog) writer.WriteLine(str);
        //     }
        // }

        private void InitializationLine()
        {
            string[] lines = {
            "Querrying ChatGPT for better JSON.",
            "Help! If you're seeing this, I'm the guy he trapped within this program to rewrite your JSON!.",
            "This is an alternate line 3.",
            "I'm sorry, but as an AI language model.",
            "Error 404: Humor not found... in your JSON.",
            "Adding a lot of useless stuff. Like, a LOT of useless stuff.",
            "Adding a mascot that's more powerful than all the others, found in London, as is tradition.",
            "Compiling JSON files into JSON files into JSON files into JSON files into JSON files into JSON files into JSON files...",
            "You better be using .sdls files.",
            "Adding gluten to your JSON.",
            "Jason? JASON!",
            "Press X to JSON",
            "Adding exponentially more data.",
            "JSON is honestly just a Trojan Horse to smuggle Javascript into other languages.",
            "\n\nIn Xanadu did Kubla Khan\nA stately pleasure-dome decree:\nWhere Alph, the sacred river, ran\nThrough caverns measureless to man\nDown to a sunless sea.",
            "She Simplifying my Data Loading till I Sunless",
            "Screw it. Grok, give me some more jokes for the JSON.",
            "\nCan you guess where the JSON goes?\nThat's right!\nIt goes in the square hole!",
            "You merely adopted the JSON. I was born in it, molded by it.",
            };

            string line = lines[new System.Random().Next(0, lines.Length)];
            Log(line + "\n");
        }

        // Simplified log functions
        public void Log(object message) { Logger.LogInfo(message); }
        public void Warn(object message) { Console.WriteLine(message); }
        public void Error(object message) { Console.WriteLine(message); }
#if DEBUG
        // Log functions that don't run when built in Release mode
        public void DLog(object message) { Log(message); }
        public void DWarn(object message) { Warn(message); }
        public void DError(object message) { Error(message); }
#else
        // Empty overload methods to make sure the plugin doesn't crash when built in release mode
        private  void DLog(object message) { }
        private  void DWarn(object message) { }
        private  void DError(object message) { }
#endif
    }
}