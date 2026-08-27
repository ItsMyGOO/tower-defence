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

## TowerSlot 建造槽位与状态管理
TowerSlot 继承 Node2D，以 `IsOccupied`（private set）与 `CurrentTower` 两个只读属性封装槽位占用状态，仅通过 `PlaceTower(Tower)` 方法在内部原子性地切换状态并挂载塔节点，杜绝外部非法篡改导致同一槽位重复建塔的竞态问题。槽位本身只负责"能否建造"的布尔判定与塔实例持有，不参与任何经济扣费或塔实例化，保持单一职责，便于后续升级出售、塔查询、槽位高亮等功能扩展。

## TowerManager 建造事务与事件广播
TowerManager 采用严格的事务顺序：参数与槽位占用校验 → `EconomyManager.TrySpendGold` 原子扣费 → 实例化 Tower 预制体并注入数据 → `slot.PlaceTower` 挂载 → 任意步失败立即回滚（扣费失败直接返回、挂载失败则 `AddGold` 返还金币），确保金币与塔实例状态始终一致。成功建造后通过 `EventBus.RaiseTowerBuilt(towerData, slot.GlobalPosition)` 广播携带塔配置与世界坐标的事件，供 UI、音效、成就等模块松耦合订阅，避免 TowerManager 反向依赖上层模块。为保持与 EconomyManager 一致的访问风格，TowerManager 补充 `Instance` 静态单例，便于 HUD 层快速调用。

## HUDView 局内 HUD 与 EventBus 解耦刷新
HUDView 继承 CanvasLayer，通过 `[Export]` 暴露 GoldLabel / HpLabel / WaveLabel 与 BuildButtonsContainer 四个节点引用，将 UI 与 Gameplay 状态完全通过 EventBus 解耦刷新：`_Ready` 统一订阅 `OnGoldChanged` / `OnPlayerHpChanged` / `OnWaveStarted` 三个事件，`_ExitTree` 对称取消订阅防止内存泄漏。收到事件时调用独立的 `RefreshXxxLabel()` 私有方法按预设文本格式更新 UI，绝不直接访问 EconomyManager 或 WaveManager 内部状态，保持 UI 层零依赖零反向引用，新增 HUD 实现可独立迁移到其他场景或替换为其他 UI 框架。

## TowerBuildButton 建造按钮与金币变化响应逻辑
TowerBuildButton 继承 Button，通过 `[Export] TowerData Data` 绑定具体塔配置，在 `_Ready` 时订阅 EventBus.OnGoldChanged 并根据玩家金币实时刷新 Disabled 状态：当前金币 >= `Data.BuildCost` 时按钮启用，不足时自动置灰禁用。按钮点击事件直接设置 TowerManager.Instance.CurrentSelectedTowerData = Data，将待建造塔类型交还给业务层，自身不参与任何金币扣除或塔实例化逻辑，保持单一职责，便于后续接入建造预览、价格字体高亮、技能冷却计时等 UI 扩展。
