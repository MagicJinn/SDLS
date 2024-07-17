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
using System.Xml.XPath;

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
        private Dictionary<string, Dictionary<int, Dictionary<string, object>>> mergedModsDict = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
        // Dictionary: string = filename, object is list.
        // List: list of IDictionaries (a list of JSON objects).
        // Dictionary = The actual JSON objects. string = key, object = value / nested objects.

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


                            Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, modFolderInAddon));
                            string trashedJSON = TrashJSON(fileContent, fileName);
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
                DebugTimer("LogConflictsToFile"); LogConflictsToFile(); DebugTimer("LogConflictsToFile");
            }
        }

        private string TrashJSON(string strObjJoined, string name)
        {
            try
            {
                List<string> strObjList = new List<string>(); // List to store all the JSON strings

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
            Dictionary<string, object> mergeJSONObj, // Data will get merged INTO this object
            Dictionary<string, object> compareData = null // Data will get compared to this object
        )
        {
            // Copy the input dictionary to a new dictionary as a preventitive measure to not repeat the events of 16/05/2023
            Dictionary<string, object> mergeJSONObjCopy = new Dictionary<string, object>(mergeJSONObj);

            // Ensure compareData is not null
            // When copying over values during regular trashing, this saves performance, since values are not copied over unnecessarily
            // When mod merging, it ensures no actual values are overwritten by default values during merging
            compareData ??= new Dictionary<string, object>(mergeJSONObj);

            foreach (var kvp in tracedJSONObj)
            {
                var tracedKey = kvp.Key; // Name of key, e.g., UseEvent
                var tracedValue = kvp.Value; // Value of key, e.g., 500500

                if (tracedValue /* content of the current field */ is /* another JSON object */ Dictionary<string, object> tracedDict /*&&
                mergeJSONObjCopy[tracedKey] is Dictionary<string, object> mergeDict*/)
                {
                    var passedInComparedData = GetAComponent(tracedKey); // 
                    mergeJSONObjCopy[tracedKey] = ApplyFieldsToMold(tracedDict, mergeJSONObjCopy/*mergeDict*/, passedInComparedData);
                }
                else
                {
                    mergeJSONObjCopy[tracedKey] = /* If */ componentNames.Contains(/* the currently handled */ tracedKey)
                    // Is there a default component for this field?
                        ? HandleComponent(tracedValue, tracedKey) // Yes
                        : HandleDefault(tracedValue); // No
                }
            }

            return mergeJSONObjCopy;
        }

        private object HandleComponent(object tracedValue, string tracedKey)
        {
            return tracedValue switch
            {
                IEnumerable<object> array =>
                HandleArray(array.Cast<Dictionary<string, object>>().ToList(), tracedKey),
                _ =>
                HandleObject(tracedValue, tracedKey)
            };
        }

        private object HandleArray(List<Dictionary<string, object>> array, string fieldName, List<Dictionary<string, object>> arrayToMergeInto = null)
        {
            if (arrayToMergeInto == null) arrayToMergeInto = new List<Dictionary<string, object>>(); // Set the arrayToMergeInto to an empty array (list) if none are provided.

            foreach (var item in array)
            {
                if (item is Dictionary<string, object> itemDict) // If the item is a dictionary (eg, storylet, quality etc)
                {
                    // Some objects use different primary keys. For example, QualityAffected uses it's AssociatedQualityId as a primary key.
                    string keyToCheckFor = FindPrimaryKey(itemDict);

                    MergeOrAddToArray(
                        itemDict, arrayToMergeInto, keyToCheckFor,
                        out int mergeIntoIndex // The index of the object in the mergeInto array
                    );

                    if (mergeIntoIndex != -1) // Handles the event where 2 json objects should be merged
                    {
                        var currentData = arrayToMergeInto[mergeIntoIndex];
                        arrayToMergeInto[mergeIntoIndex] = ApplyFieldsToMold(itemDict, currentData);
                    }
                    else
                    {
                        object objectItem = HandleObject(itemDict, fieldName);

                        // "convert" the object HandleObject returns into a dictionary, since the function itself cannot be altered
                        // AE Arrays will never contain another array
                        // But if I don't fix it the compiler won't stop complaining
                        if (objectItem is Dictionary<string, object> dictionaryItem)
                            arrayToMergeInto.Add(dictionaryItem);
                        else throw new InvalidCastException("Guhh? The returned object is not a Dictionary<string, object>.");

                    }
                }

                else arrayToMergeInto.Add(item); // Else, if the item is a value (for example, "weather", results in ["value"])

            }
            return arrayToMergeInto;
        }

        private void MergeOrAddToArray(
    Dictionary<string, object> item,
    List<Dictionary<string, object>> arrayToMergeInto,
    string keyToCheckFor,
    out int mergeIntoIndex
)
        {
            mergeIntoIndex = -1;
            Console.WriteLine($"Checking for key: {keyToCheckFor}");

            if (item.ContainsKey(keyToCheckFor))
            {
                Console.WriteLine($"Item contains key: {keyToCheckFor}. Value: {item[keyToCheckFor]}");

                for (int i = 0; i < arrayToMergeInto.Count; i++)
                {
                    if (arrayToMergeInto[i].ContainsKey(keyToCheckFor))
                    {

                        if (Equals(arrayToMergeInto[i][keyToCheckFor], item[keyToCheckFor]))
                        {
                            mergeIntoIndex = i;
                            Console.WriteLine($"Match found at index: {mergeIntoIndex}");
                            break;
                        }
                        // else
                        // {
                        //     Console.WriteLine("Values do not match");
                        // }
                    }
                }
            }
            else
            {
                Console.WriteLine($"Item does not contain key: {keyToCheckFor}");
            }

            Console.WriteLine($"Final mergeIntoIndex: {mergeIntoIndex}");
        }
        private object HandleObject(object fieldValue, string fieldName)
        {
            var newMoldItem = GetAComponent(fieldName);
            return fieldValue is Dictionary<string, object> fieldValueDict
                ? ApplyFieldsToMold(fieldValueDict, newMoldItem)
                : fieldValue;
        }

        private object HandleDefault(object fieldValue)
        {
            return fieldValue is Dictionary<string, object> nestedJSON
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
                mergedModsDict[relativeFilePath] = new Dictionary<int, Dictionary<string, object>>(); // A list of dictionaries, with each dictionary being a JSON object
            }


            foreach (string strObjSplit in JSON.SplitJSON(strObjJoined)) // Adds each JSON object to the Mergemods name category
            {
                var deserializedJSON = JSON.Deserialize(strObjSplit);

                int Id;
                if (relativeFilePath.StartsWith("constants")) Id = 0; // Constants must always overwrite eachother, there are no different objects in constants
                else
                {
                    Id = NameOrId(deserializedJSON); // Name is converted to an integer during processing (REMEMBER THIS)
                }

                if (mergedModsDict[relativeFilePath].ContainsKey(Id)) // Begin the merging process if an object is already present.
                {
                    Dictionary<string, object> tracedTree = deserializedJSON;
                    Dictionary<string, object> mergeTree = mergedModsDict[relativeFilePath][Id];
                    Dictionary<string, object> compareData = embeddedData;

                    mergedModsDict[relativeFilePath][Id] = ApplyFieldsToMold(tracedTree, mergeTree, compareData);
                }
                else mergedModsDict[relativeFilePath][Id] = deserializedJSON; // Add the inputObject to the registry if there is no entry there
            }
            // Nothing happens at the end of this function, as this function only runs inside of a foreach loop
            // and once it concludes other code begins to run, which will create and save the JSON.
        }

        private int NameOrId(Dictionary<string, object> JSONObj)
        {
            string[] keys = { "AssociatedQualityId", "Id", "Name" };
            string primaryKey = FindPrimaryKey(JSONObj);

            if (keys.Contains(primaryKey))
            {
                return primaryKey == "Name" ?
                JSONObj[primaryKey].GetHashCode() :
                (int)JSONObj[primaryKey];
            }

            throw new ArgumentException($"The provided JSON object does not contain an 'Id', 'AssociatedQualityId' or 'Name' field. Object {JSON.Serialize(JSONObj)}");
        }

        private string FindPrimaryKey(Dictionary<string, object> JSONObj)
        {
            string[] keys = { "AssociatedQualityId", "Id", "Name" };

            foreach (string key in keys)
            {
                if (JSONObj.ContainsKey(key))
                {
                    return key;
                }
            }

            throw new ArgumentException($"The provided JSON object does not contain an 'Id', 'AssociatedQualityId', or 'Name' field. Object: {JSON.Serialize(JSONObj)}");
        }



        // private Dictionary<string, object> MergeTrees(Dictionary<string, object> tracedTree, Dictionary<string, object> mergeTree, Dictionary<string, object> compareData)
        // {
        //     foreach (var key in mergeTree.Keys)
        //     {
        //         if (tracedTree.ContainsKey(key))
        //         {
        //             if (tracedTree[key] is Dictionary<string, object> tracedSubTree && mergeTree[key] is Dictionary<string, object> mergeSubTree)
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
        //         if (mergeItem is Dictionary<string, object> mergeDict && mergeDict.ContainsKey("Id"))
        //         {
        //             int mergeId = (int)mergeDict["Id"];
        //             var existingItem = mergedList
        //                 .OfType<Dictionary<string, object>>()
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

        // private Dictionary<string, object> GetNextCompareData(Dictionary<string, object> compareData, string key)
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
            // if (lastIndex == -1) return "";

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

                File.WriteAllText(writePath, relativeWritePath.ToLower().Contains("constants")
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

                string overwrittenValues = $"Key '{key}' overwritten in Id '{NameOrId}'.\nOld value: {oldValue}\nNew value: {newValue}";
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