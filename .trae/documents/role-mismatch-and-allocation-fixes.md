# 角色错配修复 + 装备/腰带/EMP 重构计划

> 阶段 C 第二批需求实施计划。承接前一批"自动装备重配 + 重甲优先 + 奴隶工作 + 狩猎限制"已完成的工作。

## 一、需求来源

用户原话（4 项需求）：

> 1. 角色定位是自由后排为何还要自动装备近战武器并且配备护盾腰带，而角色定位是轻甲工人的却安排了近战武器，导致出严重问题，请解决此问题。
> 2. 取消携带多个装备的需求，每个人只选择最适合自己的装备。
> 3. 评级较低的非重甲前排人员，至少保证 2 人持有 EMP 手雷。
> 4. 重甲前排至少 2 人配备消防背包，优先给评级较低者。

用户澄清决策（4 点）：

| 澄清项 | 用户决策 |
|--------|---------|
| "取消携带多个装备"范围 | **仅取消副武器**，保留药品库存 |
| "非重甲前排"含义 | **Flexible 后排**（Shooter/Hunter/Leader） |
| EMP 手雷携带方式 | **库存携带（副武器特例）** |
| "评级较低"阈值 | **按排序计算**：特定范围要求的候选里评级相对低的，若都是 SSS，则 S 也算较低 |

## 二、根因分析

### Bug 1：Flexible 后排拿近战武器 + 护盾腰带

| 直接根因 | 文件 | 位置 |
|---------|------|------|
| `WeaponSkillScorer` 不参考 Role，仅按 `skill.Level × passionMult` 评分，近战 DPS 高即胜出 | `WeaponSkillScorer.cs` | L16-55 |
| `WeaponTraitScorer` 对 Flexible/Light + 近战武器无 veto | `WeaponTraitScorer.cs` | L28-112 |
| `BeltAllocator.CollectCandidatePawns` 用 `PawnCombatProfile.IsPureMeleeShooter`（passion-based），Flexible 无火者被放入候选池 | `BeltAllocator.cs` | L148 |
| `PawnCombatProfile.IsPureMeleeShooter` 仅检查 `shooting.passion == Passion.None`，不检查 Role | `PawnCombatProfile.cs` | L22-28 |

链式触发：Flexible 角色 → 错误拿近战武器 → 或手动给护盾腰带 → `WeaponTraitScorer.IsWearingShieldBelt` 让远程武器 -9000 veto → 近战武器胜出 → 死循环。

### Bug 2：Light 工人拿近战武器

| 直接根因 | 文件 | 位置 |
|---------|------|------|
| `WeaponTraitScorer` 对 Light 角色（Worker/Doctor）无近战 veto | `WeaponTraitScorer.cs` | L28-112 |
| `WeaponSkillScorer` 无角色惩罚，Light 角色若近战技能高即拿近战 | `WeaponSkillScorer.cs` | L16-55 |

## 三、设计方案

### 变更 1：WeaponTraitScorer 新增 Role-based veto

**文件**：`Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponTraitScorer.cs`

**改动**：在 `Score` 方法开头（`IsWearingShieldBelt` 检查之后、`Brawler` 特质检查之前）新增 Role-based 近战 veto：

```csharp
// 角色定位硬约束：仅 Brawler（重甲前排）允许近战武器
// 设计意图：Worker/Doctor/Pacifist/Default（Light）应避免近战（轻甲无防护），
// Shooter/Hunter/Leader（Flexible）应优先远程输出，近战武器会让他们失去远程优势
if (isMelee && role != Role.Brawler)
{
    breakdown.Veto(-9000f);
    breakdown.AddScore(Name, "非格斗者+近战=拒绝", -9000f);
    return;
}
```

**关键点**：
- 仅 Brawler 角色（基于特质或技能判定）允许近战武器
- Pacifist 角色（X 档）会在其他 Scorer 中被 veto 所有武器，这里不重复处理
- 技能型 Brawler（无特质但近战 ≥ 8）也允许近战武器，因为 Role 枚举已包含这种情况

### 变更 2：BeltAllocator 重构（候选门控 + 消防背包优先级）

**文件**：`Source/AutoEverything/Allocation/BeltAllocator.cs`

**改动 2.1**：`CollectCandidatePawns` 候选门控改为 Role-based

```csharp
// 旧：if (!PawnCombatProfile.IsPureMeleeShooter(pawn)) continue;
// 新：仅重甲前排（Heavy=Brawler）参与 belt 分配
Role role = RoleDetector.DetectRole(pawn);
if (RoleDetector.GetArmorPreference(role) != ArmorPreference.Heavy) continue;
```

**改动 2.2**：候选排序改为按 `CombatTier` 升序（评级低者优先）

```csharp
// 旧：candidatePawns.Sort((a, b) => combatValueCache[b].CompareTo(combatValueCache[a]));
// 新：重甲前排候选按 CombatTier 升序排序（评级低者优先配消防背包）
// 设计意图：评级低的重甲前排承担伤害能力较弱，更需要消防背包应对火灾/机械族
candidatePawns.Sort((a, b) =>
    CombatEvaluator.GetAutoCombatTier(a).CompareTo(CombatEvaluator.GetAutoCombatTier(b)));
```

**改动 2.3**：重构分配流程，移除全局保底逻辑

```csharp
// 前 2 人强制分配消防背包（若库存有），其余配护盾腰带
int firefoamCount = 0;
const int MinFirefoamPawns = 2;

for (int i = 0; i < candidatePawns.Count; i++)
{
    Pawn pawn = candidatePawns[i];
    if (HasBelt(pawn)) continue;

    // 优先给前 2 人（评级最低者）配消防背包
    if (firefoamCount < MinFirefoamPawns)
    {
        int firefoamIdx = FindFirstFirefoamPackIndex();
        if (firefoamIdx >= 0)
        {
            AssignBelt(pawn, candidateBelts[firefoamIdx], "重甲前排消防背包(评级优先)");
            candidateBelts[firefoamIdx] = null;
            firefoamCount++;
            continue;
        }
    }

    // 其余候选在护盾腰带与消防背包中按评分选择
    // ScoreBelt 保持：Heavy+护盾+100，Heavy+消防+60（默认护盾腰带胜出）
    // ... 原 ScoreBelt 评分循环
}
```

**改动 2.4**：移除 `anyFirefoamWorn` 全局保底逻辑（被新规则替代）
- 移除 `CollectCandidatePawns` 的 `ref bool anyFirefoamWorn` 参数
- 移除 `IsWearingFirefoamPack` 检查（不再需要）
- 移除 `forceFirefoam`/`firefoamAssigned` 逻辑

**改动 2.5**：`ScoreBelt` 评分保持不变（Heavy+护盾+100，Heavy+消防+60），但移除 Flexible/Light 分支（候选池已无此类角色）

### 变更 3：SidearmAllocator 重构（取消反向副武器 + EMP 给 Flexible 后排）

**文件**：`Source/AutoEverything/Allocation/SidearmAllocator.cs`

**改动 3.1**：类注释更新为"EMP 手雷全局分配器"

```csharp
/// <summary>
/// EMP 手雷全局分配器：为 Flexible 后排（Shooter/Hunter/Leader）评级较低者分配 EMP 手雷。
///
/// 设计目的：
/// - 取消携带多个装备：每人只选最适合自己的主武器，不再自动分配反向类型副武器
/// - EMP 手雷特例：评级较低的 Flexible 后排至少 2 人持有 EMP 手雷，
///   应对机械族/护盾等需要 EMP 的战术场景
/// - 评级低者优先：重甲前排承担近战，评级低的后排承担 EMP 战术支援
///
/// 分配规则：
/// 1. 收集所有 Flexible 后排（IsBackRow）且无 EMP 副武器的殖民者
/// 2. 按 CombatTier 升序排序（评级低者优先）
/// 3. 收集地图上所有 EMP 武器候选
/// 4. 前 2 人分配 EMP 武器（库存携带，副武器特例）
/// </summary>
```

**改动 3.2**：`CollectCandidatePawns` 候选门控改为仅 Flexible 后排

```csharp
// 旧：所有殖民者（无 Role 过滤）
// 新：仅 Flexible 后排（Shooter/Hunter/Leader）参与 EMP 手雷分配
Role role = RoleDetector.DetectRole(pawn);
if (!RoleDetector.IsBackRow(role)) continue;

// 必须有主武器（用于排除无武器的新殖民者）
Thing primary = pawn.equipment?.Primary;
if (primary == null) continue;

// 库存中已有 EMP 武器则跳过（避免重复分配）
if (HasEmpSidearm(pawn)) continue;
```

**改动 3.3**：候选排序改为按 `CombatTier` 升序

```csharp
// 旧：candidatePawns.Sort((a, b) => combatValueCache[b].CompareTo(combatValueCache[a]));
// 新：Flexible 后排候选按 CombatTier 升序排序（评级低者优先配 EMP 手雷）
candidatePawns.Sort((a, b) =>
    CombatEvaluator.GetAutoCombatTier(a).CompareTo(CombatEvaluator.GetAutoCombatTier(b)));
```

**改动 3.4**：重构分配流程，移除反向类型副武器逻辑

```csharp
// 移除：bool needMelee = primary.def.IsRangedWeapon;
//       bool needRanged = primary.def.IsMeleeWeapon;
//       bool hasShieldBelt = IsWearingShieldBelt(pawn);
//       if (needRanged && hasShieldBelt) continue;
//       以及 needMelee/needRanged 的类型筛选循环

// 新逻辑：前 2 人分配 EMP 武器
int empCount = 0;
const int MinEmpPawns = 2;

for (int i = 0; i < candidatePawns.Count; i++)
{
    Pawn pawn = candidatePawns[i];
    if (HasEmpSidearm(pawn)) continue;

    if (empCount < MinEmpPawns)
    {
        int empIdx = FindFirstEmpWeaponIndex();
        if (empIdx >= 0)
        {
            AssignSidearm(pawn, candidateWeapons[empIdx], "EMP手雷(评级优先)");
            candidateWeapons[empIdx] = null;
            empCount++;
            continue;
        }
    }

    // 已达 2 人配额或无 EMP 武器可用，不再分配其他副武器
    // 设计意图：每人只选最适合自己的主武器，不携带多个装备
}
```

**改动 3.5**：`CollectCandidateWeapons` 仅收集 EMP 武器

```csharp
// 旧：收集所有武器
// 新：仅收集 EMP 武器（取消其他副武器分配）
private static void CollectCandidateWeapons(Map map)
{
    foreach (Thing weapon in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
    {
        if (!GearDefClassifier.IsEmpWeapon(weapon)) continue;
        candidateWeapons.Add(weapon);
    }
}
```

**改动 3.6**：新增辅助方法

```csharp
/// <summary>
/// 检查 Pawn 库存中是否已持有 EMP 武器。
/// </summary>
private static bool HasEmpSidearm(Pawn pawn)
{
    foreach (Thing item in pawn.inventory.innerContainer)
    {
        if (GearDefClassifier.IsEmpWeapon(item)) return true;
    }
    return false;
}

/// <summary>
/// 在候选武器池中查找首个 EMP 武器索引。
/// </summary>
private static int FindFirstEmpWeaponIndex()
{
    for (int i = 0; i < candidateWeapons.Count; i++)
    {
        Thing w = candidateWeapons[i];
        if (w != null && GearDefClassifier.IsEmpWeapon(w)) return i;
    }
    return -1;
}
```

**改动 3.7**：移除 `HasSidearm`（被 `HasEmpSidearm` 替代）、移除 `IsCandidateSidearm`（被 `IsEmpWeapon` 替代）、移除 `IsWearingShieldBelt`（不再需要）

### 变更 4：PawnCombatProfile 删除

**文件**：`Source/AutoEverything/Allocation/PawnCombatProfile.cs`

**改动**：删除整个文件

**理由**：
- `IsPureMeleeShooter` 不再被 `BeltAllocator` 和 `SidearmAllocator` 使用
- 两个调用方都改为 Role-based 判定
- KISS 原则：删除优于保留无用代码

### 变更 5：CompGearManager 调用点保持不变

**文件**：`Source/AutoEverything/AutoEquipment/CompGearManager.cs`

**说明**：
- L113-115：征召路径的 `CheckMeleeSidearm` 保留（应对玩家手动给副武器的近战切换）
- L204：`EvaluateSidearm` 保留（委托给 `SidearmAllocator`，但内部只做 EMP 手雷分配）
- L491：`BeltAllocator.AllocateForPawn` 保留（委托给 `BeltAllocator`，但内部候选门控已变）
- L703-708：`EvaluateSidearm` 内部委托保留

被调用方内部逻辑变了，调用方代码不需要修改。

### 变更 6：README.md 同步

**文件**：`README.md`

**改动 6.1**：`## 主武器选择规则` 表格移除副武器列，改为说明每人只选主武器

旧：
| 殖民者类型 | 主武器 | 副武器 | 说明 |

新：
| 殖民者类型 | 主武器 | 说明 |

并补充说明："每人只选最适合自己的主武器，不再自动分配反向类型副武器。EMP 手雷作为库存携带的副武器特例，见下方 EMP 手雷全局分配。"

**改动 6.2**：`## 腰带附件全局分配` 章节更新

- 候选：仅重甲前排（Heavy=Brawler）+ belt 层空缺
- 排序：按 `CombatTier` 升序（评级低者优先）
- 分配规则：前 2 人强制分配消防背包，其余配护盾腰带
- 移除"全局保底"逻辑说明
- 表格更新：护盾腰带仅 Heavy +100；消防背包仅 Heavy +60

**改动 6.3**：`## 副武器全局分配` 章节改为 EMP 手雷全局分配

旧标题：副武器全局分配
新标题：EMP 手雷全局分配（库存携带）

新内容：
- 候选：仅 Flexible 后排（IsBackRow）+ 无 EMP 副武器
- 排序：按 `CombatTier` 升序（评级低者优先）
- 分配规则：前 2 人分配 EMP 武器（库存携带）
- 取消反向类型副武器：每人只选主武器，不携带多个装备

**改动 6.4**：`## 副武器类型选择规则` 表格移除，改为 EMP 手雷特例说明

**改动 6.5**：`## 主武器选择规则` 表格下方"护盾腰带约束"段落更新

补充说明：护盾腰带仅分配给重甲前排（Brawler），自由后排（Flexible）与轻甲工人（Light）不参与腰带分配。

**改动 6.6**：`## 角色检测规则` 章节下方"关键约束"段落补充

新增说明：仅 Brawler 角色（基于特质或技能判定）允许装备近战武器；Worker/Doctor/Pacifist/Default（Light）与 Shooter/Hunter/Leader（Flexible）在武器评分时会触发 `Veto(-9000f)` 拒绝近战武器。

### 变更 7：翻译 XML 同步

**文件**：
- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml`
- `Languages/English/Keyed/AE_Keyed.xml`

**改动**：检查 `AE_TT_Sidearms`、`AE_TT_AutoMeleeSidearm` 等 tooltip 是否需要更新描述（取消反向副武器后描述应调整）

### 变更 8：make check 验证

执行 `make check` 确保零警告零错误。失败则修复后再次验证。

## 四、文件改动清单

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `Source/AutoEverything/AutoEquipment/Scoring/Weapon/WeaponTraitScorer.cs` | 修改 | 新增 Role-based 近战 veto |
| `Source/AutoEverything/Allocation/BeltAllocator.cs` | 修改 | 候选门控改 Role-based + 消防背包优先级重构 |
| `Source/AutoEverything/Allocation/SidearmAllocator.cs` | 修改 | 取消反向副武器 + EMP 给 Flexible 后排 |
| `Source/AutoEverything/Allocation/PawnCombatProfile.cs` | 删除 | 不再使用 |
| `README.md` | 修改 | 4 个章节同步更新 |
| `Languages/ChineseSimplified/Keyed/AE_Keyed.xml` | 修改 | tooltip 描述更新（如需要） |
| `Languages/English/Keyed/AE_Keyed.xml` | 修改 | tooltip 描述更新（如需要） |

## 五、风险评估

### 风险 1：删除 PawnCombatProfile 可能影响其他调用方

**检查**：Grep `IsPureMeleeShooter` 全项目引用，确认仅 BeltAllocator 和 SidearmAllocator 使用。

**缓解**：删除前再次 Grep 确认。

### 风险 2：CombatTier 升序排序可能让评级高的重甲前排拿不到护盾腰带

**场景**：地图上只有 1 个护盾腰带 + 1 个消防背包，重甲前排有 3 人（评级 D/C/S）。

**结果**：
- 候选排序：D → C → S
- D 拿消防背包（前 2 人配额）
- C 拿护盾腰带（评分选择）
- S 无腰带可拿

**评估**：符合用户需求"优先给评级较低者"，且重甲前排候选池通常较小（Brawler 角色数量有限），不会出现大量高评级无腰带的情况。

### 风险 3：EMP 手雷候选池可能为空

**场景**：地图上无 EMP 武器。

**结果**：`FindFirstEmpWeaponIndex` 返回 -1，前 2 人跳过分配，无人拿到 EMP。

**评估**：符合预期，无 EMP 武器时不强制分配。

### 风险 4：征召路径 CheckMeleeSidearm 保留但无副武器可切换

**场景**：玩家未手动给副武器，征召时受近战攻击。

**结果**：`CheckMeleeSidearm` 检测库存无近战副武器，直接 return。

**评估**：符合"取消携带多个装备"需求，玩家若需近战切换可手动给副武器。

## 六、验证清单

实施完成后逐项验证：

- [ ] `make check` 通过（0 警告 0 错误）
- [ ] WeaponTraitScorer: Light/Flexible + 近战 → veto -9000
- [ ] BeltAllocator: 仅 Heavy 进入候选池
- [ ] BeltAllocator: 前 2 人配消防背包，其余配护盾腰带
- [ ] SidearmAllocator: 仅 Flexible 后排进入候选池
- [ ] SidearmAllocator: 前 2 人配 EMP 手雷，其余不分配
- [ ] PawnCombatProfile.cs 已删除
- [ ] README.md 4 个章节已同步
- [ ] 翻译 XML tooltip 已更新（如需要）

## 七、实施顺序

1. 变更 1：WeaponTraitScorer 新增 Role-based veto
2. 变更 2：BeltAllocator 重构
3. 变更 3：SidearmAllocator 重构
4. 变更 4：PawnCombatProfile 删除
5. `make check` 验证编译
6. 变更 6：README.md 同步
7. 变更 7：翻译 XML 同步
8. `make check` 最终验证
