using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Pawn 检视面板的自定义标签页：展示角色、情境、装备状态与自定义评级。
    /// 全局重配规则（战斗价值公式、护甲偏好等）移至 Dialog_GlobalReallocate 对话框，
    /// 点击"全局重配"按钮后弹出对话框，确认后才执行重配。
    /// 食尸鬼不显示此面板。
    /// </summary>
    public class ITab_GearManager : ITab
    {
        public ITab_GearManager()
        {
            labelKey = "AE_Tab";
            // 内容精简后无需 ScrollView：高度容纳 Pawn 状态 + 自定义评级 + 按钮
            size = new Vector2(340f, 460f);
        }

        public override bool IsVisible
        {
            get
            {
                // 通过 BasePawn 注入 ITab 时，动物/机械族等也会创建实例
                // 此处过滤：仅玩家阵营人类like 且非食尸鬼才显示
                return SelPawn is Pawn pawn
                    && pawn.Faction == Faction.OfPlayer
                    && PawnSuitabilityChecker.CanManageGear(pawn)
                    && !DLCCompat.IsGhoul(pawn);
            }
        }

        protected override void FillTab()
        {
            if (!(SelPawn is Pawn pawn)) return;

            var comp = pawn.GetComp<CompGearManager>();
            if (comp == null) return;

            // 底部按钮区预留高度
            float buttonHeight = 30f;
            float buttonGap = 10f;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            // 内容区高度 = 总高 - 按钮区
            Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight + buttonGap));

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // ===================== 顶部：当前 Pawn 状态 =====================
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 当前角色（DEBUG 模式下附加评级：系统档始终显示，自定义档写入括号）
            // 格式：[S#王五]（无自定义）或 [S(A)#王五]（有自定义，A 为玩家指定档）
            Role role = comp.CurrentRole;
            string tierSuffix = AEDebug.IsActive
                ? " [" + AEDebug.Label(pawn) + "]"
                : "";
            l.Label("AE_CurrentRole".Translate() + ": " + ("AE_Role_" + role).Translate() + tierSuffix);

            // 当前情境
            GearContext context = ContextDetector.GetContext(pawn);
            l.Label("AE_CurrentContext".Translate() + ": " + ("AE_Context_" + context).Translate());
            l.Gap();

            // 锁定开关
            l.CheckboxLabeled("AE_LockGear".Translate(), ref comp.locked);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            l.Label("AE_LockGear_Desc".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            l.Gap();

            // 角色覆盖
            l.CheckboxLabeled("AE_OverrideRole".Translate(), ref comp.overrideRole);
            if (comp.overrideRole)
            {
                l.Gap(4f);
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (Role r in System.Enum.GetValues(typeof(Role)))
                {
                    // 闭包捕获：必须用局部变量，避免循环变量全部指向最后一个枚举值
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

            // ===================== 中部：当前装备摘要 =====================
            l.Label("AE_PrimaryWeapon".Translate() + ": "
                + (pawn.equipment?.Primary?.LabelShort ?? "AE_None".Translate()));

            if (comp.sidearm != null)
            {
                l.Label("AE_Sidearm".Translate() + ": " + comp.sidearm.LabelShort);
            }

            l.GapLine();

            // ===================== 自定义评级识别码 =====================
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CustomTier".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_CustomTier_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // 显示当前 Pawn 的识别码：系统档固定，自定义档写入括号
            // 格式：S#王五（无自定义）或 S(A)#王五（有自定义）
            string pawnName = SidearmAllocator.GetPawnLookupName(pawn);
            CombatTier autoTier = SidearmAllocator.GetAutoCombatTier(pawn);
            bool hasCustom = AESettings.TryGetCustomTier(pawnName, out CombatTier customTier);

            string tierCode = hasCustom
                ? autoTier + "(" + customTier + ")#" + pawnName
                : autoTier + "#" + pawnName;
            l.Label("AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode);

            // 两按钮并排：设置自定义档次 / 清除自定义
            Rect tierBtnRect = l.GetRect(30f);
            float tierBtnWidth = (tierBtnRect.width - 8f) * 0.5f;
            if (Widgets.ButtonText(new Rect(tierBtnRect.x, tierBtnRect.y, tierBtnWidth, 30f),
                                   "AE_ReallocRules_SetCustomTier".Translate()))
            {
                // 弹出 FloatMenu 选择档次 S/A/B/C/D/X
                List<FloatMenuOption> tierOptions = new List<FloatMenuOption>();
                // 倒序展示：S 在最上
                for (int t = (int)CombatTier.S; t >= (int)CombatTier.X; t--)
                {
                    CombatTier localTier = (CombatTier)t;
                    tierOptions.Add(new FloatMenuOption(
                        localTier + " - " + ("AE_Tier_" + localTier).Translate(),
                        () =>
                        {
                            AESettings.SetCustomTier(pawnName, localTier);
                        }));
                }
                Find.WindowStack.Add(new FloatMenu(tierOptions));
            }
            if (Widgets.ButtonText(new Rect(tierBtnRect.x + tierBtnWidth + 8f, tierBtnRect.y, tierBtnWidth, 30f),
                                   "AE_ReallocRules_ClearCustomTier".Translate()))
            {
                if (hasCustom)
                {
                    AESettings.ClearCustomTier(pawnName);
                }
            }
            l.Gap(4f);

            l.End();

            // ===================== 底部：全局重配按钮 =====================
            // 点击后弹出 Dialog_GlobalReallocate 显示规则，确认后才执行
            Rect buttonRect = new Rect(
                rect.x,
                contentRect.yMax + buttonGap,
                rect.width,
                buttonHeight);

            if (Widgets.ButtonText(buttonRect, "AE_GlobalReallocate".Translate()))
            {
                Find.WindowStack.Add(new Dialog_GlobalReallocate());
            }
        }
    }
}
