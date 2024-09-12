using BepInEx;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using Sunless.Game.Utilities;

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
        private bool doCleanup; // Whether the files created by SDLS should be cleared during shutdown
        private bool logDebugTimers;
        // Config options end

        // Tracks every file SDLS creates. Used during cleanup
        private List<string> createdFiles = new();

        private HashSet<string> componentNames; // Contains the names of all the JSON defaults
        private Dictionary<string, Dictionary<string, object>> componentCache = new(); // Cache for loaded components
        private bool TilesSpecialCase = false; // Variable for the special case of Tiles.json. Check GetAComponent for more info


        private string currentModName; // Variable for tracking which mod is currently being merged. Used for logging conflicts
        private List<string> conflictLog = new List<string>(); // List of conflicts
        private Dictionary<string, Stopwatch> DebugTimers = new Dictionary<string, Stopwatch>(); // List of Debug timers, used by DebugTimer()


        private Dictionary<string, Dictionary<int, Dictionary<string, object>>> mergedModsDict = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
        // Dictionary structure breakdown:
        // - string: Represents the filename or category (eg events.json).
        // - Dictionary<int, Dictionary<string, object>>: 
        //    - int: Id of an entry, either Id or Name or AssociatedQualityId.
        //    - Dictionary<string, object>: The actual data from a JSON object.
        //       - string: The key from the JSON object.
        //       - object: The value, which can be a primitive value or a nested object.wi

        private void Awake( /* Run by Unity on game start */ )
        {
            JSON.PrepareJSONManipulation();

            LoadConfig();

            Log($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            InitializationLine();

            DebugTimer("TrashAllJSON");
            TrashAllJSON();
            DebugTimer("TrashAllJSON");
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
                foreach (string file in createdFiles) RemoveJSON(file);
                DebugTimer("Cleanup");
            }
        }

        private void TrashAllJSON()
        {
            string[] filePaths = JSON.GetFilePaths(); // List of all possible moddable files

            componentNames = FindComponents(); // list of each default component

            // if (doMerge && basegameMerge)
            // {
            //     foreach (string filePath in filePaths)
            //     {
            //         DebugTimer("Basegame merge " + filePath);
            //         GetBasegameData(filePath);
            //         DebugTimer("Basegame merge " + filePath);
            //     }
            // }

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

                        // if (doMerge)
                        // {
                        //     DebugTimer("Merge " + fullRelativePath);
                        //     RemoveJSON(fullRelativePath);
                        //     MergeMods(fileContent, filePath);


                        //     Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, modFolderInAddon));
                        //     string trashedJSON = TrashJSON(fileContent, fileName);
                        //     DebugTimer("Merge " + fullRelativePath);
                        //     DebugTimer("Trash " + fullRelativePath);
                        // }
                        // else
                        // {
                        //     RemoveDirectory("SDLS_MERGED");

                        string trashedJSON = TrashJSON(fileContent, fileName);
                        DebugTimer("Trash " + fullRelativePath);

                        DebugTimer("Create " + fullRelativePath);
                        CreateJSON(trashedJSON, fullRelativePath);
                        DebugTimer("Create " + fullRelativePath);

                        // }
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
            Dictionary<string, object> mergeJSONObjCopy = new Dictionary<string, object>(mergeJSONObj);

            foreach (var kvp in tracedJSONObj)
            {
                var tracedKey = kvp.Key; // Name of key, e.g., UseEvent
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

                // Check if baselineObject is of the correct type, if not, pass null
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
            if (arrayToMergeInto == null) arrayToMergeInto = new List<Dictionary<string, object>>(); // Set the arrayToMergeInto to an empty array (list) if none are provided.

            foreach (var item in array)
            {
                if (item is Dictionary<string, object> itemDict) // If the item is a dictionary (eg, storylet, quality etc)
                {
                    // "convert" the object HandleObject returns into a dictionary, since the function itself cannot be altered
                    // AE Arrays will never contain another array
                    // But if I don't fix it the compiler won't stop complaining
                    arrayToMergeInto.Add((Dictionary<string, object>)HandleObject(itemDict, fieldName));

                }
                // Else, if the item is a value (for example, "SubsurfaceWeather", results in ["value"])
                else arrayToMergeInto.Add(item);

            }
            return arrayToMergeInto;
        }

        // private void GetBasegameData(string relativeFilePath)
        // {
        //     string strObjJoined = JSON.ReadGameJson(relativeFilePath + ".json");
        //     MergeMods(strObjJoined, relativeFilePath);
        // }

        // private void MergeMods(string strObjJoined, string relativeFilePath) // Running this will add inputData to the mergedMods registry
        // {
        //     string fileName = GetLastWord(relativeFilePath);

        //     var embeddedData = GetAComponent(fileName);

        //     // Creates entries for mod in a dictionary that can be referenced later.
        //     // Names like entities/events, which will be used later to create JSON files in the correct locations.
        //     if (!mergedModsDict.ContainsKey(relativeFilePath))
        //     {
        //         mergedModsDict[relativeFilePath] = new Dictionary<int, Dictionary<string, object>>(); // A list of dictionaries, with each dictionary being a JSON object
        //     }


        //     foreach (string strObjSplit in JSON.SplitJSON(strObjJoined)) // Adds each JSON object to the Mergemods name category
        //     {
        //         var deserializedJSON = JSON.Deserialize(strObjSplit);

        //         int Id;
        //         if (relativeFilePath.StartsWith("constants")) Id = 0; // Constants must always overwrite eachother, there are no different objects in constants
        //         else
        //         {
        //             Id = NameOrId(deserializedJSON); // Name is converted to an integer during processing (REMEMBER THIS)
        //         }

        //         if (mergedModsDict[relativeFilePath].ContainsKey(Id)) // Begin the merging process if an object is already present.
        //         {
        //             Dictionary<string, object> tracedTree = deserializedJSON;
        //             Dictionary<string, object> mergeTree = mergedModsDict[relativeFilePath][Id];
        //             Dictionary<string, object> compareData = embeddedData;

        //             mergedModsDict[relativeFilePath][Id] = ApplyFieldsToMold(tracedTree, mergeTree);
        //         }
        //         else mergedModsDict[relativeFilePath][Id] = deserializedJSON; // Add the inputObject to the registry if there is no entry there
        //     }
        //     // Nothing happens at the end of this function, as this function only runs inside of a foreach loop
        //     // and once it concludes other code begins to run, which will create and save the JSON.
        // }

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


        private string GetLastWord(string str)
        {
            string result = str.Split(new char[] { '/', '\\' }).Last(/*Returns only the resource name*/);
            return result;
        }

        string GetParentPath(string filePath)
        {
            int lastIndex = filePath.LastIndexOfAny(new char[] { '/', '\\' });

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

        private HashSet<string> FindComponents() // Fetches a list of all files (names) in the defaultComponents folder
        {
            string embeddedPath = GetEmbeddedPath("default");
            var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            var names = new HashSet<string>();
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
                    if (line.Contains('=')) // Check if the line contains an '=' character
                    {
                        // Remove all spaces from the line and split it at the first occurrence of '=' into two parts
                        string[] keyValue = line.Replace(" ", "").Split(new[] { '=' }, 2);
                        optionsDict.Add(keyValue[0], keyValue[1]);
                    }
                }

                doMerge = bool.Parse(optionsDict["doMerge"]);
                logConflicts = doMerge ? // Respect log conflicts option if merging is enabled
                                    bool.Parse(optionsDict["logMergeConflicts"]) :
                                    false;

                basegameMerge = bool.Parse(optionsDict["basegameMerge"]);

                doCleanup = bool.Parse(optionsDict["doCleanup"]);

                logDebugTimers = bool.Parse(optionsDict["logDebugTimers"]);
            }
            catch (Exception)
            {
                LoadConfig( /*loadDefault =*/ true); // Load config with default values
            }
        }

        private void DebugTimer(string name)
        {
            if (!logDebugTimers) return;
            if (!DebugTimers.ContainsKey(name))
            {
                // Start a new timer
                Log("Starting process " + name);
                DebugTimers[name] = new Stopwatch();
                DebugTimers[name].Start();
            }
            else if (DebugTimers[name].IsRunning)
            { // Stop the timer and log the result
                DebugTimers[name].Stop();
                Log($"Finished process {name}. Took {DebugTimers[name].Elapsed.TotalSeconds:F3} seconds.");
            }
            else
            { // Removes the timer and starts it again
                DebugTimers.Remove(name);
                DebugTimer(name);
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
            "Press X to JSON",
            "Adding exponentially more data.",
            "JSON is honestly just a Trojan Horse to smuggle Javascript into other languages.",
            "\n\nIn Xanadu did Kubla Khan\nA stately pleasure-dome decree:\nWhere Alph, the sacred river, ran\nThrough caverns measureless to man\nDown to a sunless sea.",
            "She Simplifying my Data Loading till I Sunless",
            "Screw it. Grok, give me some more jokes for the JSON.",
            "\nCan you guess where the JSON goes?\nThat's right!\nIt goes in the square hole!",
            "You merely adopted the JSON. I was born in it, molded by it.",
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