using Verse;

namespace AutoEquipment
{
    public static class AEDebug
    {
        private static bool? _debugActive;
        public static bool IsActive
        {
            get
            {
                if (!_debugActive.HasValue)
                    _debugActive = AESettings.debugLogging;
                return _debugActive.Value;
            }
        }
        public static void Log(string message)
        {
            if (IsActive) Verse.Log.Message(message);
        }
    }
}
