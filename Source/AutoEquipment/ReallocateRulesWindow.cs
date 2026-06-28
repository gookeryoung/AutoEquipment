using RimWorld;
using UnityEngine;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// 全局重配规则窗口：向玩家展示分配规则，并支持自定义部分开关。
    ///
    /// 规则两类：
    /// 1. 不可调规则（固定机制）：战斗价值计算、Brawler Veto、双修远程偏好等
    ///    —— 这些是评分模型的一部分，玩家不可改，仅作说明展示
    /// 2. 可调规则（玩家偏好）：是否放下当前武器、是否跳过征召/锁定/生物编码
    ///    —— 这些影响"重配激进程度"，玩家可按需开关
    /// </summary>
    public class ReallocateRulesWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;
        private float lastContentHeight = 500f;

        public override Vector2 InitialSize => new Vector2(440f, 540f);

        public ReallocateRulesWindow()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ScrollView：内容可能超出窗口高度
            Rect scrollRect = inRect;
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, lastContentHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(contentRect);

            // ===================== 标题 =====================
            Text.Font = GameFont.Medium;
            l.Label("AE_ReallocRules_Title".Translate());
            Text.Font = GameFont.Small;
            l.Gap();

            // ===================== 固定规则说明（不可调） =====================
            l.GapLine();
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_FixedSection".Translate());
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            l.Label("AE_ReallocRules_Fixed_Desc1".Translate());
            l.Label("AE_ReallocRules_Fixed_Desc2".Translate());
            l.Label("AE_ReallocRules_Fixed_Desc3".Translate());
            l.Label("AE_ReallocRules_Fixed_Desc4".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // ===================== 可调规则 =====================
            l.GapLine();
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CustomSection".Translate());
            GUI.color = Color.white;

            // 每个开关下方跟一行小字解释后果
            l.CheckboxLabeled("AE_ReallocRules_DropWeapons".Translate(), ref AESettings.reallocateDropWeapons);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_DropWeapons_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            l.CheckboxLabeled("AE_ReallocRules_RespectDrafted".Translate(), ref AESettings.reallocateRespectDrafted);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_RespectDrafted_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            l.CheckboxLabeled("AE_ReallocRules_RespectLocked".Translate(), ref AESettings.reallocateRespectLocked);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_RespectLocked_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            l.CheckboxLabeled("AE_ReallocRules_RespectBiocoded".Translate(), ref AESettings.reallocateRespectBiocoded);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            l.Label("AE_ReallocRules_RespectBiocoded_Desc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.Gap(4f);

            // ===================== 战斗价值公式说明 =====================
            l.GapLine();
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            l.Label("AE_ReallocRules_CombatValue".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            l.Label("AE_ReallocRules_CombatValue_Formula".Translate());
            l.Label("AE_ReallocRules_CombatValue_PassionNone".Translate());
            l.Label("AE_ReallocRules_CombatValue_PassionMinor".Translate());
            l.Label("AE_ReallocRules_CombatValue_PassionMajor".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            l.End();

            // 记录实际内容高度，供下次绘制使用
            lastContentHeight = l.CurHeight + 20f;

            Widgets.EndScrollView();
        }
    }
}
