using BepInEx;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using JsonFx.Json;
using Sunless.Game.Utilities;
using Sunless.Game.ApplicationProviders;

namespace SDLS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private string gameName;

        // Config options
        private const string CONFIG = "SDLS_Config.ini";
        private bool fMode; private float fChance;

        private bool doMerge;
        private bool logConflicts;

        private JsonReader jsonReader; private JsonWriter jsonWriter;
        private List<string> components; // Contains the names of all the "Components" files. Data that doesn't have it's own file, but are needed by qualities or events.

        private IDictionary<int, object> mergedMods = new Dictionary<int, object>();

        private void Awake()
        {
            GetGameName();

            LoadConfig();

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            jsonReader = new JsonReader(); jsonWriter = new JsonWriter();

            TrashAllJson();
        }

        private void TrashAllJson()
        {
            InitializationLine();

            string[] filePaths = { // All possible files able to be modded (with .json removed)
            "entities/qualities",
            "entities/areas",
            "entities/events",
            "entities/exchanges",
            "entities/personas",
            "geography/TileRules",
            "geography/Tiles",
            "geography/TileSets",
            "encyclopaedia/CombatAttacks",
            "encyclopaedia/CombatItems",
            "encyclopaedia/SpawnedEntities",
            "encyclopaedia/Associations",
            "encyclopaedia/Tutorials",
            "encyclopaedia/Flavours"
            };

            components = GetComponents();

            // Iterate over each subdirectory returned by FileHelper.GetAllSubDirectories()
            // FileHelper.GetAllSubDirectories() is a function provided by the game to list all
            // Subdirectories in addons (A list of all mods)
            foreach (string subDir in FileHelper.GetAllSubDirectories())
            {
                // Concatenate each file path with the directory path and read the file
                foreach (string filePath in filePaths)
                {
                    string fullPath = "addon/" + subDir + "/" + filePath;

                    // Attempt to read the file with ".sdls" extension
                    string sdlsFile = FileHelper.ReadTextFile(fullPath + ".sdls");
                    // Attempt to read the file with ".SDLS.json" extension
                    string prefixFile = FileHelper.ReadTextFile(fullPath + "SDLS.json");

                    // Choose the content based on availability of files
                    string fileContent = sdlsFile != null ? sdlsFile : prefixFile;

                    // Log a warning if both file types are found, choosing the one with ".sdls" extension
                    if (sdlsFile != null && prefixFile != null)
                    {
                        Logger.LogWarning("Detected both a .sdlf and a SDLF.json file. The .sdlf file will be used.");
                        Logger.LogWarning("Please consider removing one of these files to avoid confusion.");
                    }
                    if (fileContent != null)
                    {
                        fileContent = fileContent.Substring(1, fileContent.Length - 2); // Remove the [ ] around the string
                        string trashedJson = TrashJson(fileContent, GetLastWord(fullPath));
                        if (!doMerge)
                        {
                            CreateJson(trashedJson, fullPath);
                        }
                        else
                        {
                            string targetPath = Application.persistentDataPath + "/";
                            string jsonFilePath = targetPath + fullPath + ".json";

                            if (File.Exists(jsonFilePath))
                            {
                                Logger.LogWarning("Removing " + filePath + " before merging mods.");
                                File.Delete(jsonFilePath); // Delete any files that exist, that might screw up SDLS merge loading
                            }
                        }
                    }
                }
            }
        }

        private string TrashJson(string providedJson, string name)
        {
            try
            {
                // Deserialize the provided JSON
                String[] splitStrings = SplitJson(providedJson);
                List<string> objectList = new List<string>();
                foreach (String splitString in splitStrings)
                {
                    var deserializedJson = Deserialize(splitString);
                    // Deserialize the default mold data
                    string directory = components.Contains(name) ? "defaultComponents" : "default";
                    var embeddedData = Deserialize(JsonAsText(name, directory));
                    if (doMerge)
                    {
                        MergeMods(deserializedJson, mergedMods);
                        continue;
                    } // Rest is skipped

                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJson, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    objectList.Add(Serialize(returnData));
                }

                if (doMerge)
                {
                    return null;
                } // The rest is skipped

                string result = string.Join(",", objectList.ToArray());
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return providedJson; // Return providedJson as fallback
            }
        }

        private IDictionary<string, object> ApplyFieldsToMold(IDictionary<string, object> jsonData, IDictionary<string, object> moldData)
        {
            var mold = new Dictionary<string, object>(moldData);
            foreach (var kvp in jsonData)
            {
                var basin = kvp.Key;
                var crucible = kvp.Value;

                // Check if the field exists in the components list
                if (components.Contains(basin))
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
            var newMoldItem = Deserialize(JsonAsText(fieldName, "defaultComponents"));
            return fieldValue is IDictionary<string, object> fieldValueDict
                ? ApplyFieldsToMold(fieldValueDict, newMoldItem)
                : fieldValue;
        }

        private object HandleDefault(object fieldValue)
        {
            return fieldValue is IDictionary<string, object> nestedJson
                ? ApplyFieldsToMold(nestedJson, new Dictionary<string, object>())
                : fieldValue is IEnumerable<object> array
                    ? array.Select(HandleDefault).ToList()
                    : fieldValue;
        }

        private void MergeMods(IDictionary<string, object> inputData, IDictionary<int, object> destination)
        {
            int Id = (int)inputData["Id"];
            Logger.LogInfo("Id: " + Id);
            if (!destination.ContainsKey(Id))
            {
                destination.Add(Id, inputData);
            }
            else
            {
                Logger.LogWarning("AAAA");
            }

        }

        private string[] SplitJson(string jsonText) // Splits json objects from eachother, so they can be processed individually
        {
            var objects = new List<string>();

            int depth = 0;
            int startIndex = 0;
            bool isInObject = false; // Flag to track if inside a JSON object

            for (int i = 0; i < jsonText.Length; i++)
            {
                char currentChar = jsonText[i];

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
                    objects.Add(jsonText.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }

            // Add the last JSON object
            objects.Add(jsonText.Substring(startIndex));

            return objects.ToArray();
        }

        private string Serialize(object data)
        {
            return new JsonWriter().Write(data);
        }

        private Dictionary<string, object> Deserialize(string jsonText)
        {
            return jsonReader.Read<Dictionary<string, object>>(jsonText);
        }

        private string GetLastWord(string str)
        {
            return str.Split('/').Last(/* Returns only the resource name */);

        }

        private void CreateJson(string trashedJson, string path) // Creates json out of plaintext, and puts them in the addon folder next to the original
        {
            string targetPath = Application.persistentDataPath + "/" + path;
            try
            {
                Logger.LogInfo("Created new file at " + path + ".json");
                File.WriteAllText(targetPath + ".json", "[" + trashedJson + "]");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error writing file: " + ex.Message);
            }
        }

        private string JsonAsText(string resourceName, string folderName) // Method for loading embedded json resources
        {

            string fullPath = GetEmbeddedPath(gameName + "." + folderName);
            string fullResourceName = $"{fullPath}.{resourceName}.json"; // Construct the full resource name

            return ReadTextResource(fullResourceName);
        }

        private string ReadTextResource(string resource)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Logger.LogWarning("Tried to get resource that doesn't exist: " + resource);
                    return null; // Return null if the embedded resource doesn't exist
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd(); // Read and return the embedded resource
                }
            }
        }

        private List<string> GetComponents() // Fetches a list of all files (names) in the defaultComponents folder
        {
            string embeddedPath = GetEmbeddedPath("defaultComponents");
            var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            var components = new List<string>();
            foreach (var name in resourceNames)
            {
                if (name.StartsWith(embeddedPath))
                {
                    var component = name.Substring(embeddedPath.Length);
                    component = component.TrimStart('.').Replace(".json", "");
                    components.Add(component);
                }
            }

            return components;
        }

        private string GetEmbeddedPath(string folderName = "") // Get the path of embedded resources
        {
            string projectName = Assembly.GetExecutingAssembly().GetName().Name;
            string fullPath = $"{projectName}.{folderName}";
            return fullPath;
        }

        private void GetGameName()
        {
            string currentProcessName = Process.GetCurrentProcess().ProcessName;
            Logger.LogWarning("Current game is: ", currentProcessName);
            gameName = currentProcessName.Replace(" ", "_");
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
                Logger.LogWarning("Config not found or corrupt, using default values.");
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
                fMode = bool.Parse(optionsDict["funnyMode"]); fChance = float.Parse(optionsDict["funnyChance"]);

                doMerge = bool.Parse(optionsDict["mergeMode"]);
                logConflicts = bool.Parse(optionsDict["logMergeConflicts"]);
            }
            catch (Exception)
            {
                LoadConfig(true); // Load config with default values
            }
        }

        private void InitializationLine()
        {
            string[] alternateLines = {
        "Querrying ChatGPT for better Json.",
        "Help! If you're seeing this, I'm the guy he trapped within this program to rewrite your Json!.",
        "This is an alternate line 3.",
        "I'm sorry, but as an AI language model.",
        "Error 404: Humor not found... in your Json.",
        "Adding a lot of useless stuff. Like, a LOT of useless stuff.",
        "Adding a mascot that's more powerful than all the others, found in London, as is tradition.",
        "Compiling Json files into Json files into Json files into Json files into Json files into Json files into Json files...",
        "You better be using .sdls files.",
        "Adding gluten to your Json.",
        "Jason? JASON!",
        "Adding exponentially more data.",
        "Json is honestly just a Trojan Horse to smuggle Javascript into other languages.",
        "In Xanadu did Kubla Khan\nA stately pleasure-dome decree:\nWhere Alph, the sacred river, ran\nThrough caverns measureless to man\n   Down to a sunless sea.",
        "She Simplifying my Data Loading till I Sunless",
        "Screw it. Grok, give me some more jokes for the Json."
            };

            int randomIndex = fMode ? (new System.Random().NextDouble() < fChance ? new System.Random().Next(0, alternateLines.Length) : -1) : -1;
            Logger.LogInfo(randomIndex != -1 ? alternateLines[randomIndex] : "Compiling SDLS files into Json.");
        }
    }
}