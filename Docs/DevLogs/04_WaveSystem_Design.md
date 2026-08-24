# 波次系统设计：数据驱动配置 + 定时器刷怪 + EventBus 状态闭环

## 一、架构总览：波次系统在项目中的位置

```
Tower Defence 整体架构（核心模块 1-4）：

┌────────────────────────────────────────────────────────────────────┐
│                          游戏主场景 (Main)                          │
│                                                                    │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────────┐   │
│  │  EventBus   │  │ GameManager  │  │      WaveManager         │   │
│  │  (AutoLoad) │  │ (游戏流程控) │  │  ├─ Waves: WaveData[]    │   │
│  │             │  │              │  │  ├─ EnemyBaseScene       │   │
│  │ 事件广播中心│  │ 调用 Start   │  │  ├─ TargetPath: Path2D   │   │
│  └──────┬──────┘  │  NextWave()  │  │  └─ 定时刷怪逻辑         │   │
│         │         └──────┬───────┘  └──────┬───────────────────┘   │
│         │                │                  │                       │
│         │    (订阅/发布) │   (直接方法调用)  │ (Instantiate + AddChild)
│         ▼                ▼                  ▼                       │
│  ┌───────────────────────────────────────────────────┐             │
│  │                  Gameplay 层                      │             │
│  │                                                   │             │
│  │  ┌──────────────────────────────────────────┐     │             │
│  │  │         EnemyPath (Path2D)               │     │             │
│  │  │   （关卡策划画出的路径曲线）              │     │             │
│  │  │                                          │     │             │
│  │  │  ┌─────────┐ ┌─────────┐ ┌─────────┐    │     │             │
│  │  │  │ Enemy 1 │ │ Enemy 2 │ │ Enemy 3 │    │     │             │
│  │  │  │ PathF2D │ │ PathF2D │ │ PathF2D │    │     │             │
│  │  │  └────┬────┘ └────┬────┘ └────┬────┘    │     │             │
│  │  └───────┼───────────┼───────────┼──────────┘     │             │
│  │          │ 击杀/逃跑  │           │                │             │
│  │          └───────────┼───────────┘                │             │
│  │                      │ RaiseEnemyKilled /         │             │
│  │                      │ RaiseEnemyReachedEnd       │             │
│  └──────────────────────┼────────────────────────────┘             │
│                         ▼                                           │
│                    EventBus 事件广播                                │
│                    ├── OnEnemyKilled ────→ 经济系统 +金币           │
│                    ├── OnEnemyReachedEnd → 玩家系统 -HP             │
│                    ├── OnWaveStarted   ───→ UI 显示波次预告         │
│                    └── OnWaveCompleted ───→ GameManager 解锁下一波  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
                    （配置层：Resource Assets）
                    ┌──────────────────────────────┐
                    │  Config 目录                  │
                    │  ├─ Enemies/EnemyData.tres    │
                    │  └─ Waves/Wave_1.tres         │
                    │     └─ EnemyTypes[] 引用以上  │
                    └──────────────────────────────┘
```

本模块（#4 波次系统）的核心职责：**根据 WaveData 配置，按时间节奏生成 Enemy 实例并挂载到 Path2D，然后通过事件追踪敌人存活状态，驱动波次开始/完成的状态闭环。**

---

## 二、数据驱动设计：为什么波次也是 Resource

### 2.1 Resource 驱动的三大好处

塔防游戏有三类"策划资产"必须独立于代码：防御塔属性、敌人属性、波次配置。前两者已在 #2、#3 模块实现为 Resource，本模块延续同样的模式：

| 优势 | 传统硬编码写法 | Resource 数据驱动写法 |
|------|---------------|----------------------|
| **迭代效率** | 波次数量/敌人顺序/间隔一改就要编译重启 | 在 Godot Inspector 中拖拽调整，保存即生效 |
| **职责分离** | 程序员兼做数值策划，改逻辑怕影响数值 | 策划独立填 `.tres` 资源，完全不碰 C# 代码 |
| **版本管理** | `.cs` diff 难分清楚是"加逻辑"还是"调数值" | `.tres` 是纯文本，diff 清晰，冲突易解 |
| **热更新友好** | 改数值必须重新发版 | 将来接资源热更框架时，替换 `.tres` 即可在线调难度 |

### 2.2 EnemyTypes 为什么是有序数组而非 (类型, 数量) 字典

两种配置方式对比：

**方式 A：(EnemyId, Count) 字典（简单但不灵活）**
```
Wave:
  Slime: 10
  Goblin: 5
  Orc: 2
  SpawnInterval: 1.0
```
- 优点：配置体量小，"10 只史莱姆"一目了然
- **致命缺点**：不能控制刷怪顺序。玩家需要的是"史莱姆史莱姆哥布林史莱姆哥布林兽人..."这种穿插节奏，而不是 10 只史莱姆后 5 只哥布林。想做 BOSS 关（先小兵后 BOSS）更是不可能。

**方式 B：EnemyData[] 有序数组（灵活且直观）**
```
EnemyTypes: [Slime, Slime, Goblin, Slime, Goblin, Orc, Orc]
```
- 数组下标 = 刷怪顺序，策划在编辑器里上下拖拽即可体验节奏变化
- 同一敌人可任意重复出现（10 个 Slime 元素就是 10 只）
- 零额外解析逻辑，代码里按顺序 `cursor++` 即可

**结论**：塔防波次的"节奏感"远重于配置简洁度，选择方式 B。

### 2.3 配置资产之间的引用关系

```
Wave_1.tres
├── WaveIndex = 1
├── SpawnInterval = 1.2
├── DelayBeforeStart = 5.0
└── EnemyTypes[] (Godot Resource 引用，不是字符串 ID)
      ├── [0] → res://Game/Config/Enemies/Slime.tres (EnemyData)
      ├── [1] → res://Game/Config/Enemies/Slime.tres
      ├── [2] → res://Game/Config/Enemies/Goblin.tres
      └── ...
```

**注意**：EnemyTypes 直接引用 `EnemyData` Resource 对象，而不是存字符串 EnemyId。好处：
- 在 Godot Inspector 中拖入 `.tres` 文件即可，不用手动打字再做"字符串 → Resource"查表
- WaveManager 拿到手直接赋值给 `enemy.Data = enemyData`，省去注册表查找
- 引用一致性有 Godot 资源系统兜底（如果删了 EnemyData，WaveData 里会直接显示 Missing 占位，不会 silent bug）

---

## 三、刷怪定时逻辑：双 Timer + 游标的状态机

### 3.1 为什么用 Godot Timer 而不是 `_Process` 累加 delta

两种写法在功能上等价，但工程上差距显著：

| 对比维度 | `_Process` 里 float 累加 delta | Godot Timer 节点 |
|---------|-------------------------------|------------------|
| **代码可读性** | 声明 `_spawnTimerAcc = 0.0f`，每帧 `+= delta`，`if >= interval { 触发; -= interval }`，5-6 行，还需处理浮点精度 | 3 行：`new Timer { WaitTime = x, OneShot = y }.Timeout += Handler; AddChild(timer); timer.Start()` |
| **暂停兼容** | 需要自己判断 `GetTree().Paused`，否则游戏暂停时定时器还在跑，一恢复就瞬间补 N 只怪 | `Timer.PauseMode = PauseModeEnum.Inherit`，自动跟随场景树暂停，无需额外代码 |
| **调试可视化** | 变量值要打 Watch 才能看到 | Godot Debugger 的 Scene 树面板里实时显示 Timer 的 TimeLeft，Inspect 改 WaitTime 可现场调参 |
| **生命周期** | 手动管理，场景树移除时仍可能有残留逻辑在跑 | 作为 WaveManager 的子节点，父节点 QueueFree 时自动销毁，无泄漏风险 |

**结论**：Godot 项目中"有明确时间间隔、需要暂停支持"的逻辑，一律优先用 Timer 节点。

### 3.2 双定时器的状态迁移完整流程

```
                                    ┌─────────────────────────────┐
                                    │        初始状态              │
                                    │  CurrentWaveIndex = -1      │
                                    │  IsWaveActive = false       │
                                    └──────────────┬──────────────┘
                                                   │ StartNextWave()
                                                   ▼
                              ┌──────────────────────────────────────┐
                              │        1. 配置校验与状态初始化        │
                              │  CurrentWaveIndex++                  │
                              │  CurrentWave = Waves[index]          │
                              │  IsWaveActive = true                 │
                              │  _enemyCursor = 0                    │
                              │  RemainingSpawnCount = TotalCount    │
                              │  AliveEnemyCount = 0                 │
                              └──────────────┬───────────────────────┘
                                             │
                                             ▼
                              ┌──────────────────────────────────────┐
                              │  2. 准备阶段 (_delayTimer, OneShot)  │
                              │  WaitTime = DelayBeforeStart         │
                              │  （如果 DelayBeforeStart <= 0，       │
                              │    直接跳到步骤 3）                   │
                              └──────────────┬───────────────────────┘
                                             │ Timeout
                                             ▼
                              ┌──────────────────────────────────────┐
                              │  3. 波次正式开始                      │
                              │  EventBus.RaiseWaveStarted()         │
                              │  SpawnEnemy() → 第 1 只立即生成 ✨   │
                              │  _spawnTimer.Start() (Loop=true)     │
                              └──────────────┬───────────────────────┘
                                             │
                                             ▼
                         ┌─────────────────────────────────────────────────┐
                         │  4. 循环刷怪 (_spawnTimer.Timeout × N 次)      │
                         │                                                 │
                         │  ┌───────────────────────────────────────────┐ │
                         │  │ cursor < EnemyTypes.Count ?               │ │
                         │  │     YES → SpawnEnemy() → cursor++         │ │
                         │  │     NO  → _spawnTimer.Stop()              │ │
                         │  │           → 进入步骤 5                    │ │
                         │  └───────────────────────────────────────────┘ │
                         └─────────────────────────────────────────────────┘
                                             │
                                             ▼
                              ┌──────────────────────────────────────┐
                              │  5. 等待敌人全部清除                   │
                              │  (此时已无怪可刷，但场上仍有存活敌人)   │
                              │  等待 EventBus.OnEnemyKilled /        │
                              │        OnEnemyReachedEnd 事件递减     │
                              └──────────────┬───────────────────────┘
                                             │
                                             ▼
                              ┌──────────────────────────────────────┐
                              │  6. 波次完成判定                      │
                              │  CheckWaveCompleted()                 │
                              │  RemainingSpawnCount == 0             │
                              │       && AliveEnemyCount == 0 ?       │
                              └──────────────┬───────────────────────┘
                                             │ YES
                                             ▼
                              ┌──────────────────────────────────────┐
                              │  7. 完成闭环                          │
                              │  IsWaveActive = false                 │
                              │  _spawnTimer/_delayTimer 全 Stop()    │
                              │  EventBus.RaiseWaveCompleted()        │
                              └──────────────────────────────────────┘
                                  (等待 GameManager 下次调用
                                   StartNextWave() 进入下一波)
```

### 3.3 为什么"第 1 只怪"不等 SpawnInterval

**Bad UX（等待时间 = Delay + Interval）**：
```
玩家点"开始波次" → 等 3 秒准备 → 又等 1.2 秒间隔 → 才出现第 1 只怪
  合计 4.2 秒空窗期！玩家感觉"卡了"、"按钮没反应"
```

**Good UX（当前实现：Delay 结束即刷首只）**：
```
玩家点"开始波次" → 等 3 秒准备 → 第 1 只怪立刻出现
                              → 隔 1.2 秒第 2 只
                              → 隔 1.2 秒第 3 只
  玩家体感：准备结束的瞬间"波次开始"是真实的，不是预告而已
```

代码实现（`OnDelayTimerTimeout()` 里先调一次 `SpawnEnemy()` 再启动循环定时器）：
```csharp
EventBus.RaiseWaveStarted(CurrentWave.WaveIndex);
SpawnEnemy();  // ← 第 1 只不等 SpawnInterval
_spawnTimer.WaitTime = Mathf.Max(0.01f, CurrentWave.SpawnInterval);
_spawnTimer.Start();
```

### 3.4 为什么 AliveEnemyCount 用 EventBus 事件递减，而不是扫描场景树

两种方案对比：

| 方案 | 实现 | 时间复杂度 | 问题 |
|------|------|-----------|------|
| **A：GetTree().GetNodesInGroup("enemies") 每帧数** | CheckWaveCompleted 里数组的 Length | O(N) 每帧或每次检查 | 1) 敌人要主动加入 "enemies" Group，容易忘；2) 如果有测试场景残留 Enemy 或 Particle 也打了这个 Tag，计数不准；3) 检查波次完成需要"等"到某个检查时机才触发 |
| **B：订阅 EventBus.OnEnemyKilled + OnEnemyReachedEnd（当前方案）** | 事件回调里 `-= 1` 后立即 `CheckWaveCompleted()` | O(1) 精确计数 | 无，事件触发时机就是"敌人数量减 1"的精确时刻 |

**EventBus 驱动 vs 轮询扫描**：
- 事件驱动：敌人被击杀的**同一帧**，AliveEnemyCount 归零 → CheckWaveCompleted() → 立刻广播 OnWaveCompleted → UI "波次完成"弹出。玩家觉得"响应很跟手"。
- 轮询扫描：可能有最多一帧的延迟（检查频率如果是 0.1s 定时器则最多 0.1s 延迟），体感上不致命，但架构上属于"波次系统不必要地依赖场景树结构"，耦合了不该耦合的东西。

**结论**：既然 EventBus 已经在 Enemy 里发布了 OnEnemyKilled / OnEnemyReachedEnd，WaveManager 直接订阅就是最干净、最精确、最低开销的方案。

---

## 四、与 EventBus 的状态闭环：从"开始"到"完成"的完整链路

### 4.1 EventBus 事件定义回顾（模块 1 已实现）

从模块 1（EventBus）中我们已经预留了波次相关的两个事件签名：

```csharp
// 当新一波敌人开始生成时触发
public static event Action<int> OnWaveStarted;
public static void RaiseWaveStarted(int waveIndex) => OnWaveStarted?.Invoke(waveIndex);

// 当一波敌人全部清理完毕（波次完成）时触发
public static event Action<int> OnWaveCompleted;
public static void RaiseWaveCompleted(int waveIndex) => OnWaveCompleted?.Invoke(waveIndex);
```

签名设计要点：**只传 waveIndex（或 CurrentWave.WaveIndex）**，不传 WaveData 对象本身。原因：
- **跨领域最小信息量原则**：UI 只需要显示"第 3 波完成了"，不需要拿到 WaveData 里的 SpawnInterval；经济系统只需要知道"第几波结束该给多少波次奖励"，可以自己根据 waveIndex 查奖励表。
- **防止订阅方误依赖内部配置**：如果传 WaveData 引用，订阅方可能会偷偷修改 EnemyTypes，破坏纯配置的不可变性。
- **序列化兼容**：如果将来接多人联机、存档回播，int 比 Resource 引用容易序列化得多。

### 4.2 典型订阅方一览（未来模块将实现）

| 模块 | 订阅事件 | 响应行为 |
|------|---------|---------|
| **UI - 波次信息面板** | OnWaveStarted | 显示"第 X 波来袭！"大字提示，2 秒后淡出；更新顶部"当前波次"Label |
| **UI - 波次进度条** | OnWaveStarted / OnEnemyKilled / OnEnemyReachedEnd | 计算「已刷怪数 / 总怪数」进度条，随敌人消失实时推进 |
| **GameManager（游戏流程）** | OnWaveCompleted | 1) 发放本波奖励金币；2) 检查 index 是否是最后一波 → 是则触发通关；否则启用「下一波」按钮 |
| **AudioManager（音效）** | OnWaveStarted / OnWaveCompleted | 播放 BOSS 来袭 / 波次胜利音效 |
| **SaveLoad（存档）** | OnWaveCompleted | 存档写入"已完成到第 X 波" |
| **Achievement（成就）** | OnWaveCompleted | 解锁"通关第 10 波"等成就 |

通过 EventBus，WaveManager **只知道自己在"刷怪 + 计数 + 发事件"**，完全不关心 UI 长什么样、奖励发多少、成就怎么解锁——这就是解耦的威力。

### 4.3 状态闭环的"防重入"保护

WaveManager 用 `IsWaveActive` 布尔量锁住自己，防止外部误操作：

```csharp
public bool StartNextWave()
{
    if (IsWaveActive)
    {
        GD.PrintWarn("[WaveManager] 当前波次尚未完成，无法开启下一波。");
        return false;
    }
    // ... 正常逻辑
}
```

可能触发重入的场景（防御到位才不会出线上事故）：
1. **玩家快速连点「下一波」按钮**：UI 按钮没做禁用时可能连点 5 次 → 第一次 IsWaveActive=false 成功进入，后 4 次被拦截，不会刷 5 倍怪。
2. **测试代码在 OnWaveCompleted 回调里立即调用 StartNextWave**：OnWaveCompleted 发布在 `IsWaveActive = false` 之后，所以可以立刻开下一波（没问题，支持连续波次无间隔）。
3. **网络延迟同步开波**：多人模式下主机 + 客机各自 StartNextWave → 客机应只听主机的事件来驱动，本地直接调会被拦截（除非重置后）。

---

## 五、防御性编程清单：从 7 层 null 检查说起

### 5.1 外部依赖注入的 4 层检查（StartNextWave）

```csharp
CurrentWave == null ?              // 策划在 Waves[] 里塞了个空元素
TargetPath == null ?               // 场景中忘了拖 Path2D 节点到 Inspector
EnemyBaseScene == null ?           // 忘了拖敌人预制体
EnemyBaseScene.Instantiate()       // 拖错了预制体（根节点是 CharacterBody2D 不是 Enemy）
  is not Enemy enemyNode ?
```

任何一层出错都不崩，仅 `GD.PrintErr` 输出清晰的定位信息（"EnemyBaseScene 根节点类型不是 Enemy"），让策划/程序一眼知道该改哪里。

### 5.2 空 EnemyData 的容错

EnemyTypes 数组中某一格为空（策划拖 Asset 时漏了一格）：

```csharp
EnemyData enemyData = CurrentWave.EnemyTypes[_enemyCursor];
if (enemyData == null)
{
    GD.PrintErr($"[WaveManager] EnemyTypes[{_enemyCursor}] 为空，跳过该敌人。");
    _enemyCursor++;          // ← 记得前进，否则下一轮 Spawn 还卡在这里死循环
    RemainingSpawnCount--;   // ← 记得递减，否则波次永远无法完成
    return;
}
```

**注意**：跳过不合法条目时，**一定要同步更新 cursor 和 RemainingSpawnCount**！否则：
- cursor 不前进 → 每次定时器回调都卡着打印同一条错误 → 刷不出后续敌人
- RemainingSpawnCount 不减少 → 永远 > 0 → CheckWaveCompleted 无法通过 → 下一波永远不能开

### 5.3 SpawnInterval 最小值保护

```csharp
_spawnTimer.WaitTime = Mathf.Max(0.01f, CurrentWave.SpawnInterval);
```

防止策划手滑填 0 或负数导致 Timer WaitTime <= 0：Godot 在这种情况下会把 Timer 视为"立即触发"且 `_spawnTimer` OneShot=false → 每帧触发 N 次，瞬间刷出上万敌人 → 游戏直接崩溃。

### 5.4 事件回调中的 IsWaveActive 早返回

```csharp
private void HandleEnemyKilled(string enemyId, int goldReward)
{
    if (!IsWaveActive) return;   // ← 早返回
    if (AliveEnemyCount > 0) AliveEnemyCount--;
    CheckWaveCompleted();
}
```

**为什么需要这个判断？** 场景可能残留了上一局的"老 Enemy"（比如没清干净）或测试场景里玩家手动拖了个 Enemy 节点，它们死亡时也会 RaiseEnemyKilled。如果没有 IsWaveActive 门闩，AliveEnemyCount 会被减成负数——波次还没开始，AliveEnemyCount 已经是 -3 了，后续逻辑全错。

---

## 六、路径挂载规范：Enemy 为什么必须 AddChild 到 Path2D 下

### 6.1 PathFollow2D 的工作机制回顾（模块 3）

`Enemy : PathFollow2D` 在 Godot 内部的 `_Ready` 阶段会这样寻找路径：
```
Enemy._Ready()
  → 向上遍历父链，找第一个 Path2D 祖先
  → 读取其 Curve2D 数据缓存到内部
  → 之后每帧修改 Progress 时，根据缓存的 Curve 插值出 Position
```

**所以层级必须是这样：**
```
Path2D (TargetPath)       ← WaveManager.TargetPath 指向这里
  └── Enemy               ← WaveManager 里 TargetPath.AddChild(enemyNode)
        ↑
        PathFollow2D 的父链查找 → 找到 Path2D → 正常工作
```

**如果层级放错（Enemy 直接挂主场景）：**
```
MainScene
  ├── Path2D (TargetPath)
  └── Enemy               ← MainScene.AddChild(enemy) — ❌ 错误！
        ↑
        父链向上找 → 找不到 Path2D → Progress 改了白改 → 敌人原地不动
```

### 6.2 WaveManager 中的正确实现

```csharp
// Enemy.cs 要求：enemy.Data 必须在 _Ready 之前不为 null
enemyNode.Data = enemyData;           // ① 先注入配置（在 _Ready 前赋值）
enemyNode.Name = $"Enemy_{CurrentWave.WaveIndex}_{_enemyCursor}";
TargetPath.AddChild(enemyNode);       // ② 再加到 Path2D 下 → 触发 enemy._Ready()
                                        //    此时 Data 已赋值，Path2D 也找得到 ✅
```

**注入顺序很重要**：先 Data 后 AddChild，否则 Enemy._Ready 里 `Data == null` → 报错 QueueFree。

### 6.3 为什么不要求 Enemy 自己在 _Ready 里做"找 Path2D 父节点"的校验

模块 3 的 Enemy._Ready 目前只做了 Data null 检查，没校验父节点是不是 Path2D。本模块在 WaveManager 侧严格按规范写，就可以保证运行时不出错。

如果未来要加校验（防其他人写的代码不按规范），建议放在 Enemy._Ready：
```csharp
// 可作为后续增强加入 Enemy.cs
if (GetParent() is not Path2D)
    GD.PrintErr($"[Enemy] {Name} 父节点必须是 Path2D，当前是 {GetParent()?.GetType().Name}");
```

---

## 七、性能与可扩展性前瞻

### 7.1 当前实现的性能上限

WaveManager 的 CPU 开销主要来自两处：
1. **Timer.Timeout 回调 + SpawnEnemy 实例化**：每 0.5s-2s 触发 1 次，O(1)，可忽略
2. **EventBus 事件回调**：每次敌人死亡时被调用 1 次，2 次减运算 + 1 次 if 检查，可忽略

实际瓶颈不在这里，而在同屏 Enemy 数量（PathFollow2D 每帧插值）和 Tower 寻敌扫描（模块 5 才做）。**MVP 阶段完全不用优化波次管理器本身。**

### 7.2 功能扩展清单（版本 > 1.0 再做）

| 扩展功能 | 修改点 | 难度 |
|---------|-------|------|
| **多路径分路刷怪** | TargetPath → TargetPaths[]，SpawnEnemy 里轮询 / 权重分配 AddChild 的父节点 | ⭐⭐ |
| **波次内多变间隔** | EnemyTypes → WaveEntry[] { EnemyData Data; float DelayAfter; }，每次 Spawn 后重新设置 `_spawnTimer.WaitTime = currentEntry.DelayAfter` | ⭐⭐⭐ |
| **中途援军 / 随机事件** | 新增 `EventBus.OnWaveReinforcements(int waveIndex, EnemyData[] extras)` 事件，WaveManager 暴露 `InjectEnemies(EnemyData[])` 方法把援军追加到刷怪队列尾，AliveEnemyCount 同步增加 | ⭐⭐⭐ |
| **游戏倍速兼容** | Timer 的 `ProcessCallback = ProcessCallbackEnum.Idle` 受 Engine.TimeScale 影响；若游戏倍速用 `Engine.TimeScale = 2.0` 实现则天然加速刷怪，无需改 WaveManager | ⭐（不用改） |
| **存档加载时恢复波次状态** | 新增 `SetState(int waveIndex, bool isActive, int remaining, int alive)` + `GetState()` 接口，SaveLoad 模块存读；恢复时重建 cursor 和 Timer | ⭐⭐⭐⭐ |
| **波次预览功能（编辑器工具）** | 写一个 Godot Editor 插件，根据 WaveData 直接在编辑器里模拟播放刷怪时间轴，策划可视化确认节奏 | ⭐⭐⭐⭐⭐ |
| **敌人生成特效/音效** | SpawnEnemy 成功后 Raise 新事件 `OnEnemySpawned(string enemyId, Vector2 position)`，VFX / SFX 系统订阅 | ⭐ |

### 7.3 与未来模块的接口契约

为了让后续模块（GameManager、Tower、UI、经济系统）能无缝接入，WaveManager 需要保持以下"对外承诺"稳定不变（除非重构升级大版本）：

1. **EventBus 签名稳定**：`OnWaveStarted(int)` / `OnWaveCompleted(int)` 不改签名、不变含义
2. **StartNextWave() 无参调用稳定**：外部只需要调这个方法就能推进波次，不需要给 WaveManager 塞上下文
3. **公共 Get 属性只读**：CurrentWaveIndex / CurrentWave / IsWaveActive / AllWavesCompleted 外部只读取，不写入
4. **实例化流程稳定**：Enemy → Data 注入 → TargetPath.AddChild 的三步顺序不变，避免 Enemy._Ready 里的假设被破坏

---

## 八、与前三个模块的架构一致性检查

| 架构原则 | 模块 1 EventBus | 模块 2 TowerData | 模块 3 EnemySystem | 模块 4 WaveSystem | 一致？ |
|---------|-----------------|------------------|--------------------|-------------------|--------|
| **C# XML 中文注释** | ✅ 公共成员全有 | ✅ | ✅ | ✅ WaveData + WaveManager 均完整 | ✅ |
| **[GlobalClass] Resource** | N/A (static 类) | ✅ TowerData | ✅ EnemyData | ✅ WaveData | ✅ |
| **数据驱动分层** | N/A | TowerData 纯配置 / Tower节点逻辑（待做） | EnemyData 纯配置 / Enemy 节点逻辑 | ✅ WaveData 纯配置 / WaveManager 节点逻辑 | ✅ |
| **EventBus 解耦跨领域** | ✅ 事件中心 | N/A (暂无跨领域) | ✅ OnEnemyKilled / OnEnemyReachedEnd | ✅ OnWaveStarted / OnWaveCompleted | ✅ |
| **命名空间与目录匹配** | Core.AutoLoads | Config.Towers | Config.Enemies + Gameplay.Enemies | ✅ Config.Waves + Gameplay.Waves | ✅ |
| **Godot 特性正确使用** | N/A | N/A | ✅ PathFollow2D + QueueFree | ✅ Timer 双定时器 + AddChild 层级正确 | ✅ |

---

## 九、总结：波次系统的设计决策矩阵

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **配置层** | WaveData : Resource（而非代码枚举/JSON） | 数据驱动，策划独立配置，版本管理清晰 |
| **EnemyTypes 结构** | `EnemyData[]` 有序数组（而非字典<类型,数量>） | 支持灵活刷怪顺序（强弱穿插、BOSS 压轴），Inspector 拖拽即调 |
| **定时器实现** | 双 Godot Timer 节点：_delayTimer(OneShot) + _spawnTimer(Loop)（而非 _Process 累加） | 生命周期清晰、自动支持暂停、调试可视化、代码量少 |
| **首只怪生成时机** | Delay 结束立即 Spawn（而非再等一个 SpawnInterval） | UX 更好，玩家体感"准备结束 = 波次真的开始了" |
| **存活计数方式** | EventBus 事件回调 --AliveEnemyCount（而非轮询扫描场景树） | O(1) 精确、与事件触发同帧判定、不依赖场景树 Group 标记 |
| **波次完成判定** | RemainingSpawnCount==0 **且** AliveEnemyCount==0（二条件缺一不可） | 只看后者会把"还没开始刷怪"误判为"波次已完成" |
| **EventBus 载荷** | 仅 int waveIndex（不传 WaveData 对象） | 最小信息量、不可变性、跨领域订阅方不依赖配置层类型 |
| **防重入** | StartNextWave() 开头 IsWaveActive 门闩（bool） | 防玩家连点按钮、测试代码乱调、网络同步误触发 |
| **敌人挂载层级** | `TargetPath.AddChild(enemyNode)`（而非加到主场景） | PathFollow2D 必须能在父链上找到 Path2D 才能工作 |
| **生命周期** | _Ready 订阅事件 / _ExitTree 取消订阅（严格对称） | 防止 EventBus static 强引用导致的内存泄漏 |

波次系统仅用 **~50 行 WaveData（纯配置） + ~250 行 WaveManager（含防御性检查与注释）** 就实现了完整的数据驱动刷怪 + 存活追踪 + EventBus 状态闭环，完全符合 TowerDefence-Core 项目既定的「Resource 数据驱动 + EventBus 解耦 + Godot 原生组件正确使用」架构三部曲。
