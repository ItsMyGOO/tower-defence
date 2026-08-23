# Enemy 系统生成提示词与设计要点

## 生成提示词（Prompt）

```
请帮我完成《TowerDefense-Core》项目的第三个核心模块：敌人数据配置与基础实体逻辑 (EnemyData.cs & Enemy.cs)

1. Git 操作：基于当前 main 分支运行 git checkout -b feature/03-enemy-system 创建并切换到新分支。
2. 创建敌人数据 Resource：在 Game/Config/Enemies/ 目录下创建 EnemyData.cs 脚本：
   - 继承自 Resource，加上 [GlobalClass] 属性。
   - 包含以下暴露字段 ([Export]):
     - EnemyId (string, 敌人唯一标识)
     - EnemyName (string, 敌人名称)
     - MaxHp (float, 最大生命值)
     - MoveSpeed (float, 移动速度像素/秒)
     - RewardGold (int, 击杀奖励金币)
     - DamageToPlayer (int, 扣除玩家血量)
   - 添加完整 XML 注释。
3. 创建敌人实体逻辑：在 Game/Gameplay/Enemies/ 目录下创建 Enemy.cs 脚本：
   - 继承自 PathFollow2D (Godot 4 沿路径移动的标准节点)。
   - 包含 [Export] public EnemyData Data 属性。
   - 实现生命值初始化、_Process 中沿路径移动 (Progress += MoveSpeed * delta)。
   - 实现 TakeDamage(float damage) 扣血方法。
   - 当 HP <= 0 时销毁自己，并触发 EventBus.OnEnemyKilled?.Invoke(Data.EnemyId, Data.RewardGold)。
   - 当移动至路径尽头 (ProgressRatio >= 1.0f) 时销毁自己，并触发 EventBus.OnPlayerHpChanged。
4. 创建沉淀文档：
   - 在 Docs/AI_Prompts/03_EnemySystem_Prompt.md 中记录本次提示词。
   - 在 Docs/DevLogs/03_EnemySystem_PathFollow.md 中记录 Godot 4 中 Path2D + PathFollow2D 路径移动机制及与 EventBus 通讯的实现思考。

要求：
1. 所有公共类、接口、方法、属性、枚举必须包含清晰的 C# XML 中文注释。
2. 注释需说明用途、参数含义、返回值、异常情况等关键信息。
3. 命名空间：EnemyData -> TowerDefence.Config.Enemies，Enemy -> TowerDefence.Gameplay.Enemies
4. 所有 [Export] 属性需提供合理的默认值，避免编辑器中字段为空导致空引用异常。
5. 遵循数据驱动设计原则：EnemyData 仅存储纯数据，Enemy 节点负责业务逻辑。
6. 注意 EventBus 解耦：Enemy 到达路径尽头时应通过新增中间事件 OnEnemyReachedEnd(int damageToPlayer) 通知玩家管理系统，而非直接修改玩家 HP。
```

## 设计要点记录

### 1. EventBus 事件设计：为什么新增 OnEnemyReachedEnd 而非直接调用 OnPlayerHpChanged

原需求中"到达路径尽头时触发 EventBus.OnPlayerHpChanged"存在一个架构层面的矛盾：

| 事件 | 签名 | 语义 | 谁负责计算 |
|------|------|------|-----------|
| `OnPlayerHpChanged` | `Action<int> newHp` | **玩家 HP 的新值是多少** | 玩家管理系统（知道当前 HP） |
| `OnEnemyReachedEnd` | `Action<int> damageToPlayer` | **敌人造成了多少伤害** | Enemy 实体（知道自己的 DamageToPlayer） |

**问题核心**：Enemy 只知道 `DamageToPlayer`（扣多少血），但不知道玩家当前的 HP 总值，因此无法给出 `newHp` 这个"绝对值"。如果硬要让 Enemy 调 `RaisePlayerHpChanged(-damage)`，就会改变原有事件的语义（订阅方原本以为收到的是"新生命值"，结果收到的是"伤害增量"），造成 bug 和歧义。

**解决方案**：新增一层中间事件，职责明确：
1. Enemy → `RaiseEnemyReachedEnd(damage)`（我造成了 X 点伤害）
2. PlayerManager 订阅 → 内部扣 HP → `RaisePlayerHpChanged(newHp)`（玩家新的 HP 是 Y）

这符合 **单一职责 + 事件解耦** 的架构原则。

### 2. EnemyData 字段设计说明

| 字段名 | 类型 | 默认值 | 设计意图 |
|--------|------|--------|----------|
| EnemyId | string | `""` | 敌人的逻辑唯一标识，用于事件参数、存档键、波次配置表查找 |
| EnemyName | string | `""` | UI 展示用，和 EnemyId 分离以支持独立本地化（i18n） |
| MaxHp | float | `100.0f` | 出生时的 HP 上限；用 float 支持 fractional 伤害（护甲减伤 30% 等小数场景） |
| MoveSpeed | float | `100.0f` | **像素/秒**。在 PathFollow2D 中 `Progress` 属性的单位是"路径像素距离"，因此 `Progress += MoveSpeed * delta` 直接正确 |
| RewardGold | int | `10` | 击杀奖励金币（整数，符合塔防惯例）；通过 OnEnemyKilled 事件传给经济系统 |
| DamageToPlayer | int | `1` | 逃脱时造成的 HP 伤害；通过 OnEnemyReachedEnd 事件传给玩家管理系统 |

### 3. 命名空间与目录映射

| 类 | 命名空间 | 路径 |
|----|---------|------|
| EnemyData | `TowerDefence.Config.Enemies` | `Game/Config/Enemies/EnemyData.cs` |
| Enemy | `TowerDefence.Gameplay.Enemies` | `Game/Gameplay/Enemies/Enemy.cs` |

### 4. Enemy 节点生命周期关键流程

```
Enemy 实例化 → _Ready()
  │
  ├─ 检查 Data != null → 空则报错 QueueFree
  ├─ CurrentHp = Data.MaxHp
  └─ Progress = 0（从路径起点开始）
        │
        ▼
每帧 _Process(delta)
  │
  ├─ Progress += MoveSpeed * delta  （沿路径推进）
  │
  └─ ProgressRatio >= 1.0f ?
        ├─ 否 → 下一帧继续
        └─ 是 → RaiseEnemyReachedEnd(DamageToPlayer) → QueueFree()
                  （成功逃脱，玩家扣血）

外部调用 TakeDamage(damage)
  │
  ├─ damage < 0 → 截断为 0（防负数回血 bug）
  ├─ CurrentHp -= damage
  └─ CurrentHp <= 0 ?
        ├─ 否 → 继续存活
        └─ 是 → RaiseEnemyKilled(EnemyId, RewardGold) → QueueFree()
                  （被击杀，玩家获得金币）
```

### 5. QueueFree() 的使用时机

Godot 中 `QueueFree()` 是"延迟在下一帧销毁节点"，**不要**使用 `Free()` 立即销毁，原因：
- TakeDamage 可能是在其他节点的 `_Process` 或信号回调中调用的，此时 Godot 正在遍历场景树，立即 `Free()` 会导致遍历异常
- `QueueFree()` 是线程安全的，它会将销毁请求排入 Godot 内部队列，在当前帧末尾安全执行
- OnEnemyKilled / OnEnemyReachedEnd 事件触发在 `QueueFree()` 之前，确保订阅方能在节点销毁前拿到有效的 EnemyData 引用

### 6. Enemy 作为 PathFollow2D 的层级要求

```
Level (Node2D)
└── EnemyPath (Path2D)        ← 由关卡设计人员在编辑器中画出路径曲线
    └── Enemy (PathFollow2D)  ← 波次管理器运行时 Instantiate 后 AddChild 到这里
        ├── Sprite2D          ← 敌人外观贴图
        ├── HealthBar         ← 血条 UI（可后续添加）
        └── CollisionShape2D  ← 被攻击判定碰撞体
```

**重要**：Enemy 必须是某个 Path2D 的**直接子节点**，PathFollow2D 才能正确读取父节点的 `Curve` 属性进行位置插值。如果层级错放（比如 Enemy 放在 Level 根节点下），`Progress` 的修改不会产生任何位移！
