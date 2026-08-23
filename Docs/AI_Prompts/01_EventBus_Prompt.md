# EventBus 生成提示词与设计要点

## 生成提示词（Prompt）

```
请帮我完成《TowerDefense-Core》项目的第一个核心模块开发：

创建核心代码：在 Game/Core/AutoLoads/ 目录下创建 EventBus.cs，实现纯静态类 (static class) 形式的全局事件总线。包含以下 Action 委托（附带完整 XML 注释）：

经济与玩家：OnGoldChanged (int), OnPlayerHpChanged (int)
防御塔：OnTowerPlaced (Vector2I, string), OnTowerSold (Vector2I)
敌人与波次：OnEnemyKilled (string, int), OnWaveStarted (int), OnWaveCompleted (int)
局内胜负：OnGameOver (bool)

要求：
1. 所有公共类、接口、方法、属性、枚举必须包含清晰的 C# XML 中文注释。
2. 注释需说明用途、参数含义、返回值、异常情况等关键信息。
3. 事件总线以发布/订阅模式工作，模块间禁止直接引用。
4. 每个 event 附带对应的 Raise 发布方法，使用 null-conditional operator (?.) 调用。
5. 使用 Godot 的 Vector2I 类型表示网格坐标。
```

## 设计要点记录

### 1. 核心架构决策
- **实现形式**：`static class` 纯静态类，无需实例化，全局唯一入口
- **通信模式**：发布/订阅（Pub/Sub），发布者与订阅者完全解耦
- **委托类型**：使用 `System.Action<T...>` 标准泛型委托，避免自定义委托冗余

### 2. 事件分类设计
| 分类 | 事件名 | 参数 | 触发时机 |
|------|--------|------|----------|
| 经济与玩家 | OnGoldChanged | int (newGold) | 玩家金币数量增减 |
| 经济与玩家 | OnPlayerHpChanged | int (newHp) | 玩家生命值变化 |
| 防御塔 | OnTowerPlaced | Vector2I (gridPos), string (towerId) | 塔放置成功 |
| 防御塔 | OnTowerSold | Vector2I (gridPos) | 塔被出售/拆除 |
| 敌人与波次 | OnEnemyKilled | string (enemyId), int (goldReward) | 敌人死亡结算 |
| 敌人与波次 | OnWaveStarted | int (waveIndex) | 新波次开始生成 |
| 敌人与波次 | OnWaveCompleted | int (waveIndex) | 波次敌人全部清除 |
| 局内胜负 | OnGameOver | bool (isVictory) | 游戏胜利/失败 |

### 3. 命名规范
- **事件声明**：`On` + 动词过去式（如 `OnGoldChanged`），表示"在...之后触发"语义
- **发布方法**：`Raise` + 事件名去掉 `On` 前缀（如 `RaiseGoldChanged`）
- **参数命名**：采用业务领域术语，不使用缩写，确保可读性

### 4. 发布方法设计
- 与事件一一对应，提供统一的调用入口
- 内部使用 `?.Invoke()` 空条件调用，避免未订阅时 NullReferenceException
- 封装后便于后续扩展：日志记录、性能监控、多线程调度等可在发布方法内统一添加

### 5. 命名空间规划
- 命名空间：`TowerDefence.Core.AutoLoads`
- 路径映射：`Game/Core/AutoLoads/EventBus.cs`
- 预留 AutoLoads 目录：未来可放入需要 Godot 自动加载的 Singleton 节点脚本（当前 EventBus 为纯静态类，不需 Godot AutoLoad 注册）
