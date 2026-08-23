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

## 2. 目录结构

新增的测试相关文件位于以下路径：

```
tower-defence/
├── Game/
│   └── Config/
│       └── Enemies/
│           ├── EnemyData.cs                    # 已存在：敌人数据类
│           └── Test_SlimeData.tres             # 🆕 新增：测试用史莱姆数据资源
├── Tests/
│   ├── README.md                               # 已存在
│   └── Scenes/
│       └── EnemyTest.cs                        # 🆕 新增：测试场景控制脚本
└── Docs/
    └── DevLogs/
        ├── 03_EnemySystem_PathFollow.md        # 已存在
        └── 03_1_TestScene_Setup.md             # 🆕 新增：本文档
```

---

## 3. 测试资源配置

### 3.1 Test_SlimeData.tres

**路径**: `Game/Config/Enemies/Test_SlimeData.tres`

测试用敌人配置资源，绑定到 `EnemyData` 类，属性如下：

| 属性 | 值 | 说明 |
|------|----|------|
| `EnemyId` | `"test_slime"` | 唯一标识符 |
| `EnemyName` | `"测试史莱姆"` | 显示名称 |
| `MaxHp` | `100.0` | 最大生命值 |
| `MoveSpeed` | `150.0` | 移动速度（像素/秒），比默认值快便于观察 |
| `RewardGold` | `10` | 击杀奖励金币 |
| `DamageToPlayer` | `1` | 逃脱时对玩家造成的伤害 |

---

## 4. Tests/ 目录配置

### 4.1 Tests/Scenes/EnemyTest.cs

测试场景控制器脚本，需挂载到测试场景的根节点上（Node2D 或其子类）。

#### 4.1.1 Inspector 导出属性

| 属性名 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `EnemyPath` | `Path2D` | ✅ 是 | 场景中定义敌人行走路径的节点 |
| `TestEnemyData` | `EnemyData` | ✅ 是 | 绑定 `Test_SlimeData.tres` |
| `EnemyPrefab` | `PackedScene` | ❌ 否 | 可选的敌人预制体；为空则动态创建绿色方块占位 |
| `SpawnInterval` | `float` | — | 敌人生成间隔，默认 2.0 秒 |
| `MaxSpawnCount` | `int` | — | 最大生成敌人数，默认 5 个 |

#### 4.1.2 核心逻辑流程

```
_Ready()
  ├── SubscribeEvents()       # 订阅 OnEnemyReachedEnd / OnEnemyKilled
  ├── ValidateBindings()      # 校验 Inspector 绑定
  └── SetupSpawnTimer()       # 创建定时器，按间隔生成敌人
      └── SpawnEnemy() (重复) # 动态创建 Enemy -> 挂载到 Path2D
          └── Enemy._Process()
              └── ProgressRatio >= 1.0
                  └── EventBus.RaiseEnemyReachedEnd()
                      └── HandleEnemyReachedEnd() -> GD.Print 日志
```

#### 4.1.3 事件验证输出

| 事件 | 控制台输出示例 |
|------|--------------|
| 场景启动 | `[EnemyTest] ========== 测试场景启动 ==========` |
| 敌人 #n 生成 | `[EnemyTest] 生成敌人 #1: Enemy_1 (HP=100, Speed=150)` |
| 敌人逃脱 | `[EnemyTest] ✅ OnEnemyReachedEnd 事件触发！对玩家造成伤害: 1` |
| 敌人击杀 | `[EnemyTest] ✅ OnEnemyKilled 事件触发！敌人: test_slime, 奖励金币: 10` |
| 达到生成上限 | `[EnemyTest] 已达到最大生成数 5，停止生成。` |

---

## 5. Path2D 节点绑定

### 5.1 在 Godot 编辑器中搭建测试场景

按以下步骤创建并配置 `Tests/Scenes/EnemyTest.tscn`（在编辑器中手动创建）：

#### 步骤 1：创建场景根节点

1. 在 Godot 编辑器中新建场景（Scene → New Scene）
2. 添加 `Node2D` 作为根节点，命名为 `EnemyTestRoot`
3. 将 `Tests/Scenes/EnemyTest.cs` 脚本附加到根节点

#### 步骤 2：创建 Path2D 路径节点

1. 在 `EnemyTestRoot` 下添加子节点 `Path2D`，命名为 `EnemyPath`
2. 选中 `EnemyPath`，在工具栏点击 **Curve** 工具开始绘制路径
3. 在 2D 视图中点击创建多个控制点，绘制一条从左到右的 S 形或直线曲线：
   - 起点建议在屏幕左侧 (x ≈ 100, y ≈ 300)
   - 终点建议在屏幕右侧 (x ≈ 1100, y ≈ 300)
4. 选中根节点 `EnemyTestRoot`，在 Inspector 的 **EnemyPath** 导出属性中拖动绑定刚创建的 `EnemyPath` 节点

#### 步骤 3：绑定测试数据

1. 选中根节点 `EnemyTestRoot`
2. 在 Inspector 中找到 **TestEnemyData** 属性
3. 点击下拉 → Load → 选择 `res://Game/Config/Enemies/Test_SlimeData.tres`

#### 步骤 4：保存场景

将场景保存为 `Tests/Scenes/EnemyTest.tscn`

---

## 6. 可视化调试流程

### 6.1 运行前检查清单

- [ ] `EnemyPath` 节点已绑定到 Inspector
- [ ] `TestEnemyData` 已绑定 `Test_SlimeData.tres`
- [ ] Path2D 曲线至少包含 2 个以上控制点
- [ ] 项目设置中 `EventBus.cs` 已正确配置为 AutoLoad（单例）

### 6.2 运行与观察

**方式 A：通过编辑器直接运行测试场景**

1. 在编辑器中打开 `EnemyTest.tscn`
2. 点击工具栏 **Play Scene** (F6) 运行当前场景
3. 观察：
   - **2D 视图**：绿色方块敌人按间隔生成，沿路径移动
   - **Output 控制台**：检查以下日志序列是否出现：
     ```
     [EnemyTest] ========== 测试场景启动 ==========
     [EnemyTest] 敌人路径绑定: OK
     [EnemyTest] 敌人数据绑定: 测试史莱姆
     [EnemyTest] 计划生成 5 个敌人，间隔 2s
     [EnemyTest] 生成敌人 #1: Enemy_1 (HP=100, Speed=150)
     ...
     [EnemyTest] ✅ OnEnemyReachedEnd 事件触发！对玩家造成伤害: 1
     ```

**方式 B：手动触发击杀事件（验证 OnEnemyKilled）**

由于当前测试场景未接入攻击系统，可通过以下任一方式验证击杀：

1. **GDScript 调试断点**：在运行时暂停，选中 Enemy 节点，在 Debugger 面板调用 `take_damage(200.0)`
2. **扩展 EnemyTest 脚本**：在 `_Input` 中监听按键（如空格），对第一个敌人调用 `TakeDamage`
3. **直接降低 MaxHp 并接塔**：后续接入防御塔系统后可直接使用塔攻击

### 6.3 预期行为与验收标准

| # | 测试项 | 预期结果 | 是否通过 |
|---|--------|----------|----------|
| T1 | 敌人沿路径移动 | 敌人节点沿 Path2D 曲线方向移动，无位置偏移 | ☐ |
| T2 | 敌人生成间隔 | 每隔约 SpawnInterval 秒生成一个新敌人 | ☐ |
| T3 | 数量限制 | 达到 MaxSpawnCount 后停止生成 | ☐ |
| T4 | 逃脱事件触发 | 敌人走到路径尽头时控制台打印 `OnEnemyReachedEnd` 日志 | ☐ |
| T5 | 逃脱参数正确 | 日志中 `damageToPlayer` 值 = 1（Test_SlimeData 配置） | ☐ |
| T6 | 击杀事件触发 | 调用 TakeDamage 致死时控制台打印 `OnEnemyKilled` 日志 | ☐ |
| T7 | 击杀参数正确 | 日志中 `enemyId` = `"test_slime"`，`goldReward` = 10 | ☐ |
| T8 | 内存清理 | 敌人逃脱或被击杀后节点被 QueueFree，场景树中无残留 | ☐ |

---

## 7. 常见问题排查

### 7.1 敌人不移动

| 可能原因 | 排查方法 |
|----------|----------|
| Path2D 曲线为空 | 选中 EnemyPath，检查 Curve 中点数量 |
| EnemyData 未绑定 | 检查控制台是否有 `[Enemy] EnemyData 未配置！` 错误 |
| MoveSpeed = 0 | 确认 Test_SlimeData 中 MoveSpeed = 150 |

### 7.2 事件不触发

| 可能原因 | 排查方法 |
|----------|----------|
| EventBus 未配置为 AutoLoad | 检查 `project.godot` 中的 autoload 配置 |
| Enemy 节点未挂在 Path2D 下 | 在 Remote 场景树中确认 Enemy 的父节点是 EnemyPath |
| ProgressRatio 从未达到 1 | 确认路径起点 ≠ 终点，且 MoveSpeed 为正值 |

### 7.3 编译错误（C#）

1. 点击 Godot 编辑器菜单 **Mono → Build Project** 重新编译
2. 或在项目根目录运行：
   ```bash
   dotnet build
   ```

---

## 8. 后续扩展建议

- **接入 HP 条**：为动态创建的 Enemy 增加 ProgressBar 子节点显示血条
- **可视化伤害**：在测试脚本中添加按键监听，按 1/2/3 对敌人造成不同伤害
- **路径可视化**：为 Path2D 添加 Line2D 渲染让路径在运行时可见
- **自动化测试**：基于此场景后续编写 GUT (Godot Unit Test) 单元测试

---

## 9. 提交规范

完成测试验证后：

1. 所有验收项（T1~T8）通过后截图保存 Output 控制台
2. 更新本文档的验收标准列表，打勾标记通过项
3. 使用 `fix: test/01` 前缀提交 Git 变更
4. 合并回 `main` 分支前确保场景文件 `EnemyTest.tscn` 已纳入版本控制
