using HarmonyLib;
using Sunless.Game.Data.BaseClasses;
using Sunless.Game.Data;
using FailBetter.Core;
using System.Reflection;

namespace SDLS
{
    internal static class Fixes
    {
        public static void DoMiscFixes()
        {
            Harmony.CreateAndPatchAll(typeof(PatchBaseCollectionRepositoryGet));
        }

        [HarmonyPatch]
        private static class PatchBaseCollectionRepositoryGet
        {
            private static MethodBase TargetMethod() // I do not understand this
            {
                return typeof(BaseCollectionRepository<,>)
                        .MakeGenericType(typeof(int), typeof(Quality))
                            .GetMethod("Get", [typeof(int)]);
            }

            private static bool Prefix(int id, ref object __result, object __instance)
            {
                var instance = __instance as BaseCollectionRepository<int, Quality>;
                RepositoryManager.Instance.Initialise();
                if (instance.Entities.TryGetValue(id, out Quality output))
                {
                    __result = output;
                    return false;
                }

                Plugin.Log($"{id} not found, creating a placeholder.");

                // Create placeholder Quality
                var result = JSON.Deserialize<Quality>(JSON.ReadInternalJson("qualities"));

                // Give it some defaults
                result.Name = $"Quality {id}";
                result.Description = $"This quality was part of a mod that is no longer installed.\r\n{nameof(SDLS)} has replaced it to keep your save from being corrupted.";
                result.Id = id;
                result.Category = FailBetter.Core.Enums.Category.Curiosity;
                result.Nature = FailBetter.Core.Enums.Nature.Thing;

                __result = result;
                return false; // Skip original method
            }
        }
    }
}

