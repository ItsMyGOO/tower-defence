# 03_1 测试场景搭建 - Enemy Path Test

> **日期**: 2026-08-23  
> **分支**: `test/01-enemy-path-test`  
> **里程碑**: Test Milestone 01 — 敌人路径与事件可视化测试

---

## 1. 概述

本文档记录 TowerDefense-Core 项目第一个可视化测试场景的搭建流程。该测试场景的目标是：

1. 验证 `Enemy` 节点沿 `Path2D` 路径移动的逻辑
2. 验证 `EventBus.OnEnemyReachedEnd` 事件在敌人走完全程时正确触发
3. 验证 `EventBus.OnEnemyKilled` 事件在敌人被击杀时正确触发
4. 通过控制台日志输出提供可观测的可视化调试手段

---

## 2. Tests/ 目录规范与结构

### 2.1 目录隔离原则

测试资产（资源文件、场景、脚本）统一放在 `Tests/` 目录下，与 `Game/` 目录下的正式业务代码严格分离：

```
Tests/
├── Data/                          # 测试专用 Resource 数据（仅测试场景引用）
│   └── Enemies/
│       └── Test_SlimeData.tres    # 测试用敌人配置资源
├── Scenes/                        # 测试场景（含 .tscn + 控制脚本）
│   └── EnemyTest/                 # 每个测试场景独立子目录
│       ├── EnemyTest.cs           # 场景控制脚本（继承 Node2D）
│       └── EnemyTest.tscn         # Godot 4 文本场景文件
└── README.md
```

### 2.2 与 Game/ 目录的边界

| 目录 | 用途 | 示例 |
|------|------|------|
| `Game/Config/Enemies/` | **正式**敌人配置，随游戏发布 | 后续的 `SlimeData.tres`、`GoblinData.tres` 等 |
| `Tests/Data/Enemies/` | **仅测试**用数据，不参与正式打包 | `Test_SlimeData.tres` |
| `Game/Scenes/` | 正式游戏场景（主菜单、关卡等） | 后续的 `Level01.tscn` |
| `Tests/Scenes/` | 开发期验证/调试/回归测试场景 | `EnemyTest.tscn` |

> **设计目的**：测试数据与正式资源解耦，避免测试用的极端参数、占位数据污染正式配置表；测试场景独立目录便于后续批量接入自动化测试框架。

---

## 3. 测试数据 (.tres) 配置

### 3.1 Test_SlimeData.tres

**路径**: `Tests/Data/Enemies/Test_SlimeData.tres`

测试用敌人配置资源，绑定到 `EnemyData` 类（位于 `Game/Config/Enemies/EnemyData.cs`），属性如下：

| 属性 | 值 | 说明 |
|------|----|------|
| `EnemyId` | `"test_slime"` | 唯一标识符，测试专用前缀 `test_` |
| `EnemyName` | `"测试史莱姆"` | 显示名称，支持本地化占位 |
| `MaxHp` | `100.0` | 最大生命值 |
| `MoveSpeed` | `150.0` | 移动速度（像素/秒），比默认 100 快 50% 便于快速观察 |
| `RewardGold` | `10` | 击杀奖励金币（验证 OnEnemyKilled 参数） |
| `DamageToPlayer` | `1` | 逃脱时对玩家造成的伤害（验证 OnEnemyReachedEnd 参数） |

> **隔离说明**：该 `.tres` 仅存放于 `Tests/Data/` 路径下，禁止被 `Game/` 目录下任何正式场景或脚本引用。

---

## 4. 测试场景配置

### 4.1 场景文件：EnemyTest.tscn

**路径**: `Tests/Scenes/EnemyTest/EnemyTest.tscn`

采用 Godot 4 标准 `[gd_scene format=3]` 文本格式编写，已预置以下节点结构：

```
EnemyTestRoot (Node2D)              # 根节点，挂载 EnemyTest.cs 脚本
  └── EnemyPath (Path2D)            # 敌人行走路径节点，预置 Curve2D 曲线
```

#### 预置绑定（Inspector 导出属性已在 .tscn 中赋值）

| 属性 | 值 | 说明 |
|------|----|------|
| `script` | `EnemyTest.cs` | 根节点脚本 |
| `EnemyPath` | `NodePath("EnemyPath")` | 自动绑定子节点 EnemyPath |
| `TestEnemyData` | `Test_SlimeData.tres` | 自动绑定 Tests/Data 下的测试资源 |
| `SpawnInterval` | `2.0` | 每 2 秒生成一个敌人 |
| `MaxSpawnCount` | `5` | 共生成 5 个测试敌人 |

#### 预置 Path2D 曲线

场景中的 `EnemyPath` 节点已包含一条从左到右的测试曲线：
- 位置起点：`(100, 300)`（EnemyPath 节点自身 position）
- Curve2D 控制点：`(0,0) → (300,0) → (600,200) → (900,0) → (1200,0)`
- 实际世界坐标下终点约为 `(1300, 300)`，适合在 1280×720 视口下观察

### 4.2 控制脚本：EnemyTest.cs

**路径**: `Tests/Scenes/EnemyTest/EnemyTest.cs`  
**继承**: `Node2D`

#### 4.2.1 Inspector 导出属性

| 属性名 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `EnemyPath` | `Path2D` | ✅ 是 | 场景中定义敌人行走路径的节点（.tscn 已绑定） |
| `TestEnemyData` | `EnemyData` | ✅ 是 | 绑定 Tests/Data/Enemies/Test_SlimeData.tres（.tscn 已绑定） |
| `EnemyPrefab` | `PackedScene` | ❌ 否 | 可选的敌人预制体；为空则动态创建绿色方块占位 |
| `SpawnInterval` | `float` | — | 敌人生成间隔，默认 2.0 秒 |
| `MaxSpawnCount` | `int` | — | 最大生成敌人数，默认 5 个 |

#### 4.2.2 核心逻辑流程

```
_Ready()
  ├── SubscribeEvents()       # 订阅 OnEnemyReachedEnd / OnEnemyKilled
  ├── ValidateBindings()      # 校验节点/数据绑定完整性
  └── SetupSpawnTimer()       # 创建定时器，按 SpawnInterval 间隔触发
      └── SpawnEnemy() (循环 MaxSpawnCount 次)
          ├── new Enemy + ColorRect 占位视觉 (20×20 绿色方块)
          ├── enemy.Data = TestEnemyData
          └── EnemyPath.AddChild(enemy)
              └── Enemy._Process()
                  └── ProgressRatio >= 1.0
                      └── EventBus.RaiseEnemyReachedEnd()
                          └── HandleEnemyReachedEnd() → GD.Print
```

#### 4.2.3 事件订阅与控制台输出

脚本在 `_Ready` 中订阅两个全局事件：

```csharp
EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
EventBus.OnEnemyKilled     += HandleEnemyKilled;
```

| 触发场景 | 控制台输出示例 |
|----------|--------------|
| 场景启动 | `[EnemyTest] ========== 测试场景启动 ==========` |
| 绑定状态报告 | `[EnemyTest] 敌人路径绑定: OK` / `敌人数据绑定: 测试史莱姆` |
| 敌人 #n 生成 | `[EnemyTest] 生成敌人 #1: Enemy_1 (HP=100, Speed=150)` |
| 敌人走到尽头 | `[EnemyTest] ✅ OnEnemyReachedEnd 事件触发！对玩家造成伤害: 1` |
| 敌人被击杀 | `[EnemyTest] ✅ OnEnemyKilled 事件触发！敌人: test_slime, 奖励金币: 10` |
| 停止生成 | `[EnemyTest] 已达到最大生成数 5，停止生成。` |

> **事件验证**：`OnEnemyReachedEnd` 与 `OnEnemyKilled` 的参数与 Test_SlimeData.tres 中配置一一对应，确保 EventBus 的事件传递链路完整无误。

---

## 5. Path2D 可视化验证流程

### 5.1 运行前检查清单

- [ ] `EnemyTest.tscn` 文件存在且未损坏
- [ ] EventBus.cs 已在项目中（无需额外配置 AutoLoad，当前为静态类）
- [ ] Godot 编辑器首次打开后执行过一次 **Build Project**（生成 .csproj）

### 5.2 运行步骤（Godot 编辑器）

1. 启动 Godot 4 编辑器并打开项目
2. 在 FileSystem 面板定位到 `Tests/Scenes/EnemyTest/EnemyTest.tscn`，双击打开
3. 点击工具栏 **Play Scene**（快捷键 **F6**）仅运行当前场景
4. 观察 2D 视图与 Output 控制台：
   - **视觉验证**：绿色 20×20 方块（Enemy）依次沿 Path2D 曲线从左向右移动
   - **日志验证**：检查控制台日志序列是否与 §4.2.3 表格一致
5. 等待所有敌人走完路径，确认每个敌人都触发一次 `OnEnemyReachedEnd` 日志

### 5.3 手动验证击杀事件（OnEnemyKilled）

由于当前测试场景未接入防御塔攻击系统，需手动触发击杀：

**方法 A（推荐）：扩展 EnemyTest 脚本加入调试按键**

在 `EnemyTest.cs` 的 `_Ready` 末尾或新增 `_Input` 方法中添加：

```csharp
public override void _Input(InputEvent @event)
{
    if (@event.IsActionPressed("ui_accept") && EnemyPath.GetChildCount() > 0)
    {
        (EnemyPath.GetChild(0) as Enemy)?.TakeDamage(999.0f);
    }
}
```
按 **Enter** 键可击杀最前方敌人，观察 `OnEnemyKilled` 日志。

**方法 B：运行时 Debugger 调用**
1. 运行场景后点击 **Pause**（F7）暂停
2. 在 Remote 场景树中展开 EnemyPath，选中任一 Enemy 子节点
3. 切换到 Debugger 面板 → Inspector 中调用 `TakeDamage(200.0)`
4. 继续运行观察控制台日志

### 5.4 验收标准（Path2D 可视化）

| # | 测试项 | 预期结果 | 是否通过 |
|---|--------|----------|----------|
| T1 | 路径一致性 | Enemy 始终沿 Path2D 曲线切线方向移动，无抖动或偏移 | ☐ |
| T2 | 节点父子关系 | 运行时所有 Enemy 节点均为 EnemyPath 的子节点 | ☐ |
| T3 | 移动速度匹配 | 以 Speed=150 观察：1200 像素路径 ≈ 8 秒走完全程 | ☐ |
| T4 | 逃脱事件触发 | 每个敌人到达终点立即打印 `OnEnemyReachedEnd`（参数=1） | ☐ |
| T5 | 击杀事件触发 | TakeDamage 致死后立即打印 `OnEnemyKilled`（id=test_slime, gold=10） | ☐ |
| T6 | 内存清理 | 敌人逃脱/击杀后场景树中 Enemy 节点消失（QueueFree 生效） | ☐ |
| T7 | 生成数量控制 | 只生成 5 个敌人后定时器停止 | ☐ |
| T8 | 绑定健壮性 | 修改 EnemyPath 或 TestEnemyData 为 null 时正确打印 Err 并不崩溃 | ☐ |

---

## 6. 常见问题排查

### 6.1 场景打开报错：Unable to load ext_resource

| 可能原因 | 排查方法 |
|----------|----------|
| EnemyTest.cs 未编译 | 首次打开项目先执行 **Project → Tools → C# → Create C# Solution**，然后 Build |
| .tres 路径不一致 | 确认 Test_SlimeData.tres 位于 `res://Tests/Data/Enemies/` 下，与 .tscn 中 ext_resource 匹配 |

### 6.2 敌人不移动

| 可能原因 | 排查方法 |
|----------|----------|
| EnemyPath 节点未绑定 | 检查启动日志中 `敌人路径绑定: OK`，否则重新在 Inspector 绑定 |
| TestEnemyData 为 null | 检查启动日志中 `敌人数据绑定` 行，如为 MISSING 则在 Inspector 重选 .tres |
| MoveSpeed 被覆盖 | 确认 Tests/Data/Enemies/Test_SlimeData.tres 中 MoveSpeed = 150 |

### 6.3 事件未触发

| 可能原因 | 排查方法 |
|----------|----------|
| Enemy 未挂在 Path2D 下 | 在 Remote 场景树确认 Enemy 父节点是 EnemyPath |
| Enemy 节点 Name 异常 | PathFollow2D 要求在 Path2D 子树中；如果 AddChild 到错误父节点则 Progress 不更新 |
| 路径长度为 0 | 选中 EnemyPath → Curve 面板确认控制点数量 ≥ 2 且起点 ≠ 终点 |

### 6.4 C# 编译错误

1. Godot 菜单 **Build → Build Project**（或 Alt+B）
2. 命令行：在项目根目录执行 `dotnet build`（需先生成 .sln）

---

## 7. Tests/ 目录后续扩展约定

1. **每个测试场景独立子目录**：`Tests/Scenes/{场景名}/{场景名}.{tscn,cs}`
2. **测试数据命名**：前缀 `Test_`，如 `Test_TowerData_Level1.tres`
3. **测试脚本命名空间**：统一使用 `TowerDefence.Tests.Scenes`
4. **自动化测试**：后续接入 GUT 框架时，可在 `Tests/Unit/` 下添加单元测试，复用 `Tests/Data/` 资源
5. **正式数据迁移**：某个测试 Resource 经过验证可转为正式数据时，从 `Tests/Data/` 移动到 `Game/Config/` 对应子目录，更新引用路径

---

## 8. 提交与合并规范

完成 T1~T8 验收后：

1. 截图保存 Output 控制台完整输出（包含从启动到第 5 个敌人逃脱的日志）
2. 在本文档 §5.4 验收表格中打勾标记通过项
3. 确认以下文件均纳入版本控制：
   - `Tests/Data/Enemies/Test_SlimeData.tres`
   - `Tests/Scenes/EnemyTest/EnemyTest.cs`
   - `Tests/Scenes/EnemyTest/EnemyTest.tscn`
   - `Docs/DevLogs/03_1_TestScene_Setup.md`
4. Git 提交：
   ```bash
   git add -A
   git commit -m "test: 01-enemy-path-test 可视化测试场景"
   ```
5. 发起 PR 合并回 `main`，描述中粘贴验收截图链接
