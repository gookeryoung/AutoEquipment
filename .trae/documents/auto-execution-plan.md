# 工作自动配置 + 人员自动评级 实现方案

## Context

当前 ITab 底部有三个手动按钮：全局人物评级、全局装备重配、全局工作重配。玩家需要手动点击才能执行评级标签应用与工作优先级重配，频繁操作繁琐。

本方案将"全局人物评级"和"全局工作重配"改为勾选框控制的自动执行：
- 勾选时立即执行一次，并启用每 3000 tick 自动执行 + 新增殖民者时立即触发
- 默认勾选
- "全局装备重配"按钮保留不变

**取消勾选的副作用**（已与用户确认）：
- 人员评级取消勾选 → 清除所有评级标签恢复原名（保留原 FloatMenu 的清除功能）
- 工作配置取消勾选 → 仅停止自动执行，保留当前工作分配（工作优先级无法撤销）

## 设计决策

### 自动执行入口：复用 CompTick + 静态门控（不新增 MapComponent）

项目已有成熟的"全局静态门控"模式（`SidearmAllocator`/`BeltAllocator` 的 `lastAllocationTick` + 间隔检查），由 `CompGearManager.CompTick` 触发。本方案沿用此模式，新增 `AutoExecutor` 静态类，由 `CompTick` 每 tick 调用 `TryTick()`，内部静态门控每 60 tick 仅一次实际检查。

不新增 `MapComponent` 的理由：KISS 原则，`CompTick` 已是现成的每 tick 入口，加一层静态门控即可。

### 字段决策：复用 `autoWorkEnabled` + 新增 `autoTierTag`

- **工作自动配置**：复用现有 `AESettings.autoWorkEnabled`（默认 true，已持久化）。勾选 = 启用并立即执行，取消勾选 = 关闭 AutoWork。
- **人员自动评级**：新增 `AESettings.autoTierTag`（默认 true）。

### 新增殖民者检测：数量对比

在 `AutoExecutor.TryTick()` 内记录 `lastColonistCount`，用 `PawnsFinder.AllMaps_FreeColonists.Count` 对比。计数增加时立即触发工作+评级。`lastColonistCount` 初始 -1，首次检查只记录不触发，避免存档加载误触发。

## 文件改动清单

### 1. 新建 `Source/AutoEverything/Core/AutoExecutor.cs`

命名空间 `AutoEverything.Core`。静态类，负责工作重配与人员评级的周期/新增殖民者触发。

**静态字段**：
- `ExecuteInterval = 3000`（周期触发间隔）
- `CheckInterval = 60`（殖民者数量检查间隔）
- `lastCheckTick`、`lastWorkTick`、`lastTierTick`、`lastColonistCount`（初始 -1）
- `WorkErrorSalt = 0xA200`、`TierErrorSalt = 0xA300`（错误去重 salt）

**公开方法**：
- `TryTick()` — 由 CompTick 每 tick 调用，静态门控每 60 tick 检查一次。逻辑：首次初始化守卫 → 新增殖民者检测（计数增加立即触发，不弹消息）→ 周期触发（3000 tick，不弹消息）
- `TriggerWorkNow()` — ITab 勾选时调用，立即执行并弹消息框
- `TriggerTierNow()` — ITab 勾选时调用，立即执行并弹消息框

**私有方法**：
- `ExecuteWork(int tick, bool showMessage)` — 调用 `WorkAllocator.ReallocateAll()`，try-catch + `Log.ErrorOnce`
- `ExecuteTier(int tick, bool showMessage)` — 调用 `AESettings.ApplyTierTagsWithDefaultSort()`，try-catch + `Log.ErrorOnce`

**设计要点**：
- 自动周期路径不弹消息框（避免刷屏），仅走 `AEDebug.Log`
- 手动触发路径弹 `Messages.Message` 给玩家反馈
- 工作与评级各自独立 try-catch，salt 独立
- 新增殖民者检测用 `PawnsFinder.AllMaps_FreeColonists.Count`（不含商队/食尸鬼/奴隶）

### 2. 改 `Source/AutoEverything/Core/AESettings.cs`

**新增字段**（line 20 `autoWorkEnabled` 附近）：
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
    // 放在 locked 检查之前：即使本 Pawn 被锁定，全局重配仍应为其他殖民者执行
    AutoExecutor.TryTick();

    if (locked) return;
```

### 4. 改 `Source/AutoEverything/UI/ITab_GearManager.cs`

**布局常量**（line 141-147）：

原 `buttonHeight*3 + buttonGap*3` 改为 `checkboxHeight*2 + buttonHeight + buttonGap*3`（新增 `checkboxHeight = 24f`）。

**底部区重写**（替换 line 313-391 整段）：

布局顺序（自上而下）：
1. 人员自动评级勾选框（`AESettings.autoTierTag`）
   - 勾选时：`AutoExecutor.TriggerTierNow()`
   - 取消勾选时：`AESettings.ClearTierTagsFromAllPawns()` + 弹清除结果消息
2. 工作自动配置勾选框（`AESettings.autoWorkEnabled`）
   - 勾选时：`AutoExecutor.TriggerWorkNow()`
   - 取消勾选时：仅停止自动（无副作用）
3. 全局装备重配按钮（保留原逻辑，打开 `Dialog_GlobalReallocate`）

**勾选框绘制模板**（文字防换行强制）：
```csharp
bool prevWrap = Text.WordWrap;
Text.WordWrap = false;
Widgets.CheckboxLabeled(rect, "AE_AutoTierTag".Translate(), ref AESettings.autoTierTag);
// ... 状态变化检测 ...
Text.WordWrap = prevWrap;
```

**移除原 `if (AESettings.autoWorkEnabled)` 条件门控**：工作勾选框始终显示，让玩家可随时切换。

### 5. 改翻译文件（中英文同步）

**ChineseSimplified/Keyed/AE_Keyed.xml** 新增：
```xml
<AE_AutoTierTag>人员自动评级</AE_AutoTierTag>
<AE_AutoWorkConfig>工作自动配置</AE_AutoWorkConfig>
<AE_TT_AutoTierTag>勾选后自动执行人员评级：每 3000 tick 一次，新增殖民者时立即触发。
勾选时立即执行一次；取消勾选时清除所有评级标签并恢复原名。</AE_TT_AutoTierTag>
<AE_TT_AutoWorkConfig>勾选后自动执行工作重配：每 3000 tick 一次，新增殖民者时立即触发。
勾选时立即执行一次；取消勾选仅停止自动执行，保留当前工作分配。</AE_TT_AutoWorkConfig>
```

**English/Keyed/AE_Keyed.xml** 新增对应英文键。

旧键 `AE_GlobalTierTag`/`AE_GlobalWorkReallocate` 保留不删（避免破坏兼容），但不再使用。

**Mod 选项标签同步**：`AESettings.cs:624` 的 Mod 选项勾选框仍用 `AE_AutoWork` 翻译键（原"自动工作分配"）。为保持 ITab 与 Mod 选项文案一致，将 `AE_AutoWork` 翻译更新为"工作自动配置"（中英文同步），与 ITab 的 `AE_AutoWorkConfig` 文案对齐。两处均绑定 `autoWorkEnabled` 字段。

### 6. 改 README.md

**A. 评估周期表格**：新增 3 行（AutoExecutor 殖民者检查 60 tick、工作重配 3000 tick、人员评级 3000 tick）

**B. 全局人物评级标签章节**：改为描述勾选框行为（勾选立即执行 + 自动执行，取消勾选清除标签）

**C. 自动工作分配章节入口**：改为描述勾选框行为（勾选立即执行 + 自动执行，取消勾选仅停止）

**D. 同步检查清单**：新增 AutoExecutor.cs / ITab 底部勾选框的同步条目

## 实现步骤

1. 新建 `AutoExecutor.cs`
2. 改 `AESettings.cs` 加 `autoTierTag` 字段 + 持久化
3. 改 `CompGearManager.cs` CompTick 插入 `AutoExecutor.TryTick()`
4. 改 `ITab_GearManager.cs` 布局常量 + 底部区重写
5. 改翻译 XML（中英文同步）
6. 改 `README.md`（同步章节）
7. `make check` 验证零警告零错误

## 验证

- `make check` 通过（0 警告 0 错误）
- 游戏内验证：
  - 新建殖民地：两项默认勾选，殖民者出现后 3000 tick 自动执行
  - 新增殖民者（加入/出生）：立即触发工作重配 + 人员评级
  - 取消人员评级勾选：评级标签被清除，殖民者 Nick 恢复原名
  - 取消工作配置勾选：工作优先级保留不变，仅停止自动执行
  - 重新勾选：立即执行一次
