# Core Architecture 架构日志

## EventBus 全局事件总线
纯静态类实现发布/订阅模式，解耦跨领域模块（Gameplay → 经济/UI/音效/波次）。事件命名用过去式（`OnXxxChanged`），发布方法 `RaiseXxx()` 配合 `?.Invoke()`。订阅必须对称：`_EnterTree` 注册、`_ExitTree` 取消，禁止匿名 lambda，防止内存泄漏。同领域（Tower↔Enemy）直接方法调用，跨领域一律走 EventBus。

## TowerData / EnemyData Custom Resource
继承 `Resource` 并标注 `[GlobalClass]`，让 Godot 编辑器可识别并序列化。只存纯数据（无业务逻辑），通过 `[Export]` 暴露字段并给合理默认值。优势：调参零编译（改 `.tres` 即时生效）、策划独立工作、多难度/多渠道靠资源目录切换、同一份代码驱动 N 种塔/敌人。运行时切勿修改共享 Resource 实例属性。

## Enemy & PathFollow2D
Enemy 继承 `PathFollow2D`，必须作为 `Path2D` 直接子节点，`Progress += MoveSpeed * delta` 实现精确沿路径移动。到达终点（`ProgressRatio >= 1.0f`）触发 `OnEnemyReachedEnd(damage)` 中间事件，而非直接改玩家 HP——保持单一职责：Enemy 只知道自己造成多少伤害，玩家管理系统负责扣血并广播 `OnPlayerHpChanged`。击杀/逃脱后用 `QueueFree()` 延迟销毁，避免帧遍历崩溃。
