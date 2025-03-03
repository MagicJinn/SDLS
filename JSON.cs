using System;
using System.Text;
using JsonFx.Json;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDLS
{
    internal static class JSON
    {
        private static readonly JsonReader JSONReader = new JsonReader();
        private static readonly JsonWriter JSONWriter = new JsonWriter();

        public static string ReadFileSystemJson(string fullFilePath)
        {
            if (!File.Exists(fullFilePath)) return null; // Return null if the file is not found

            string str = File.ReadAllText(fullFilePath);
            return (str != null) ?
            (str.StartsWith("[") && str.EndsWith("]") ?
             str.Substring(1, str.Length - 2) :
             str) : // Return the string if the file isn't in an array
             null; // Return null if the file is empty
        }

        public static string ReadInternalJson(string resourceName) // Method for loading embedded JSON resources
        {
            try
            {
                string name = Plugin.GetLastWord(resourceName);
                string fullPath = Plugin.GetEmbeddedPath("default");
                string fullResourceName = $"{fullPath}.{name}.json"; // Construct the full resource name
                return Plugin.ReadTextResource(fullResourceName);
            }
            catch (Exception ex)
            {
                Plugin.Instance.Error("Something went seriously wrong and SDLS was not able to load it's own embedded resource.");
                Plugin.Instance.Error(ex.Message);
                return "";
            }
        }

        public static string Serialize(object obj)
        {
            string serializedData = JSONWriter.Write(obj);
            return serializedData;
        }

        public static Dictionary<string, object> Deserialize(string strObj)
        {
            var deserializedData = JSONReader.Read<Dictionary<string, object>>(strObj);
            return deserializedData;
        }

        public static string[] SplitJSON(string strObjJoined)
        {
            var jsonObjects = new List<string>();
            for (int i = 0, depth = 0, start = 0; i < strObjJoined.Length; i++)
            {
                if (strObjJoined[i] == '{' && depth++ == 0) start = i;
                if (strObjJoined[i] == '}' && --depth == 0) jsonObjects.Add(strObjJoined.Substring(start, i - start + 1));
            }
            string[] jsonObjectsArray = jsonObjects.ToArray();
            return jsonObjectsArray;
        }

        public static void CreateJSON(string strObjJoined, string relativeWritePath)
        {
            string writePath = Path.Combine(Plugin.PersistentDataPath, relativeWritePath) + ".json";
            string path = Plugin.GetParentPath(writePath);

            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                File.WriteAllText(writePath, relativeWritePath.ToLower().Contains("constants")
                ? strObjJoined  // Output file as a single object
                : $"[{strObjJoined}]"); // Put file in an array
            }
            catch (Exception ex)
            {
                Plugin.Instance.Error("Error writing file: " + ex.Message);
            }
        }



        public static void RemoveJSON(string fullpath)
        {
            string filePath = fullpath + ".json";
            if (File.Exists(filePath))
            {
                Plugin.Instance.Warn("Removing " + fullpath);
                File.Delete(filePath);
            }
        }

        public static string JoinJSON(List<string> strList, string sign = ",")
        {
            if (strList.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < strList.Count - 1; i++)
            {
                sb.Append(strList[i]).Append(sign);
            }
            sb.Append(strList[strList.Count - 1]);
            return sb.ToString();
        }

        public static string[] GetFilePaths() => new[]
        {
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
        "encyclopaedia/Flavours",
        "constants/combatconstants",
        "constants/navigationconstants"
        };
    }
}
