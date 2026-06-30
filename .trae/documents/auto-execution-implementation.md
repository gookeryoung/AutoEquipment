# 工作自动配置 + 人员自动评级 实现方案

## Summary

将 ITab 底部的"全局人物评级"与"全局工作重配"两个手动按钮改为勾选框控制的自动执行：勾选时立即执行一次并启用周期自动执行（每 3000 tick + 新增殖民者时立即触发），默认勾选。"全局装备重配"按钮保留不变。

## Current State Analysis（Phase 1 探索已验证）

### 现状确认
- **AESettings.cs**：`autoWorkEnabled`（line 20，默认 true，line 452 持久化）已存在；`autoTierTag` 字段**不存在**，需新增。`ApplyTierTagsWithDefaultSort()`（line 247）与 `ClearTierTagsFromAllPawns()`（line 167）已实现。
- **CompGearManager.cs**：CompTick（line 67-69）仍是 `if (!AESettings.enabled || locked) return;` 合并检查，需拆分。
- **ITab_GearManager.cs**：底部区（line 313-391）仍是三按钮布局（全局人物评级 / 全局装备重配 / 全局工作重配[受 autoWorkEnabled 门控]）。
- **AutoExecutor.cs**：**不存在**，需新建。
- **AE_Keyed.xml**（中英）：已有 `AE_AutoWork`/`AE_GlobalWorkReallocate`/`AE_GlobalTierTag` 等键，缺 `AE_AutoTierTag`/`AE_AutoWorkConfig` 及 tooltip。
- **WorkAllocator.ReallocateAll()**（line 45）：返回受影响殖民者数，已存在。
- **SidearmAllocator/BeltAllocator**：已有 `lastAllocationTick + 间隔检查` 静态门控模式可借鉴。

### 已确认的设计模式
- 静态门控模式：`lastAllocationTick` 字段 + 间隔检查，由 CompTick 触发
- 错误隔离：`Log.ErrorOnce(message, thingIDNumber ^ salt)`，每个错误点独立 salt
- 文字防换行：`Text.WordWrap = false` 包裹 CheckboxLabeled
- LookCompat 持久化：旧键 + `ae_` 前缀双读

## Proposed Changes

### 1. 新建 `Source/AutoEverything/Core/AutoExecutor.cs`

**文件路径**：`e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Source\AutoEverything\Core\AutoExecutor.cs`
**命名空间**：`AutoEverything.Core`
**类型**：`internal static class`

**静态字段**：
```csharp
private const int ExecuteInterval = 3000;   // 周期触发间隔
private const int CheckInterval = 60;       // 殖民者数量检查间隔
private static int lastCheckTick = -9999;
private static int lastWorkTick = -9999;
private static int lastTierTick = -9999;
private static int lastColonistCount = -1;   // -1 = 首次只记录不触发
private const int WorkErrorSalt = 0xA200;
private const int TierErrorSalt = 0xA300;
```

**公开方法**：
- `TryTick()` — 由 CompTick 每 tick 调用。静态门控每 60 tick 一次实际检查。
  - 首次初始化守卫：`lastWorkTick < 0` 或 `lastTierTick < 0` 时设为当前 tick，不触发执行
  - 新增殖民者检测：`PawnsFinder.AllMaps_FreeColonists.Count` 比 `lastColonistCount` 增加 → 立即触发工作+评级（不弹消息）
  - 周期触发：tick - lastWorkTick ≥ 3000 → 触发工作（不弹消息）；评级同理
- `TriggerWorkNow()` — ITab 勾选时调用，立即执行 `ExecuteWork(Find.TickManager.TicksGame, showMessage: true)`
- `TriggerTierNow()` — ITab 勾选时调用，立即执行 `ExecuteTier(Find.TickManager.TicksGame, showMessage: true)`

**私有方法**：
```csharp
private static void ExecuteWork(int tick, bool showMessage)
{
    lastWorkTick = tick;
    if (!AESettings.autoWorkEnabled) return;
    try
    {
        int n = WorkAllocator.ReallocateAll();
        AEDebug.Log(() => $"[AutoExecutor] 工作自动配置: {n} 个殖民者 (tick={tick})");
        if (showMessage)
            Messages.Message("AE_GlobalWorkReallocateResult".Translate(n), MessageTypeDefOf.TaskCompletion);
    }
    catch (Exception ex)
    {
        Log.ErrorOnce("[AutoEverything] 工作自动配置失败: " + ex.Message, WorkErrorSalt);
    }
}

private static void ExecuteTier(int tick, bool showMessage)
{
    lastTierTick = tick;
    if (!AESettings.autoTierTag) return;
    try
    {
        int n = AESettings.ApplyTierTagsWithDefaultSort();
        AEDebug.Log(() => $"[AutoExecutor] 人员自动评级: {n} 个殖民者 (tick={tick})");
        if (showMessage)
            Messages.Message("AE_TierTag_ApplyResult".Translate(n), MessageTypeDefOf.TaskCompletion);
    }
    catch (Exception ex)
    {
        Log.ErrorOnce("[AutoEverything] 人员自动评级失败: " + ex.Message, TierErrorSalt);
    }
}
```

**using 列表**：`System;` `RimWorld;` `Verse;` `AutoEverything.AutoWork;`

### 2. 改 `Source/AutoEverything/Core/AESettings.cs`

**新增字段**（line 20 `autoWorkEnabled` 后）：
```csharp
public static bool autoTierTag = true;  // 人员自动评级（周期触发 + 新增人员触发）
```

**ExposeData 持久化**（line 452 `LookCompat(ref autoWorkEnabled, ...)` 后）：
```csharp
LookCompat(ref autoTierTag, "autoTierTag", true);
```

### 3. 改 `Source/AutoEverything/AutoEquipment/CompGearManager.cs`

**CompTick 入口**（line 67-69）：

原：
```csharp
public override void CompTick()
{
    if (!AESettings.enabled || locked) return;
```

改为：
```csharp
public override void CompTick()
{
    if (!AESettings.enabled) return;

    // 全局自动执行（工作重配 + 人员评级）：静态门控，每 60 tick 检查一次
    // 放在 locked 检查之前：即使本 Pawn 被锁定，全局自动执行仍应为其他殖民者运行
    AutoExecutor.TryTick();

    if (locked) return;
```

**新增 using**：`using AutoEverything.Core;` 已存在（同命名空间），无需新增。但 `AutoExecutor` 在 `AutoEverything.Core` 命名空间下，CompGearManager 在 `AutoEverything.AutoEquipment`，需新增 `using AutoEverything.Core;`（若不存在）。

### 4. 改 `Source/AutoEverything/UI/ITab_GearManager.cs`

**布局常量**（line 141-147）：

原：
```csharp
float buttonHeight = 30f;
float buttonGap = 8f;
// contentRect 高度预留 buttonHeight*3 + buttonGap*3
Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight * 3 + buttonGap * 3));
```

改为：
```csharp
float buttonHeight = 30f;
float buttonGap = 8f;
float checkboxHeight = 24f;
// 底部区：2 勾选框 + 1 按钮 + 3 间隔
Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (checkboxHeight * 2 + buttonHeight + buttonGap * 3));
```

**底部区重写**（替换 line 313-391 整段）：

布局顺序（自上而下）：
1. 人员自动评级勾选框（`AESettings.autoTierTag`）
   - 绘制前保存 `prevTierTag = AESettings.autoTierTag`
   - CheckboxLabeled 后检测变化：`if (AESettings.autoTierTag != prevTierTag)`
     - 若变 true：`AutoExecutor.TriggerTierNow()`
     - 若变 false：`AESettings.ClearTierTagsFromAllPawns()` + 弹清除结果消息
   - Tooltip：`AE_TT_AutoTierTag`
2. 工作自动配置勾选框（`AESettings.autoWorkEnabled`）
   - 绘制前保存 `prevWork = AESettings.autoWorkEnabled`
   - CheckboxLabeled 后检测变化：
     - 若变 true：`AutoExecutor.TriggerWorkNow()`
     - 若变 false：仅停止自动（无副作用，无消息）
   - Tooltip：`AE_TT_AutoWorkConfig`
3. 全局装备重配按钮（保留原逻辑，打开 `Dialog_GlobalReallocate`）

**勾选框绘制模板**（文字防换行强制）：
```csharp
Rect tierCheckRect = new Rect(rect.x, contentRect.yMax + buttonGap, rect.width, checkboxHeight);
bool prevWrap = Text.WordWrap;
Text.WordWrap = false;
bool prevTierTag = AESettings.autoTierTag;
Widgets.CheckboxLabeled(tierCheckRect, "AE_AutoTierTag".Translate(), ref AESettings.autoTierTag);
Text.WordWrap = prevWrap;
TooltipHandler.TipRegion(tierCheckRect, "AE_TT_AutoTierTag".Translate());
if (AESettings.autoTierTag != prevTierTag)
{
    if (AESettings.autoTierTag) AutoExecutor.TriggerTierNow();
    else
    {
        int n = AESettings.ClearTierTagsFromAllPawns();
        Messages.Message("AE_TierTag_ClearResult".Translate(n), MessageTypeDefOf.TaskCompletion);
    }
}
```

**移除原 `if (AESettings.autoWorkEnabled)` 条件门控**：工作勾选框始终显示，让玩家可随时切换。

### 5. 改翻译文件（中英文同步）

**ChineseSimplified/Keyed/AE_Keyed.xml** 新增（在 AutoWork 注释块附近）：
```xml
<AE_AutoTierTag>人员自动评级</AE_AutoTierTag>
<AE_AutoWorkConfig>工作自动配置</AE_AutoWorkConfig>
<AE_TT_AutoTierTag>勾选后自动执行人员评级：每 3000 tick 一次，新增殖民者时立即触发。
勾选时立即执行一次；取消勾选时清除所有评级标签并恢复原名。</AE_TT_AutoTierTag>
<AE_TT_AutoWorkConfig>勾选后自动执行工作重配：每 3000 tick 一次，新增殖民者时立即触发。
勾选时立即执行一次；取消勾选仅停止自动执行，保留当前工作分配。</AE_TT_AutoWorkConfig>
```

**更新 `AE_AutoWork` 文案**：从"自动工作分配"改为"工作自动配置"（与 ITab 的 `AE_AutoWorkConfig` 对齐，两处均绑定 `autoWorkEnabled`）。
```xml
<AE_AutoWork>工作自动配置</AE_AutoWork>
```

**English/Keyed/AE_Keyed.xml** 对应新增：
```xml
<AE_AutoTierTag>Auto Tier Rating</AE_AutoTierTag>
<AE_AutoWorkConfig>Auto Work Config</AE_AutoWorkConfig>
<AE_TT_AutoTierTag>When enabled, automatically applies tier rating: every 3000 ticks and immediately when new colonists arrive.
Checking it triggers one immediate execution; unchecking clears all tier tags and restores original names.</AE_TT_AutoTierTag>
<AE_TT_AutoWorkConfig>When enabled, automatically reallocates work priorities: every 3000 ticks and immediately when new colonists arrive.
Checking it triggers one immediate execution; unchecking only stops auto execution, current work priorities are retained.</AE_TT_AutoWorkConfig>
```

**更新 `AE_AutoWork`**：从"Auto Work Assignment"改为"Auto Work Config"。

旧键 `AE_GlobalTierTag`/`AE_GlobalWorkReallocate`/`AE_TT_GlobalTierTag`/`AE_TT_GlobalWorkReallocate` 保留不删，避免破坏存档兼容。

### 6. 改 README.md

**A. 评估周期表格**（若有）：新增 3 行
| 模块 | 周期 | 触发条件 |
|------|------|----------|
| AutoExecutor 殖民者检查 | 60 tick | 数量增加时立即触发工作+评级 |
| 工作自动配置 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选 |
| 人员自动评级 | 3000 tick | 周期 + 新增殖民者 + ITab 勾选 |

**B. 全局人物评级标签章节**：改为描述勾选框行为
- 勾选时立即执行一次 + 启用自动执行
- 取消勾选时清除所有评级标签恢复原名
- 自动执行：每 3000 tick + 新增殖民者

**C. 自动工作分配章节入口**：改为描述勾选框行为
- 勾选时立即执行一次 + 启用自动执行
- 取消勾选仅停止自动，保留当前工作分配
- 自动执行：每 3000 tick + 新增殖民者

**D. 同步检查清单**（`.trae/rules/autoeverything-project.md`）：新增 AutoExecutor.cs / ITab 底部勾选框的同步条目。

## Assumptions & Decisions

1. **触发间隔**：3000 tick（约 50 秒）—— 已与用户确认
2. **装备重配按钮**：保留为按钮 —— 已与用户确认
3. **新增人员检测**：数量对比（`PawnsFinder.AllMaps_FreeColonists.Count`）—— 已与用户确认
4. **取消勾选副作用**：评级清除 + 工作保留 —— 已与用户确认
5. **字段决策**：复用 `autoWorkEnabled` + 新增 `autoTierTag`（均默认 true）—— 已与用户确认
6. **自动执行入口**：复用 CompTick + 静态门控（不新增 MapComponent）—— KISS 原则
7. **自动周期路径不弹消息框**（避免刷屏），仅走 `AEDebug.Log`
8. **手动触发路径弹 `Messages.Message`** 给玩家反馈
9. **旧翻译键保留**：避免破坏存档兼容性

## Verification Steps

1. **编译验证**：`make check` 通过（0 警告 0 错误）
2. **游戏内验证**：
   - 新建殖民地：两项默认勾选，殖民者出现后 3000 tick 自动执行
   - 新增殖民者（加入/出生）：立即触发工作重配 + 人员评级
   - 取消人员评级勾选：评级标签被清除，殖民者 Nick 恢复原名
   - 取消工作配置勾选：工作优先级保留不变，仅停止自动执行
   - 重新勾选：立即执行一次
3. **同步检查**：README.md 章节已更新，翻译键完整

## 实现步骤顺序

1. 新建 `AutoExecutor.cs`
2. 改 `AESettings.cs` 加 `autoTierTag` 字段 + 持久化
3. 改 `CompGearManager.cs` CompTick 插入 `AutoExecutor.TryTick()`
4. 改 `ITab_GearManager.cs` 布局常量 + 底部区重写
5. 改翻译 XML（中英文同步）
6. 改 `README.md`（同步章节）
7. `make check` 验证零警告零错误
