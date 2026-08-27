# Core Architecture 架构日志

## EventBus 全局事件总线
纯静态类实现发布/订阅模式，解耦跨领域模块（Gameplay → 经济/UI/音效/波次）。事件命名用过去式（`OnXxxChanged`），发布方法 `RaiseXxx()` 配合 `?.Invoke()`。订阅必须对称：`_EnterTree` 注册、`_ExitTree` 取消，禁止匿名 lambda，防止内存泄漏。同领域（Tower↔Enemy）直接方法调用，跨领域一律走 EventBus。

## TowerData / EnemyData Custom Resource
继承 `Resource` 并标注 `[GlobalClass]`，让 Godot 编辑器可识别并序列化。只存纯数据（无业务逻辑），通过 `[Export]` 暴露字段并给合理默认值。优势：调参零编译（改 `.tres` 即时生效）、策划独立工作、多难度/多渠道靠资源目录切换、同一份代码驱动 N 种塔/敌人。运行时切勿修改共享 Resource 实例属性。

## Enemy & PathFollow2D
Enemy 继承 `PathFollow2D`，必须作为 `Path2D` 直接子节点，`Progress += MoveSpeed * delta` 实现精确沿路径移动。到达终点（`ProgressRatio >= 1.0f`）触发 `OnEnemyReachedEnd(damage)` 中间事件，而非直接改玩家 HP——保持单一职责：Enemy 只知道自己造成多少伤害，玩家管理系统负责扣血并广播 `OnPlayerHpChanged`。击杀/逃脱后用 `QueueFree()` 延迟销毁，避免帧遍历崩溃。

## EconomyManager 经济与生命值管理器
EconomyManager 作为玩家状态的唯一收拢点，通过 `[Export]` 暴露 InitialGold / InitialHp，运行时以 CurrentGold / CurrentHp 私有 set 确保所有变更都走统一入口（EventBus 订阅 + TrySpendGold 方法），杜绝外部直接修改状态导致事件漏发。订阅 `OnEnemyKilled` 累加金币、`OnEnemyReachedEnd` 递减 HP，每次状态变更立即 `RaiseGoldChanged` / `RaisePlayerHpChanged`；HP 归零则置位 `IsGameOver` 并发布 `OnGameOver(false)`，随后拒绝所有后续经济/扣血操作，防止负金币或多次 GameOver。对外提供 `CanAfford` / `TrySpendGold` 防御塔建造/升级的幂等消费接口，金额为 0 或负数时直接短路避免无意义事件。

## Tower 防御塔索敌与攻击
Tower 在 `_Ready` 中动态挂载 Area2D+CircleShape2D（半径=Data.AttackRange）作为索敌检测区，通过 AreaEntered/AreaExited 信号维护进入范围的 Enemy 列表，并用 `List<Enemy>` 保证顺序优先攻击最早进入的目标。攻击循环依赖独立 Timer（WaitTime=Data.AttackInterval）驱动，Timeout 时从目标列表非空即对首目标调用 `TakeDamage(Data.Damage)`，同时订阅每个目标的 `TreeExiting` 实现敌人死亡/逃脱时自动从列表移除，避免空引用与重复索敌。
