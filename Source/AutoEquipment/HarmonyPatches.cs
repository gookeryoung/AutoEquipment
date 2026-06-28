using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutoEquipment
{
    /// <summary>
    /// Auto Equipment MOD 的全部 Harmony 补丁集合。
    /// 补丁职责：
    /// 1) 游戏加载时为所有 Pawn 注入 CompGearManager 组件
    /// 2) 取消征召时恢复 Pawn 的主武器
    /// 全部采用 Postfix 零侵入方式，不拦截原方法。
    /// </summary>
    public static class HarmonyPatches
    {
        // Harmony ID：整个 MOD 单一实例，发布后不可更改
        public const string HarmonyID = "gookeryoung.autoequipment";

        public static void Init()
        {
            var harmony = new Harmony(HarmonyID);
            harmony.PatchAll();
            Log.Message("[AutoEquipment] Harmony 补丁已应用");
        }

        /// <summary>
        /// 新游戏开始时为所有 Pawn 类型 ThingDef 注入装备管理组件。
        /// 运行时机：游戏初始化，避免修改原始 XML，运行时遍历 DefDatabase 添加。
        /// </summary>
        [HarmonyPatch(typeof(Verse.Game), "InitNewGame")]
        public static class Game_InitNewGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
                AddCompToExistingPawns();
            }
        }

        /// <summary>
        /// 加载存档时同样注入组件，并给已存在的 Pawn 补注入 ThingComp 实例。
        /// 关键：ThingDef.comps 注入只影响"后续生成"的 Pawn，
        /// 已存在于存档中的 Pawn 不会自动获得新 ThingComp，必须运行时补注入。
        /// </summary>
        [HarmonyPatch(typeof(ScribeLoader), "LoadGame")]
        public static class ScribeLoader_LoadGame_Patch
        {
            static void Postfix()
            {
                AddCompToPawnDefs();
                AddCompToExistingPawns();
            }
        }

        // 防止重复注入标志：注入操作只需执行一次
        private static bool _compAdded;

        /// <summary>
        /// 遍历 DefDatabase 中所有 Pawn 类别 ThingDef，
        /// 若未挂载 CompGearManager 则注入。已存在则跳过，避免重复。
        /// 时机：[StaticConstructorOnStartup]（DefDatabase 已加载，Pawn 未生成）。
        /// 注意：ThingDef.comps 可能为 null（XML 未声明 comps 节点），
        /// 此时应初始化为空列表再注入，而不是跳过——否则 Human 等基础种族会被漏掉。
        /// </summary>
        public static void AddCompToPawnDefs()
        {
            if (_compAdded) return;
            _compAdded = true;

            int injected = 0;
            int skipped = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.category != ThingCategory.Pawn) continue;
                // 关键修复：comps 为 null 时初始化空列表，而非跳过
                // Human 等基础种族的 ThingDef.comps 可能是 null
                if (def.comps == null) def.comps = new List<CompProperties>();

                // 检查是否已存在组件，避免重复注入
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
                    injected++;
                }
                else
                {
                    skipped++;
                }
            }
            Log.Message($"[AutoEquipment] ThingComp 注入完成: 新增={injected}, 已存在跳过={skipped}");
        }

        /// <summary>
        /// 给地图上已存在的 Pawn 补注入 CompGearManager 实例。
        /// 用途：旧存档加载时，Pawn 是基于"注入前"的 ThingDef 生成的，
        /// 不会自动获得新 ThingComp。此方法遍历所有 Pawn，对缺失组件者补注入。
        /// 时机：存档加载完成（ScribeLoader.LoadGame Postfix）。
        /// </summary>
        public static void AddCompToExistingPawns()
        {
            int injected = 0;
            int already = 0;

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    // 已有组件则跳过
                    if (pawn.GetComp<CompGearManager>() != null) { already++; continue; }

                    // 食尸鬼不参与装备管理，跳过
                    if (DLCCompat.IsGhoul(pawn)) continue;

                    // 运行时创建 ThingComp 实例并注入
                    // 复现 Pawn.AddComps 的标准流程：创建实例 -> 设 parent -> 加入 AllComps -> Initialize
                    var comp = new CompGearManager();
                    comp.parent = pawn;
                    pawn.AllComps.Add(comp);
                    comp.Initialize(new CompProperties_GearManager());
                    injected++;
                }
            }
            Log.Message($"[AutoEquipment] 已存在 Pawn 组件补注入: 新增={injected}, 已存在={already}");
        }

        /// <summary>
        /// 取消征召时的 Postfix：若 Pawn 此前为应对近战切出了副武器，
        /// 则恢复其主武器。食尸鬼不使用装备管理，直接跳过。
        /// </summary>
        [HarmonyPatch(typeof(Pawn_DraftController), "SetDrafted")]
        public static class DraftController_SetDrafted_Patch
        {
            static void Postfix(Pawn_DraftController __instance, bool drafted)
            {
                // 仅处理「取消征召」事件，征召时无需干预
                if (drafted) return;
                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                // 食尸鬼不使用 CompGearManager，跳过取消征召时的副武器恢复
                if (DLCCompat.IsGhoul(pawn)) return;

                var comp = pawn.GetComp<CompGearManager>();
                if (comp != null)
                {
                    // 异常隔离：单个 Pawn 取消征召失败不应影响其他 Pawn
                    try { comp.OnUndraft(); }
                    catch (System.Exception ex)
                    {
                        Log.Warning("[AutoEquipment] " + pawn.LabelShort + " 取消征召恢复失败: " + ex.Message);
                    }
                }
            }
        }
    }
}
