using BepInEx;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using Sunless.Game.Utilities;
using Sunless.Game.ApplicationProviders;

namespace SDLS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        //private string gameName;

        // Config options
        private const string CONFIG = "SDLS_Config.ini";
        private bool doMerge;
        private bool logConflicts;

        private bool doCleanup;

        private List<string> createdFiles = new List<string>(); // Tracks every file SDLS creates. Used during cleanup

        private List<string> componentNames; // Contains the names of all the "Components" files. Data that doesn't have it's own file, but are needed by qualities or events.
        private Dictionary<string, Dictionary<string, object>> componentCache = new Dictionary<string, Dictionary<string, object>>();
        private bool TilesSpecialCase = false;

        // Dictionary of lists of IDictionaries.
        private Dictionary<string, List<IDictionary<string, object>>> mergedMods = new Dictionary<string, List<IDictionary<string, object>>>();
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
            // Removes all files created by SDLS before closing, because otherwise they would cause problems with Vortex
            // So basically, if a user had SDLS installed, and used Vortex to manage their mods, if they wanted to remove
            // a mod, Vortex would only remove the .sdls files, and not the .json files, which would cause the game to
            // still run them, and the user would have no clue why. 
            if (doCleanup)
            {
                RemoveDirectory("SDLS_MERGED");
                foreach (string file in createdFiles) RemoveJSON(file);
            }
        }

        private void TrashAllJSON()
        {

            string[] filePaths = JSON.GetFilePaths(); // List of all possible moddable files

            componentNames = FindComponents(); // list of each default component

            // Iterate over each subdirectory returned by FileHelper.GetAllSubDirectories()
            // FileHelper.GetAllSubDirectories() is a function provided by the game to list all
            // Subdirectories in addons (A list of all mods)
            foreach (string subDir in FileHelper.GetAllSubDirectories())
            {
                foreach (string filePath in filePaths)
                {
                    string pathTo = Path.Combine("addon", subDir);
                    string fullPath = Path.Combine(pathTo, filePath);

                    string sdlsFile = FileHelper.ReadTextFile(fullPath + ".sdls"); // Attempt to read the file with ".sdls" extension
                    string prefixFile = FileHelper.ReadTextFile(fullPath + "SDLS.json"); // Attempt to read the file with "SDLS.json" extension
                    string fileContent = sdlsFile != null ? sdlsFile : prefixFile; // Pick the available option, favouring .sdls files

                    if (sdlsFile != null && prefixFile != null) // Log a warning if both file types are found, choosing the one with ".sdls" extension
                        Warn("Detected both a .sdlf and a SDLF.json file. The .sdlf file will be used.\nPlease consider removing one of these files to avoid confusion.");

                    if (fileContent != null)
                    {
                        fileContent = fileContent.Substring(1, fileContent.Length - 2); // Remove the [ ] around the string
                        string fileName = GetLastWord(fullPath);

                        DebugTimer("Trash " + fileName);

                        // if (doMerge)
                        // {
                        //     RemoveJSON(fullPath);
                        //     MergeMods(fileContent, filePath);


                        //     string trashedJSON = TrashJSON(fileContent, fileName);
                        //     Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, pathTo));
                        // }
                        // else
                        // {
                        RemoveDirectory("SDLS_MERGED");


                        string trashedJSON = TrashJSON(fileContent, fileName);

                        DebugTimer("Trash " + fileName);

                        DebugTimer("Create " + fileName);
                        CreateJSON(trashedJSON, fullPath);
                        DebugTimer("Create " + fileName);

                        // }
                    }
                }
            }

            foreach (var merged in mergedMods)
            {
                string fileName = merged.Key;
                var JSON = merged.Value;
                List<string> objectList = new List<string>();
                foreach /*Loop through all */ (var str in JSON)
                { // to check
                    if (str != null) // to remove them from the list by only including non-null values
                    {
                        // Randomly stopped working
                        //objectList.Add(JSON.Serialize(str));
                    }
                }

                string trashedJSON = TrashJSON(JoinJSON(objectList), fileName); // Join all JSON together and Trash it like you would any other JSON
                string pathTo = Path.Combine("addon", "SDLS_MERGED"); // Gets output path
                Warn("DO NOT DISTRIBUTE MERGED JSON. IT WILL CONTAIN ALL MODS YOU HAVE INSTALLED, WHICH BELONG TO THEIR RESPECTIVE MOD AUTHORS.");
                CreateJSON(trashedJSON, Path.Combine(pathTo, merged.Key)); // Creates JSON files in a designated SDLS_MERGED output folder
            }
        }

        private string TrashJSON(string providedJSON, string name)
        {
            try
            {
                List<string> objectList = new List<string>(); // List to catch all the JSON

                foreach (string splitString in SplitJSON(providedJSON))
                {
                    var embeddedData = GetAComponent(name); // Deserialize the default mold data
                    var deserializedJSON = JSON.Deserialize(splitString);

                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJSON, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    objectList.Add(JSON.Serialize(returnData));

                    if (TilesSpecialCase) TilesSpecialCase = false;  // Stupid special case for Tiles, check GetComponent for details
                }

                string result = JoinJSON(objectList);
                return result;
            }
            catch (Exception ex)
            {
                Error($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return providedJSON; // Return providedJSON as fallback
            }
        }

        private IDictionary<string, object> ApplyFieldsToMold(
            IDictionary<string, object> JSONData,
            IDictionary<string, object> moldData,
            IDictionary<string, object> mergeData = null
            )
        {
            var mold = new Dictionary<string, object>(moldData);

            foreach (var kvp in JSONData)
            {
                var basin = kvp.Key; // Name of key, Eg UseEvent
                var crucible = kvp.Value; // Value of key, Eg 500500
                                          // var mergeCrucible = null
                                          // if (mergeData != null)
                                          // {
                                          //     if (mergeData.ContainsKey(basin))
                                          //     {
                                          //         mergeCrucible = mergeData[basin];
                                          //         Logger.LogWarning(mergeCrucible);
                                          //     }
                                          // }

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
            foreach (var item in array)
            {
                newArray.Add(item is IDictionary<string, object> itemDict
                    ? HandleObject(itemDict, fieldName)
                    : item);
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


        private void MergeMods(string inputData, string name) // Running this will add inputData to the mergedMods registry
        {
            var embeddedData = GetAComponent(name);

            // Creates entries for mod in a dictionary that can be referenced later.
            // Names like entities/events, which will be used later to create JSON files in the correct locations.
            if (!mergedMods.ContainsKey(name))
            {
                mergedMods[name] = new List<IDictionary<string, object>>(); // A list of dictionaries, with each dictionary being a JSON object
            }

            foreach (string splitString in SplitJSON(inputData)) // Adds each JSON object to the Mergemods name category
            {
                var deserializedJSON = JSON.Deserialize(splitString);

                int Id = (int)deserializedJSON["Id"];
                while (mergedMods[name].Count <= Id) mergedMods[name].Add(null); // Makes sure the list expands to acommodate all Id entries

                mergedMods[name][Id] ??= deserializedJSON; // Add the inputObject to the registry if there is no entry there
                if (mergedMods[name][Id] != null) // Begin the merging process if an object is already present.
                {
                    mergedMods[name][Id] = ApplyFieldsToMold(mergedMods[name][Id], embeddedData, deserializedJSON);
                }
            }

            // Nothing happens at the end of this function, as this function only runs inside of a foreach loop
            // and once it concludes other code begins to run, which will create and save the JSON.
        }

        private string[] SplitJSON(string JSONText) // Splits JSON objects from eachother, so they can be processed individually
        {
            var objects = new List<string>();

            int depth = 0;
            int startIndex = 0;
            bool isInObject = false; // Flag to track if inside a JSON object

            for (int i = 0; i < JSONText.Length; i++)
            {
                char currentChar = JSONText[i];

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
                    objects.Add(JSONText.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }

            // Add the last JSON object
            objects.Add(JSONText.Substring(startIndex));

            return objects.ToArray();
        }

        private string JoinJSON(List<string> list) // Rejoins all JSON objects
        {
            return string.Join(",", list.ToArray());
        }

        private string GetLastWord(string str)
        {
            string result = str.Split(new char[] { '/', '\\' }).Last(/* Returns only the resource name */);
            return result;
        }

        string GetParentPath(string fullPath)
        {
            // Find the last index of directory separator
            int lastIndex = fullPath.LastIndexOfAny(new char[] { '/', '\\' });

            if (lastIndex == -1) return ""; // If no directory separator is found, return an empty string

            // Return the substring from the start of the string up to the last directory separator
            return fullPath.Substring(0, lastIndex + 1);
        }

        private void CreateJSON(string trashedJSON, string path) // Creates JSON out of plaintext, and puts them in the addon folder next to the original
        {
            string writePath = Path.Combine(Application.persistentDataPath, path) + ".json";
            string actualPath = GetParentPath(writePath);

            try
            {
                // Create the directory if it doesn't exist
                if (!Directory.Exists(actualPath))
                {
                    Directory.CreateDirectory(actualPath);
                }

                File.WriteAllText(writePath, "[" + trashedJSON + "]");
                Log("Created new file at " + path);
                createdFiles.Add(path);
            }
            catch (Exception ex)
            {
                Error("Error writing file: " + ex.Message);
            }
        }

        private void RemoveJSON(string filePath)
        {
            string targetPath = Application.persistentDataPath + "/";
            string JSONFilePath = targetPath + filePath + ".json";
            if (File.Exists(JSONFilePath))
            {
                Warn("Removing " + filePath);
                File.Delete(JSONFilePath); // Delete any files that exist, that might screw up SDLS merge loading
            }
        }

        private void RemoveDirectory(string path) // Removes any directory in Addon
        {
            string pathToDelete = Path.Combine(Application.persistentDataPath, Path.Combine("addon", path));

            try
            {
                Directory.Delete(pathToDelete, true);
                Warn("Removing " + pathToDelete);
            }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex)
            {
                Error($"Error deleting directory: {ex.Message}");
            }
        }

        private string JSONAsText(string resourceName, string folderName) // Method for loading embedded JSON resources
        {
            try
            {
                string name = GetLastWord(resourceName);
                string fullPath = GetEmbeddedPath(folderName);
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

        private string ReadTextResource(string resource)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Warn("Tried to get resource that doesn't exist: " + resource);
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
            if (!componentCache.ContainsKey(name))
            {
                componentCache[name] = JSON.Deserialize(JSONAsText(name, "default"));
            }
            else
            {
                // So the deal is, Tiles.json contains a field called Tiles, spelled exactly the same.
                // So if we request the component, there is no way to know which one it requests.
                // We get around this by checking whether we are currently handling the Tiles.json object,
                // And if we are, return TilesTiles instead, since TilesTiles is only used inside of Tiles.json
                // We set TilesSpecialCase to false after we handle the Tiles object.

                if (name == "Tiles" && TilesSpecialCase) // If Tiles is requested, but TilesSpecialCase is true, it means it has been requested before in this instance, meaning it should 
                {
                    name = "TilesTiles"; // Set the name to TilesTiles to return the correct component
                    if (!componentCache.ContainsKey("TilesTiles")) // If the TilesTiles component hasn't been added, add it.
                    {
                        componentCache[name] = JSON.Deserialize(JSONAsText(name, "default"));
                    }
                }
            }

            if (name == "Tiles") TilesSpecialCase = true; // Set special case to true after the first time Tiles has been requested and returned
            return componentCache[name];
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
                doCleanup = bool.Parse(optionsDict["doCleanup"]);
            }
            catch (Exception)
            {
                LoadConfig(true); // Load config with default values
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
                    DLog($"Finished {name}. Took {DebugTimers[name].Elapsed.TotalSeconds:F3} seconds.");
                }
                else
                { // Removes the timer and starts it again
                    DebugTimers.Remove(name);
                    DebugTimer(name);
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
            "\nCan you guess where the JSON goes?\nThat's right!\n It goes in the square hole!",
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