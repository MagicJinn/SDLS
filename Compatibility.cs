using System.Diagnostics;
using UnityEngine;

namespace SDLS
{
    public static class Compatibility
    {
        public static string GetGameName()
        {
            string currentProcessName = Process.GetCurrentProcess().ProcessName;
            return currentProcessName.Replace(" ", "_");
        }

        public static string[] GetFilePaths()
        {
#if !SKIES // Sea Build
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
#else //      Skies build
            return new string[] { // You're the expert here, you gotta figure out how this is gonna look
                                    "qualities",
                                    "areas",
                                    "events",
                                    "exchanges"
        };
#endif
        }
    }
}