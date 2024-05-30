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
        private Dictionary<string, Dictionary<int, IDictionary<string, object>>> mergedModsDict = new Dictionary<string, Dictionary<int, IDictionary<string, object>>>();
        // Dictionary: string = filename, object is list.
        // List: list of IDictionaries (a list of JSON objects).
        // IDictionary = The actual JSON objects. string = key, object = value / nested objects.

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
            if (doCleanup)
            {
                // Removes all files created by SDLS before closing, because otherwise they would cause problems with Vortex
                // So basically, if a user had SDLS installed, and used Vortex to manage their mods, if they wanted to remove
                // a mod, Vortex would only remove the .sdls files, and not the .json files, which would cause the game to
                // still run them, and the user would have no clue why. 
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

            if (doMerge && basegameMerge)
            {
                foreach (string filePath in filePaths)
                {
                    DebugTimer("Basegame merge " + filePath);
                    GetBasegameData(filePath);
                    DebugTimer("Basegame merge " + filePath);
                }
            }

            // Iterate over each subdirectory returned by FileHelper.GetAllSubDirectories()
            // FileHelper.GetAllSubDirectories() is a function provided by the game to list all
            // Subdirectories in addons (A list of all mods)
            foreach (string modFolder in FileHelper.GetAllSubDirectories())
            {
                foreach (string filePath in filePaths)
                {
                    string modFolderInAddon = Path.Combine("addon", modFolder);
                    string fullRelativePath = Path.Combine(modFolderInAddon, filePath);

                    string fileContent = JSON.ReadGameJson(fullRelativePath + ".sdls"); // Attempt to read the file with ".sdls" extension
                    if (fileContent == null) fileContent = JSON.ReadGameJson(fullRelativePath + "SDLS.json"); // Attempt to read the file with "SDLS.json" extension only if .sdls file is not found

                    if (fileContent != null)
                    {
                        string fileName = GetLastWord(fullRelativePath);

                        DebugTimer("Trash " + fullRelativePath);

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
                foreach (var fileDictEntry in mergedModsDict)
                {
                    string fileName = fileDictEntry.Key;
                    var objectsDict = fileDictEntry.Value;
                    List<string> JSONObjList = new List<string>();

                    foreach (var objDict in objectsDict)
                    {
                        JSONObjList.Add(JSON.Serialize(objDict.Value));
                    }

                    string trashedJSON = TrashJSON(JSON.JoinJSON(JSONObjList), fileName); // Join all JSON together and Trash it like you would any other JSON
                    string pathTo = Path.Combine("addon", "SDLS_MERGED"); // Gets output path
                    CreateJSON(trashedJSON, Path.Combine(pathTo, fileDictEntry.Key)); // Creates JSON files in a designated SDLS_MERGED output folder
                }
                DebugTimer("LogConflictsToFile");
                LogConflictsToFile();
                DebugTimer("LogConflictsToFile");
            }
        }

        private string TrashJSON(string strObjJoined, string name)
        {
            try
            {
                List<string> strObjList = new List<string>(); // List to catch all the JSON

                foreach (string splitString in JSON.SplitJSON(strObjJoined))
                {
                    var embeddedData = GetAComponent(name); // Deserialize the default mold data
                    var deserializedJSON = JSON.Deserialize(splitString);

                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJSON, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    strObjList.Add(JSON.Serialize(returnData));

                    if (TilesSpecialCase) TilesSpecialCase = false;  // Stupid special case for Tiles, check GetAComponent for details
                }

                string result = JSON.JoinJSON(strObjList);
                return result;
            }
            catch (Exception ex)
            {
                Error($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return strObjJoined; // Return providedJSON as fallback
            }
        }

        private IDictionary<string, object> ApplyFieldsToMold(
            IDictionary<string, object> tracedJSONObj, // This data will get copied over
            IDictionary<string, object> mergeJSONObj, // Data will get merged INTO this object
            IDictionary<string, object> compareData = null // Data will get compared to this object
        )
        {
            IDictionary<string, object> mergeJSONObjCopy = new Dictionary<string, object>(mergeJSONObj);

            // Ensure compareData is not null, initialize it with mergeJSONObj if necessary
            compareData ??= new Dictionary<string, object>(mergeJSONObj);

            foreach (var kvp in tracedJSONObj)
            {
                var tracedKey = kvp.Key; // Name of key, e.g., UseEvent
                var tracedValue = kvp.Value; // Value of key, e.g., 500500

                // Rule 1: If mergeJSONObjCopy doesn't have the key, copy the traced value directly
                if (!mergeJSONObjCopy.ContainsKey(tracedKey))
                {
                    mergeJSONObjCopy[tracedKey] = tracedValue;
                    continue;
                }

                // Rule 2: If overwriting data is equal to compare data and the key exists, skip overwrite
                if (compareData.TryGetValue(tracedKey, out var compareValue) && Equals(tracedValue, compareValue))
                {
                    DLog($"Overwriting data was default, skipping. {tracedKey} {tracedValue} {compareValue}");
                    continue;
                }

                // Rule 3: If overwriting value is different from merge tree value and merge tree value is different from compare data
                if (mergeJSONObjCopy.TryGetValue(tracedKey, out var mergeValue) && !Equals(tracedValue, mergeValue) && !Equals(mergeValue, compareValue))
                {
                    var nameOrId = NameOrId(mergeJSONObj);
                    if (mergeJSONObjCopy.TryGetValue("Id", out var idObj) && idObj is int id)
                    {
                        LogValueOverwritten(tracedKey, id, mergeValue, tracedValue);
                    }
                }

                // Handle the merge for nested dictionaries according to the rules
                if (tracedValue is IDictionary<string, object> tracedDict && mergeJSONObjCopy[tracedKey] is IDictionary<string, object> mergeDict)
                {
                    var passedInComparedData = GetAComponent(tracedKey);
                    mergeJSONObjCopy[tracedKey] = ApplyFieldsToMold(tracedDict, mergeDict, passedInComparedData);
                }
                else
                {
                    // Overwrite the value (Rule 4 and Rule 5)
                    mergeJSONObjCopy[tracedKey] = componentNames.Contains(tracedKey)
                        ? HandleComponent(tracedValue, tracedKey)
                        : HandleDefault(tracedValue);
                }
            }

            return mergeJSONObjCopy;
        }

        private object HandleComponent(object tracedValue, string tracedKey)
        {
            return tracedValue switch
            {
                IEnumerable<object> array => HandleArray(array, tracedKey),
                _ => HandleObject(tracedValue, tracedKey)
            };
        }

        private object HandleArray(IEnumerable<object> array, string fieldName)
        {
            var newArray = new List<object>();
            foreach (var item in array)
            {
                if (item is IDictionary<string, object> itemDict)
                {
                    var itemId = NameOrId(itemDict, fieldName);
                    var match = newArray.OfType<IDictionary<string, object>>().FirstOrDefault(x => NameOrId(x, fieldName) == itemId);
                    if (match != null)
                    {
                        ApplyFieldsToMold(itemDict, match);
                    }
                    else
                    {
                        newArray.Add(HandleObject(itemDict, fieldName));
                    }
                }
                else
                {
                    newArray.Add(item);
                }
            }
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

        private void GetBasegameData(string relativeFilePath)
        {
            string strObjJoined = JSON.ReadGameJson(relativeFilePath + ".json");
            MergeMods(strObjJoined, relativeFilePath);
        }

        private void MergeMods(string strObjJoined, string relativeFilePath) // Running this will add inputData to the mergedMods registry
        {
            string fileName = GetLastWord(relativeFilePath);

            var embeddedData = GetAComponent(fileName);

            // Creates entries for mod in a dictionary that can be referenced later.
            // Names like entities/events, which will be used later to create JSON files in the correct locations.
            if (!mergedModsDict.ContainsKey(relativeFilePath))
            {
                mergedModsDict[relativeFilePath] = new Dictionary<int, IDictionary<string, object>>(); // A list of dictionaries, with each dictionary being a JSON object
            }


            foreach (string strObjSplit in JSON.SplitJSON(strObjJoined)) // Adds each JSON object to the Mergemods name category
            {
                var deserializedJSON = JSON.Deserialize(strObjSplit);

                int Id;
                if (relativeFilePath.StartsWith("constants")) Id = 0; // Constants must always overwrite eachother, there are no different objects in constants
                else
                {
                    Id = NameOrId(deserializedJSON, relativeFilePath); // Name is converted to an integer during processing (REMEMBER THIS)
                }

                if (mergedModsDict[relativeFilePath].ContainsKey(Id)) // Begin the merging process if an object is already present.
                {
                    IDictionary<string, object> tracedTree = deserializedJSON;
                    IDictionary<string, object> mergeTree = mergedModsDict[relativeFilePath][Id];
                    IDictionary<string, object> compareData = embeddedData;

                    mergedModsDict[relativeFilePath][Id] = ApplyFieldsToMold(tracedTree, mergeTree, compareData);
                }
                else mergedModsDict[relativeFilePath][Id] = deserializedJSON; // Add the inputObject to the registry if there is no entry there
            }
            // Nothing happens at the end of this function, as this function only runs inside of a foreach loop
            // and once it concludes other code begins to run, which will create and save the JSON.
        }

        private int NameOrId(IDictionary<string, object> JSONObj, string relativeFilePath)
        {

            string[] componentsWithoutId = JSON.ComponentsWithoutId();
            bool objectHasNameInsteadOfId = componentsWithoutId
                                            .Select(GetLastWord)
                                            .Contains(GetLastWord(relativeFilePath)); // Checks whether the object has an Id key in it or only a name
            int Id = !objectHasNameInsteadOfId ?
                    (int)JSONObj["Id"] :
                    JSONObj["Name"].GetHashCode();
            return Id;
        }

        // private IDictionary<string, object> MergeTrees(IDictionary<string, object> tracedTree, IDictionary<string, object> mergeTree, IDictionary<string, object> compareData)
        // {
        //     foreach (var key in mergeTree.Keys)
        //     {
        //         if (tracedTree.ContainsKey(key))
        //         {
        //             if (tracedTree[key] is IDictionary<string, object> tracedSubTree && mergeTree[key] is IDictionary<string, object> mergeSubTree)
        //             {
        //                 // Both are dictionaries, so we merge them recursively.
        //                 var nextCompareData = GetNextCompareData(compareData, key);
        //                 tracedTree[key] = MergeTrees(tracedSubTree, mergeSubTree, nextCompareData);
        //             }
        //             else if (tracedTree[key] is IEnumerable<object> tracedList && mergeTree[key] is IEnumerable<object> mergeList)
        //             {
        //                 tracedTree[key] = MergeLists(tracedList, mergeList);
        //             }
        //             else
        //             {
        //                 // Overwrite value only if they are different and not the default value.
        //                 bool valuesAreDifferent = !tracedTree[key].Equals(mergeTree[key]);
        //                 bool overwritingValueIsNotDefault = compareData != null && compareData[key] != null && compareData.ContainsKey(key) && !compareData[key].Equals(mergeTree[key]);
        //                 if (valuesAreDifferent && overwritingValueIsNotDefault)
        //                 {
        //                     LogValueOverwritten(key, (int)tracedTree["Id"], tracedTree[key], mergeTree[key]);
        //                     tracedTree[key] = mergeTree[key];
        //                 }
        //             }
        //         }
        //         else
        //         {
        //             // Key does not exist in tracedTree, just add it.
        //             tracedTree[key] = mergeTree[key];
        //         }
        //     }
        //     return tracedTree;
        // }

        // private IEnumerable<object> MergeLists(IEnumerable<object> tracedList, IEnumerable<object> mergeList)
        // {
        //     var mergedList = tracedList.ToList();

        //     foreach (var mergeItem in mergeList)
        //     {
        //         if (mergeItem is IDictionary<string, object> mergeDict && mergeDict.ContainsKey("Id"))
        //         {
        //             int mergeId = (int)mergeDict["Id"];
        //             var existingItem = mergedList
        //                 .OfType<IDictionary<string, object>>()
        //                 .FirstOrDefault(item => item.ContainsKey("Id") && (int)item["Id"] == mergeId);

        //             if (existingItem != null)
        //             {
        //                 // Merge objects with the same Id.
        //                 var mergedItem = MergeTrees(existingItem, mergeDict, null);
        //                 var index = mergedList.IndexOf(existingItem);
        //                 mergedList[index] = mergedItem;
        //             }
        //             else
        //             {
        //                 // Add new item.
        //                 mergedList.Add(mergeDict);
        //             }
        //         }
        //         else
        //         {
        //             // Add new item if no Id.
        //             mergedList.Add(mergeItem);
        //         }
        //     }

        //     return mergedList;
        // }

        // private IDictionary<string, object> GetNextCompareData(IDictionary<string, object> compareData, string key)
        // {
        //     bool compareDataValid = compareData != null && compareData.ContainsKey(key) && componentNames.Contains(key);
        //     if (compareDataValid) return GetAComponent(key);
        //     return compareData;
        // }

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

        private void CreateJSON(string strObjJoined, string relativeWritePath)
        {
            string writePath = Path.Combine(Application.persistentDataPath, relativeWritePath) + ".json";
            string path = GetParentPath(writePath);

            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                File.WriteAllText(writePath, (relativeWritePath.ToLower().Contains("constants"))
                ? strObjJoined : // Output file as a single object
                $"[{strObjJoined}]"); // Put file in an array
                Log("Created new file at " + relativeWritePath);
                createdFiles.Add(relativeWritePath);
            }
            catch (Exception ex)
            {
                Error("Error writing file: " + ex.Message);
            }
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
                logConflicts = doMerge ?
                                    bool.Parse(optionsDict["logMergeConflicts"]) :
                                    false;

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

        private void LogValueOverwritten(string key, object NameOrId, object oldValue, object newValue)
        {
            if (logConflicts)
            {
                string modToBlame = $"{currentModName} overwrote a value:";
                Warn(modToBlame);
                conflictLog.Add(modToBlame);

                string overwrittenValues = $"Key '{key}' overwritten in Id '{NameOrId}'. Old value: {oldValue}, New value: {newValue}";
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