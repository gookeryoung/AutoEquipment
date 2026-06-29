# AutoWork 模块实现续接计划

## 背景与当前状态

本计划承接已批准的 `autowork-module-plan.md`，原计划共 7 项变更，已完成 2 项：

- ✅ 变更 1：完善 `.trae/rules/rimworld-mod-dev.md`（已完成）
- ✅ 变更 2：新建 `Source/AutoEverything/AutoWork/WorkAllocator.cs`（已完成，已验证内容正确）

剩余 5 项变更 + 1 项编译验证待执行。所有行号已通过 Phase 1 探索重新验证，与原计划一致。

## 已验证的当前文件状态

| 文件 | 关键位置 | 当前内容 | 验证状态 |
|------|---------|---------|---------|
| `AESettings.cs` L19 | 字段声明 | `public static bool sidearms = true;` | ✅ |
| `AESettings.cs` L450 | Scribe 区 | `LookCompat(ref debugLogging, "debugLogging", false);` | ✅ |
| `AESettings.cs` L621 | Checkbox 区 | `l.CheckboxLabeled("AE_Sidearms".Translate(), ref sidearms);` | ✅ |
| `ITab_GearManager.cs` L112 | 面板尺寸 | `size = new Vector2(360f, 560f);` | ✅ |
| `ITab_GearManager.cs` L143 | 内容区高度 | `buttonHeight * 2 + buttonGap * 2` | ✅ |
| `ITab_GearManager.cs` L360 | 第二按钮末尾 | `TooltipHandler.TipRegion(buttonRect, "AE_TT_GlobalReallocate".Translate());` | ✅ |
| `ChineseSimplified/AE_Keyed.xml` L215 | 末尾键 | `<AE_TT_GlobalReallocate>...` | ✅ |
| `English/AE_Keyed.xml` L215 | 末尾键 | `<AE_TT_GlobalReallocate>...` | ✅ |
| `README.md` L337-339 | 章节边界 | `## 架构模型` 紧接 `### 保护规则` 之后 | ✅ |
| `autoeverything-project.md` L25, L33 | 命名空间/职责 | Allocation 行 | ✅ |
| `DLCCompat.cs` L43 | IsSlave 方法 | `public static bool IsSlave(Pawn pawn)` | ✅ |
| `DLCCompat.cs` L26 | IsGhoul 方法 | `public static bool IsGhoul(Pawn pawn)` | ✅ |

## 剩余变更清单

### 变更 3：AESettings.cs 添加 AutoWork 设置

**文件**：[AESettings.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/Core/AESettings.cs)

**修改 1**：在 L19 `sidearms` 字段之后新增字段声明

```csharp
public static bool autoWorkEnabled = true;  // AutoWork 自动工作分配主开关
```

**修改 2**：在 L450 `debugLogging` 的 LookCompat 之后新增 Scribe 持久化

```csharp
LookCompat(ref autoWorkEnabled, "autoWorkEnabled", true);
```

**修改 3**：在 L621 `sidearms` checkbox 之后新增 checkbox UI

```csharp
l.CheckboxLabeled("AE_AutoWork".Translate(), ref autoWorkEnabled);
```

### 变更 4：ITab_GearManager.cs 新增"全局工作重配"按钮

**文件**：[ITab_GearManager.cs](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Source/AutoEverything/UI/ITab_GearManager.cs)

**修改 1**：文件顶部 using 区（L9 之后）新增

```csharp
using AutoEverything.AutoWork;
```

**修改 2**：L112 面板尺寸 560f → 600f（增加 40f 容纳第三个按钮）

```csharp
// 原：size = new Vector2(360f, 560f);
// 改：
size = new Vector2(360f, 600f);
```

**修改 3**：L143 内容区高度公式改为 3 按钮

```csharp
// 原：
Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight * 2 + buttonGap * 2));
// 改：
Rect contentRect = new Rect(rect.x, rect.y, rect.width, rect.height - (buttonHeight * 3 + buttonGap * 3));
```

**修改 4**：在 L360（第二按钮的 `TooltipHandler.TipRegion(buttonRect, "AE_TT_GlobalReallocate".Translate());` 之后、`}` 之前）新增第三个按钮

```csharp
// 第三个按钮：全局工作重配（仅当 autoWorkEnabled=true 时显示）
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
        int triggered = WorkAllocator.ReallocateAll();
        Messages.Message(
            "AE_GlobalWorkReallocateResult".Translate(triggered),
            MessageTypeDefOf.TaskCompletion);
    }
    GUI.backgroundColor = prevWorkBg;
    TooltipHandler.TipRegion(workBtnRect, "AE_TT_GlobalWorkReallocate".Translate());
}
```

**设计决策**：
- 按钮 `yMax` 偏移链：`tierTagBtnRect` → `buttonRect` → `workBtnRect`，复用现有偏移模式
- 仅 `autoWorkEnabled=true` 时显示，避免关闭 AutoWork 后按钮悬空
- 复用 `ColorPrimaryBtnBg`（与第二按钮统一视觉风格，同属主操作按钮）

### 变更 5：翻译键新增（中英文）

**文件 1**：[ChineseSimplified/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/ChineseSimplified/Keyed/AE_Keyed.xml)

在 L215 `<AE_TT_GlobalReallocate>...</AE_TT_GlobalReallocate>` 之后、L216 `</LanguageData>` 之前新增：

```xml
<!-- AutoWork 自动工作分配 -->
<AE_AutoWork>自动工作分配</AE_AutoWork>
<AE_GlobalWorkReallocate>全局工作重配</AE_GlobalWorkReallocate>
<AE_GlobalWorkReallocateResult>已为 {0} 个殖民者重新分配工作优先级</AE_GlobalWorkReallocateResult>
<AE_TT_GlobalWorkReallocate>按兴趣与评级重新分配所有殖民者的工作优先级。自动启用自定义优先级开关。紧急工作=1，有兴趣的技能=2，搬运清洁按档位=1/3/4，无人有兴趣的技能取前2人=3。</AE_TT_GlobalWorkReallocate>
```

**文件 2**：[English/AE_Keyed.xml](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/Languages/English/Keyed/AE_Keyed.xml)

在 L215 `<AE_TT_GlobalReallocate>...</AE_TT_GlobalReallocate>` 之后、L216 `</LanguageData>` 之前新增：

```xml
<!-- AutoWork -->
<AE_AutoWork>Auto Work Assignment</AE_AutoWork>
<AE_GlobalWorkReallocate>Global Work Reallocate</AE_GlobalWorkReallocate>
<AE_GlobalWorkReallocateResult>Reassigned work priorities for {0} colonists</AE_GlobalWorkReallocateResult>
<AE_TT_GlobalWorkReallocate>Reassign all colonists' work priorities by passion and tier. Auto-enables custom priorities. Emergency=1, passionate skills=2, hauling/cleaning by tier=1/3/4, unpassionate skills top 2=3.</AE_TT_GlobalWorkReallocate>
```

### 变更 6：README.md 新增 AutoWork 章节

**文件**：[README.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/README.md)

在 L338（`### 保护规则` 表格末行）之后、L339 `## 架构模型` 之前新增章节：

```markdown
## 自动工作分配（AutoWork）

`AutoWork/WorkAllocator.cs` 提供基于兴趣（Passion）与全局评级（CombatTier）的工作优先级自动分配。

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
- 殖民者装备面板（ITab）底部 → "全局工作重配"按钮（仅当 autoWorkEnabled=true 时显示）
```

### 变更 7：autoeverything-project.md 更新模块职责

**文件**：[autoeverything-project.md](file:///e:/SteamLibrary/steamapps/common/RimWorld/Mods/AutoEverything/.trae/rules/autoeverything-project.md)

**修改 1**：在 L25 `- \`Source/AutoEverything/Allocation/*\` → \`namespace AutoEverything.Allocation\`` 之后新增命名空间映射：

```
- `Source/AutoEverything/AutoWork/*` → `namespace AutoEverything.AutoWork`
```

**修改 2**：在 L33 `- **Allocation**：...` 之后新增模块职责：

```
- **AutoWork**：自动工作优先级分配（`WorkAllocator`）
```

### 变更 8：编译验证

执行 `make check`，预期零警告零错误，输出 `Assemblies/AutoEverything.dll`。

## 执行顺序

1. AESettings.cs（变更 3）— 字段 + Scribe + Checkbox
2. ITab_GearManager.cs（变更 4）— using + 尺寸 + 高度公式 + 第三按钮
3. ChineseSimplified/AE_Keyed.xml（变更 5.1）
4. English/AE_Keyed.xml（变更 5.2）
5. README.md（变更 6）
6. autoeverything-project.md（变更 7）
7. `make check` 编译验证（变更 8）

## 假设与决策

延续原计划的决策，无需重新确认：

1. **不创建独立 CompWorkManager**：WorkAllocator 仅由按钮触发，不需要 ThingComp 注入
2. **不创建 Dialog_GlobalWorkReallocate**：规则固定不可调，直接按钮触发+消息反馈
3. **按钮条件显示**：仅当 `autoWorkEnabled=true` 时显示，避免关闭后悬空按钮
4. **非技能工作优先级=3**：Flicking 等无技能关联工作，所有人设为 3
5. **未成年保留**：未成年可参与工作（Biotech 允许）
6. **跳过奴隶**：奴隶有独立工作管理系统
7. **WorkTypeDef 懒加载**：避免静态字段初始化器调用 DefDatabase（已实现在 WorkAllocator.cs）
8. **第三按钮复用 ColorPrimaryBtnBg**：与"全局装备重配"同属主操作按钮，视觉一致

## 验证步骤

### 编译验证
```bash
make check
```
预期：零警告零错误。

### 功能验证清单
- [ ] `make check` 通过
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
