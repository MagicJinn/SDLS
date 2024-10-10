using UnityEngine;


namespace SDLS
{
    static class Logging
    {
        // Simplified log functions
        public static void Log(object message) { Debug.Log(message); }
        public static void Warn(object message) { Debug.LogWarning(message); }
        public static void Error(object message) { Debug.LogError(message); }
#if DEBUG
        // Log functions that don't run when built in Release mode
        public static void DLog(object message) { Log(message); }
        public static void DWarn(object message) { Warn(message); }
        public static void DError(object message) { Error(message); }
#else
        // Empty overload methods to make sure the plugin doesn't crash when built in release mode
        private static void DLog(object message) { }
        private static void DWarn(object message) { }
        private static void DError(object message) { }
#endif
    }
}