using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// Colony-wide weapon assigner. Periodically evaluates colonist pawns and
    /// redistributes weapons for better fit based on combat skills and roles.
    /// </summary>
    public static class WeaponAssigner
    {
        private static int lastRunTick = -9999;
        private const int RunInterval = 6000; // ~2 minutes game time

        public static void Tick()
        {
            if (!AESettings.enabled || !AESettings.autoWeapons) return;

            int tick = Find.TickManager.TicksGame;
            if (tick - lastRunTick < RunInterval) return;
            lastRunTick = tick;

            // Collect all player pawns needing evaluation
            List<Pawn> pawns = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMaps_FreeColonistsAndPrisonersSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (pawn.Faction != Faction.OfPlayer) continue;
                // FIX: Ghouls are mutant pawns that cannot use weapons -- exclude them
                // from colony-wide weapon assignment so they don't wander looking for gear.
                if (pawn.IsGhoul) continue;
                // Don't reassign children's weapons
                if (ModsConfig.BiotechActive && !pawn.DevelopmentalStage.Adult()) continue;
                pawns.Add(pawn);
            }

            if (pawns.Count == 0) return;

            // Build a list of all available weapons
            List<Thing> allWeapons = new List<Thing>();
            foreach (var map in Find.Maps)
            {
                foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
                {
                    if (!weapon.def.IsWeapon) continue;
                    if (weapon.def.IsStuff) continue;
                    allWeapons.Add(weapon);
                }
            }

            // Greedy assignment: for each pawn, find best weapon
            // (Could be improved with Hungarian algorithm, but greedy is good enough)
            var assignments = new Dictionary<Pawn, Thing>();
            var assignedWeapons = new HashSet<Thing>();

            // Sort pawns by best combat skill (assign best weapons to best fighters)
            var sortedPawns = pawns.OrderByDescending(p =>
            {
                int shooting = p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
                int melee = p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
                return Mathf.Max(shooting, melee);
            }).ToList();

            foreach (Pawn pawn in sortedPawns)
            {
                Role role = RoleDetector.DetectRole(pawn);
                GearContext context = ContextDetector.GetContext(pawn);

                Thing currentWeapon = pawn.equipment?.Primary;
                float currentScore = currentWeapon != null
                    ? GearScorer.ScoreWeapon(pawn, currentWeapon, role, context) : -500f;

                Thing bestWeapon = currentWeapon;
                float bestScore = currentScore;

                foreach (Thing weapon in allWeapons)
                {
                    if (assignedWeapons.Contains(weapon)) continue;
                    if (weapon.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(weapon)) continue;

                    // Biocoded weapons: only consider if coded to this pawn
                    var bio = weapon.TryGetComp<CompBiocodable>();
                    if (bio != null && bio.Biocoded && bio.CodedPawn != pawn) continue;

                    float score = GearScorer.ScoreWeapon(pawn, weapon, role, context);
                    if (score > bestScore + 20f) // Only reassign if significantly better
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

            // Execute assignments
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
    }
}
