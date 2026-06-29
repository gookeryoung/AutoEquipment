# 项目重构计划：自动装备 → 自动万物 + 模块化结构

## Context

项目已从 `AutoEquipment` 重命名为 `AutoEverything`（外部标识完成），但内部仍是单层平铺结构，所有 .cs 文件都在 `Source/AutoEverything/` 根目录，共享 `namespace AutoEverything`。这导致：

1. **职责耦合**：`SidearmAllocator.cs` 同时承担角色评价（CombatTier 枚举 + 5 个评价方法）和副武器分配；`SGSettings.cs` 单文件聚合 4 个不相关类型（AESettings/AutoEverythingMod/PresetDetailsWindow/ColonistBarSortMode）。
2. **扩展困难**：未来要新增"自动药物"、"自动食物"等功能时，缺乏清晰的模块边界。
3. **名称陈旧**：玩家可见的中文显示名仍是"自动装备"，与项目新名"AutoEverything/自动万物"不一致。

本次重构目标：① 显示名改为"自动万物"；② 按功能拆分文件夹与命名空间，为未来扩展建立清晰边界。一次性完成（单 commit）。

## 目标文件夹结构

```
Source/AutoEverything/
├── AutoEverything.csproj
├── Core/                           → namespace AutoEverything.Core
│   ├── ModController.cs            # MOD 入口
│   ├── HarmonyPatches.cs           # Harmony 补丁
│   ├── AutoEverythingMod.cs        # 从 SGSettings 拆分（Mod 设置入口）
│   ├── DLCCompat.cs                # DLC API 包装
│   ├── AEDebug.cs                  # 由 DebugHelper.cs 重命名
│   ├── DebugMonitor.cs
│   ├── PawnSuitabilityChecker.cs
│   ├── CombatTier.cs               # 新建：从 SidearmAllocator 提取枚举
│   ├── AESettings.cs               # 从 SGSettings 拆分（8 文件引用）
│   └── ColonistBarSortMode.cs      # 从 SGSettings 拆分
├── RoleEvaluation/                 → namespace AutoEverything.RoleEvaluation
│   ├── PawnRole.cs                 # Role/ArmorPreference/RoleDetector
│   ├── GearContext.cs              # GearContext/ContextDetector（被 29 文件引用）
│   ├── PawnStateCleaner.cs
│   └── CombatEvaluator.cs          # 新建：从 SidearmAllocator 拆分评价部分
├── AutoEquipment/                  → namespace AutoEverything.AutoEquipment
│   ├── CompGearManager.cs         # Tick 入口
│   ├── GearScorer.cs              # 评分门面
│   ├── GearDefClassifier.cs       # 装备分类
│   └── Scoring/                    → namespace AutoEverything.AutoEquipment.Scoring
│       ├── IScorer.cs, ScoringPipeline.cs, ScoringPipelineFactory.cs
│       ├── ScoreBreakdown.cs, GearWeights.cs, GearPreset.cs, GearPolicyEngine.cs
│       ├── Weapon/                 → ...AutoEquipment.Scoring.Weapon
│       └── Apparels/               → ...AutoEquipment.Scoring.Apparels
├── Allocation/                     → namespace AutoEverything.Allocation
│   ├── GlobalAllocator.cs
│   ├── SidearmAllocator.cs         # 仅副武器分配（剥离评价部分）
│   ├── BeltAllocator.cs
│   └── PawnCombatProfile.cs        # 仅被 Allocation 内引用
└── UI/                             → namespace AutoEverything.UI
    ├── ITab_GearManager.cs
    ├── Dialog_GlobalReallocate.cs
    └── PresetDetailsWindow.cs      # 从 SGSettings 拆分
```

## 执行步骤

### 步骤 1：显示名改名"自动装备"→"自动万物"

替换以下文件中的"自动装备"为"自动万物"：
- `Languages/ChineseSimplified/Keyed/AE_Keyed.xml`：AE_Tab / AE_TabTitle / AE_SettingsCategory / AE_Enabled / AE_LockGear_Desc / AE_Badge_NoAutoEquip
- `About/About.xml`：description 字段
- `README.md`：标题与正文中的"自动装备"
- `.trae/rules/autoeverything-project.md`：设置界面显示名
- `Source/AutoEverything/ITab_GearManager.cs`、`CompGearManager.cs`：注释

### 步骤 2：创建新文件夹结构

用 PowerShell 在 `Source/AutoEverything/` 下创建：`Core/`、`RoleEvaluation/`、`AutoEquipment/`、`Allocation/`、`UI/`。`AutoEquipment/Scoring/`、`AutoEquipment/Scoring/Weapon/`、`AutoEquipment/Scoring/Apparels/` 一并创建。

### 步骤 3：拆分 SidearmAllocator.cs

**新建 `Core/CombatTier.cs`**（namespace AutoEverything.Core）：
- 移动 `enum CombatTier : byte { X, D, C, B, A, S }` 及其 `tierRepresentativeScore[]` 数组

**新建 `RoleEvaluation/CombatEvaluator.cs`**（namespace AutoEverything.RoleEvaluation）：
- 移动公共方法：`ComputeCombatValue`、`GetCombatTier`、`GetAutoCombatTier`、`ComputePawnValueScore`、`GetPawnLookupName`
- 移动私有辅助：`GetPassionMult`、`AddSkillScore`、`CountPassions`、`IsPassion`、`HasSpecialTalentTrait`、`HasNegativeTrait`、`StripTierTagPrefixFromLabel`、`HasCombatTrait`（若死代码则删除）
- 移动 14 个 TraitDef 缓存字段
- 类设为 `public static class CombatEvaluator`

**修改 `SidearmAllocator.cs`**（移至 `Allocation/`）：
- 仅保留 `AllocateForPawn` + 7 个私有分配方法 + 4 个分配字段（AllocationInterval、lastAllocationTick、candidatePawns、candidateWeapons）
- 内部调用 `CombatEvaluator.ComputeCombatValue` 替代原 `ComputeCombatValue`
- `using AutoEverything.Core;`、`using AutoEverything.RoleEvaluation;`

### 步骤 4：拆分 SGSettings.cs

**新建 `Core/AESettings.cs`**：移动 `class AESettings : ModSettings`（保留 AE 前缀）
**新建 `Core/ColonistBarSortMode.cs`**：移动 `enum ColonistBarSortMode`
**新建 `Core/AutoEverythingMod.cs`**：移动 `class AutoEverythingMod : Mod`
**新建 `UI/PresetDetailsWindow.cs`**：移动 `class PresetDetailsWindow : Window`
**删除 `SGSettings.cs`**

### 步骤 5：移动其他文件到目标文件夹

| 文件 | 目标文件夹 |
|---|---|
| ModController.cs, HarmonyPatches.cs, DLCCompat.cs, DebugHelper.cs, DebugMonitor.cs, PawnSuitabilityChecker.cs | Core/ |
| PawnRole.cs, GearContext.cs, PawnStateCleaner.cs | RoleEvaluation/ |
| CompGearManager.cs, GearScorer.cs, GearDefClassifier.cs | AutoEquipment/ |
| Scoring/*（含 Weapon/Apparels 子目录） | AutoEquipment/Scoring/ |
| GlobalAllocator.cs, SidearmAllocator.cs, BeltAllocator.cs, PawnCombatProfile.cs | Allocation/ |
| ITab_GearManager.cs, Dialog_GlobalReallocate.cs | UI/ |

**重命名**：`DebugHelper.cs` → `AEDebug.cs`（类名 `AEDebug` 不变，仅文件名同步）

### 步骤 6：批量更新命名空间与 using 语句

**命名空间声明更新**（每个文件的 `namespace` 行）：
- `Core/*.cs` → `namespace AutoEverything.Core`
- `RoleEvaluation/*.cs` → `namespace AutoEverything.RoleEvaluation`
- `AutoEquipment/*.cs` → `namespace AutoEverything.AutoEquipment`
- `AutoEquipment/Scoring/*.cs` → `namespace AutoEverything.AutoEquipment.Scoring`
- `AutoEquipment/Scoring/Weapon/*.cs` → `namespace AutoEverything.AutoEquipment.Scoring.Weapon`
- `AutoEquipment/Scoring/Apparels/*.cs` → `namespace AutoEverything.AutoEquipment.Scoring.Apparels`
- `Allocation/*.cs` → `namespace AutoEverything.Allocation`
- `UI/*.cs` → `namespace AutoEverything.UI`

**新增 using 语句**（按需添加，参考依赖关系）：
- `using AutoEverything.Core;`：几乎所有非 Core 文件（引用 AESettings/AEDebug/DLCCompat/CombatTier/PawnSuitabilityChecker/ColonistBarSortMode）
- `using AutoEverything.RoleEvaluation;`：全部 Scoring 文件（引用 GearContext）、CompGearManager/GearScorer/GlobalAllocator/SidearmAllocator/BeltAllocator/ITab/CombatEvaluator/AEDebug
- `using AutoEverything.AutoEquipment;`：ITab/GlobalAllocator/SidearmAllocator（引用 GearScorer/CompGearManager/GearDefClassifier）
- `using AutoEverything.AutoEquipment.Scoring;`：CompGearManager/GearScorer/PresetDetailsWindow/GlobalAllocator
- `using AutoEverything.AutoEquipment.Scoring.Weapon;`、`using AutoEverything.AutoEquipment.Scoring.Apparels;`：ScoringPipelineFactory
- `using AutoEverything.Allocation;`：CompGearManager/ITab（引用 GlobalAllocator/SidearmAllocator/BeltAllocator）
- `using AutoEverything.UI;`：HarmonyPatches（若 ITab 注册在 Patch）

### 步骤 7：更新调用点

**SidearmAllocator.X → CombatEvaluator.X**（5 个公共调用点）：
- `BeltAllocator.cs`：`SidearmAllocator.ComputeCombatValue` → `CombatEvaluator.ComputeCombatValue`
- `GlobalAllocator.cs`：5 处调用全部改名
- `ITab_GearManager.cs`：5 处调用全部改名
- `AESettings.cs`：`SidearmAllocator.GetAutoCombatTier/GetCombatTier/ComputeCombatValue` 改名
- `AEDebug.cs`（原 DebugHelper）：`SidearmAllocator.GetAutoCombatTier/GetPawnLookupName` 改名

### 步骤 8：更新 README.md 与规则文件

- `README.md`：更新目录结构图、命名空间示例、模块说明
- `.trae/rules/autoeverything-project.md`：更新命名空间与文件夹结构映射表
- `.trae/rules/rimworld-mod-dev.md`：无需改动（通用规则）

### 步骤 9：验证 make check

在项目根目录执行 `make check`，确保零警告零错误。失败则按错误信息修复 using 语句或调用点。

## 关键风险点

1. **GearContext 迁移波及 29 文件**：所有 Scoring 文件需新增 `using AutoEverything.RoleEvaluation;`
2. **AEDebug/DLCCompat 迁移波及 8-9 文件**：批量加 `using AutoEverything.Core;`
3. **Scoring 命名空间变化**：`AutoEverything.Scoring` → `AutoEverything.AutoEquipment.Scoring`，所有引用需更新
4. **SGSettings.cs 删除后**：原 `using AutoEverything;` 中的 AESettings/AutoEverythingMod 等需改为 `using AutoEverything.Core;` 和 `using AutoEverything.UI;`
5. **CombatTier 引用点**：5 个文件需新增 `using AutoEverything.Core;`

## 验证清单

- [ ] `make check` 通过零警告零错误
- [ ] 游戏内 ITab 标签显示"自动万物"
- [ ] 设置分类显示"自动万物"
- [ ] 全局重配按钮可用
- [ ] 装备评级 S/A/B/C/D/X 正常显示
- [ ] 副武器/腰带分配功能正常
- [ ] 食尸鬼被排除在装备管理外
- [ ] 无 `AutoEquipment` 残留引用（grep 验证）
- [ ] 无 `自动装备` 残留引用（grep 验证）

## 关键文件

- `e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Source\AutoEverything\SidearmAllocator.cs`（拆分源头）
- `e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Source\AutoEverything\SGSettings.cs`（拆分源头）
- `e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Source\AutoEverything\GearContext.cs`（被 29 文件引用）
- `e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Source\AutoEverything\DebugHelper.cs`（被 8-9 文件引用）
- `e:\SteamLibrary\steamapps\common\RimWorld\Mods\AutoEverything\Languages\ChineseSimplified\Keyed\AE_Keyed.xml`（显示名）
