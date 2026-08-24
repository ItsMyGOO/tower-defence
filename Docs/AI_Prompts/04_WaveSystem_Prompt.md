# 波次系统生成提示词与设计要点

## 生成提示词（Prompt）

```
请帮我完成《TowerDefense-Core》项目的第四个核心模块：波次配置与波次管理器 (WaveData.cs & WaveManager.cs)

1. Git 操作：基于当前 main 分支运行 git checkout -b feature/04-wave-system 创建并切换到新分支。
2. 创建波次配置 Resource：在 Game/Config/Waves/ 目录下创建 WaveData.cs 脚本：
   - 继承自 Resource，加上 [GlobalClass] 属性。
   - 包含字段 ([Export]):
     - WaveIndex (int, 波次序号)
     - EnemyTypes (Godot.Collections.Array<EnemyData>, 该波次生成的敌人类型列表)
     - SpawnInterval (float, 刷怪时间间隔秒数)
     - DelayBeforeStart (float, 波次开始前的准备时间)
   - 添加完整 C# XML 中文注释。
3. 创建波次管理器：在 Game/Gameplay/Waves/ 目录下创建 WaveManager.cs 脚本：
   - 继承自 Node。
   - 包含字段 ([Export]):
     - Waves (Godot.Collections.Array<WaveData>, 所有波次配置)
     - EnemyBaseScene (PackedScene, 敌人基础场景预制体)
     - TargetPath (Path2D, 刷怪的目标路径)
   - 实现逻辑：
     - 实现 StartNextWave() 与 SpawnEnemy() 定时生成逻辑。
     - 生成敌人实例时自动将其作为 PathFollow2D 挂载至 TargetPath，并为其赋值 EnemyData。
     - 结合敌人死亡 (OnEnemyKilled) 与到达终点 (OnEnemyReachedEnd) 追踪存活敌人数量，全部清除后触发 EventBus.OnWaveCompleted(waveIndex)。
     - 在波次开始时触发 EventBus.OnWaveStarted(waveIndex)。
4. 创建沉淀文档：
   - 在 Docs/AI_Prompts/04_WaveSystem_Prompt.md 中记录本次提示词。
   - 在 Docs/DevLogs/04_WaveSystem_Design.md 中记录波次数据驱动、刷怪定时器逻辑以及与 EventBus 状态闭环的设计思考。

要求：
1. 所有公共类、接口、方法、属性、枚举必须包含清晰的 C# XML 中文注释。
2. 注释需说明用途、参数含义、返回值、异常情况等关键信息。
3. 命名空间：WaveData -> TowerDefence.Config.Waves，WaveManager -> TowerDefence.Gameplay.Waves
4. 所有 [Export] 属性需提供合理的默认值，避免编辑器中字段为空导致空引用异常。
5. 遵循数据驱动设计原则：WaveData 仅存储纯数据，WaveManager 节点负责业务逻辑。
6. 注意 EventBus 解耦：必须通过 EventBus.OnWaveStarted / OnWaveCompleted 广播波次状态，禁止模块间硬编码引用。
7. 严格的防御性编程：对 EnemyBaseScene、TargetPath、WaveData、EnemyData 等外部依赖注入进行 null 检查，出错时打印明确的 GD.PrintErr 日志。
8. 波次完成判定必须同时满足两个条件：(a) 本波所有敌人已生成完毕 (b) 存活敌人数量 = 0。
```

## 设计要点记录

### 1. WaveData 字段设计说明

| 字段名 | 类型 | 默认值 | 设计意图 |
|--------|------|--------|----------|
| WaveIndex | int | `1` | 波次序号，用于 UI 显示（"第 3 波"）和事件参数；建议从 1 开始递增，符合玩家认知 |
| EnemyTypes | Array<EnemyData> | `[]` | 刷怪顺序队列，按列表下标依次生成；可重复放入同一种 EnemyData 实现连续刷同种怪 |
| SpawnInterval | float | `1.0f` | 两次刷怪之间的间隔秒数；值越小波次越密集；最小值被内部限定为 0.01s 防止除零 |
| DelayBeforeStart | float | `3.0f` | 玩家布防准备时间，倒计时后才开始刷怪；UI 可订阅 OnWaveStarted 前自行做倒计时展示 |

### 2. 命名空间与目录映射

| 类 | 命名空间 | 路径 |
|----|---------|------|
| WaveData | `TowerDefence.Config.Waves` | `Game/Config/Waves/WaveData.cs` |
| WaveManager | `TowerDefence.Gameplay.Waves` | `Game/Gameplay/Waves/WaveManager.cs` |

### 3. WaveManager 内部状态说明

| 状态字段 | 类型 | 变化时机 | 判定用途 |
|---------|------|---------|---------|
| CurrentWaveIndex | int | StartNextWave() 时 +1 | 当前正在处理的 Waves[] 下标（-1 表示尚未开始） |
| CurrentWave | WaveData | StartNextWave() 时赋值 | 当前波次的配置引用，方便内部快速访问 |
| RemainingSpawnCount | int | 开始时 = EnemyTypes.Count，每生成 1 个 -1 | 记录「还有多少只怪没生成」 |
| AliveEnemyCount | int | SpawnEnemy() 时 +1；OnEnemyKilled / OnEnemyReachedEnd 时 -1 | 记录「场上还有多少存活敌人」 |
| IsWaveActive | bool | StartNextWave() 时 = true；CheckWaveCompleted() 通过后 = false | 当前是否有进行中的波次，用于防止重入调用 StartNextWave() |

**波次完成的判定公式**：
```csharp
if (RemainingSpawnCount == 0 && AliveEnemyCount == 0) → RaiseWaveCompleted()
```

注意：不能只看 AliveEnemyCount == 0！因为在波次刚开始、还没生成任何敌人时 AliveEnemyCount 也是 0，但此时显然不应该算波次完成。必须两个计数器同时归零才是"真正的波次结束"。

### 4. 双定时器架构：为什么用 _delayTimer + _spawnTimer 而不是单个定时器

```
StartNextWave()
      │
      ├── 配置校验通过
      ├── IsWaveActive = true
      ├── 初始化计数器
      │
      ▼
  ┌──────────────────────────┐
  │  _delayTimer (OneShot)   │  ← DelayBeforeStart 秒倒计时
  │  WaitTime = 3.0f         │
  │  一次性，到时自动停止     │
  └────────────┬─────────────┘
               │ Timeout
               ▼
      RaiseWaveStarted()
      SpawnEnemy()   ← 立即生成第 1 只（不等 SpawnInterval）
               │
               ▼
  ┌──────────────────────────┐
  │  _spawnTimer (Loop)      │  ← 按 SpawnInterval 循环生成后续敌人
  │  WaitTime = 1.0f         │
  │  循环触发，直到全部生成   │
  └────────────┬─────────────┘
               │ Timeout
               ▼
       _enemyCursor < Count ?
           ├─ YES → SpawnEnemy()
           └─ NO  → Stop() + CheckWaveCompleted()
```

**设计考量**：
- **分离关注点**：`_delayTimer` 只管"准备阶段倒计时"，`_spawnTimer` 只管"刷怪节奏"，两者职责不同，定时器参数和 OneShot 配置也不同，合在一起会导致状态机混乱。
- **立即生成首只怪**：波次准备阶段结束时，先 `SpawnEnemy()` 再启动 `_spawnTimer`，这样玩家不会等完 Delay 再等一个 SpawnInterval 才看到第一只怪（体感上"太慢了"）。
- **OneShot vs Loop**：准备阶段只执行一次（OneShot=true），刷怪是 N 次循环（OneShot=false）。

### 5. 波次状态闭环 EventBus 时序图

```
玩家控制器 / GameManager
        │
        │  调用 StartNextWave()
        ▼
  WaveManager.StartNextWave()
        │
        ├─ [内部] IsWaveActive=true, 计数器初始化
        ├─ [内部] _delayTimer.Start()
        │
        │  ... DelayBeforeStart 秒 ...
        │
        ▼  _delayTimer.Timeout
  EventBus.RaiseWaveStarted(waveIndex)    ← 广播：波次开始
        │
        │  ┌───────────────────────────────────────────────┐
        │  │  UI 订阅：                                   │
        │  │    OnWaveStarted += 显示"第 X 波来袭"提示     │
        │  │    OnWaveCompleted += 显示"第 X 波完成"       │
        │  └───────────────────────────────────────────────┘
        │
        ▼  SpawnEnemy() × N（按 SpawnInterval 间隔）
  ┌───────────────────────────────────────────────────┐
  │  Enemy (PathFollow2D) 被实例化                   │
  │    → enemy.Data = EnemyData                     │
  │    → TargetPath.AddChild(enemy)                  │
  │    → AliveEnemyCount++                           │
  └──────────────────────┬────────────────────────────┘
                         │
          ┌──────────────┴───────────────┐
          ▼                              ▼
   敌人被塔击杀                    敌人走到路径尽头
   TakeDamage() 触发               ProgressRatio>=1 触发
          │                              │
          ▼                              ▼
EventBus.RaiseEnemyKilled()    EventBus.RaiseEnemyReachedEnd()
          │                              │
          └──────────────┬───────────────┘
                         ▼
           WaveManager 内部事件处理器
           HandleEnemyKilled / HandleEnemyReachedEnd
                         │
                         ├─ AliveEnemyCount--
                         └─ CheckWaveCompleted()
                                │
                 RemainingSpawnCount==0 && AliveEnemyCount==0 ?
                         ├─ NO  → 什么都不做
                         └─ YES → EventBus.RaiseWaveCompleted(waveIndex)
                                                  │
                                                  ▼
                                        ┌──────────────────────────┐
                                        │ GameManager 订阅：       │
                                        │  OnWaveCompleted        │
                                        │    → 解锁下一波按钮      │
                                        │    → 发放波次完成奖励    │
                                        │    → 检查是否通关        │
                                        └──────────────────────────┘
```

### 6. EventBus 订阅/取消订阅的生命周期管理

EventBus 是 `static` 类，其事件委托持有订阅方的强引用。如果 WaveManager 从场景树移除时不解绑，会导致内存泄漏（GC 无法回收 WaveManager 实例，因为 EventBus 还引用着它）。

```csharp
public override void _Ready()
{
    // ... 定时器初始化 ...
    EventBus.OnEnemyKilled += HandleEnemyKilled;           // 订阅
    EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;   // 订阅
}

public override void _ExitTree()
{
    EventBus.OnEnemyKilled -= HandleEnemyKilled;           // 取消订阅
    EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;   // 取消订阅
}
```

**对称原则**：`_Ready` 里 `+=` 了几个事件，`_ExitTree` 里就要 `-=` 几个事件，顺序和数量严格对应。

### 7. SpawnEnemy 中的防御性检查层级

```csharp
SpawnEnemy()
  │
  ├─ 1. 基础上下文检查
  │    CurrentWave != null ?
  │    EnemyBaseScene != null ?
  │    TargetPath != null ?
  │    → 全为空则 GD.PrintErr 并 return（外部配置错误）
  │
  ├─ 2. 单条 EnemyData 检查
  │    CurrentWave.EnemyTypes[cursor] != null ?
  │    → 空则跳过这条，cursor++，return（策划填错了某一格）
  │
  ├─ 3. 实例化类型检查
  │    instance is Enemy enemyNode ?
  │    → 策划拖了错误的预制体（根节点不是 Enemy），
  │       QueueFree() 防止孤儿节点，return
  │
  └─ 4. 正常流程
       enemyNode.Data = enemyData
       enemyNode.Name = "Enemy_X_Y"  (唯一命名，方便调试)
       TargetPath.AddChild(enemyNode)
       AliveEnemyCount++
       cursor++
```

任何一层出错都不会导致程序崩溃，只会在控制台输出可定位的错误信息，符合"编辑器友好"的开发体验。

### 8. 波次配置示例（策划如何填）

```
Wave_1.tres (WaveData)
├── WaveIndex = 1
├── EnemyTypes = [
│     Slime, Slime, Slime, Slime, Slime,   // 5 只史莱姆刷兵线
│   ]
├── SpawnInterval = 1.5f  (每 1.5 秒 1 只)
└── DelayBeforeStart = 3.0f

Wave_2.tres (WaveData)
├── WaveIndex = 2
├── EnemyTypes = [
│     Slime, Slime, Goblin, Slime, Slime,  // 穿插精英
│     Goblin, Goblin, Orc,                 // 后期加强度
│   ]
├── SpawnInterval = 1.0f  (节奏加快)
└── DelayBeforeStart = 5.0f
```

EnemyTypes 是**有序列表**而不是字典「EnemyId → Count」的好处：
- 可以灵活调整刷怪顺序（强弱穿插、精英压轴、BOSS 最后出场）
- 不需要额外的解析逻辑，直接取下标即可
- 直观地展示在 Godot 编辑器中，拖放调整顺序即可，不需要理解额外配置格式

### 9. 与现有 Enemy 系统的集成要求

Enemy 根节点类型必须是 `Enemy : PathFollow2D`，并且其 `Data` 属性为 `[Export] public EnemyData Data`。只有满足这两个条件，WaveManager 的：
```csharp
enemyNode.Data = enemyData;     // 注入配置
TargetPath.AddChild(enemyNode); // 挂载到 Path2D，PathFollow2D 自动生效
```
才能工作正常。对应 Enemy._Ready() 中对 Data != null 的检查也保证了注入顺序正确（在 AddChild 之前赋值 → _Ready 时可用）。

### 10. 后续可扩展方向（当前版本暂未实现）

| 扩展 | 说明 |
|------|------|
| 波次内多间隔 | 某些敌人之间间隔不同（"出 3 只史莱姆，等 5 秒，再出 BOSS"），可将 EnemyTypes 换成 `Array<WaveEntry>`，每个 WaveEntry 包含 EnemyData + DelayAfter 字段 |
| 多路径刷怪 | TargetPath 扩展为 `Array<Path2D>`，按轮询或权重分配敌人到不同路径（分路塔防） |
| 波次无限模式 | Waves 走完后自动生成递增难度的无限波，EnemyTypes 按规则自动组合 |
| 波次暂停/加速 | 暴露 PauseSpawning() / ResumeSpawning() / SetSpawnSpeedScale(float) 接口，配合游戏倍速功能 |
| 波次中途事件 | 新事件 `OnHalfWaveCleared(waveIndex)` 等，用于触发援军、BOSS 登场、剧情过场 |
