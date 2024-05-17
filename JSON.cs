using JsonFx.Json;
using System.Collections.Generic;

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

                public static string Serialize(object data)
                {
                        string serializedData = JSONWriter.Write(data);
                        return serializedData;
                }

                public static Dictionary<string, object> Deserialize(string JSONText)
                {
                        Dictionary<string, object> deserializedData = JSONReader.Read<Dictionary<string, object>>(JSONText);
                        return deserializedData;
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
                                    "encyclopaedia/Flavours"
        };
                }
        }
}
