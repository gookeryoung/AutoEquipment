using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Custom inspector tab for pawns that shows role, context, and gear status.
    /// Also provides lock/override controls.
    /// </summary>
    public class ITab_GearManager : ITab
    {
        private Vector2 scrollPos;
        private float lastHeight;

        public ITab_GearManager()
        {
            labelKey = "AE_Tab";
            size = new Vector2(300f, 450f);
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelPawn as Pawn;
                return pawn != null
                    && pawn.Faction == Faction.OfPlayer
                    && !pawn.IsGhoul; // Don't show tab for ghouls
            }
        }

        protected override void FillTab()
        {
            Pawn pawn = SelPawn as Pawn;
            if (pawn == null) return;

            var comp = pawn.GetComp<CompGearManager>();
            if (comp == null) return;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Rect scrollRect = rect;
            scrollRect.height -= 20f; // Space for lock toggle

            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, lastHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // Title
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // Current role
            Role role = comp.CurrentRole;
            l.Label("AE_CurrentRole".Translate() + ": " + ("AE_Role_" + role).Translate());

            // Current context
            GearContext context = ContextDetector.GetContext(pawn);
            l.Label("AE_CurrentContext".Translate() + ": " + ("AE_Context_" + context).Translate());
            l.Gap();

            // Lock toggle
            l.CheckboxLabeled("AE_LockGear".Translate(), ref comp.locked);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            l.Label("AE_LockGear_Desc".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            l.Gap();

            // Role override
            l.CheckboxLabeled("AE_OverrideRole".Translate(), ref comp.overrideRole);
            if (comp.overrideRole)
            {
                l.Gap(4f);
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (Role r in System.Enum.GetValues(typeof(Role)))
                {
                    Role localRole = r;
                    options.Add(new FloatMenuOption(
                        ("AE_Role_" + r).Translate(),
                        () => comp.manualRole = localRole));
                }
                if (l.ButtonText("AE_Role".Translate() + ": " + ("AE_Role_" + comp.manualRole).Translate()))
                {
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            l.GapLine();

            // Current equipment
            l.Label("AE_PrimaryWeapon".Translate() + ": "
                + (pawn.equipment?.Primary?.LabelShort ?? "AE_None".Translate()));

            // Sidearm
            if (comp.sidearm != null)
            {
                l.Label("AE_Sidearm".Translate() + ": " + comp.sidearm.LabelShort);
            }

            l.GapLine();

            // Worn apparel summary
            if (pawn.apparel?.WornApparel != null)
            {
                l.Label("AE_WornApparel".Translate() + " (" + pawn.apparel.WornApparel.Count + "):");
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    l.Label("  - " + apparel.LabelShort);
                }
            }

            l.End();

            lastHeight = contentRect.height;

            Widgets.EndScrollView();
        }
    }
}
