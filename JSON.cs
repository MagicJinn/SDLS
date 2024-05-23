using JsonFx.Json;
using System.Collections.Generic;
using Sunless.Game.Utilities;

namespace SDLS
{
        public static class JSON
        {
                private static JsonReader JSONReader;
                private static JsonWriter JSONWriter;

                public static void PrepareJSONManipulation()
                {
                        JSONReader = new JsonReader();
                        JSONWriter = new JsonWriter();
                }

                public static string ReadGameJson(string relativeFilePath)
                {
                        string str = FileHelper.ReadTextFile(relativeFilePath);
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
                        Dictionary<string, object> deserializedData = JSONReader.Read<Dictionary<string, object>>(strObj);
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

                public static string JoinJSON(List<string> strList, string sign = ",") // Rejoins all JSON objects
                {
                        return string.Join(sign, strList.ToArray());
                }

                public static string[] GetFilePaths()
                {
                        return new string[] { // All possible files able to be modded (with .json removed)
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

                public static string[] ComponentsWithoutId()
                {
                        return new string[] {
                                "geography/TileRules",
                                "geography/Tiles",
                                "geography/TileSets",
                                "encyclopaedia/CombatAttacks",
                                "encyclopaedia/CombatItems",
                                "encyclopaedia/SpawnedEntities",
                                "encyclopaedia/Associations",
                                "encyclopaedia/Flavours",
                                "constants/combatconstants",
                                "constants/navigationconstants"
                        };
                }
        }
}
