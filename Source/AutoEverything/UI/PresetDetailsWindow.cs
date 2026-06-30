using RimWorld;
using UnityEngine;
using Verse;
using AutoEverything.AutoEquipment.Scoring;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;
using AutoEverything.AutoEquipment;
using AutoEverything.Allocation;

namespace AutoEverything.UI
{
    /// <summary>
    /// 预设方案详情窗口：显示当前预设权重并允许微调。
    /// 用 ScrollView 包裹内容，未来新增权重项时自动支持滚动。
    /// </summary>
    public class PresetDetailsWindow : Window
    {
        // static 保持滚动位置：窗口关闭再打开时恢复上次位置，符合用户体验
        private static Vector2 scrollPos = Vector2.zero;
        // ScrollView 内容高度：首帧用默认值，之后按实际绘制高度更新
        private static float contentHeight = 300f;

        public override Vector2 InitialSize => new Vector2(420f, 480f);

        public PresetDetailsWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ScrollView 包裹：inner rect 从 (0,0) 开始，宽度比 outer 少 16f 预留滚动条
            Rect innerRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(inRect, ref scrollPos, innerRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(innerRect);

            Text.Font = GameFont.Medium;
            l.Label("AE_Preset_Details".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // 显示当前预设
            l.Label("AE_Preset".Translate() + ": " + ("AE_Preset_" + GearPolicyEngine.ActivePreset).Translate());
            l.GapLine();

            // 显示该预设的默认权重
            GearWeights defaultW = GearPolicyEngine.ActivePreset.GetDefaultWeights();
            l.Label("AE_Preset_DefaultWeights".Translate());
            l.Label($"  {("AE_Weight_Skill".Translate())}: {defaultW.w_skill:F1}");
            l.Label($"  {("AE_Weight_DPS".Translate())}: {defaultW.w_dps:F1}");
            l.Label($"  {("AE_Weight_Range".Translate())}: {defaultW.w_range:F1}");
            l.Label($"  {("AE_Weight_Quality".Translate())}: {defaultW.w_quality:F1}");
            l.Label($"  {("AE_Weight_Armor".Translate())}: {defaultW.w_armor:F1}");
            l.Label($"  {("AE_Weight_Insulation".Translate())}: {defaultW.w_insulation:F1}");
            l.Label($"  {("AE_Weight_MoveSpeed".Translate())}: {defaultW.w_movespeed:F1}");
            l.Label($"  {("AE_Weight_WorkSpeed".Translate())}: {defaultW.w_workspeed:F1}");

            l.End();
            // 按实际绘制高度更新缓存，下一帧 ScrollView 使用正确高度
            contentHeight = l.CurHeight + 20f;
            Widgets.EndScrollView();
        }
    }
}