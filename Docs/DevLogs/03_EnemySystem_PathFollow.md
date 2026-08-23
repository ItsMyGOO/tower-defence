# Godot 4 Path2D + PathFollow2D 路径移动机制与 EventBus 通讯实现思考

## 一、塔防游戏中的敌人移动：为什么不用 CharacterBody2D / RigidBody2D？

在实现 Tower Defense 敌人移动时，开发者的第一反应可能是使用 Godot 4 推荐的物理节点：

| 方案 | 适用场景 | 塔防中的问题 |
|------|---------|-------------|
| **CharacterBody2D** | 平台跳跃、动作游戏，玩家操作可控角色 | 需要每帧 `MoveAndSlide()` 手动计算方向和速度；路径寻路逻辑要自己写 A* 或 NavAgent；多拐弯路径时平滑过弯非常麻烦 |
| **RigidBody2D** | 物理模拟、弹珠台球、汽车游戏 | 受重力和碰撞力影响，敌人会"乱跑"；无法保证精确沿预设路径走；塔防需要的是确定性、可预测的移动，不是物理仿真 |
| **Path2D + PathFollow2D** ✅ | 轨道相机、巡逻敌人、塔防路径移动、过场动画 | **专为"沿预设曲线精确移动"设计**；位置由数学曲线插值保证 100% 确定性；拐弯处自动平滑；代码量极少 |

**结论**：对于"关卡设计人员画出一条固定路径，敌人严格按顺序前进，不迷路、不绕开障碍"的塔防需求，`PathFollow2D` 是 Godot 官方推荐的标准选择。

---

## 二、Path2D + PathFollow2D 的工作原理（源码级理解）

### 2.1 核心关系：父子节点配对

```
EnemyPath (Path2D)       ← 持有 Curve2D 数据（路径形状定义）
    └── Enemy (PathFollow2D)   ← 持有 Progress 值，根据 Progress 在父 Path2D 的 Curve 上插值出位置
```

- **Path2D** 只存曲线数据，不参与位置计算。它的核心成员是 `Curve2D Curve { get; set; }`。
- **PathFollow2D** 必须作为 Path2D 的**直接子节点**（或在同一个 Scene 树层级中，父链上有 Path2D）。它在 `_Ready()` 时会向上递归找第一个 Path2D 祖先，读取其 Curve。

### 2.2 Curve2D 的三种控制点模式

Path2D 的曲线由多个 **CurvePoint** 组成，每个点有 2 个控制柄（in / out），类似 Photoshop 钢笔工具或 SVG path：

```
控制点 A ────────(control_in / control_out)──────── 控制点 B
```

| 模式 | 视觉效果 | 塔防适用度 |
|------|---------|-----------|
| **Free（自由）** | 两个控制柄独立调节，可做任意贝塞尔曲线 | ⭐⭐⭐⭐⭐ 复杂蜿蜒路径 |
| **Mirrored（对称）** | 两个控制柄始终反向对称，拐弯对称平滑 | ⭐⭐⭐⭐ 简单拐弯 |
| **Aligned（对齐）** | 控制柄始终与相邻两点连线共线，拐点更"硬" | ⭐⭐⭐ 直角拐点多的网格塔防 |

关卡策划在编辑器中用 Path2D 节点可视化绘制路径，零代码即可产出完整的敌人行进路线。

### 2.3 PathFollow2D 的三大核心属性

```csharp
public partial class PathFollow2D : Node2D
{
    // 核心 1：沿路径前进了多少「像素距离」
    // 直接累加 MoveSpeed * delta 就是正确的移动公式！
    public float Progress { get; set; }

    // 核心 2：沿路径前进了多少「比例」（0.0 = 起点，1.0 = 终点）
    // 适合做"是否到达终点"判定，不依赖具体路径长度
    public float ProgressRatio { get; set; }

    // 核心 3：子节点的 Transform 跟随策略（枚举）
    // - None: 只跟随位置，不旋转
    // - Rotation: 子节点旋转对齐路径切线方向（敌人面朝前方）
    // - Translation: 只跟随位置
    // - Orientation: 旋转 + 镜像翻转（用于复杂路径翻转）
    public PathFollow2D.RotationEnum RotationMode { get; set; }
}
```

**关键公式**：假设 Path2D 的 Curve 总长度为 L 像素，则：

```
ProgressRatio = Progress / L
Progress      = ProgressRatio * L
```

在 Enemy.cs 的 `_Process` 中：
```csharp
Progress += Data.MoveSpeed * (float)delta;
// MoveSpeed 单位是像素/秒，delta 是秒，乘积就是「本帧推进了多少像素」
// 完美匹配 Progress 的单位！
```

### 2.4 Loop / Clamp 模式

PathFollow2D 有一个 `bool Loop` 属性：
- **Loop = true**（默认）：`Progress` 超过 L 会自动取模回到起点，适合巡逻敌人无限绕圈
- **Loop = false**：`Progress` 超过 L 会被 clamp 在终点，适合塔防敌人"到终点停住（然后扣玩家血）"

在本项目 Enemy 中，我们使用 `ProgressRatio >= 1.0f` 作为到达判定，即使 Loop=true 也能正确触发（触发后立即 QueueFree，不会真的绕圈回来）。

---

## 三、Enemy 实例化与生命周期的完整流程

### 3.1 波次管理器的伪代码（后续模块，此处预览架构）

```csharp
// 后续 WaveManager.cs 中大概长这样：
public void SpawnEnemy(string enemyId)
{
    // 1. 从配置注册表加载 EnemyData
    var enemyData = EnemyRegistry.Get(enemyId);

    // 2. 加载 Enemy 的 PackedScene（包含 Sprite2D、CollisionShape2D 等）
    var enemyScene = GD.Load<PackedScene>("res://Game/Scenes/Enemies/Enemy_Base.tscn");
    var enemy = enemyScene.Instantiate<Enemy>();

    // 3. 注入配置 → 必须在 AddChild 之前，这样 _Ready() 时 Data 不为 null
    enemy.Data = enemyData;

    // 4. 添加到 Path2D 下（关键！否则 PathFollow2D 找不到父 Path2D）
    _enemyPath.AddChild(enemy);
    //   此时内部调用链：
    //   enemy._Ready()
    //     → CurrentHp = Data.MaxHp;
    //     → Progress = 0;  // 从路径起点开始
    //     → Position 自动被 PathFollow2D 设置为 Curve 的起点坐标
}
```

### 3.2 防御塔攻击链路（跨模块通讯，体现 EventBus 的价值）

```
Tower 节点发现目标：
  Tower._Process(delta)
    → 扫描范围内 Enemy（通过 GetTree().GetNodesInGroup("enemies") 或 Area2D body_entered）
    → 调用 enemy.TakeDamage(Data.Damage)

Enemy 扣血：
  Enemy.TakeDamage(damage)
    → CurrentHp -= damage
    → CurrentHp <= 0 ?
        YES → EventBus.RaiseEnemyKilled(EnemyId, RewardGold)
              → QueueFree()

EventBus 广播（解耦的威力！谁关心谁订阅，互不引用）：
  ├── 经济系统 → 玩家金币 += RewardGold → RaiseGoldChanged(newGold)
  ├── 音效系统 → 播放"击杀"音效
  ├── UI 系统 → 飘字 "+10 金币"
  └── 波次系统 → 本波存活敌人计数--，检查是否全清 → RaiseWaveCompleted()

敌人走到路径尽头：
  Enemy._Process(delta)
    → ProgressRatio >= 1.0f
        YES → EventBus.RaiseEnemyReachedEnd(DamageToPlayer)
              → QueueFree()

EventBus 广播：
  └── 玩家管理系统 → PlayerHp -= DamageToPlayer → RaisePlayerHpChanged(newHp)
        → newHp <= 0 ? → RaiseGameOver(isVictory: false)
```

**架构优势**：Tower 和 Enemy 彼此直接交互（TakeDamage 是方法调用，同在 Gameplay 层，合理），但它们与经济系统、玩家系统、UI 系统之间**完全零引用**——全部通过 EventBus 事件解耦。任何一方的修改都不会影响另一方。

### 3.3 为什么 TakeDamage 用方法调用而非 EventBus 事件？

你可能会问：为什么塔打敌人不也走 EventBus？比如 `EventBus.RaiseDamageEnemy(enemyId, damage)`？

答案：**EventBus 用于跨领域的模块解耦，同一领域内的直接交互可以（且应该）直接方法调用。**

| 交互 | 是否跨领域 | 正确手段 | 原因 |
|------|-----------|---------|------|
| Tower → Enemy (TakeDamage) | 同属「Gameplay 战斗层」 | 直接方法调用 | 强因果、高频、需要立即执行；发事件反而会增加复杂度和延迟 |
| Enemy → Gold 增加 | Gameplay → 经济系统 | EventBus | 跨领域解耦；将来换一套经济系统实现时 Enemy 代码零改动 |
| Enemy → HP 扣除 | Gameplay → 玩家系统 | EventBus | 跨领域解耦；PlayerManager 可能在客户端、服务端、存档层等不同位置 |

**经验法则**：在同一个 Gameplay 子命名空间内的类（Gameplay.Towers / Gameplay.Enemies），互相调用方法是合理的；跨出 Gameplay 命名空间到 Config / Core / UI / Audio / SaveLoad，一律走 EventBus。

---

## 四、容易踩的坑与防御性编程

### 坑 1：Enemy 不是 Path2D 的子节点 → 原地不动

```csharp
// ❌ 错误：直接加到 Level 根节点
Level.AddChild(enemy);
//   Enemy 向上找 Path2D 祖先 → 找不到 → Progress 改了 Position 也不动！

// ✅ 正确：加到 Path2D 下
EnemyPath.AddChild(enemy);
```

防御手段：在 Enemy._Ready() 中加校验（可后续增强）：
```csharp
if (GetParent() is not Path2D)
    GD.PrintErr($"[Enemy] {Name} 必须是 Path2D 的直接子节点！");
```

### 坑 2：Data 为 null → 空引用崩溃

策划在编辑器中拖了个 Enemy 节点到场景里玩，但没赋值 Data。

防御手段：Enemy._Ready() 已做检查，null 时 `GD.PrintErr + QueueFree()` 保证不会在 `_Process` 里 NRE。

### 坑 3：TakeDamage 传入负数 → 敌人无限回血

```csharp
// 某个塔的算法 bug：伤害 = baseDamage * (1 - armor)，结果 armor > 1 时 damage < 0
enemy.TakeDamage(-50);  // CurrentHp += 50！无敌了

// ✅ Enemy.TakeDamage 内部已做截断：if (damage < 0) damage = 0;
```

### 坑 4：HP 扣成负数后重复触发击杀

多座塔在同一帧内对残血敌人同时攻击，`TakeDamage` 被调用 3 次：
1. TakeDamage(10) → HP = -5 → QueueFree → 触发击杀事件 ✅
2. TakeDamage(10) → HP = -15 → **又触发一次击杀事件** ❌ → 金币翻倍！

防御手段（后续优化方向）：
```csharp
private bool _isDead = false;

public void TakeDamage(float damage)
{
    if (_isDead) return;   // 早返回，死亡后忽略后续伤害
    // ... 扣血 ...
    if (CurrentHp <= 0)
    {
        _isDead = true;
        EventBus.RaiseEnemyKilled(...);
        QueueFree();
    }
}
```

当前版本未做优化，但在 MVP 阶段可接受；后续接入多塔攻击时需补上。

### 坑 5：Progress 累加精度问题

float 精度在 32 位下，Progress 值累积到百万级别会出现抖动。塔防单条路径一般 < 10000 像素，实际完全够用。若遇到超长路径（特殊大地图），改用 `ProgressRatio`（0-1 浮点空间，精度更均匀）即可。

---

## 五、PathFollow2D 的性能考量（商业化项目）

### 5.1 PathFollow2D 内部的计算开销

每帧（对每个 Enemy）Godot 内部会做：
1. `Progress` → 根据 Curve 的 point 数据做贝塞尔分段查找 → 计算 `(x, y, tangent)` 坐标
2. 将结果写入 `Enemy.Position` 和 `Rotation`（根据 RotationMode）

复杂度近似 O(log N) 二分查找（N 为 Curve 上 ControlPoint 数）。**对于塔防常规 < 100 个敌人同屏，CPU 占用可忽略不计。**

### 5.2 同屏敌人数量爆炸怎么办？

极端情况下（手游同屏 500+ 小怪），PathFollow2D 的开销可能占比上升。优化手段：
- **空间切分**：把地图切成 4 条 Path2D（左上、右上、左下、右下），每条只负责一段，Enemy 到段末时切换到下一段
- **自定义 Position 计算**：不用 PathFollow2D，自己写 `Vector2 MoveAlongPath(float progress)` 缓存路径的分段数据数组（用 `Curve2D.SampleBaked()` 预烘焙 1000 个点到 `Vector2[]`，每帧直接查表，O(1)）
- **批处理**：用 `MultiMeshInstance2D` 渲染大批外观相同的敌人（CPU 计算 + GPU 实例化绘制，10000 个也能跑）

**MVP 阶段直接 PathFollow2D，不做过早优化**——等性能分析器（Godot Debug → Monitors → Time）显示 PathFollow 计算真的是热点时再动手。

---

## 六、总结：Enemy 系统的设计决策矩阵

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **移动节点** | PathFollow2D（而非 CharacterBody2D） | 精确沿预定义路径、零寻路代码、自动平滑拐弯 |
| **数据层** | EnemyData : Resource + [GlobalClass] | 数据驱动，策划在编辑器中调参无需编译 |
| **事件解耦** | 新增 OnEnemyReachedEnd 中间事件 | Enemy 不知道玩家 HP 总值，只能传递「伤害量」而非「新 HP 值」 |
| **跨领域通讯** | EventBus（击杀、逃脱） | 经济、UI、音效、波次系统订阅即可，零依赖 |
| **同领域通讯** | 直接调用 enemy.TakeDamage() | Tower 和 Enemy 同属 Gameplay，强因果高频调用，直接方法更高效 |
| **销毁安全** | QueueFree() 而非 Free() | Godot 帧遍历内销毁节点必须延迟到帧末，避免遍历崩溃 |
| **防御性** | Data null 检查 + damage 负数截断 | 防止策划误操作和外部算法 bug 导致的崩溃 / 逻辑错误 |

本 Enemy 模块仅用 **~80 行 EnemyData + ~90 行 Enemy** 实现了完整的数据配置 + 路径移动 + 生命值 + 击杀/逃脱事件广播，完全符合 Tower Defence 项目的「Resource-driven + EventBus 解耦」架构规范。
