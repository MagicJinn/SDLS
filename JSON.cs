using System.Text;
using JsonFx.Json;
using System.Collections.Generic;
using System.IO;


namespace SDLS
{
        public static class JSON
        {
                private static readonly JsonReader JSONReader = new JsonReader();
                private static readonly JsonWriter JSONWriter = new JsonWriter();

                public static string ReadGameJson(string fullFilePath)
                {
                        if (!File.Exists(fullFilePath)) return null; // Return null if the file is not found

                        string str = File.ReadAllText(fullFilePath);
                        return (str != null) ?
                        (str.StartsWith("[") && str.EndsWith("]") ?
                         str.Substring(1, str.Length - 2) :
                         str) : // Return the string if the file isn't in an array
                         null; // Return null if the file is empty
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
                        var objects = new List<string>();
                        for (int i = 0, depth = 0, start = 0; i < strObjJoined.Length; i++)
                        {
                                if (strObjJoined[i] == '{' && depth++ == 0) start = i;
                                if (strObjJoined[i] == '}' && --depth == 0) objects.Add(strObjJoined.Substring(start, i - start + 1));
                        }
                        return objects.ToArray();
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

                // public static string[] ComponentsWithoutId() => new[]
                // {
                // "geography/TileRules",
                // "geography/Tiles",
                // "geography/TileSets",
                // "encyclopaedia/CombatAttacks",
                // "encyclopaedia/CombatItems",
                // "encyclopaedia/SpawnedEntities",
                // "encyclopaedia/Associations",
                // "encyclopaedia/Flavours",
                // "constants/combatconstants",
                // "constants/navigationconstants"
                // };
        }
}
