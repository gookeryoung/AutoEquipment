using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEverything.Core
{
    public class AutoEverythingMod : Mod
    {
        public static AESettings settings;

        public AutoEverythingMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AESettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            AESettings.DrawSettings(inRect);
        }

        public override string SettingsCategory() => "AE_SettingsCategory".Translate();
    }
}
