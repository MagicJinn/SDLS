using BepInEx;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using Sunless.Game.Utilities;
using Sunless.Game.ApplicationProviders;

namespace SDLS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // Config options
        private const string CONFIG = "SDLS_Config.ini";
        private bool doMerge;
        private bool logConflicts;

        private bool basegameMerge;

        private bool doCleanup;
        // Config options end

        private List<string> createdFiles = new List<string>(); // Tracks every file SDLS creates. Used during cleanup

        private List<string> componentNames; // Contains the names of all the JSON defaults
        private Dictionary<string, Dictionary<string, object>> componentCache = new Dictionary<string, Dictionary<string, object>>(); // Cache for loaded components
        private bool TilesSpecialCase = false; // Variable for the special case of Tiles.json. Check GetAComponent for more info


        private string currentModName; // Variable for tracking which mod is currently being merged. Used for logging conflicts
        private List<string> conflictLog = new List<string>(); // List of conflicts
        // Dictionary of lists of IDictionaries.
        private Dictionary<string, Dictionary<int, IDictionary<string, object>>> mergedMods = new Dictionary<string, Dictionary<int, IDictionary<string, object>>>();
        // Dictionary: string = filename, object is list.
        // List: list of IDictionaries (a list of JSON objects).
        // IDictionary = The actual JSON objects. string = key, object = value / nested objects.

        // Loads all basegame json files if basegameMerge = true
        // private Dictionary<string, Dictionary<string, object>> basegameCache = new Dictionary<string, Dictionary<string, object>>();
        private Dictionary<string, Dictionary<string, IDictionary<string, object>>> basegameCache = new Dictionary<string, Dictionary<string, IDictionary<string, object>>>();

        private Dictionary<string, Stopwatch> DebugTimers = new Dictionary<string, Stopwatch>();

        private void Awake()
        {
            JSON.PrepareJSONManipulation();

            LoadConfig();

            Log($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            InitializationLine();

            DebugTimer("TrashAllJSON");
            TrashAllJSON();
            DebugTimer("TrashAllJSON");
        }

        void OnApplicationQuit()
        {
            // Removes all files created by SDLS before closing, because otherwise they would cause problems with Vortex
            // So basically, if a user had SDLS installed, and used Vortex to manage their mods, if they wanted to remove
            // a mod, Vortex would only remove the .sdls files, and not the .json files, which would cause the game to
            // still run them, and the user would have no clue why. 
            if (doCleanup)
            {
                DebugTimer("Cleanup");
                RemoveDirectory("SDLS_MERGED");
                foreach (string file in createdFiles) RemoveJSON(file);
                DebugTimer("Cleanup");
            }
        }

        private void TrashAllJSON()
        {
            string[] filePaths = JSON.GetFilePaths(); // List of all possible moddable files

            componentNames = FindComponents(); // list of each default component

            // Iterate over each subdirectory returned by FileHelper.GetAllSubDirectories()
            // FileHelper.GetAllSubDirectories() is a function provided by the game to list all
            // Subdirectories in addons (A list of all mods)
            foreach (string modFolder in FileHelper.GetAllSubDirectories())
            {
                foreach (string filePath in filePaths)
                {
                    string modFolderInAddon = Path.Combine("addon", modFolder);
                    string fullRelativePath = Path.Combine(modFolderInAddon, filePath);

                    string sdlsFile = FileHelper.ReadTextFile(fullRelativePath + ".sdls"); // Attempt to read the file with ".sdls" extension
                    string prefixFile = FileHelper.ReadTextFile(fullRelativePath + "SDLS.json"); // Attempt to read the file with "SDLS.json" extension
                    string fileContent = sdlsFile != null ? sdlsFile : prefixFile; // Pick the available option, favouring .sdls files

                    if (sdlsFile != null && prefixFile != null) // Log a warning if both file types are found, choosing the one with ".sdls" extension
                        Warn("Detected both a .sdlf and a SDLF.json file. The .sdlf file will be used.\nPlease consider removing one of these files to avoid confusion.");

                    if (fileContent != null)
                    {
                        string fileName = GetLastWord(fullRelativePath);

                        DebugTimer("Trash " + fullRelativePath);

                        fileContent = fileContent.Substring(1, fileContent.Length - 2); // Remove the [ ] around the string

                        currentModName = fullRelativePath;

                        if (doMerge)
                        {
                            DebugTimer("Merge " + fullRelativePath);
                            RemoveJSON(fullRelativePath);
                            MergeMods(fileContent, filePath);


                            string trashedJSON = TrashJSON(fileContent, fileName);
                            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, modFolderInAddon));
                            DebugTimer("Merge " + fullRelativePath);
                            DebugTimer("Trash " + fullRelativePath);
                        }
                        else
                        {
                            RemoveDirectory("SDLS_MERGED");

                            string trashedJSON = TrashJSON(fileContent, fileName);
                            DebugTimer("Trash " + fullRelativePath);

                            DebugTimer("Create " + fullRelativePath);
                            CreateJSON(trashedJSON, fullRelativePath);
                            DebugTimer("Create " + fullRelativePath);

                        }
                    }
                }
            }
            if (doMerge)
            {
                Warn("DO NOT DISTRIBUTE MERGED JSON. IT WILL CONTAIN ALL MODS YOU HAVE INSTALLED, WHICH BELONG TO THEIR RESPECTIVE MOD AUTHORS.");
                foreach (var merged in mergedMods)
                {
                    string fileName = merged.Key;
                    var objectsDict = merged.Value;
                    List<string> objectList = new List<string>();

                    foreach (var objDict in objectsDict.Values)
                    {
                        objectList.Add(JSON.Serialize(objDict));
                    }

                    string trashedJSON = TrashJSON(JoinJSON(objectList), fileName); // Join all JSON together and Trash it like you would any other JSON
                    string pathTo = Path.Combine("addon", "SDLS_MERGED"); // Gets output path
                    DLog(pathTo);
                    DLog(merged.Key);
                    CreateJSON(trashedJSON, Path.Combine(pathTo, merged.Key)); // Creates JSON files in a designated SDLS_MERGED output folder
                }
                LogConflictsToFile();
            }
        }

        private string TrashJSON(string strObjJoined, string name)
        {
            try
            {
                List<string> strObjList = new List<string>(); // List to catch all the JSON

                foreach (string splitString in SplitJSON(strObjJoined))
                {
                    var embeddedData = GetAComponent(name); // Deserialize the default mold data
                    var deserializedJSON = JSON.Deserialize(splitString);

                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJSON, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    strObjList.Add(JSON.Serialize(returnData));

                    if (TilesSpecialCase) TilesSpecialCase = false;  // Stupid special case for Tiles, check GetAComponent for details
                }

                string result = JoinJSON(strObjList);
                return result;
            }
            catch (Exception ex)
            {
                Error($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return strObjJoined; // Return providedJSON as fallback
            }
        }

        private IDictionary<string, object> ApplyFieldsToMold(
            IDictionary<string, object> jsonObj,
            IDictionary<string, object> moldData
            )
        {
            var mold = new Dictionary<string, object>(moldData);

            foreach (var kvp in jsonObj)
            {
                var basin = kvp.Key; // Name of key, Eg UseEvent
                var crucible = kvp.Value; // Value of key, Eg 500500

                // Check if the field exists in the components list
                if (componentNames.Contains(basin))
                {
                    if (crucible is IEnumerable<object> array)
                    {
                        mold[basin] = HandleArray(array, basin);
                    }
                    else
                    {
                        mold[basin] = HandleObject(crucible, basin);
                    }
                }
                else
                {
                    mold[basin] = HandleDefault(crucible);
                }
            }
            return mold;
        }

        private object HandleArray(IEnumerable<object> array, string fieldName)
        {
            var newArray = new List<object>();
            foreach (var item in array) newArray.Add(item is IDictionary<string, object> itemDict
                    ? HandleObject(itemDict, fieldName)
                    : item);
            return newArray;
        }

        private object HandleObject(object fieldValue, string fieldName)
        {
            var newMoldItem = GetAComponent(fieldName);
            return fieldValue is IDictionary<string, object> fieldValueDict
                ? ApplyFieldsToMold(fieldValueDict, newMoldItem)
                : fieldValue;
        }

        private object HandleDefault(object fieldValue)
        {
            return fieldValue is IDictionary<string, object> nestedJSON
                ? ApplyFieldsToMold(nestedJSON, new Dictionary<string, object>())
                : fieldValue is IEnumerable<object> array
                    ? array.Select(HandleDefault).ToList()
                    : fieldValue;
        }

        // private Dictionary<string, IDictionary<string, object>> GetBasegameData(string name)
        // {
        //     if (!basegameCache.ContainsKey(name))
        //     {
        //         DLog("1");
        //         basegameCache[name] = new Dictionary<string, IDictionary<string, object>>(); // Initialize the inner dictionary
        //         DLog("2");
        //         string asText = FileHelper.ReadTextFile(name + ".json");
        //         asText = asText.Substring(1, asText.Length - 2); // Remove the [ ] around the string
        //         string[] splitText = SplitJSON(asText);
        //         DLog("3");
        //         foreach (var str in splitText)
        //         {
        //             var deserializedJSON = JSON.Deserialize(str);
        //             DLog("4");
        //             int Id = (int)deserializedJSON["Id"];
        //             DLog("5");
        //             basegameCache[name][Convert.ToString(Id)] = deserializedJSON;
        //             DLog("6");
        //         }
        //     }
        //     DLog("7");
        //     return basegameCache[name];
        // }

        private void MergeMods(string strObjJoined, string relativePathDirectory) // Running this will add inputData to the mergedMods registry
        {
            string fileName = GetLastWord(relativePathDirectory);

            var embeddedData = GetAComponent(fileName);

            // var basegameData = GetBasegameData(name);

            // Creates entries for mod in a dictionary that can be referenced later.
            // Names like entities/events, which will be used later to create JSON files in the correct locations.
            if (!mergedMods.ContainsKey(relativePathDirectory))
            {
                mergedMods[relativePathDirectory] = new Dictionary<int, IDictionary<string, object>>(); // A list of dictionaries, with each dictionary being a JSON object
            }

            foreach (string strObjSplit in SplitJSON(strObjJoined)) // Adds each JSON object to the Mergemods name category
            {
                var deserializedJSON = JSON.Deserialize(strObjSplit);

                int Id = (int)deserializedJSON["Id"];

                if (mergedMods[relativePathDirectory].ContainsKey(Id)) // Begin the merging process if an object is already present.
                {
                    IDictionary<string, object> tracedTree = mergedMods[relativePathDirectory][Id];
                    IDictionary<string, object> mergeTree = deserializedJSON;
                    IDictionary<string, object> compareData = embeddedData;

                    mergedMods[relativePathDirectory][Id] = MergeTrees(tracedTree, mergeTree, compareData);

                }
                else mergedMods[relativePathDirectory][Id] = deserializedJSON; // Add the inputObject to the registry if there is no entry there
            }
            // Nothing happens at the end of this function, as this function only runs inside of a foreach loop
            // and once it concludes other code begins to run, which will create and save the JSON.
        }

        private IDictionary<string, object> MergeTrees(IDictionary<string, object> tracedTree, IDictionary<string, object> mergeTree, IDictionary<string, object> compareData)
        {
            foreach (var key in mergeTree.Keys)
            {
                if (tracedTree.ContainsKey(key))
                {
                    if (tracedTree[key] is IDictionary<string, object> tracedSubTree && mergeTree[key] is IDictionary<string, object> mergeSubTree)
                    {
                        // Both are dictionaries, so we merge them recursively.
                        var nextCompareData = GetNextCompareData(compareData, key);
                        tracedTree[key] = MergeTrees(tracedSubTree, mergeSubTree, nextCompareData);
                    }
                    else if (tracedTree[key] is IEnumerable<object> tracedList && mergeTree[key] is IEnumerable<object> mergeList)
                    {
                        tracedTree[key] = MergeLists(tracedList, mergeList);
                    }
                    else
                    {
                        // Overwrite value only if they are different and not the default value.
                        bool valuesAreDifferent = !tracedTree[key].Equals(mergeTree[key]);
                        bool overwritingValueIsNotDefault = compareData != null && compareData[key] != null && compareData.ContainsKey(key) && !compareData[key].Equals(mergeTree[key]);
                        if (valuesAreDifferent && overwritingValueIsNotDefault)
                        {
                            LogValueOverwritten(key, (int)tracedTree["Id"], tracedTree[key], mergeTree[key]);
                            tracedTree[key] = mergeTree[key];
                        }
                    }
                }
                else
                {
                    // Key does not exist in tracedTree, just add it.
                    tracedTree[key] = mergeTree[key];
                }
            }
            return tracedTree;
        }

        private IEnumerable<object> MergeLists(IEnumerable<object> tracedList, IEnumerable<object> mergeList)
        {
            var mergedList = tracedList.ToList();

            foreach (var mergeItem in mergeList)
            {
                if (mergeItem is IDictionary<string, object> mergeDict && mergeDict.ContainsKey("Id"))
                {
                    int mergeId = (int)mergeDict["Id"];
                    var existingItem = mergedList
                        .OfType<IDictionary<string, object>>()
                        .FirstOrDefault(item => item.ContainsKey("Id") && (int)item["Id"] == mergeId);

                    if (existingItem != null)
                    {
                        // Merge objects with the same Id.
                        var mergedItem = MergeTrees(existingItem, mergeDict, null);
                        var index = mergedList.IndexOf(existingItem);
                        mergedList[index] = mergedItem;
                    }
                    else
                    {
                        // Add new item.
                        mergedList.Add(mergeDict);
                    }
                }
                else
                {
                    // Add new item if no Id.
                    mergedList.Add(mergeItem);
                }
            }

            return mergedList;
        }

        private IDictionary<string, object> GetNextCompareData(IDictionary<string, object> compareData, string key)
        {
            bool compareDataValid = compareData != null && compareData.ContainsKey(key) && componentNames.Contains(key);
            if (compareDataValid) return GetAComponent(key);
            return compareData;
        }

        private string[] SplitJSON(string strObjJoined) // Splits JSON objects from eachother, so they can be processed individually
        {
            var objects = new List<string>();

            int depth = 0;
            int startIndex = 0;
            bool isInObject = false; // Flag to track if inside a JSON object

            for (int i = 0; i < strObjJoined.Length; i++)
            {
                char currentChar = strObjJoined[i];

                if (currentChar == '{')
                {
                    depth++;
                    isInObject = true;
                }
                else if (currentChar == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        isInObject = false;
                    }
                }
                else if (currentChar == ',' && depth == 0 && !isInObject)
                {
                    // Ignore commas that are within nested objects
                    objects.Add(strObjJoined.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }

            // Add the last JSON object
            objects.Add(strObjJoined.Substring(startIndex));

            return objects.ToArray();
        }

        private string JoinJSON(List<string> strList) // Rejoins all JSON objects
        {
            return string.Join(",", strList.ToArray());
        }

        private string GetLastWord(string str)
        {
            string result = str.Split(new char[] { '/', '\\' }).Last(/*Returns only the resource name*/);
            return result;
        }

        string GetParentPath(string filePath)
        {
            int lastIndex = filePath.LastIndexOfAny(new char[] { '/', '\\' });
            if (lastIndex == -1) return "";

            // Return the substring from the start of the string up to the last directory separator
            return filePath.Substring(0, lastIndex + 1);
        }

        private void CreateJSON(string strObjJoined, string relativeWritePath) // Creates JSON out of plaintext, and puts them in the addon folder next to the original
        {
            string writePath = Path.Combine(Application.persistentDataPath, relativeWritePath) + ".json";
            string path = GetParentPath(writePath);

            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path); // Create the directory if it doesn't exist

                File.WriteAllText(writePath, $"[{strObjJoined}]");
                Log("Created new file at " + relativeWritePath);
                createdFiles.Add(relativeWritePath);
            }
            catch (Exception ex) { Error("Error writing file: " + ex.Message); }
        }

        private void RemoveJSON(string relativeFilePath)
        {
            string path = Application.persistentDataPath + "/";
            string filePath = path + relativeFilePath + ".json";
            if (File.Exists(filePath))
            {
                Warn("Removing " + relativeFilePath);
                File.Delete(filePath);
            }
        }

        private void RemoveDirectory(string relativePath) // Removes any directory in Addon
        {
            string relativePathDirectory = Path.Combine("addon", relativePath);
            string path = Path.Combine(Application.persistentDataPath, relativePathDirectory);

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

        private string JSONAsText(string resourceName) // Method for loading embedded JSON resources
        {
            try
            {
                string name = GetLastWord(resourceName);
                string fullPath = GetEmbeddedPath("default");
                string fullResourceName = $"{fullPath}.{name}.json"; // Construct the full resource name
                return ReadTextResource(fullResourceName);
            }
            catch (Exception ex)
            {
                Error("Something went seriously wrong and SDLS was not able to load it's own embedded resource.");
                Error(ex.Message);
                return "";
            }
        }

        private string ReadTextResource(string fullResourceName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
            {
                if (stream == null)
                {
                    Warn("Tried to get resource that doesn't exist: " + fullResourceName);
                    return null; // Return null if the embedded resource doesn't exist
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd(); // Read and return the embedded resource
                }
            }
        }

        private List<string> FindComponents() // Fetches a list of all files (names) in the defaultComponents folder
        {
            string embeddedPath = GetEmbeddedPath("default");
            var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            var names = new List<string>();
            foreach (var name in resourceNames)
            {
                if (name.StartsWith(embeddedPath))
                {
                    var component = name.Substring(embeddedPath.Length);
                    component = component.TrimStart('.').Replace(".json", "");
                    names.Add(component);
                }
            }
            if (names.Count == 0) Error("Something went seriously wrong and SDLS was unable to load any default components!");
            else DLog("Components found:\n" + string.Join(", ", names.ToArray())); // Log all components

            return names;
        }

        private Dictionary<string, object> GetAComponent(string name)
        {
            string componentName = name;
            if (!componentCache.ContainsKey(componentName))
            {
                string asText = JSONAsText(componentName);

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

                if (componentName == "Tiles" && TilesSpecialCase) // If Tiles is requested, but TilesSpecialCase is true, it means it has been requested before in this instance, meaning it should 
                {
                    componentName = "TilesTiles"; // Set the name to TilesTiles to return the correct component
                    if (!componentCache.ContainsKey("TilesTiles")) // If the TilesTiles component hasn't been added, add it.
                    {
                        componentCache[componentName] = JSON.Deserialize(JSONAsText(componentName));
                    }
                }
            }

            if (componentName == "Tiles") TilesSpecialCase = true; // Set special case to true after the first time Tiles has been requested and returned
            return componentCache[componentName];
        }

        private string GetEmbeddedPath(string folderName = "") // Get the path of embedded resources
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
                string file = ReadTextResource(GetEmbeddedPath() + CONFIG);

                lines = file.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            }

            var optionsDict = new Dictionary<string, string>();
            try
            {
                foreach (var line in lines)
                {
                    string line2 = line.Replace(" ", "");

                    if (line2.StartsWith("#") || line2.StartsWith("["))
                    {
                        continue;
                    }
                    else if (line2.Contains("="))
                    {
                        string[] split = line2.Split('=');
                        optionsDict.Add(split[0], split[1]);
                    }
                }

                doMerge = bool.Parse(optionsDict["doMerge"]);
                logConflicts = bool.Parse(optionsDict["logMergeConflicts"]);

                basegameMerge = bool.Parse(optionsDict["basegameMerge"]);

                doCleanup = bool.Parse(optionsDict["doCleanup"]);
            }
            catch (Exception)
            {
                LoadConfig( /*loadDefault =*/true); // Load config with default values
            }
        }

        private void DebugTimer(string name)
        {
            if (!DebugTimers.ContainsKey(name))
            {
                DLog("Starting process " + name);
                DebugTimers[name] = new Stopwatch();
                DebugTimers[name].Start();
            }
            else
            {
                if (DebugTimers[name].IsRunning)
                {
                    DebugTimers[name].Stop();
                    DLog($"Finished process {name}. Took {DebugTimers[name].Elapsed.TotalSeconds:F3} seconds.");
                }
                else
                { // Removes the timer and starts it again
                    DebugTimers.Remove(name);
                    DebugTimer(name);
                }
            }
        }

        private void LogValueOverwritten(string key, int Id, object oldValue, object newValue)
        {
            if (logConflicts)
            {
                string modToBlame = $"{currentModName} overwrote a value:";
                Warn(modToBlame);
                conflictLog.Add(modToBlame);

                string overwrittenValues = $"Key '{key}' overwritten in Id '{Id}'. Old value: {oldValue}, New value: {newValue}";
                Warn(overwrittenValues);
                conflictLog.Add(overwrittenValues);
            }
        }

        private void LogConflictsToFile()
        {
            if (conflictLog.Count > 0)
            {
                string fileName = "SDLS_Merge_Conflicts.log";
                string writePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                using (StreamWriter writer = new StreamWriter(writePath, false))
                {
                    foreach (string str in conflictLog) writer.WriteLine(str);
                }
            }
        }

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
            "Adding exponentially more data.",
            "JSON is honestly just a Trojan Horse to smuggle Javascript into other languages.",
            "\n\nIn Xanadu did Kubla Khan\nA stately pleasure-dome decree:\nWhere Alph, the sacred river, ran\nThrough caverns measureless to man\nDown to a sunless sea.",
            "She Simplifying my Data Loading till I Sunless",
            "Screw it. Grok, give me some more jokes for the JSON.",
            "\nCan you guess where the JSON goes?\nThat's right!\nIt goes in the square hole!",
            };

            Log(lines[new System.Random().Next(0, lines.Length)] + "\n");
        }

        // Simplified log functions
        private void Log(object message) { Logger.LogInfo(message); }
        private void Warn(object message) { Logger.LogWarning(message); }
        private void Error(object message) { Logger.LogError(message); }
#if DEBUG
        // Log functions that don't run when built in Release mode
        private void DLog(object message){Log(message);}
        private void DWarn(object message){Warn(message);}
        private void DError(object message){Error(message);}
#else
        // Empty overload methods to make sure the plugin doesn't crash when built in release mode
        private void DLog(object message) { }
        private void DWarn(object message) { }
        private void DError(object message) { }
#endif
    }
}