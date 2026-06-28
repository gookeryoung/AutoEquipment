using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// All Harmony patches for Auto Equipment mod.
    /// Patches:
    /// 1) Add CompGearManager to all pawns on game load
    /// 2) Restore primary weapon when pawn is undrafted
    /// </summary>
    public static class HarmonyPatches
    {
        public const string HarmonyID = "autoequipment.mod";

        public static void Init()
        {
            var harmony = new Harmony(HarmonyID);
            harmony.PatchAll();
            Log.Message("[AutoEquipment] Harmony patches applied");
        }

        /// <summary>
        /// Add CompGearManager to all ThingDefs that are pawns.
        /// Runs at game start to inject our component without XML patches.
        /// </summary>
        [HarmonyPatch(typeof(Verse.Game), "InitNewGame")]
        public static class Game_InitNewGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
            }
        }

        [HarmonyPatch(typeof(ScribeLoader), "LoadGame")]
        public static class ScribeLoader_LoadGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
            }
        }

        private static bool _compAdded;
        private static void AddCompToPawnDefs()
        {
            if (_compAdded) return;
            _compAdded = true;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.category != ThingCategory.Pawn) continue;
                if (def.comps == null) continue;

                bool hasComp = false;
                foreach (var comp in def.comps)
                {
                    if (comp is CompProperties_GearManager)
                    {
                        hasComp = true;
                        break;
                    }
                }

                if (!hasComp)
                {
                    def.comps.Add(new CompProperties_GearManager());
                }
            }
        }

        /// <summary>
        /// When a pawn is undrafted, restore their primary weapon if a sidearm was drawn.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_DraftController), "SetDrafted")]
        public static class DraftController_SetDrafted_Patch
        {
            static void Postfix(Pawn_DraftController __instance, bool drafted)
            {
                if (drafted) return;
                Pawn pawn = __instance.pawn;
                if (pawn == null) return;
                // FIX: Ghouls don't use CompGearManager, skip them
                if (pawn.IsGhoul) return;

                var comp = pawn.GetComp<CompGearManager>();
                if (comp != null)
                {
                    try { comp.OnUndraft(); }
                    catch (System.Exception ex)
                    {
                        Log.Warning("[AutoEquipment] OnUndraft error for " + pawn.LabelShort + ": " + ex.Message);
                    }
                }
            }
        }
    }
}
