using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using AutoEverything.Core;
using AutoEverything.RoleEvaluation;

namespace AutoEverything.AutoWork
{
    /// <summary>
    /// 全局工作优先级分配器。
    /// 按兴趣（Passion）与全局评级（CombatTier）智能分配殖民者的工作优先级。
    /// 触发方式：ITab 底部"全局工作重配"按钮手动调用。
    /// </summary>
    public static class WorkAllocator
    {
        // 候选殖民者缓存（复用避免 GC）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();

        // 临时评分缓存（用于"无人有兴趣"兜底排序）
        private static readonly List<KeyValuePair<Pawn, float>> scoredPawns = new List<KeyValuePair<Pawn, float>>();

        // WorkTypeDef 缓存（懒加载，避免静态字段初始化器跨线程调用 DefDatabase）
        private static List<WorkTypeDef> cachedWorkTypes;

        /// <summary>
        /// 全局工作优先级重配入口。
        /// 自动启用自定义优先级开关，遍历所有殖民者按规则分配工作优先级。
        /// 返回受影响的殖民者数量。
        /// </summary>
        public static int ReallocateAll()
        {
            // 1. 自动启用"自定义优先级"开关（玩家若未开启则 1-4 优先级不生效）
            if (!Find.PlaySettings.useWorkPriorities)
            {
                Find.PlaySettings.useWorkPriorities = true;
            }

            // 2. 收集候选殖民者（复用 BeltAllocator/GlobalAllocator 的过滤链）
            candidatePawns.Clear();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    if (DLCCompat.IsSlave(pawn)) continue;
                    // 未成年可参与工作，保留
                    candidatePawns.Add(pawn);
                }
            }

            if (candidatePawns.Count == 0) return 0;

            // 3. 懒加载 WorkTypeDef 列表（避免静态字段初始化器调用 DefDatabase）
            if (cachedWorkTypes == null)
                cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            // 4. 遍历所有工作类型，按规则分配优先级
            for (int i = 0; i < cachedWorkTypes.Count; i++)
            {
                AssignPrioritiesForWorkType(cachedWorkTypes[i]);
            }

            return candidatePawns.Count;
        }

        /// <summary>
        /// 为单个 WorkTypeDef 分配所有候选殖民者的优先级。
        /// 按工作类型分类应用不同规则。
        /// </summary>
        private static void AssignPrioritiesForWorkType(WorkTypeDef workType)
        {
            // 紧急工作判断：灭火用 WorkTags 检测，就医/卧床用 defName 检测
            // 原因：Patient/PatientBedRest 的 workTags 为 None，无法用 WorkTags 按位与检测
            bool isEmergency = (workType.workTags & WorkTags.Firefighting) != 0
                || workType.defName == "Patient"
                || workType.defName == "PatientBedRest";
            bool isHaulingOrCleaning = workType.defName == "Hauling" || workType.defName == "Cleaning";
            bool hasSkills = workType.relevantSkills != null && workType.relevantSkills.Count > 0;

            if (isEmergency)
            {
                // 规则 1：紧急工作（灭火/就医/卧床）→ 全部优先级 1
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    pawn.workSettings.SetPriority(workType, 1);
                }
            }
            else if (isHaulingOrCleaning)
            {
                // 规则 3：搬运/清洁 → 按档位分配（S=4, D/X=1, A/B/C=3）
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    CombatTier tier = CombatEvaluator.GetCombatTier(pawn);
                    int priority;
                    switch (tier)
                    {
                        case CombatTier.S: priority = 4; break;
                        case CombatTier.D:
                        case CombatTier.X: priority = 1; break;
                        default: priority = 3; break; // A/B/C
                    }
                    pawn.workSettings.SetPriority(workType, priority);
                }
            }
            else if (hasSkills)
            {
                // 规则 2：技能工作 → 有兴趣=2，无兴趣=0，全无人有兴趣则 top2=3
                AssignSkillWorkPriorities(workType);
            }
            else
            {
                // 规则 4：非技能工作（如开关 Flicking）→ 全部优先级 3
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    pawn.workSettings.SetPriority(workType, 3);
                }
            }
        }

        /// <summary>
        /// 技能工作的优先级分配（含"全殖民地无人有兴趣"兜底逻辑）。
        /// 有人有兴趣：有兴趣=2，无兴趣=0
        /// 无人有兴趣：按技能等级取前2人=3，其余=0
        /// </summary>
        private static void AssignSkillWorkPriorities(WorkTypeDef workType)
        {
            // 第一遍：检查是否有人对该工作的相关技能有兴趣
            bool anyPassion = false;
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                if (HasPassionForAnySkill(pawn, workType.relevantSkills))
                {
                    anyPassion = true;
                    break;
                }
            }

            if (anyPassion)
            {
                // 有人有兴趣：有兴趣=2，无兴趣=0
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    int priority = HasPassionForAnySkill(pawn, workType.relevantSkills) ? 2 : 0;
                    pawn.workSettings.SetPriority(workType, priority);
                }
            }
            else
            {
                // 全殖民地无人有兴趣：取技能等级最高的 2 人 → 优先级 3，其余 → 0
                scoredPawns.Clear();
                for (int i = 0; i < candidatePawns.Count; i++)
                {
                    Pawn pawn = candidatePawns[i];
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    float score = ComputeSkillScore(pawn, workType.relevantSkills);
                    scoredPawns.Add(new KeyValuePair<Pawn, float>(pawn, score));
                }
                // 降序排序（List.Sort 非 LINQ，符合规范）
                scoredPawns.Sort((a, b) => b.Value.CompareTo(a.Value));

                for (int i = 0; i < scoredPawns.Count; i++)
                {
                    int priority = i < 2 ? 3 : 0;
                    scoredPawns[i].Key.workSettings.SetPriority(workType, priority);
                }
            }
        }

        /// <summary>
        /// 检查殖民者是否对任一指定技能有兴趣（Minor 或 Major）。
        /// </summary>
        private static bool HasPassionForAnySkill(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return false;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr != null && sr.passion != Passion.None)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 计算殖民者在指定技能集上的总等级（用于"无人有兴趣"兜底排序）。
        /// </summary>
        private static float ComputeSkillScore(Pawn pawn, List<SkillDef> skills)
        {
            if (pawn?.skills == null) return 0f;
            float total = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord sr = pawn.skills.GetSkill(skills[i]);
                if (sr == null) continue;
                total += sr.Level;
            }
            return total;
        }
    }
}
