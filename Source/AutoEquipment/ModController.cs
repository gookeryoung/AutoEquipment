using Verse;
using RimWorld;

namespace AutoEquipment
{
    /// <summary>
    /// Bootstrap class that initializes the mod when the game starts.
    /// RimWorld calls the static constructor automatically on game load.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModController
    {
        static ModController()
        {
            HarmonyPatches.Init();
            Log.Message("[AutoEquipment] Mod initialized");
        }
    }
}
