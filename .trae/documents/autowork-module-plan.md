# AutoWork 模块新增 + 规则文件完善计划

## 背景与目标

用户请求两项工作：
1. **完善 `.trae/rules/rimworld-mod-dev.md`**：结合最佳实践补充错误处理、代码组织、测试验证等章节
2. **新增 AutoWork 模块**：自动分配殖民者工作优先级，按兴趣（Passion）与全局评级（CombatTier）智能分配

### 工作优先级规则（用户确认）

| 工作类型 | 分配规则 |
|---------|---------|
| 紧急工作（灭火/就医/卧床） | 全部 → 优先级 1 |
| 技能工作 + 有兴趣 | → 优先级 2 |
| 技能工作 + 无兴趣 | → 优先级 0（禁用） |
| 技能工作 + 全殖民地无人有兴趣 | 取技能等级最高的 2 人 → 优先级 3，其余 → 0 |
| 搬运/清洁 + S 档 | → 优先级 4（高价值殖民者少做杂活） |
| 搬运/清洁 + D/X 档 | → 优先级 1（低价值殖民者多做杂活） |
| 搬运/清洁 + A/B/C 档 | → 优先级 3 |
| 非技能工作（如开关） | → 优先级 3（所有人都能做） |

### 触发方式

- MOD 选项开关 `autoWorkEnabled` 控制是否启用
- ITab 底部新增"全局工作重配"按钮手动触发
- 执行前自动检查 `Find.PlaySettings.useWorkPriorities`，若未启用则自动启用

## 现有模式复用

基于 Phase 1 探索，复用以下已有模式：

| 模式 | 参考文件 | 复用点 |
|------|---------|--------|
| 静态分配器 + 静态候选缓存 | [BeltAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Allocation/BeltAllocator.cs) | `ReallocateAll` 方法结构、Pawn 过滤链 |
| 设置字段 + Scribe + UI | [AESettings.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.cs) L14-83, L415-485, L594-720 | `LookCompat` 双读、`CheckboxLabeled` |
| ITab 底部按钮 | [ITab_GearManager.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/ITab_GearManager.cs) L304-360 | `buttonRect.yMax + buttonGap` 偏移链 |
| CombatTier 获取 | [CombatEvaluator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs) L135-144 | `GetCombatTier(pawn)` |
| 技能/兴趣访问 | [CombatEvaluator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/RoleEvaluation/CombatEvaluator.cs) L85-92, L351-355 | `pawn.skills.GetSkill(SkillDefOf.X).passion` |
| Pawn 过滤链 | [GlobalAllocator.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Allocation/GlobalAllocator.cs) L49-110 | IsGhoul/CanManageGear/IsSlave/Dead/Downed |
| 翻译键命名 | [AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/ChineseSimplified/Keyed/AE_Keyed.xml) | `AE_<类别>_<名称>` 前缀规范 |

## 实施方案

### 变更 1：完善 rimworld-mod-dev.md

**文件**：[.trae/rules/rimworld-mod-dev.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/.trae/rules/rimworld-mod-dev.md)

**新增章节**：

1. **Karpathy 四原则**（在"编码规范"章节内展开）：
   - 简单优于复杂（Simple over Complex）
   - 删除优于扩展（Delete over Extend）
   - 理解优于记忆（Understand over Memorize）
   - 原型优于规划（Prototype over Plan）

2. **错误处理与日志**（新章节，在"性能"之后）：
   - Tick 路径必须 try-catch 隔离，单 Pawn 失败不影响其他
   - `Log.ErrorOnce(message, id)` 防重复，id 用 `thingIDNumber ^ salt`
   - DLC API 调用必须异常隔离（参考 `DLCCompat.cs` 模式）
   - 禁止静默吞异常，至少 `Log.Error` 记录

3. **代码组织**（新章节，在"编码规范"之后）：
   - 单一职责：一个类/文件只做一件事（参考 `BeltAllocator` 只管腰带）
   - 文件大小上限：单文件超 500 行考虑拆分（参考 `SGSettings.cs` 拆分先例）
   - 模块边界：跨命名空间引用必须显式 `using`，禁止循环依赖
   - 静态类模式：无状态的分配器/评估器用静态类 + 静态缓存

4. **测试与验证**（新章节，在"通用工作流"之后）：
   - 编译验证：`make check`（每次修改后）
   - 完整重建：`make rebuild-check`（大改动后）
   - 游戏内验证清单：
     - 无 DLC 环境启动无报错
     - 有 DLC 环境功能正常
     - 旧存档加载不丢失数据
   - 边界用例：空地图、单殖民者、全奴隶、全食尸鬼

### 变更 2：新增 AutoWork 模块

**新建文件**：`Source/AutoEverything/AutoWork/WorkAllocator.cs`

```csharp
namespace AutoEverything.AutoWork
{
    /// <summary>
    /// 全局工作优先级分配器。
    /// 按兴趣与 CombatTier 智能分配殖民者的工作优先级。
    /// </summary>
    public static class WorkAllocator
    {
        // 候选殖民者缓存（复用避免 GC，虽然按钮触发非 Tick 路径）
        private static readonly List<Pawn> candidatePawns = new List<Pawn>();
        
        // 临时评分缓存（用于"无人有兴趣"兜底排序）
        private static readonly List<KeyValuePair<Pawn, float>> scoredPawns = new List<KeyValuePair<Pawn, float>>();
        
        // WorkTypeDef 缓存（懒加载，避免静态字段初始化器跨线程问题）
        private static List<WorkTypeDef> cachedWorkTypes;
        
        public static int ReallocateAll()
        {
            // 1. 自动启用"自定义优先级"开关
            if (!Find.PlaySettings.useWorkPriorities)
            {
                Find.PlaySettings.useWorkPriorities = true;
            }
            
            // 2. 收集候选殖民者
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
            
            // 3. 懒加载 WorkTypeDef 列表
            if (cachedWorkTypes == null)
                cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            
            // 4. 遍历所有工作类型，按规则分配优先级
            foreach (WorkTypeDef workType in cachedWorkTypes)
            {
                AssignPrioritiesForWorkType(workType);
            }
            
            return candidatePawns.Count;
        }
        
        private static void AssignPrioritiesForWorkType(WorkTypeDef workType)
        {
            // 分类判断
            bool isEmergency = (workType.workTags & (WorkTags.Firefighting | WorkTags.PatientEmergency | WorkTags.PatientBedRest)) != 0;
            bool isHaulingOrCleaning = workType.defName == "Hauling" || workType.defName == "Cleaning";
            bool hasSkills = workType.relevantSkills != null && workType.relevantSkills.Count > 0;
            
            if (isEmergency)
            {
                // 规则 1：紧急工作 → 全部优先级 1
                foreach (Pawn pawn in candidatePawns)
                {
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    pawn.workSettings.SetPriority(workType, 1);
                }
            }
            else if (isHaulingOrCleaning)
            {
                // 规则 3：搬运/清洁 → 按档位分配
                foreach (Pawn pawn in candidatePawns)
                {
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
                // 规则 4：非技能工作（如开关）→ 全部优先级 3
                foreach (Pawn pawn in candidatePawns)
                {
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    pawn.workSettings.SetPriority(workType, 3);
                }
            }
        }
        
        private static void AssignSkillWorkPriorities(WorkTypeDef workType)
        {
            // 第一遍：检查是否有人对该工作的相关技能有兴趣
            bool anyPassion = false;
            foreach (Pawn pawn in candidatePawns)
            {
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
                foreach (Pawn pawn in candidatePawns)
                {
                    if (pawn.WorkTagIsDisabled(workType.workTags)) continue;
                    int priority = HasPassionForAnySkill(pawn, workType.relevantSkills) ? 2 : 0;
                    pawn.workSettings.SetPriority(workType, priority);
                }
            }
            else
            {
                // 全殖民地无人有兴趣：取技能等级最高的 2 人 → 优先级 3，其余 → 0
                scoredPawns.Clear();
                foreach (Pawn pawn in candidatePawns)
                {
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
```

**命名空间**：`AutoEverything.AutoWork`（匹配文件夹 `Source/AutoEverything/AutoWork/`）

### 变更 3：AESettings.cs 添加 AutoWork 设置

**文件**：[AESettings.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.cs)

**新增字段**（在第 19 行 `sidearms` 之后）：

```csharp
// AutoWork 自动工作分配
public static bool autoWorkEnabled = true;  // 主开关（默认启用）
```

**ExposeData 新增**（在第 450 行 `debugLogging` 之后）：

```csharp
LookCompat(ref autoWorkEnabled, "autoWorkEnabled", true);
```

**DrawSettings 新增**（左列，第 621 行 `sidearms` checkbox 之后）：

```csharp
l.CheckboxLabeled("AE_AutoWork".Translate(), ref autoWorkEnabled);
```

### 变更 4：ITab_GearManager.cs 新增"全局工作重配"按钮

**文件**：[ITab_GearManager.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/ITab_GearManager.cs)

**修改 1**：面板尺寸（第 112 行）

```csharp
// 原：size = new Vector2(360f, 560f);
// 改：增加 40f 容纳第三个按钮
size = new Vector2(360f, 600f);
```

**修改 2**：内容区高度公式（第 143 行）

```csharp
// 原：rect.height - (buttonHeight * 2 + buttonGap * 2)
// 改：三按钮区
Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight * 3 + buttonGap * 3));
```

**修改 3**：在第 360 行（`TooltipHandler.TipRegion(buttonRect, ...)` 之后）新增第三个按钮：

```csharp
// 第三个按钮：全局工作重配
if (AESettings.autoWorkEnabled)
{
    Rect workBtnRect = new Rect(
        rect.x,
        buttonRect.yMax + buttonGap,
        rect.width,
        buttonHeight);

    Color prevWorkBg = GUI.backgroundColor;
    GUI.backgroundColor = ColorPrimaryBtnBg;
    if (Widgets.ButtonText(workBtnRect, "AE_GlobalWorkReallocate".Translate()))
    {
        int triggered = AutoEverything.AutoWork.WorkAllocator.ReallocateAll();
        Messages.Message(
            "AE_GlobalWorkReallocateResult".Translate(triggered),
            MessageTypeDefOf.TaskCompletion);
    }
    GUI.backgroundColor = prevWorkBg;
    TooltipHandler.TipRegion(workBtnRect, "AE_TT_GlobalWorkReallocate".Translate());
}
```

**注意**：需在文件顶部添加 `using AutoEverything.AutoWork;`

### 变更 5：翻译键新增

**文件 1**：[Languages/ChineseSimplified/Keyed/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/ChineseSimplified/Keyed/AE_Keyed.xml)

```xml
<!-- AutoWork 自动工作 -->
<AE_AutoWork>自动工作分配</AE_AutoWork>
<AE_GlobalWorkReallocate>全局工作重配</AE_GlobalWorkReallocate>
<AE_GlobalWorkReallocateResult>已为 {0} 个殖民者重新分配工作优先级</AE_GlobalWorkReallocateResult>
<AE_TT_GlobalWorkReallocate>按兴趣与评级重新分配所有殖民者的工作优先级。自动启用自定义优先级开关。紧急工作=1，有兴趣的技能=2，搬运清洁按档位=1/3/4，无人有兴趣的技能取前2人=3。</AE_TT_GlobalWorkReallocate>
```

**文件 2**：[Languages/English/Keyed/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/English/Keyed/AE_Keyed.xml)

```xml
<!-- AutoWork -->
<AE_AutoWork>Auto Work Assignment</AE_AutoWork>
<AE_GlobalWorkReallocate>Global Work Reallocate</AE_GlobalWorkReallocate>
<AE_GlobalWorkReallocateResult>Reassigned work priorities for {0} colonists</AE_GlobalWorkReallocateResult>
<AE_TT_GlobalWorkReallocate>Reassign all colonists' work priorities by passion and tier. Auto-enables custom priorities. Emergency=1, passionate skills=2, hauling/cleaning by tier=1/3/4, unpassionate skills top 2=3.</AE_TT_GlobalWorkReallocate>
```

### 变更 6：README.md 新增 AutoWork 章节

**文件**：[README.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/README.md)

在"## 全局重配"章节之后新增：

```markdown
## 自动工作分配（AutoWork）

`AutoWork/WorkAllocator.cs` 提供基于兴趣与全局评级的工作优先级自动分配。

### 分配规则

| 工作类型 | 优先级 | 说明 |
|---------|--------|------|
| 紧急工作（灭火/就医/卧床） | 1 | 全部殖民者 |
| 技能工作 + 有兴趣 | 2 | 有 Passion 的工作 |
| 技能工作 + 无兴趣 | 0 | 禁用 |
| 技能工作 + 全殖民地无人有兴趣 | 3（前2人）/ 0（其余） | 按技能等级取前2人兜底 |
| 搬运/清洁 + S 档 | 4 | 高价值殖民者少做杂活 |
| 搬运/清洁 + D/X 档 | 1 | 低价值殖民者多做杂活 |
| 搬运/清洁 + A/B/C 档 | 3 | 中等 |
| 非技能工作（开关等） | 3 | 全部殖民者 |

### 自定义优先级自动启用

执行全局工作重配时，若 `Find.PlaySettings.useWorkPriorities` 未启用，自动启用为 true，否则 1-4 优先级系统不生效。

### 入口

- MOD 选项 → 启用/禁用"自动工作分配"
- 殖民者装备面板（ITab）底部 → "全局工作重配"按钮
```

### 变更 7：autoeverything-project.md 更新模块职责

**文件**：[.trae/rules/autoeverything-project.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/.trae/rules/autoeverything-project.md)

**修改 1**：命名空间映射表新增一行：

```
- `Source/AutoEverything/AutoWork/*` → `namespace AutoEverything.AutoWork`
```

**修改 2**：模块职责章节新增：

```
- **AutoWork**：自动工作优先级分配（`WorkAllocator`）
```

## 验证步骤

### 编译验证

```bash
make check
```

预期：零警告零错误，输出 `Assemblies/AutoEverything.dll`。

### 功能验证清单

- [ ] `make check` 通过
- [ ] 游戏启动无报错（无 DLC 环境）
- [ ] 游戏启动无报错（有 DLC 环境）
- [ ] MOD 选项显示"自动工作分配"复选框
- [ ] ITab 底部显示"全局工作重配"按钮（仅当 autoWorkEnabled=true）
- [ ] 点击按钮后显示结果消息"已为 N 个殖民者重新分配工作优先级"
- [ ] `Find.PlaySettings.useWorkPriorities` 自动启用
- [ ] 灭火工作优先级=1（所有人）
- [ ] 有兴趣的技能工作优先级=2
- [ ] 无兴趣的技能工作优先级=0
- [ ] 全殖民地无人有兴趣的技能工作前2人=3
- [ ] S 档搬运/清洁=4
- [ ] D/X 档搬运/清洁=1
- [ ] A/B/C 档搬运/清洁=3

## 假设与决策

1. **不创建独立 CompWorkManager**：WorkAllocator 仅由按钮触发，不需要 ThingComp 注入。若未来需要自动周期分配，再创建独立 Comp。
2. **不创建 Dialog_GlobalWorkReallocate**：工作重配规则固定（不可调），直接按钮触发+消息反馈，比装备重配简单。
3. **按钮条件显示**：仅当 `autoWorkEnabled=true` 时显示"全局工作重配"按钮，避免关闭 AutoWork 后仍显示无用按钮。
4. **非技能工作优先级=3**：Flicking 等无技能关联的工作，所有人设为优先级 3（中等工作优先级）。
5. **未成年保留**：未成年殖民者可参与工作，不被过滤（Biotech 允许）。
6. **跳过奴隶**：奴隶有独立的工作管理系统，不参与 AutoWork。
7. **WorkTypeDef 懒加载**：避免静态字段初始化器调用 DefDatabase（遵守项目规则），改用首次调用时懒加载。
