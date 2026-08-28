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

## GameManager 游戏主状态机与胜负判定闭环
GameManager 以 `GameState` 枚举（Preparation / InWave / GameWin / GameLose）四态维护局内主流程，通过订阅 EventBus 中 OnWaveStarted 切换至 InWave、OnWaveCompleted 时结合 WaveManager.AllWavesCompleted 与 AliveEnemyCount == 0 两个条件自动发布 OnGameOver(true)，另由 EconomyManager 在 HP 归零时发布 OnGameOver(false)，形成胜利与失败两条独立触发的判定闭环，任何一条路径达成均视为游戏结束，拒绝重复触发避免多次结算。GameManager 在收到 OnGameOver 回调后立即调用 `GetTree().Paused = true` 冻结全局逻辑时钟（_Process 与动画暂停、UI 交互保留），确保结算面板弹出时战斗世界不再推进，玩家可从容阅读结果并操作重新开始或退出按钮；点击"重新开始"时先显式 `Paused = false` 再 `ReloadCurrentScene()`，确保下一局从干净的非暂停态启动。

## GameOverPanel 胜负结算面板与场景重置
GameOverPanel 继承 CanvasLayer 保持在 UI 顶层，默认隐藏，收到 EventBus.OnGameOver(bool isVictory) 时根据参数将 TitleLabel 设置为"胜利!"或"战败!"并显示面板，同时通过 `[Export]` 暴露 RestartButton 与 QuitButton 两个可配置按钮引用，点击 RestartButton 按 `Paused=false → ReloadCurrentScene()` 顺序执行重置，点击 QuitButton 在编辑器模式打印提示、打包模式调用 `GetTree().Quit()`，为后续接入主菜单场景预留切换入口。整个 UI 与业务完全解耦：GameOverPanel 不直接引用 GameManager 或 EconomyManager，所有胜负信号统一由 EventBus 广播，新增移动端结算界面或成就系统可独立订阅同一 OnGameOver 事件而无需改动现有模块。

## Level_01 正式关卡场景节点树布局
Level_01.tscn 采用四层节点树分区架构：World（Node2D）承载地图背景与 EnemyPath 刷怪路径，Slots（Node2D）沿路径拐点周围对称布置 6 个 TowerSlot 槽位，Systems（Node）集中挂载 EconomyManager / WaveManager / TowerManager / GameManager 四个业务管理器并通过 NodePath 互相绑定引用，UI（CanvasLayer）作为独立图层承载 HUDView 与 GameOverPanel 预制体实例，确保世界坐标渲染、交互槽位、业务逻辑、UI 绘制在逻辑与视觉上完全分层，便于后续扩展新地图时复用预制体与系统挂载模式。

## Level_01 系统串联方案
Level_01.cs 作为关卡控制器通过 `[Export]` 引用 WaveManager、GameManager、SlotsContainer 三个关键节点，在 _Ready 中通过 FirstWaveTimer 延迟调用 WaveManager.StartNextWave() 启动首波，波次完成时在 HandleWaveCompleted 回调中判断 AllWavesCompleted，非最后一波则通过 NextWaveTimer 再次触发 StartNextWave 实现波次无缝衔接；同时遍历 SlotsContainer 下所有 TowerSlot 动态追加 Area2D+CircleShape2D 点击检测体，左键点击时读取 TowerManager.Instance.CurrentSelectedTowerData 并调用 TryBuildTower 完成建造事务入口，填补了 TowerSlot 单一职责下缺少用户输入处理的空白，使选塔→点槽→扣费→建塔流程完整贯通。

## AudioManager 音频与特效系统（EventBus 解耦触发）
AudioManager 统一管理局内全部背景音乐与一次性音效，所有触发完全基于 EventBus 订阅：_Ready 时监听 OnTowerBuilt / OnEnemyKilled / OnEnemyReachedEnd / OnGameOver 四个事件，收到事件后立即播放对应 SFX 或实例化特效，AudioManager 自身不反向引用 TowerManager、Enemy、EconomyManager 等任何业务模块，新增成就或过场音效只需在该类内追加订阅即可，零侵入现有战斗流程。
BGM 采用单个常驻 AudioStreamPlayer 子节点（命名 BGMPlayer）维护，_Ready 时自动创建并循环播放，GameOver 时通过 Tween 线性降低 VolumeDb 至静音后停止，避免结算音效与 BGM 互相遮挡；SFX 则采用按需动态实例化策略，每次 PlayOneShotSFX 时新建独立 AudioStreamPlayer 并加入 _activeSfxPlayers 集合追踪，监听其 Finished 信号后自动 QueueFree 并从集合移除，_ExitTree 时再对集合兜底强制释放，确保即使 Finished 信号因异常未触发也不会造成节点泄漏。
击杀视觉特效采用 PackedScene 导出配置（EnemyDeathEffectScene），OnEnemyKilled 事件携带 deathPosition 世界坐标参数直接传入，在该位置 Instantiate 后挂入 AudioManager 自身节点树统一管理；针对 CPUParticles2D 与 GPUParticles2D 分别采用不同销毁策略：GPU 粒子监听 Finished 信号后销毁，CPU 粒子通过 Lifetime + Preprocess 估算总持续时间后用 TweenInterval 延迟销毁，未知类型则默认 2 秒兜底，保证特效播完即回收，节点计数始终回落至基准值 1（仅 BGMPlayer 常驻）。

## 垂直切片闭环设计思考
首关垂直切片的完整数据流为：HUD 建造按钮被点击 → TowerBuildButton 将 TowerData 写入 CurrentSelectedTowerData → 玩家点击 TowerSlot → Level_01 转发给 TowerManager.TryBuildTower → EconomyManager 扣金币并广播 OnGoldChanged → 塔实例挂载后广播 OnTowerBuilt → HUD 按钮与金币标签同步刷新；战斗链路则由 WaveManager 按 WaveData 配置定时生成 Enemy 挂载至 Path2D → Enemy 沿路径移动或被 Tower 攻击击杀 → OnEnemyKilled / OnEnemyReachedEnd 分别触发金币回流 / HP 扣除 → HP 归零或 AllWavesCompleted 时任一路径触发 OnGameOver → GameManager 冻结时钟并由 GameOverPanel 弹出结算；整个闭环零硬编码引用，所有跨模块交互统一走 EventBus，新增系统（如成就、音效）只需订阅对应事件即可无缝接入而无需修改现有模块。
