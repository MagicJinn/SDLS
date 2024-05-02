using BepInEx;
using UnityEngine;
//using BepInEx.Logging;
using HarmonyLib;
using JsonFx.Json;
using FailBetter.Core;
using Sunless.Game.Utilities;
using Mono.Cecil;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using FailBetter.Core.Result;

namespace SDLS
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInProcess("Sunless Sea.exe")]
    public class Plugin : BaseUnityPlugin
    {
        private JsonReader jsonReader;
        //private JsonWriter jsonWriter;

        private List<string> components;
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            jsonReader = new JsonReader();
            //jsonWriter = new JsonWriter();

            TrashAllJson();
        }

        private void TrashAllJson()
        {
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
            foreach (string component in components)
            {
                Logger.LogInfo(component);
            }

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
                    Logger.LogInfo(subDir + " " + filePath);
                    if (fileContent != null)
                    {
                        fileContent = fileContent.Substring(1, fileContent.Length - 2); // Remove the [ ] around the string
                        string trashedJson = TrashJson(fileContent, GetLastWord(fullPath));
                        CreateJson(trashedJson, fullPath);
                    }
                }
            }

        }

        public string TrashJson(string providedJson, string name)
        {
            try
            {
                // Deserialize the provided JSON
                String[] splitStrings = SplitJson(providedJson);
                List<string> objectList = new List<string>();
                foreach (String splitString in splitStrings)
                {
                    var deserializedJson = DeserializeJson(splitString);
                    // Deserialize the default mold data
                    string directory = components.Contains(name) ? "defaultComponents" : "default";
                    var embeddedData = DeserializeJson(JsonAsText(name, directory));
                    // Apply each field found in the deserialized JSON to the mold data recursively
                    var returnData = ApplyFieldsToMold(deserializedJson, embeddedData);

                    // Serialize the updated mold data back to JSON string
                    objectList.Add(Serializer.Serialize(returnData));
                }

                string result = string.Join(",", objectList.ToArray());
                //Logger.LogWarning(result);
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error occurred while processing JSON for '{name}': {ex.Message}");
                return providedJson; // Return providedJson as fallback
            }
        }

        private IDictionary<string, object> ApplyFieldsToMold(IDictionary<string, object> jsonData, IDictionary<string, object> mold)
        {
            Dictionary<string, object> moldData = new Dictionary<string, object>(mold);
            foreach (var kvp in jsonData)
            {
                var fieldName = kvp.Key;
                var fieldValue = kvp.Value;

                // Check if the field exists in the components list
                if (components.Contains(fieldName))
                {
                    // If the field is an array, create multiple molds based on the number of items
                    if (fieldValue is IEnumerable<object> array)
                    {
                        var newArray = new List<object>();
                        foreach (var item in array)
                        {
                            if (item is IDictionary<string, object> || item is IEnumerable<KeyValuePair<string, object>>)
                            {
                                var newMoldItem = DeserializeJson(JsonAsText(fieldName, "defaultComponents"));
                                var result = ApplyFieldsToMold(item as IDictionary<string, object>, newMoldItem);
                                newArray.Add(result);
                            }
                            else
                            {
                                newArray.Add(item); // Handles array with only 1 non-dictionary item
                            }
                        }

                        moldData[fieldName] = newArray;
                    }
                    else // If the field is not an array, apply the mold data directly
                    {
                        if (fieldValue != null)
                        {
                            var newMoldItem = DeserializeJson(JsonAsText(fieldName, "defaultComponents"));
                            var result = ApplyFieldsToMold(fieldValue as IDictionary<string, object>, newMoldItem);
                            moldData[fieldName] = result;
                        }
                    }

                }
                else // If the field is not found in the components list, apply the original field value
                {
                    // If the field is a nested object, recursively apply its fields
                    if (fieldValue is IDictionary<string, object> nestedJson)
                    {
                        var nestedMoldItem = new Dictionary<string, object>();
                        var result = ApplyFieldsToMold(nestedJson, nestedMoldItem);
                        moldData[fieldName] = result;
                    }
                    // If the field is an array, apply each item's fields recursively
                    else if (fieldValue is IEnumerable<object> array)
                    {
                        var newArray = new List<object>();
                        foreach (var item in array)
                        {
                            if (item is IDictionary<string, object> nestedJsonItem)
                            {
                                var newMoldItem = new Dictionary<string, object>();
                                var result = ApplyFieldsToMold(nestedJsonItem, newMoldItem);
                                newArray.Add(result);
                            }
                        }
                        moldData[fieldName] = newArray;
                    }
                    // If the field is a scalar value, update the mold data with the field value
                    else
                    {
                        moldData[fieldName] = fieldValue;
                    }
                }
            }
            return moldData;
        }

        public string[] SplitJson(string jsonText)
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





        private Dictionary<string, object> DeserializeJson(string jsonText)
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
            Assembly assembly = Assembly.GetExecutingAssembly();
            string fullPath = GetEmbeddedPath(folderName);
            string fullResourceName = $"{fullPath}.{resourceName}.json"; // Construct the full resource name

            using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
            {
                if (stream == null)
                {
                    Logger.LogWarning("JsonAsText tried to get resource that doesn't exits: " + fullResourceName);
                    return null; // Return null if the embedded resource doesn't exist
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd(); // Read and return the embedded resource
                }
            }
        }


        public List<string> GetComponents() // Fetches a list of all files (names) in the defaultComponents folder
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

        private string GetEmbeddedPath(string folderName) // Get the path of embedded resources
        {
            string projectName = Assembly.GetExecutingAssembly().GetName().Name;
            string fullPath = $"{projectName}.{folderName}";
            return fullPath;
        }
    }
}