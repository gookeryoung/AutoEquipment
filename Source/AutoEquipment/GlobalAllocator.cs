using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoEquipment
{
    /// <summary>
    /// 全局重配：真正的"全局"分配语义。
    ///
    /// 设计目的：
    /// - 高战斗价值殖民者优先获取高价值武器
    /// - 无火小人手里的好武器会被释放给双火小人
    ///
    /// 流程：
    /// 1. 收集所有非征召、非锁定殖民者，按战斗价值降序排序
    /// 2. 第一遍：所有殖民者放下当前武器到地上（进入地图候选池）
    ///    跳过征召中、生物编码武器（个人绑定不可释放）
    /// 3. 第二遍：按战斗价值降序，为每个殖民者从地图候选池评分选最佳武器
    ///    已分配的武器从候选池移除，避免重复抢占
    /// 4. 服装/副武器/库存仍用 ForceEvaluate（按单 Pawn 评估即可）
    ///
    /// 战斗价值复用 SidearmAllocator.ComputeCombatValue：
    /// 射击等级 × 兴趣乘数 + 近战等级 × 兴趣乘数
    /// 兴趣乘数：无火 1.0，单火 1.5，双火 2.0
    /// </summary>
    public static class GlobalAllocator
    {
        // 候选缓存（手动触发，非 Tick 路径，但仍复用静态字段避免 GC）
        private static readonly List<Pawn> sortedPawns = new List<Pawn>();
        private static readonly List<Thing> candidateWeapons = new List<Thing>();
        private static readonly HashSet<int> assignedWeaponIds = new HashSet<int>();

        // 护甲重配候选缓存
        private static readonly List<Apparel> candidateApparels = new List<Apparel>();
        private static readonly HashSet<int> assignedApparelIds = new HashSet<int>();

        /// <summary>
        /// 全局重配：放下所有殖民者武器与护甲，按战斗价值降序重新分配。
        /// 返回被触发的殖民者数量。
        /// </summary>
        public static int ReallocateAll()
        {
            sortedPawns.Clear();
            candidateWeapons.Clear();
            assignedWeaponIds.Clear();
            candidateApparels.Clear();
            assignedApparelIds.Clear();

            // ========== 收集候选殖民者 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (DLCCompat.IsGhoul(pawn)) continue;
                    if (!PawnSuitabilityChecker.CanManageGear(pawn)) continue;
                    if (pawn.Dead || pawn.Downed) continue;
                    // 征召中的殖民者正在战斗，不打断（玩家可在规则面板关闭此保护）
                    if (AESettings.reallocateRespectDrafted && pawn.Drafted) continue;

                    CompGearManager comp = pawn.GetComp<CompGearManager>();
                    // 已锁定的殖民者尊重玩家意愿（玩家可在规则面板关闭此保护）
                    if (AESettings.reallocateRespectLocked && (comp == null || comp.locked)) continue;
                    if (comp == null) continue;

                    sortedPawns.Add(pawn);
                }
            }

            if (sortedPawns.Count == 0) return 0;

            // 按战斗价值降序排序：高价值殖民者优先分配
            sortedPawns.Sort(ComparePawnByCombatValueDesc);

            // ========== 武器重配 ==========
            ReallocateWeapons();

            // ========== 护甲重配（按角色偏好分配重甲/轻甲） ==========
            if (AESettings.reallocateApparel)
            {
                ReallocateApparel();
            }

            // ========== 副武器与库存：仍用 ForceEvaluate ==========
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                comp.ForceEvaluate(CompGearManager.ReloadTarget.Sidearm);
                comp.ForceEvaluate(CompGearManager.ReloadTarget.Inventory);
            }

            return sortedPawns.Count;
        }

        /// <summary>
        /// 武器重配：放下所有殖民者武器，按战斗价值降序从地图候选池评分分配。
        /// </summary>
        private static void ReallocateWeapons()
        {
            // ========== 第一遍：放下所有殖民者的当前武器 ==========
            // 设计意图：让无火小人手里的好武器进入地图候选池，供双火小人拾取
            // 玩家可在规则面板关闭此步骤，仅评估地图上已有的武器
            int droppedCount = 0;
            if (AESettings.reallocateDropWeapons)
            {
                for (int i = 0; i < sortedPawns.Count; i++)
                {
                    Pawn pawn = sortedPawns[i];
                    ThingWithComps primary = pawn.equipment?.Primary;
                    if (primary == null) continue;

                    // 生物编码武器：个人绑定，放下后无法被他人拾取，跳过
                    // 玩家可在规则面板关闭此保护（关闭后仍会放下，但他人无法拾取，纯属浪费）
                    if (AESettings.reallocateRespectBiocoded)
                    {
                        var bioApp = primary.TryGetComp<CompBiocodable>();
                        if (bioApp != null && bioApp.Biocoded) continue;
                    }

                    // 放下武器到 Pawn 位置，进入地图候选池
                    ThingWithComps dropped;
                    pawn.equipment.TryDropEquipment(primary, out dropped, pawn.Position, false);
                    if (dropped != null)
                    {
                        droppedCount++;
                        Log.Message($"[AutoEquipment] 全局重配: {AEDebug.Label(pawn)} 放下武器 {dropped.LabelShort}");
                    }
                }
                Log.Message($"[AutoEquipment] 全局重配: 共 {droppedCount} 把武器已释放到地图候选池");
            }
            else
            {
                Log.Message("[AutoEquipment] 全局重配: 已禁用'放下当前武器'，仅评估地图候选池");
            }

            // ========== 收集地图候选武器 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
                {
                    if (!thing.def.IsWeapon) continue;
                    if (!thing.def.IsRangedWeapon && !thing.def.IsMeleeWeapon) continue;
                    if (thing.def.IsStuff) continue;
                    candidateWeapons.Add(thing);
                }
            }

            // ========== 第二遍：按战斗价值降序分配最佳武器 ==========
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                if (pawn.Map == null) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                Role role = comp.CurrentRole;
                GearContext context = ContextDetector.GetContext(pawn);

                // 评分所有候选武器，选最佳
                Thing bestWeapon = null;
                float bestScore = float.MinValue;
                int bestIdx = -1;

                for (int j = 0; j < candidateWeapons.Count; j++)
                {
                    Thing w = candidateWeapons[j];
                    if (w == null) continue;
                    if (assignedWeaponIds.Contains(w.thingIDNumber)) continue;
                    if (w.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(w) || !pawn.CanReach(w, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    if (w.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;
                    if (w.def.IsMeleeWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;

                    // 生物编码检查：非编码者不能拾取（玩家可关闭此保护，但关闭后仍会被游戏原生拒绝）
                    if (AESettings.reallocateRespectBiocoded)
                    {
                        var bioApp = w.TryGetComp<CompBiocodable>();
                        if (bioApp != null && bioApp.Biocoded && bioApp.CodedPawn != pawn) continue;
                    }

                    float score = GearScorer.ScoreWeapon(pawn, w, role, context);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = w;
                        bestIdx = j;
                    }
                }

                if (bestWeapon != null)
                {
                    // 标记为已分配，避免后续殖民者抢占
                    assignedWeaponIds.Add(bestWeapon.thingIDNumber);
                    // 从候选池移除（设为 null 而非 Remove，避免列表重排开销）
                    candidateWeapons[bestIdx] = null;

                    // 创建 Equip job
                    var job = JobMaker.MakeJob(JobDefOf.Equip, bestWeapon);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    Log.Message($"[AutoEquipment] 全局重配 #{i + 1}: {AEDebug.Label(pawn)} (战斗价值={SidearmAllocator.ComputeCombatValue(pawn):F1}) ← {bestWeapon.LabelShort} (score={bestScore:F1})");
                }
                else
                {
                    Log.Message($"[AutoEquipment] 全局重配 #{i + 1}: {AEDebug.Label(pawn)} 无可用武器");
                }

                // 服装由后续 ReallocateApparel 处理
            }
        }

        /// <summary>
        /// 护甲重配：放下所有殖民者护甲，按战斗价值降序 + 角色护甲偏好重新分配。
        /// - Heavy 偏好角色（前排战士）：优先选重甲
        /// - Flexible 偏好角色（后排）：按原评分自由选择
        /// - Light 偏好角色（工人/猎人等）：优先选轻甲以提高工作效率
        /// </summary>
        private static void ReallocateApparel()
        {
            // 护甲分配按"全局价值评级"（CombatTier）降序，与武器分配解耦：
            // 武器分配用 ComputeCombatValue（射击/格斗维度）排序；
            // 护甲分配用 GetCombatTier（包含生产/社交/特质等全局价值）排序，
            // 高评级殖民者优先获得价值最高的护甲。
            sortedPawns.Sort(ComparePawnByCombatTierDesc);
            // ========== 第一遍：放下所有殖民者的当前护甲 ==========
            // 复用"放下当前武器"开关语义：放下所有护甲进入地图候选池，让低价值小人手里的好护甲可被高价值小人拾取
            int droppedApparelCount = 0;
            if (AESettings.reallocateDropWeapons)
            {
                for (int i = 0; i < sortedPawns.Count; i++)
                {
                    Pawn pawn = sortedPawns[i];
                    if (pawn.apparel?.WornApparel == null) continue;

                    // 复制一份避免在遍历中修改原列表
                    List<Apparel> wornCopy = new List<Apparel>(pawn.apparel.WornApparel);
                    for (int j = 0; j < wornCopy.Count; j++)
                    {
                        Apparel ap = wornCopy[j];

                        // 生物编码护甲：个人绑定，跳过
                        if (AESettings.reallocateRespectBiocoded)
                        {
                            var bioApp = ap.TryGetComp<CompBiocodable>();
                            if (bioApp != null && bioApp.Biocoded) continue;
                        }

                        pawn.apparel.Remove(ap);
                        Thing dropped;
                        if (GenDrop.TryDropSpawn(ap, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped))
                        {
                            droppedApparelCount++;
                        }
                    }
                }
                Log.Message($"[AutoEquipment] 全局重配护甲: 共 {droppedApparelCount} 件护甲已释放到地图候选池");
            }

            // ========== 收集地图候选护甲 ==========
            foreach (Map map in Find.Maps)
            {
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel))
                {
                    Apparel ap = thing as Apparel;
                    if (ap == null) continue;
                    if (ap.WornByCorpse) continue;  // 跳过尸体上的衣物（卫生问题）
                    candidateApparels.Add(ap);
                }
            }

            // ========== 第二遍：按战斗价值降序 + 角色偏好分配 ==========
            // 每个 Pawn 循环分配，直到无可用护甲或所有候选都已分配
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                Pawn pawn = sortedPawns[i];
                if (pawn.Map == null) continue;

                CompGearManager comp = pawn.GetComp<CompGearManager>();
                if (comp == null) continue;

                Role role = comp.CurrentRole;
                GearContext context = ContextDetector.GetContext(pawn);
                ArmorPreference pref = RoleDetector.GetArmorPreference(role);

                // 循环分配，直到该 Pawn 无可分配的护甲
                int assignedThisLoop = 0;
                while (true)
                {
                    Apparel best = null;
                    float bestScore = float.MinValue;
                    int bestIdx = -1;

                    for (int j = 0; j < candidateApparels.Count; j++)
                    {
                        Apparel ap = candidateApparels[j];
                        if (ap == null) continue;
                        if (assignedApparelIds.Contains(ap.thingIDNumber)) continue;
                        if (ap.IsForbidden(pawn)) continue;
                        if (!pawn.CanReserve(ap) || !pawn.CanReach(ap, PathEndMode.ClosestTouch, Danger.Some)) continue;

                        // Pawn 是否可穿戴：有可穿戴的身体部位，且不与已穿戴护甲冲突
                        if (!ApparelUtility.HasPartsToWear(pawn, ap.def)) continue;

                        bool conflict = false;
                        List<Apparel> worn = pawn.apparel.WornApparel;
                        for (int k = 0; k < worn.Count; k++)
                        {
                            if (!ApparelUtility.CanWearTogether(worn[k].def, ap.def, pawn.RaceProps.body))
                            {
                                conflict = true;
                                break;
                            }
                        }
                        if (conflict) continue;

                        // 评分
                        float score = GearScorer.ScoreApparel(pawn, ap, role, context);

                        // 根据护甲偏好调整
                        // 重甲判定：ArmorRating_Sharp ≥ 阈值
                        float armorSharp = ap.GetStatValue(StatDefOf.ArmorRating_Sharp);
                        bool isHeavy = armorSharp >= AESettings.heavyArmorSharpThreshold;

                        if (pref == ArmorPreference.Heavy && !isHeavy)
                            score += AESettings.heavyArmorPenaltyForLight;
                        else if (pref == ArmorPreference.Light && isHeavy)
                            score += AESettings.lightArmorPenaltyForHeavy;
                        // Flexible 不调整

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = ap;
                            bestIdx = j;
                        }
                    }

                    // 无可用护甲或评分过低（< 0 表示该护甲不适合此 Pawn）时停止
                    if (best == null || bestScore <= 0f) break;

                    assignedApparelIds.Add(best.thingIDNumber);
                    candidateApparels[bestIdx] = null;

                    // 创建 Wear job
                    var job = JobMaker.MakeJob(JobDefOf.Wear, best);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                    assignedThisLoop++;
                    Log.Message($"[AutoEquipment] 全局重配护甲 #{i + 1}.{assignedThisLoop}: {AEDebug.Label(pawn)} [{pref}] ← {best.LabelShort} (score={bestScore:F1})");
                }

                if (assignedThisLoop == 0)
                {
                    Log.Message($"[AutoEquipment] 全局重配护甲 #{i + 1}: {AEDebug.Label(pawn)} [{pref}] 无可分配护甲");
                }
            }
        }

        private static int ComparePawnByCombatValueDesc(Pawn a, Pawn b)
        {
            return SidearmAllocator.ComputeCombatValue(b).CompareTo(SidearmAllocator.ComputeCombatValue(a));
        }

        /// <summary>
        /// 按"全局价值评级"（CombatTier）降序比较：用于护甲分配优先级。
        /// 同档内再用 ComputePawnValueScore（特质数 + 兴趣 + 技能等级）精排，
        /// 让同档中培养更深的殖民者优先获得好装备。
        /// </summary>
        private static int ComparePawnByCombatTierDesc(Pawn a, Pawn b)
        {
            int tierA = (int)SidearmAllocator.GetCombatTier(a);
            int tierB = (int)SidearmAllocator.GetCombatTier(b);
            if (tierA != tierB) return tierB.CompareTo(tierA);
            return SidearmAllocator.ComputePawnValueScore(b).CompareTo(SidearmAllocator.ComputePawnValueScore(a));
        }
    }
}
