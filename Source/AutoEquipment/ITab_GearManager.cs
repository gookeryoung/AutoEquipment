using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Pawn 检视面板的自定义标签页：展示角色、情境与装备状态，
    /// 并提供锁定/角色覆盖控制。食尸鬼不显示此面板。
    /// </summary>
    public class ITab_GearManager : ITab
    {
        private Vector2 scrollPos;
        // 初始高度需大于 0：ScrollView 的内容区高度若为 0，第一次绘制时所有控件被裁剪不可见
        // 用一个足够大的初始值确保首帧即显示，之后每次 FillTab 都会用真实高度刷新
        private float lastHeight = 1500f;

        public ITab_GearManager()
        {
            labelKey = "AE_Tab";
            // 面板尺寸：高度限制在屏幕可见范围，超出部分由 ScrollView 滚动
            // 340x640 适配大多数分辨率，避免超出底部消息栏区域
            size = new Vector2(340f, 640f);
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

            // 按钮固定在面板最下方，独立于 ScrollView，避免滚动时被隐藏
            float buttonHeight = 30f;
            float buttonGap = 10f;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Rect scrollRect = rect;
            scrollRect.height -= (buttonHeight + buttonGap);

            // 内容高度按需扩展，ScrollView 会滚动
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, lastHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

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

            // 副武器
            if (comp.sidearm != null)
            {
                l.Label("AE_Sidearm".Translate() + ": " + comp.sidearm.LabelShort);
            }

            l.GapLine();

            // 已穿戴防具摘要
            if (pawn.apparel?.WornApparel != null)
            {
                l.Label("AE_WornApparel".Translate() + " (" + pawn.apparel.WornApparel.Count + "):");
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    l.Label("  - " + apparel.LabelShort);
                }
            }

            // ===================== 底部：全局重配规则（直接列出，无需打开新窗口） =====================
            l.GapLine();
            Text.Font = GameFont.Medium;
            l.Label("AE_ReallocRules_Title".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // ---- 自定义评级识别码（当前 Pawn） ----
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
                            // 当前 Pawn 显示立即刷新
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

            // ---- 战斗价值公式 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CombatValue".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            l.Label("AE_ReallocRules_CombatValue_Formula".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            // 兴趣乘数（3 个 Slider）
            l.Label("AE_ReallocRules_PassionMult".Translate());
            l.Label("  " + "AE_ReallocRules_PassionNone".Translate() + ": " + AESettings.cvPassionNoneMult.ToString("F1"));
            AESettings.cvPassionNoneMult = l.Slider(AESettings.cvPassionNoneMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMinor".Translate() + ": " + AESettings.cvPassionMinorMult.ToString("F1"));
            AESettings.cvPassionMinorMult = l.Slider(AESettings.cvPassionMinorMult, 0.5f, 3.0f);
            l.Label("  " + "AE_ReallocRules_PassionMajor".Translate() + ": " + AESettings.cvPassionMajorMult.ToString("F1"));
            AESettings.cvPassionMajorMult = l.Slider(AESettings.cvPassionMajorMult, 0.5f, 3.0f);
            l.Gap(4f);

            // 技能权重
            l.Label("AE_ReallocRules_SkillWeight".Translate() + ": " + AESettings.cvSkillWeight.ToString("F2"));
            AESettings.cvSkillWeight = l.Slider(AESettings.cvSkillWeight, 0.5f, 2.0f);
            l.Gap(4f);

            // ---- 特质加分 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_TraitBonus".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_TraitBonus_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            l.Label("  " + "AE_ReallocRules_Tough".Translate() + ": " + AESettings.cvToughBonus.ToString("F0"));
            AESettings.cvToughBonus = l.Slider(AESettings.cvToughBonus, -50f, 100f);
            l.Label("  " + "AE_ReallocRules_TriggerHappy".Translate() + ": " + AESettings.cvTriggerHappyPenalty.ToString("F0"));
            AESettings.cvTriggerHappyPenalty = l.Slider(AESettings.cvTriggerHappyPenalty, -50f, 50f);
            l.Label("  " + "AE_ReallocRules_CarefulShooter".Translate() + ": " + AESettings.cvCarefulShooterBonus.ToString("F0"));
            AESettings.cvCarefulShooterBonus = l.Slider(AESettings.cvCarefulShooterBonus, -50f, 100f);
            l.Gap(4f);

            // ---- 保护规则 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_ProtectSection".Translate());
            GUI.color = Color.white;
            l.CheckboxLabeled("AE_ReallocRules_DropWeapons".Translate(), ref AESettings.reallocateDropWeapons);
            l.CheckboxLabeled("AE_ReallocRules_RespectDrafted".Translate(), ref AESettings.reallocateRespectDrafted);
            l.CheckboxLabeled("AE_ReallocRules_RespectLocked".Translate(), ref AESettings.reallocateRespectLocked);
            l.CheckboxLabeled("AE_ReallocRules_RespectBiocoded".Translate(), ref AESettings.reallocateRespectBiocoded);
            l.Gap(4f);

            // ---- 护甲偏好规则 ----
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_ArmorPrefSection".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_ArmorPref_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // 当前 Pawn 的护甲偏好
            ArmorPreference curPref = RoleDetector.GetArmorPreference(comp.CurrentRole);
            l.Label("AE_ReallocRules_CurrentArmorPref".Translate() + ": " + ("AE_ArmorPref_" + curPref).Translate());
            l.Gap(4f);

            l.CheckboxLabeled("AE_ReallocRules_ReallocApparel".Translate(), ref AESettings.reallocateApparel);
            l.Label("  " + "AE_ReallocRules_HeavyThreshold".Translate() + ": " + AESettings.heavyArmorSharpThreshold.ToString("F2"));
            AESettings.heavyArmorSharpThreshold = l.Slider(AESettings.heavyArmorSharpThreshold, 0.1f, 1.0f);
            l.Label("  " + "AE_ReallocRules_HeavyPenaltyForLight".Translate() + ": " + AESettings.heavyArmorPenaltyForLight.ToString("F0"));
            AESettings.heavyArmorPenaltyForLight = l.Slider(AESettings.heavyArmorPenaltyForLight, -2000f, 0f);
            l.Label("  " + "AE_ReallocRules_LightPenaltyForHeavy".Translate() + ": " + AESettings.lightArmorPenaltyForHeavy.ToString("F0"));
            AESettings.lightArmorPenaltyForHeavy = l.Slider(AESettings.lightArmorPenaltyForHeavy, -2000f, 0f);

            l.End();

            // 记录内容高度，供下次绘制使用
            // 用 Mathf.Max 防止切换开关（如 overrideRole）导致高度突然变小，
            // 让 ScrollView 临时裁剪内容看起来"消失"。下一次绘制时会按真实高度撑开。
            lastHeight = Mathf.Max(lastHeight, l.CurHeight + 20f);

            Widgets.EndScrollView();

            // 全局重配按钮：固定面板最下方，占满宽度
            Rect buttonRect = new Rect(
                scrollRect.x,
                scrollRect.yMax + buttonGap,
                scrollRect.width,
                buttonHeight);

            if (Widgets.ButtonText(buttonRect, "AE_GlobalReallocate".Translate()))
            {
                int triggered = GlobalAllocator.ReallocateAll();
                Messages.Message(
                    "AE_GlobalReallocateResult".Translate(triggered),
                    MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}
