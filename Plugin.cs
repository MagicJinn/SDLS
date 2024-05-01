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
        private JsonWriter jsonWriter;

        private List<string> components;
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            jsonReader = new JsonReader(); jsonWriter = new JsonWriter();

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
                    string fileContent = FileHelper.ReadTextFile(fullPath + ".sdls"); // Read the sdls file
                    if (fileContent != null) // If a file does not exist, TrashJson doesn't run
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
            // Deserialize the provided 
            var deserializedJson = DeserializeJson(providedJson);

            // Deserialize the default mold data
            string directory = components.Contains(name) ? "defaultComponents" : "default";
            var moldData = DeserializeJson(JsonAsText(name, directory));

            // Apply each field found in the deserialized JSON to the mold data recursively
            ApplyFieldsToMold(deserializedJson, moldData);

            // Serialize the updated mold data back to JSON string
            var result = Serializer.Serialize(moldData);

            return result;
        }

        private void ApplyFieldsToMold(IDictionary<string, object> jsonData, IDictionary<string, object> moldData)
        {
            foreach (var kvp in jsonData)
            {
                var fieldName = kvp.Key;
                var fieldValue = kvp.Value;

                // Check if the field exists in the components list
                if (components.Contains(fieldName))
                {
                    Logger.LogInfo("AAAAAAAAAAA");
                    // If the field is an array, create multiple molds based on the number of items
                    if (fieldValue is IEnumerable<object> array)
                    {
                        var newArray = new List<object>();
                        foreach (var item in array)
                        {
                            var newMoldItem = DeserializeJson(JsonAsText(fieldName, "defaultComponents"));
                            ApplyFieldsToMold(item as IDictionary<string, object>, newMoldItem);
                            newArray.Add(newMoldItem);
                        }
                        moldData[fieldName] = newArray;
                    }
                    else // If the field is not an array, apply the mold data directly
                    {
                        var newMoldItem = DeserializeJson(JsonAsText(fieldName, "defaultComponents"));
                        ApplyFieldsToMold(fieldValue as IDictionary<string, object>, newMoldItem);
                        moldData[fieldName] = newMoldItem;
                    }
                }
                else // If the field is not found in the components list, apply the original field value
                {
                    // If the field is a nested object, recursively apply its fields
                    if (fieldValue is IDictionary<string, object> nestedJson)
                    {
                        var nestedMoldItem = new Dictionary<string, object>();
                        ApplyFieldsToMold(nestedJson, nestedMoldItem);
                        moldData[fieldName] = nestedMoldItem;
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
                                ApplyFieldsToMold(nestedJsonItem, newMoldItem);
                                newArray.Add(newMoldItem);
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
        }



        private Dictionary<string, object> DeserializeJson(string jsonText)
        {
            return jsonReader.Read<Dictionary<string, object>>(jsonText);
        }

        private string GetLastWord(string str)
        {
            return str.Split('/').Last(/* Returns only the resource name */);

        }
        private void CreateJson(string trashedJson, string path)
        {
            string targetPath = Application.persistentDataPath + "/" + path;
            try
            {
                File.WriteAllText(targetPath + ".json", "[" + trashedJson + "]");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error writing file: " + ex.Message);
            }
        }

        private string JsonAsText(string resourceName, string folderName)
        {

            Assembly assembly = Assembly.GetExecutingAssembly();
            string fullPath = GetEmbeddedPath(folderName);
            string fullResourceName = $"{fullPath}.{resourceName}Default.json"; // Assuming the resource file extension is always ".json"
                                                                                // Read the embedded resource and return its 

            using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        public List<string> GetComponents()
        {
            Logger.LogInfo("Hoigjaeoigjaeoigjaogejgeoiholyoageu");

            string embeddedPath = GetEmbeddedPath("defaultComponents");
            var resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();

            var components = new List<string>();
            foreach (var name in resourceNames)
            {
                if (name.StartsWith(embeddedPath))
                {
                    var component = name.Substring(embeddedPath.Length);
                    component = component.TrimStart('.').Replace("Default.json", "");
                    components.Add(component);
                }
            }

            return components;
        }

        private string GetEmbeddedPath(string folderName)
        {
            string projectName = Assembly.GetExecutingAssembly().GetName().Name;
            string fullPath = $"{projectName}.{folderName}";
            return fullPath;
        }
    }
}