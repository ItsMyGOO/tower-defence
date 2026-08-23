# EventBus 事件总线架构设计

## 一、为什么需要事件总线

在塔防游戏中，模块之间存在大量横向通信需求：
- **经济系统**：敌人被击杀 → 增加金币 → UI 更新金币显示
- **防御系统**：玩家放置塔 → 扣除金币 → 地图渲染塔 → 音效播放
- **波次系统**：波次完成 → 奖励结算 → UI 弹窗 → 准备下一波
- **游戏流程**：玩家 HP 归零 → 触发 GameOver → 暂停游戏 → 显示结算界面

如果采用**直接引用**的方式：
```
PlayerStats 直接持有 GoldUI 引用
Enemy 直接调用 PlayerStats.AddGold()
TowerManager 直接访问 MapRenderer
```
则会导致：
1. **紧耦合**：修改 A 模块可能影响 B、C、D 模块
2. **难以测试**：单元测试需构造大量依赖对象
3. **扩展困难**：新增一个"击杀统计"功能需要修改 Enemy 类

事件总线通过**中间人**模式，让发布者和订阅者互不相识，解决以上问题。

---

## 二、架构设计图示

```
┌─────────────┐    发布事件      ┌─────────────┐    订阅事件      ┌─────────────┐
│   发布者     │ ──────────────▶ │  EventBus   │ ◀────────────── │   订阅者     │
│ (Publisher)  │   RaiseXxx()    │  (Broker)   │   Action<...>   │ (Subscriber) │
└─────────────┘                  └─────────────┘                  └─────────────┘
       │                                 │                                  │
       │         ┌──────────────────┐   │   ┌──────────────────────┐        │
       └───────▶ │ OnEnemyKilled    │ ──┴──▶ │ GoldManager          │ ◀──────┘
                 │ OnGoldChanged    │ ──────▶ │ GoldUI               │
                 │ OnTowerPlaced    │ ──────▶ │ MapRenderer          │
                 │ OnPlayerHpChanged│ ──────▶ │ HpBarUI              │
                 │ OnGameOver       │ ──────▶ │ GameOverScreen       │
                 └──────────────────┘         └──────────────────────┘
```

### 数据流示例：敌人被击杀
```
Enemy.cs (死亡检测)
  └─ EventBus.RaiseEnemyKilled(enemyId, reward)
        │
        ├─▶ GoldManager 收到事件 → 增加金币 → EventBus.RaiseGoldChanged()
        │                                  └─▶ GoldUI 收到事件 → 更新数字
        │
        ├─▶ ScoreManager 收到事件 → 累计击杀数
        │
        └─▶ WaveManager 收到事件 → 判断波次是否清空
                                      └─ 清空时 → EventBus.RaiseWaveCompleted()
```

---

## 三、内存泄漏风险与取消订阅（重要）

### 1. 为什么会内存泄漏？

C# 中 `event` / `Action` 的本质是**多路广播委托（MulticastDelegate）**，内部维护一个调用列表。当订阅者通过 `+=` 注册时：
- **EventBus 持有订阅者方法的引用**
- 如果订阅者是**实例方法**，则该委托同时持有**订阅者对象实例的引用**

```csharp
public class GoldUI : Control
{
    public override void _Ready()
    {
        // ❌ 这里 EventBus 的 OnGoldChanged 内部委托链
        //    持有了当前 GoldUI 实例的强引用
        EventBus.OnGoldChanged += HandleGoldChanged;
    }

    private void HandleGoldChanged(int newGold)
    {
        GoldLabel.Text = newGold.ToString();
    }
}
```

如果场景切换、GoldUI 节点被移除/销毁，但**没有取消订阅**：
- EventBus（静态类，生命周期 = 整个 AppDomain）仍然持有 GoldUI 实例引用
- GC 无法回收该 GoldUI 及其关联的所有子节点/资源
- 多次进出该场景，内存中就会残留多个 GoldUI 实例 → **内存泄漏**

### 2. 正确的订阅模式（必遵守）

#### ✅ 模式一：Node 节点在 `_EnterTree` 订阅，`_ExitTree` 取消订阅
```csharp
public class GoldUI : Control
{
    public override void _EnterTree()
    {
        EventBus.OnGoldChanged += HandleGoldChanged;
    }

    public override void _ExitTree()
    {
        // 必须对称地 -= ，即使场景销毁时也能触发
        EventBus.OnGoldChanged -= HandleGoldChanged;
    }

    private void HandleGoldChanged(int newGold)
    {
        GoldLabel.Text = newGold.ToString();
    }
}
```

#### ✅ 模式二：生命周期更长的管理器类，在构造函数订阅、Dispose 取消订阅
```csharp
public class AudioManager : IDisposable
{
    public AudioManager()
    {
        EventBus.OnTowerPlaced += HandleTowerPlaced;
        EventBus.OnEnemyKilled += HandleEnemyKilled;
    }

    public void Dispose()
    {
        EventBus.OnTowerPlaced -= HandleTowerPlaced;
        EventBus.OnEnemyKilled -= HandleEnemyKilled;
    }

    // ...
}
```

### 3. 检查清单（Code Review 必过项）

| 检查项 | 通过标准 |
|--------|----------|
| 对称原则 | 凡出现 `+=` 的文件，必须能找到匹配的 `-=` |
| 生命周期 | 订阅者短于 EventBus 的必须取消（几乎所有业务类都短于静态类） |
| 匿名方法 | 禁止使用匿名 lambda 订阅（无法正确 `-=`，除非保存委托引用） |

#### ❌ 反例：匿名 lambda 订阅（无法取消）
```csharp
EventBus.OnGoldChanged += (g) => GoldLabel.Text = g.ToString();
// 之后即使写相同的 lambda 做 -= 也不会生效，因为委托实例不同
```

#### ✅ 正例：保存委托引用再取消
```csharp
private Action<int> _goldHandler;

public override void _EnterTree()
{
    _goldHandler = (g) => GoldLabel.Text = g.ToString();
    EventBus.OnGoldChanged += _goldHandler;
}

public override void _ExitTree()
{
    EventBus.OnGoldChanged -= _goldHandler;
}
```

---

## 四、EventBus 设计边界：静态类 vs Godot AutoLoad Node

### 当前选择：纯静态类 `static class EventBus`

**优点：**
- 零配置：不需要到 `project.godot` 的 AutoLoad 列表注册
- 零依赖：不继承 Node，单测时无需构造 Godot 场景树
- 调用语法干净：`EventBus.RaiseXxx()` 直接使用

**局限：**
- 无法直接挂载到 Godot 的 Node 树上，不能使用 `_Process`、信号等 Node 能力
- 不能在编辑器中直观查看订阅状态

### 未来可考虑的升级方案（Node 版本）
如果后续需要事件总线具备 Node 能力（如延迟派发、按帧节流、编辑器调试面板），可新增第二实现：

```csharp
// Game/Core/AutoLoads/EventBusNode.cs  (挂到 AutoLoad)
public partial class EventBusNode : Node
{
    // 转发到静态 EventBus，或直接在此声明事件
    // 可添加 _Process 内做限流、事件队列等高级能力
}
```

当前阶段先保持 `static class` 轻量实现，待需求明确时再演进。

---

## 五、事件设计原则（新增事件时参考）

1. **单一语义**：一个事件只表示一件事。避免出现 `OnTowerChanged(ChangeType, ...)` 把放置/出售/升级合并。
2. **参数从简**：尽量传原始值（int、string、Vector2I），少传复杂对象引用。传引用会增加订阅者和发布者的耦合（依赖同一个 Entity 类）。
3. **过去式命名**：`OnXxxChanged / OnXxxCompleted / OnXxxPlaced`，语义上表达"事实已发生"，订阅者不应再试图修改事件源头状态。
4. **双向禁止**：事件总线是单向通知，不要通过回调参数或返回值让订阅者"回传数据"给发布者。需要数据查询走正常依赖注入或单例查询接口。
