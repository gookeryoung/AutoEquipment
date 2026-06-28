using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    public class AESettings : ModSettings
    {
        // 主开关
        public static bool enabled = true;
        public static bool autoWeapons = true;
        public static bool autoApparel = true;
        public static bool autoInventory = true;
        public static bool sidearms = true;

        // 情境切换
        public static bool combatSwitch = true;      // 征召/取消征召时切换装备
        public static bool huntingWeapon = true;     // 狩猎工作时装备狩猎武器
        public static bool temperatureAware = true;  // 季节性服装切换
        public static bool jobAwareApparel = true;   // 按工作切换工作属性服装

        // 副武器
        public static bool autoMeleeSidearm = true;  // 受近战攻击时自动切出近战武器
        public static bool carryMedicine = true;     // 医生/战斗人员携带药品
        public static int medicineCount = 3;          // 携带数量

        // 性能
        public static int evaluateInterval = 500;    // 装备评估间隔（tick）

        // 阈值
        public static float upgradeThreshold = 0.15f; // 评分需提升 15% 才触发换装
        public static float tempDangerMargin = 5f;    // 超出舒适温度范围多少度才触发换装

        // 调试
        public static bool debugLogging = false;       // 详细日志开关

        // 设置窗口滚动位置：内容超出窗口高度时使用
        private static Vector2 settingsScrollPos = Vector2.zero;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref autoWeapons, "autoWeapons", true);
            Scribe_Values.Look(ref autoApparel, "autoApparel", true);
            Scribe_Values.Look(ref autoInventory, "autoInventory", true);
            Scribe_Values.Look(ref sidearms, "sidearms", true);
            Scribe_Values.Look(ref combatSwitch, "combatSwitch", true);
            Scribe_Values.Look(ref huntingWeapon, "huntingWeapon", true);
            Scribe_Values.Look(ref temperatureAware, "temperatureAware", true);
            Scribe_Values.Look(ref jobAwareApparel, "jobAwareApparel", true);
            Scribe_Values.Look(ref autoMeleeSidearm, "autoMeleeSidearm", true);
            Scribe_Values.Look(ref carryMedicine, "carryMedicine", true);
            Scribe_Values.Look(ref medicineCount, "medicineCount", 3);
            Scribe_Values.Look(ref evaluateInterval, "evaluateInterval", 500);
            Scribe_Values.Look(ref upgradeThreshold, "upgradeThreshold", 0.15f);
            Scribe_Values.Look(ref tempDangerMargin, "tempDangerMargin", 5f);
            Scribe_Values.Look(ref debugLogging, "debugLogging", false);
            base.ExposeData();
        }

        public static void DrawSettings(Rect inRect)
        {
            // 设置项较多，固定高度会超出窗口，使用 ScrollView 支持滚动
            // 注意：RimWorld 的 ScrollView inner rect 必须从 (0,0) 开始，
            // 否则内容会偏移到 ScrollView 外导致不可见
            float contentHeight = 560f;
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollRect, ref settingsScrollPos, viewRect);

            // 将 ScrollView 起点平移到屏幕坐标，保证 Listing 正确绘制
            // (BeginScrollView 会自动应用坐标变换，所以传入 viewRect 即可)
            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);

            l.CheckboxLabeled("AE_Enabled".Translate(), ref enabled);
            if (!enabled) { l.End(); Widgets.EndScrollView(); return; }

            l.GapLine();
            l.Label("AE_AutoSystems".Translate());
            l.CheckboxLabeled("AE_AutoWeapons".Translate(), ref autoWeapons);
            l.CheckboxLabeled("AE_AutoApparel".Translate(), ref autoApparel);
            l.CheckboxLabeled("AE_AutoInventory".Translate(), ref autoInventory);
            l.CheckboxLabeled("AE_Sidearms".Translate(), ref sidearms);

            l.GapLine();
            l.Label("AE_Context".Translate());
            l.CheckboxLabeled("AE_CombatSwitch".Translate(), ref combatSwitch);
            l.CheckboxLabeled("AE_HuntingWeapon".Translate(), ref huntingWeapon);
            l.CheckboxLabeled("AE_TemperatureAware".Translate(), ref temperatureAware);
            l.CheckboxLabeled("AE_JobAwareApparel".Translate(), ref jobAwareApparel);

            l.GapLine();
            l.Label("AE_SidearmSettings".Translate());
            l.CheckboxLabeled("AE_AutoMelee".Translate(), ref autoMeleeSidearm);
            l.CheckboxLabeled("AE_CarryMedicine".Translate(), ref carryMedicine);
            if (carryMedicine)
            {
                l.Label("AE_MedicineCount".Translate() + ": " + medicineCount);
                medicineCount = (int)l.Slider(medicineCount, 1, 10);
            }

            l.GapLine();
            l.Label("AE_UpgradeThreshold".Translate() + ": " + (upgradeThreshold * 100f).ToString("F0") + "%");
            upgradeThreshold = l.Slider(upgradeThreshold, 0.05f, 0.50f);

            l.GapLine();
            l.CheckboxLabeled("AE_DebugLogging".Translate(), ref debugLogging, "AE_DebugLogging_Desc".Translate());

            // 调试工具组：手动触发的运维操作
            l.GapLine();
            l.Label("AE_DebugTools".Translate());
            if (l.ButtonText("AE_DebugCleanGhouls".Translate()))
            {
                int cleaned = CompGearManager.CleanAllGhouls();
                Messages.Message("AE_DebugCleanGhoulsResult".Translate(cleaned), MessageTypeDefOf.TaskCompletion);
            }

            // 立即换装：点击后弹出菜单选择目标类型，再选择角色筛选
            if (l.ButtonText("AE_DebugReload".Translate()))
            {
                Find.WindowStack.Add(new ReloadTargetMenu());
            }

            l.End();

            Widgets.EndScrollView();
        }
    }

    public class AutoEquipmentMod : Mod
    {
        public static AESettings settings;

        public AutoEquipmentMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<AESettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            AESettings.DrawSettings(inRect);
        }

        public override string SettingsCategory() => "AE_SettingsCategory".Translate();
    }

    /// <summary>
    /// 立即换装第一步菜单：选择换装目标类型。
    /// 选择后再弹出角色筛选菜单。
    /// </summary>
    public class ReloadTargetMenu : Window
    {
        public override Vector2 InitialSize => new Vector2(280f, 320f);

        public ReloadTargetMenu()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("AE_DebugReload".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            l.Label("AE_DebugReload_Target".Translate());
            l.Gap(4f);

            // 目标类型列表
            var targets = new[]
            {
                CompGearManager.ReloadTarget.All,
                CompGearManager.ReloadTarget.Weapon,
                CompGearManager.ReloadTarget.Apparel,
                CompGearManager.ReloadTarget.Sidearm,
                CompGearManager.ReloadTarget.Inventory
            };

            for (int i = 0; i < targets.Length; i++)
            {
                // 闭包捕获：必须用局部变量，避免循环变量全部指向最后一个枚举值
                CompGearManager.ReloadTarget localTarget = targets[i];
                string labelKey = "AE_DebugReload_Target_" + localTarget;
                if (l.ButtonText(labelKey.Translate()))
                {
                    Close();
                    Find.WindowStack.Add(new ReloadRoleMenu(localTarget));
                }
            }

            l.End();
        }
    }

    /// <summary>
    /// 立即换装第二步菜单：选择角色筛选。
    /// 选择后立即触发换装并显示结果消息。
    /// </summary>
    public class ReloadRoleMenu : Window
    {
        private readonly CompGearManager.ReloadTarget target;

        public override Vector2 InitialSize => new Vector2(280f, 360f);

        public ReloadRoleMenu(CompGearManager.ReloadTarget target)
        {
            this.target = target;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);

            Text.Font = GameFont.Medium;
            l.Label("AE_DebugReload".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 显示当前选择的目标类型
            l.Label("AE_DebugReload_CurrentTarget".Translate(
                ("AE_DebugReload_Target_" + target).Translate()));
            l.Gap();

            l.Label("AE_DebugReload_Role".Translate());
            l.Gap(4f);

            // 角色筛选列表（Default 表示"全部"，不筛选）
            var roles = new[]
            {
                Role.Default,    // 全部
                Role.Shooter,
                Role.Brawler,
                Role.Doctor,
                Role.Hunter,
                Role.Worker,
                Role.Pacifist
            };

            for (int i = 0; i < roles.Length; i++)
            {
                // 闭包捕获
                Role localRole = roles[i];
                string labelKey = localRole == Role.Default
                    ? "AE_DebugReload_Role_All"
                    : ("AE_Role_" + localRole);
                if (l.ButtonText(labelKey.Translate()))
                {
                    Close();
                    int triggered = CompGearManager.TriggerReload(target, localRole);
                    Messages.Message(
                        "AE_DebugReloadResult".Translate(
                            ("AE_DebugReload_Target_" + target).Translate(),
                            labelKey.Translate(),
                            triggered),
                        MessageTypeDefOf.TaskCompletion);
                }
            }

            l.End();
        }
    }
}
