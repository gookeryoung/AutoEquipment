using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// 殖民地武器分配器：周期性评估殖民者并基于战斗技能重新分配武器。
    /// 使用贪心算法（可后续优化为匈牙利算法，但贪心已足够）。
    /// </summary>
    public static class WeaponAssigner
    {
        private static int lastRunTick = -9999;
        private const int RunInterval = 6000; // ~2 分钟游戏时间

        // 静态缓存集合：避免每次 Tick 重新分配造成 GC 压力
        private static readonly List<Pawn> cachedPawns = new List<Pawn>();
        private static readonly List<Thing> cachedWeapons = new List<Thing>();
        private static readonly List<PawnScore> cachedPawnScores = new List<PawnScore>();
        private static readonly Dictionary<Pawn, Thing> assignments = new Dictionary<Pawn, Thing>();
        private static readonly HashSet<Thing> assignedWeapons = new HashSet<Thing>();

        private struct PawnScore
        {
            public Pawn pawn;
            public int bestSkill;
        }

        public static void Tick()
        {
            if (!AESettings.enabled || !AESettings.autoWeapons) return;

            int tick = Find.TickManager.TicksGame;
            if (tick - lastRunTick < RunInterval) return;
            lastRunTick = tick;

            // 清空缓存集合（Capacity 保留，避免重新分配底层数组）
            cachedPawns.Clear();
            cachedWeapons.Clear();
            cachedPawnScores.Clear();
            assignments.Clear();
            assignedWeapons.Clear();

            // 收集需要评估的殖民者
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsAndPrisonersSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (pawn.Faction != Faction.OfPlayer) continue;
                // 食尸鬼无法使用武器，排除在殖民地武器分配之外
                if (DLCCompat.IsGhoul(pawn)) continue;
                // 不为未成年分配武器
                if (DLCCompat.IsChild(pawn)) continue;
                cachedPawns.Add(pawn);
            }

            if (cachedPawns.Count == 0) return;

            // 收集所有可用武器
            foreach (var map in Find.Maps)
            {
                foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
                {
                    if (!weapon.def.IsWeapon) continue;
                    if (weapon.def.IsStuff) continue;
                    cachedWeapons.Add(weapon);
                }
            }

            // 按最佳战斗技能排序（用 for 循环替代 LINQ OrderByDescending，避免闭包分配）
            for (int i = 0; i < cachedPawns.Count; i++)
            {
                Pawn p = cachedPawns[i];
                int shooting = p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                int melee = p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                cachedPawnScores.Add(new PawnScore { pawn = p, bestSkill = Mathf.Max(shooting, melee) });
            }
            cachedPawnScores.Sort(ComparePawnBySkillDesc);

            // 贪心分配：为每个 Pawn 找最佳武器
            for (int i = 0; i < cachedPawnScores.Count; i++)
            {
                Pawn pawn = cachedPawnScores[i].pawn;
                Role role = RoleDetector.DetectRole(pawn);
                GearContext context = ContextDetector.GetContext(pawn);

                Thing currentWeapon = pawn.equipment?.Primary;
                float currentScore = currentWeapon != null
                    ? GearScorer.ScoreWeapon(pawn, currentWeapon, role, context) : -500f;

                Thing bestWeapon = currentWeapon;
                float bestScore = currentScore;

                for (int w = 0; w < cachedWeapons.Count; w++)
                {
                    Thing weapon = cachedWeapons[w];
                    if (assignedWeapons.Contains(weapon)) continue;
                    if (weapon.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(weapon)) continue;

                    // 生物编码武器：仅考虑归属此 Pawn 的
                    var bio = weapon.TryGetComp<CompBiocodable>();
                    if (bio != null && bio.Biocoded && bio.CodedPawn != pawn) continue;

                    float score = GearScorer.ScoreWeapon(pawn, weapon, role, context);
                    if (score > bestScore + 20f) // 显著更好才重新分配
                    {
                        bestScore = score;
                        bestWeapon = weapon;
                    }
                }

                if (bestWeapon != currentWeapon && bestWeapon != null)
                {
                    assignments[pawn] = bestWeapon;
                    assignedWeapons.Add(bestWeapon);
                }
            }

            // 执行分配
            foreach (var kvp in assignments)
            {
                Pawn pawn = kvp.Key;
                Thing weapon = kvp.Value;
                if (pawn.Map != null && pawn.Map == weapon.Map)
                {
                    var job = JobMaker.MakeJob(JobDefOf.Equip, weapon);
                    pawn.jobs.TryTakeOrderedJob(job, Verse.AI.JobTag.Misc);
                }
            }
        }

        /// <summary>
        /// 按最佳技能降序比较（避免 LINQ OrderByDescending 闭包分配）。
        /// </summary>
        private static int ComparePawnBySkillDesc(PawnScore a, PawnScore b)
        {
            // 降序：b 在前返回正数
            return b.bestSkill.CompareTo(a.bestSkill);
        }
    }
}
