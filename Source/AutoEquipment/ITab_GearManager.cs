using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Pawn 检视面板的自定义标签页：展示角色、情境、装备状态与自定义评级。
    /// 全局装备重配规则（战斗价值公式、护甲偏好等）移至 Dialog_GlobalReallocate 对话框，
    /// 点击"全局装备重配"按钮后弹出对话框，确认后才执行重配。
    ///
    /// UI 设计：使用带颜色底色的徽章（Badge）区分类别——
    ///   角色：蓝/红/绿/橙/灰等按角色类型区分
    ///   情境：红=战斗、橙=狩猎、青=寒冷、橙红=炎热、蓝=工作、白=日常
    ///   评级：金=S、紫=A、蓝=B、绿=C、灰=D、红=X
    ///   护甲偏好：暗红=重甲[前排]、黄=自由[后排]、绿=轻甲[工人]
    ///
    /// 食尸鬼处理：食尸鬼也显示此面板，展示评级/战斗价值等信息供玩家参考，
    /// 但不参与自动装备分配。面板会显示"食尸鬼"徽章与"不参与自动装备"提示。
    /// </summary>
    public class ITab_GearManager : ITab
    {
        // FillTab 缓存：避免每帧重算角色/情境/评级等
        // 60 tick（约 1 秒）刷新一次；选中的 Pawn 变化时立即刷新
        private const int CacheInterval = 60;
        private static int cacheTick = -1;
        private static int cachedPawnId = -1;
        private static Role cachedRole;
        private static GearContext cachedContext;
        private static CombatTier cachedTier;
        private static ArmorPreference cachedArmorPref;
        private static float cachedCombatValue;
        private static float cachedPawnValue;

        // ScrollView 滚动位置：static 保持位置，切换 Pawn 时不重置
        private static Vector2 scrollPos = Vector2.zero;

        public ITab_GearManager()
        {
            labelKey = "AE_Tab";
            // 高度增加以容纳徽章区与状态摘要
            size = new Vector2(360f, 560f);
        }

        public override bool IsVisible
        {
            get
            {
                // 通过 BasePawn 注入 ITab 时，动物/机械族等也会创建实例
                // 此处过滤：仅玩家阵营人类like 才显示
                // 食尸鬼也允许显示（仅展示评级，不参与装备分配）
                return SelPawn is Pawn pawn
                    && pawn.Faction == Faction.OfPlayer
                    && PawnSuitabilityChecker.CanManageGear(pawn);
            }
        }

        protected override void FillTab()
        {
            if (!(SelPawn is Pawn pawn)) return;

            var comp = pawn.GetComp<CompGearManager>();
            // 食尸鬼可能没有 comp（被排除注入），仍允许显示评级信息
            bool isGhoul = DLCCompat.IsGhoul(pawn);

            // 底部按钮区预留高度：两按钮 + 间隔
            float buttonHeight = 30f;
            float buttonGap = 8f;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            // 内容区高度 = 总高 - 两按钮区
            Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight * 2 + buttonGap * 2));

            // ===================== 缓存计算展示数据 =====================
            // FillTab 每帧调用，角色/情境/评级计算涉及技能与特质查询，缓存 60 tick 避免重复计算
            int tick = Find.TickManager.TicksGame;
            int pawnId = pawn.thingIDNumber;
            if (tick - cacheTick >= CacheInterval || pawnId != cachedPawnId)
            {
                cacheTick = tick;
                cachedPawnId = pawnId;
                // 食尸鬼也可能有 SidearmAllocator.GetCombatTier/ComputeCombatValue，
                // 用于玩家参考其价值（即使不参与分配）
                cachedRole = comp != null ? comp.CurrentRole : RoleDetector.DetectRole(pawn);
                cachedContext = ContextDetector.GetContext(pawn);
                cachedTier = SidearmAllocator.GetCombatTier(pawn);
                cachedArmorPref = RoleDetector.GetArmorPreference(cachedRole);
                cachedCombatValue = SidearmAllocator.ComputeCombatValue(pawn);
                cachedPawnValue = SidearmAllocator.ComputePawnValueScore(pawn);
            }

            Role role = cachedRole;
            GearContext context = cachedContext;
            CombatTier tier = cachedTier;
            ArmorPreference armorPref = cachedArmorPref;
            float combatValue = cachedCombatValue;
            float pawnValue = cachedPawnValue;

            // ===================== ScrollView 包裹内容区 =====================
            // 内部 inner rect 从 (0,0) 开始，宽度比 outer 少 16f 预留滚动条
            float contentHeight = 520f;  // 预估高度并留余量
            Rect innerRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(contentRect, ref scrollPos, innerRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(innerRect);

            // ===================== 顶部标题 =====================
            Text.Font = GameFont.Medium;
            l.Label("AE_TabTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap(4f);

            // ===================== 徽章区：角色 / 情境 / 评级 / 护甲偏好 =====================
            // 食尸鬼额外显示"食尸鬼"徽章，替代护甲偏好（食尸鬼不分配护甲）
            DrawBadgeRow(l, role, context, tier, armorPref, isGhoul);

            l.Gap(4f);

            // ===================== 数值摘要：战斗价值 / 价值评分 =====================
            DrawStatRow(l, combatValue, pawnValue);

            // 食尸鬼提示：不参与自动装备
            if (isGhoul)
            {
                l.Gap(4f);
                GUI.color = new Color(0.95f, 0.4f, 0.3f);
                Text.Font = GameFont.Tiny;
                l.Label("AE_GhoulHint".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            l.GapLine();

            // ===================== 装备摘要 =====================
            DrawEquipmentSummary(l, pawn, comp);

            l.GapLine();

            if (!isGhoul && comp != null)
            {
                // ===================== 锁定 / 角色覆盖（食尸鬼不显示） =====================
                l.CheckboxLabeled("AE_LockGear".Translate(), ref comp.locked);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Text.Font = GameFont.Tiny;
                l.Label("AE_LockGear_Desc".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                l.Gap(2f);

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
            }

            // ===================== 自定义评级识别码（食尸鬼也显示，玩家可参考） =====================
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CustomTier".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_CustomTier_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // 显示当前 Pawn 的识别码：系统档固定，自定义档写入括号
            string pawnName = SidearmAllocator.GetPawnLookupName(pawn);
            CombatTier autoTier = SidearmAllocator.GetAutoCombatTier(pawn);
            bool hasCustom = AESettings.TryGetCustomTier(pawnName, out CombatTier customTier);

            string tierCode = hasCustom
                ? autoTier + "(" + customTier + ")#" + pawnName
                : autoTier + "#" + pawnName;
            l.Label("AE_ReallocRules_CurrentTier".Translate() + ": " + tierCode);

            // 两按钮并排：设置自定义档次 / 清除自定义（食尸鬼也允许设置，供评级展示）
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
            Widgets.EndScrollView();

            // ===================== 底部按钮：全局人物评级 + 全局装备重配 =====================
            // 上方按钮：全局人物评级，弹出 FloatMenu 含应用/清除评级标签两个操作
            Rect tierTagBtnRect = new Rect(
                rect.x,
                contentRect.yMax + buttonGap,
                rect.width,
                buttonHeight);

            if (Widgets.ButtonText(tierTagBtnRect, "AE_GlobalTierTag".Translate()))
            {
                // 弹出 FloatMenu：应用评级 / 清除评级标签
                var tierTagOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        "AE_TierTag_Apply".Translate(),
                        () =>
                        {
                            int n = AESettings.ApplyTierTagsToAllPawns();
                            Messages.Message(
                                "AE_TierTag_ApplyResult".Translate(n),
                                MessageTypeDefOf.TaskCompletion);
                        }),
                    new FloatMenuOption(
                        "AE_TierTag_Clear".Translate(),
                        () =>
                        {
                            int n = AESettings.ClearTierTagsFromAllPawns();
                            Messages.Message(
                                "AE_TierTag_ClearResult".Translate(n),
                                MessageTypeDefOf.TaskCompletion);
                        })
                };
                Find.WindowStack.Add(new FloatMenu(tierTagOptions));
            }

            // 下方按钮：全局装备重配，点击后弹出规则对话框，确认后才执行
            // 食尸鬼面板也显示此按钮（统一入口），但 GlobalAllocator 内部会跳过食尸鬼
            Rect buttonRect = new Rect(
                rect.x,
                tierTagBtnRect.yMax + buttonGap,
                rect.width,
                buttonHeight);

            if (Widgets.ButtonText(buttonRect, "AE_GlobalReallocate".Translate()))
            {
                Find.WindowStack.Add(new Dialog_GlobalReallocate());
            }
        }

        // ===================== 徽章绘制工具 =====================

        /// <summary>
        /// 绘制徽章行：角色 / 情境 / 评级 / 护甲偏好（或食尸鬼徽章）。
        /// 徽章自适应宽度占满整行，每个徽章等宽分配剩余空间。
        /// </summary>
        private void DrawBadgeRow(Listing_Standard l, Role role, GearContext context,
            CombatTier tier, ArmorPreference armorPref, bool isGhoul)
        {
            // 徽章行高度 26f
            Rect badgeRow = l.GetRect(26f);
            float y = badgeRow.y;
            float h = 24f;
            float gap = 6f;

            // 食尸鬼：4 个徽章 = 角色 + 情境 + 评级 + 食尸鬼徽章
            // 普通殖民者：4 个徽章 = 角色 + 情境 + 评级 + 护甲偏好
            int badgeCount = 4;
            float totalGap = gap * (badgeCount - 1);
            float badgeWidth = (badgeRow.width - totalGap) / badgeCount;

            float x = badgeRow.x;

            // 1. 角色徽章
            DrawBadge(new Rect(x, y, badgeWidth, h),
                ("AE_Role_" + role).Translate(),
                GetRoleColor(role));
            x += badgeWidth + gap;

            // 2. 情境徽章
            DrawBadge(new Rect(x, y, badgeWidth, h),
                ("AE_Context_" + context).Translate(),
                GetContextColor(context));
            x += badgeWidth + gap;

            // 3. 评级徽章
            DrawBadge(new Rect(x, y, badgeWidth, h),
                tier.ToString(),
                GetTierColor(tier));
            x += badgeWidth + gap;

            // 4. 护甲偏好徽章 / 食尸鬼徽章
            if (isGhoul)
            {
                DrawBadge(new Rect(x, y, badgeWidth, h),
                    "AE_Badge_Ghoul".Translate(),
                    new Color(0.6f, 0.2f, 0.6f));  // 紫红，区别于常规护甲偏好
            }
            else
            {
                DrawBadge(new Rect(x, y, badgeWidth, h),
                    ("AE_ArmorPref_" + armorPref).Translate(),
                    GetArmorPrefColor(armorPref));
            }
        }

        /// <summary>
        /// 绘制单个徽章：带底色 + 居中文字。
        /// </summary>
        private void DrawBadge(Rect rect, string text, Color bgColor)
        {
            // 保存原颜色，绘制后恢复
            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;

            // 半透明底色：让徽章不抢眼但清晰可辨
            Color bg = bgColor;
            bg.a = 0.85f;
            GUI.color = bg;
            // DrawBoxSolid 会用 GUI.color 填充
            Widgets.DrawBoxSolid(rect, bg);

            // 边框：深一点的颜色
            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 文字：白色或黑色，按底色亮度选择
            float brightness = bgColor.r * 0.299f + bgColor.g * 0.587f + bgColor.b * 0.114f;
            GUI.color = brightness > 0.5f ? Color.black : Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }

        /// <summary>
        /// 绘制数值摘要行：战斗价值 + 价值评分。
        /// 两个徽章等宽占满整行。
        /// </summary>
        private void DrawStatRow(Listing_Standard l, float combatValue, float pawnValue)
        {
            Rect statRow = l.GetRect(22f);
            float gap = 8f;
            float halfWidth = (statRow.width - gap) * 0.5f;

            // 左：战斗价值
            DrawStatBadge(new Rect(statRow.x, statRow.y, halfWidth, statRow.height),
                "AE_Badge_CombatValue".Translate(), combatValue.ToString("F1"),
                new Color(0.2f, 0.4f, 0.6f));

            // 右：价值评分
            DrawStatBadge(new Rect(statRow.x + halfWidth + gap, statRow.y, halfWidth, statRow.height),
                "AE_Badge_PawnValue".Translate(), pawnValue.ToString("F1"),
                new Color(0.3f, 0.3f, 0.5f));
        }

        /// <summary>
        /// 绘制带标签+数值的小徽章。
        /// </summary>
        private void DrawStatBadge(Rect rect, string label, string value, Color bgColor)
        {
            Color prev = GUI.color;

            Color bg = bgColor;
            bg.a = 0.85f;
            Widgets.DrawBoxSolid(rect, bg);

            GUI.color = Color.white * 0.5f;
            Widgets.DrawBox(rect, 1);

            // 文字：标签:数值
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, label + ": " + value);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            GUI.color = prev;
        }

        /// <summary>
        /// 绘制装备摘要：主武器 / 副武器 / 护甲数量。
        /// </summary>
        private void DrawEquipmentSummary(Listing_Standard l, Pawn pawn, CompGearManager comp)
        {
            // 主武器
            string primaryWeapon = pawn.equipment?.Primary?.LabelShort ?? "AE_None".Translate();
            DrawLabeledRow(l, "AE_PrimaryWeapon".Translate(), primaryWeapon);

            // 副武器（食尸鬼通常无 comp，跳过）
            if (comp != null && comp.sidearm != null)
            {
                DrawLabeledRow(l, "AE_Sidearm".Translate(), comp.sidearm.LabelShort);
            }

            // 护甲数量
            int wornCount = pawn.apparel?.WornApparel.Count ?? 0;
            DrawLabeledRow(l, "AE_WornApparel".Translate(), wornCount.ToString());
        }

        /// <summary>
        /// 绘制"标签: 值"行，标签灰色，值白色。
        /// </summary>
        private void DrawLabeledRow(Listing_Standard l, string label, string value)
        {
            Rect row = l.GetRect(22f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(row.x, row.y, row.width * 0.4f, row.height), label + ":");
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + row.width * 0.4f, row.y, row.width * 0.6f, row.height), value);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ===================== 颜色定义 =====================

        /// <summary>
        /// 获取角色对应的徽章颜色。
        /// </summary>
        private Color GetRoleColor(Role role)
        {
            switch (role)
            {
                case Role.Shooter:  return new Color(0.29f, 0.56f, 0.85f);  // 蓝
                case Role.Brawler:  return new Color(0.85f, 0.29f, 0.29f);  // 红
                case Role.Doctor:   return new Color(0.29f, 0.85f, 0.48f);  // 绿
                case Role.Hunter:   return new Color(0.85f, 0.63f, 0.29f);  // 橙
                case Role.Worker:   return new Color(0.6f, 0.6f, 0.6f);     // 灰
                case Role.Pacifist: return new Color(0.7f, 0.7f, 0.7f);     // 浅灰
                case Role.Leader:   return new Color(0.85f, 0.77f, 0.29f);  // 金
                default:            return new Color(0.8f, 0.8f, 0.8f);     // 白灰
            }
        }

        /// <summary>
        /// 获取情境对应的徽章颜色。
        /// </summary>
        private Color GetContextColor(GearContext context)
        {
            switch (context)
            {
                case GearContext.Combat:  return new Color(0.85f, 0.2f, 0.2f);   // 红
                case GearContext.Work:    return new Color(0.2f, 0.5f, 0.85f);   // 蓝
                case GearContext.Hunting: return new Color(0.85f, 0.5f, 0.2f);   // 橙
                case GearContext.Cold:    return new Color(0.2f, 0.7f, 0.85f);   // 青
                case GearContext.Hot:     return new Color(0.85f, 0.4f, 0.2f);   // 橙红
                default:                  return new Color(0.8f, 0.8f, 0.8f);    // 白灰
            }
        }

        /// <summary>
        /// 获取评级对应的徽章颜色。
        /// S=金、A=紫、B=蓝、C=绿、D=灰、X=红
        /// </summary>
        private Color GetTierColor(CombatTier tier)
        {
            switch (tier)
            {
                case CombatTier.S: return new Color(1.0f, 0.84f, 0.0f);    // 金
                case CombatTier.A: return new Color(0.61f, 0.35f, 0.71f);  // 紫
                case CombatTier.B: return new Color(0.2f, 0.6f, 0.85f);    // 蓝
                case CombatTier.C: return new Color(0.18f, 0.8f, 0.44f);   // 绿
                case CombatTier.D: return new Color(0.58f, 0.65f, 0.65f);  // 灰
                default:           return new Color(0.85f, 0.2f, 0.2f);    // 红（X）
            }
        }

        /// <summary>
        /// 获取护甲偏好对应的徽章颜色。
        /// Heavy=暗红、Flexible=黄、Light=绿
        /// </summary>
        private Color GetArmorPrefColor(ArmorPreference pref)
        {
            switch (pref)
            {
                case ArmorPreference.Heavy:    return new Color(0.75f, 0.22f, 0.17f);  // 暗红
                case ArmorPreference.Flexible: return new Color(0.95f, 0.77f, 0.06f);  // 黄
                default:                       return new Color(0.15f, 0.68f, 0.38f);  // 绿
            }
        }
    }
}
