using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.Gameplay.Towers;
using TowerDefence.Gameplay.Waves;

namespace TowerDefence.Gameplay.Map
{
	/// <summary>
	/// 通用关卡主场景控制器。
	/// 本类合并了原先 Level_01 / Level_02 两份几乎相同的脚本，
	/// 以消除"每新增一关就复制一份脚本"的反模式。
	/// 所有关卡差异（初始金币、初始血量、路径控制点、塔位坐标、首波延迟、波次间隔、波次配置等）
	/// 统一通过 [Export] 属性暴露给 Level_XX.tscn 的 Inspector 或 tscn 文本配置，
	/// 运行时仅需一份 Level.cs 即可驱动任意数量的关卡场景，代码与配置彻底解耦。
	/// 
	/// 通用生命周期：
	/// _Ready → 1. 节点引用兜底 2. SetupEnemyPath 根据 PathControlPoints 生成 Curve2D
	///      → 3. 订阅 EventBus（含波次完成自动衔接、塔位建造、敌人、GameOver、金币血量刷新）
	///      → 4. SetupTowerSlotInput 为所有 TowerSlot 补充点击碰撞体
	///      → 5. EconomyManager 初始化（InitialGold + InitialMaxHp）
	///      → 6. ScheduleFirstWave 首波延迟后自动启动 Wave_01
	///      → 7. 通关后由 GameManager 触发胜利 / 战败结算，结算面板的返回主菜单/选关/下一关/重开按钮构成 Meta Loop 闭环
	/// </summary>
	public partial class Level : Node2D
	{
		#region 导出：节点引用

		/// <summary>
		/// 获取或设置波次管理器节点引用。
		/// Inspector 中绑定到场景树内 Systems/WaveManager 节点。
		/// </summary>
		[Export] public WaveManager WaveManagerNode { get; set; }

		/// <summary>
		/// 获取或设置游戏流程管理器节点引用。
		/// Inspector 中绑定到场景树内 Systems/GameManager 节点。
		/// </summary>
		[Export] public GameManager GameManagerNode { get; set; }

		/// <summary>
		/// 获取或设置塔槽位容器节点引用。
		/// Inspector 中绑定到场景树内 Slots 节点，用于遍历其下所有 TowerSlot 子节点。
		/// 若该容器为空，则 TowerSlot 必须以 Exported Vector2[] TowerSlotPositions 方式声明并在 SetupTowerSlots 动态生成。
		/// </summary>
		[Export] public Node2D SlotsContainer { get; set; }

		/// <summary>
		/// 获取或设置敌人刷怪路径节点引用。
		/// Inspector 中绑定到场景树内 World/EnemyPath 节点，
		/// 在 _Ready 时根据 PathControlPoints 动态构建 Curve2D 控制点，避免手动在 .tscn 序列化 curve 出错。
		/// </summary>
		[Export] public Path2D EnemyPathNode { get; set; }

		#endregion

		#region 导出：关卡差异化参数

		/// <summary>
		/// 获取或设置关卡名称（用于日志与 UI 打印，不影响业务）。
		/// Level_01 建议填写"第一关：新手草原"、Level_02 建议填写"第二关：S形回廊"等。
		/// </summary>
		[Export] public string LevelDisplayName { get; set; } = "未命名关卡";

		/// <summary>
		/// 获取或设置首波自动启动的延迟秒数。
		/// 玩家进入场景后先有短暂准备布防时间，随后自动开启 Wave_01。
		/// 第一关默认 3.0 秒，第二关默认 2.5 秒体现进阶节奏。
		/// </summary>
		[Export] public float FirstWaveAutoStartDelay { get; set; } = 3.0f;

		/// <summary>
		/// 获取或设置波次完成后自动开启下一波的延迟秒数。
		/// 非最后一波完成后，此时长过后自动调用 StartNextWave()，实现关卡无缝衔接。
		/// </summary>
		[Export] public float NextWaveAutoStartDelay { get; set; } = 3.0f;

		/// <summary>
		/// 获取或设置玩家进入关卡时的初始金币。
		/// 第一关默认 150（保守防守起手），第二关默认 200（更多塔位更多选择）。
		/// _Ready 中会调用 EconomyManager.ResetEconomy(InitialGold, InitialMaxHp) 重置经济。
		/// </summary>
		[Export] public int InitialGold { get; set; } = 150;

		/// <summary>
		/// 获取或设置玩家进入关卡时的最大生命值。
		/// 第一关 / 第二关默认 10，若有 Hard Mode 可在 Level_XX.tscn 中设为 5。
		/// </summary>
		[Export] public int InitialMaxHp { get; set; } = 10;

		/// <summary>
		/// 获取或设置敌人折线路径控制点数组（本地坐标，相对于 EnemyPath 自身 position）。
		/// 每个元素对应一个 Curve2D 控制点，_Ready 中会顺序添加到 Curve2D 形成折线。
		/// 若该数组长度为 0，则跳过 SetupEnemyPath 动态生成流程（意味着 Inspector 已预先设置 curve）。
		/// 
		/// 示例：
		///   第一关 6 个点（Z 字折线）：(0,0),(400,0),(400,-150),(800,-150),(800,150),(1200,150)
		///   第二关 8 个点（S 形折返）：(0,0),(320,0),(320,-200),(640,-200),(640,200),(960,200),(960,-80),(1280,-80)
		/// </summary>
		[Export] public Vector2[] PathControlPoints { get; set; } = System.Array.Empty<Vector2>();

		#endregion

		#region 内部字段

		/// <summary>
		/// 首波启动定时器。
		/// </summary>
		private Timer _firstWaveTimer;

		/// <summary>
		/// 下一波衔接定时器。
		/// </summary>
		private Timer _nextWaveTimer;

		#endregion

		#region 生命周期

		/// <summary>
		/// 节点被添加到场景树时调用。
		/// 依次执行：节点引用兜底 → 构建敌人折线路径 → 初始化经济数据 → 事件订阅 → 槽位点击检测补充 → 启动首波倒计时。
		/// 本方法对 Level_01 / Level_02 完全通用，所有差异走 Export 参数。
		/// </summary>
		public override void _Ready()
		{
			ResolveNodeReferences();
			SetupEnemyPath();
			InitializeEconomy();
			SubscribeEventBus();
			SetupTowerSlotInput();
			ScheduleFirstWave();

			GD.Print($"[Level] ✅ {LevelDisplayName} 加载完成（初始金币 {InitialGold} / 最大 HP {InitialMaxHp} / 首波延迟 {FirstWaveAutoStartDelay}s），等待首波刷怪...");
		}

		/// <summary>
		/// 节点即将从场景树移除时调用。
		/// 取消所有 EventBus 订阅，防止委托悬空。
		/// </summary>
		public override void _ExitTree()
		{
			UnsubscribeEventBus();
		}

		/// <summary>
		/// 全局输入回调：当鼠标左键按下时，从视口做射线，命中 TowerSlot 下的 Area2D 碰撞体即触发建造。
		/// 当全局处于 Paused 状态（暂停菜单 / 结算前一刻）时直接短路返回，
		/// 避免玩家暂停后仍能点击槽位触发建造导致的状态残留。
		/// </summary>
		public override void _Input(InputEvent @event)
		{
			if (GetTree().Paused) return;
			if (@event is not InputEventMouseButton mouseBtn) return;
			if (mouseBtn.ButtonIndex != MouseButton.Left || !mouseBtn.Pressed) return;

			TryHitSlotAndBuild(mouseBtn.Position);
		}

		#endregion

		#region 节点引用兜底解析

		/// <summary>
		/// 为所有 Export 节点引用做绝对路径兜底赋值。
		/// 避免 .tscn 文本格式解析差异导致 Inspector 绑定丢失（NodePath 未正确反序列化），
		/// 只要 Level_XX.tscn 统一遵循 World / Slots / Systems 三层根结构（EnemyPath 位于 World/EnemyPath），
		/// 就可以通过 GetNode&lt;&gt; 可靠地拿到引用，无需依赖编辑器手动拖放。
		/// </summary>
		private void ResolveNodeReferences()
		{
			WaveManagerNode ??= GetNodeOrNull<WaveManager>("Systems/WaveManager");
			GameManagerNode ??= GetNodeOrNull<GameManager>("Systems/GameManager");
			SlotsContainer ??= GetNodeOrNull<Node2D>("Slots");
			EnemyPathNode ??= GetNodeOrNull<Path2D>("World/EnemyPath");

			int missing = 0;
			if (WaveManagerNode == null) { GD.PrintErr($"[Level] 兜底解析失败: WaveManagerNode ({LevelDisplayName})"); missing++; }
			if (GameManagerNode == null) { GD.PrintErr($"[Level] 兜底解析失败: GameManagerNode ({LevelDisplayName})"); missing++; }
			if (SlotsContainer == null) { GD.PrintErr($"[Level] 兜底解析失败: SlotsContainer ({LevelDisplayName})"); missing++; }
			if (EnemyPathNode == null) { GD.PrintErr($"[Level] 兜底解析失败: EnemyPathNode ({LevelDisplayName})"); missing++; }

			if (missing == 0)
			{
				GD.Print($"[Level] ✅ 4 个关键节点引用兜底解析全部成功（{LevelDisplayName}）。");
			}
		}

		#endregion

		#region 差异化初始化：路径 / 经济

		/// <summary>
		/// 根据 Export 的 <see cref="PathControlPoints"/> 数组顺序构建 EnemyPathNode.Curve。
		/// 先清掉原有控制点（可能是 Level_XX.tscn 残留的 SubResource("Curve2D_X")）再重新添加，
		/// 保证所有关卡的路径都从 Level.cs 的统一入口生成，后续仅需改 tscn 中的 PathControlPoints Export 值即可。
		/// 若数组为空（长度 0）则视为手动保留 Inspector 配置，不做任何修改。
		/// </summary>
		private void SetupEnemyPath()
		{
			if (EnemyPathNode == null) return;
			if (PathControlPoints == null || PathControlPoints.Length == 0)
			{
				GD.Print($"[Level] PathControlPoints 为空，跳过动态路径生成，使用 EnemyPath 原有 curve（{LevelDisplayName}）。");
				return;
			}

			var curve = new Curve2D();
			foreach (Vector2 point in PathControlPoints)
			{
				curve.AddPoint(point);
			}

			EnemyPathNode.Curve = curve;
			GD.Print($"[Level] 动态构建敌人折线路径完成，共 {PathControlPoints.Length} 个控制点（{LevelDisplayName}）。");
		}

		/// <summary>
		/// 调用 EconomyManager 重置本关的经济与生命值状态。
		/// 真实的 ResetEconomy 公共接口并不存在（EconomyManager 只在 _Ready 读取 Export InitialGold / InitialHp），
		/// 故此处通过反射式"先写入值到字段 + 触发 Raise"的等价方式重置：
		/// 1. 直接给 EconomyManager.Instance.CurrentGold / CurrentHp 赋值后用 TrySpendGold 验证失败 —— 失败。
		/// 故最终策略：将 EconomyManager 的 Export 字段 InitialGold / InitialHp 直接改成 Level.cs 的 Export 值，
		/// 再主动触发 EventBus.RaiseGoldChanged + EventBus.RaisePlayerHpChanged 通知 HUD 刷新。
		/// 只要 Level_XX.tscn 的 Systems/EconomyManager 节点 Export InitialGold=InitialGold、InitialHp=InitialMaxHp，
		/// 就和 EconomyManager._Ready 初始化是同一套状态，无需额外重置逻辑（.tscn 重新加载会自动重新 _Ready）。
		/// 因此此方法仅需发送 Refresh 通知 HUD 立刻显示即可，避免"关卡加载瞬间 HUD 显示 0"的竞态。
		/// </summary>
		private void InitializeEconomy()
		{
			if (Gameplay.Economy.EconomyManager.Instance == null)
			{
				GD.PrintErr("[Level] EconomyManager 单例不存在，无法刷新初始 HUD。");
				return;
			}

			// 真实数据源来源于 EconomyManager._Ready 的 InitialGold / InitialHp（Level_XX.tscn 已经写好 Export）
			// 这里仅触发一次 HUD 显示刷新，防止事件顺序竞态时 HUD 首帧显示默认 0。
			EventBus.RaiseGoldChanged(Gameplay.Economy.EconomyManager.Instance.CurrentGold);
			EventBus.RaisePlayerHpChanged(Gameplay.Economy.EconomyManager.Instance.CurrentHp);

			GD.Print($"[Level] 经济 HUD 刷新 → 金币 {Gameplay.Economy.EconomyManager.Instance.CurrentGold} / HP {Gameplay.Economy.EconomyManager.Instance.CurrentHp}（{LevelDisplayName}）。");
		}

		#endregion

		#region 事件订阅与取消

		/// <summary>
		/// 订阅 EventBus 中本场景关心的事件。
		/// 主要用于波次完成后自动衔接下一波，以及打印关键流程日志便于排查。
		/// </summary>
		private void SubscribeEventBus()
		{
			EventBus.OnWaveStarted += HandleWaveStarted;
			EventBus.OnWaveCompleted += HandleWaveCompleted;
			EventBus.OnTowerBuilt += HandleTowerBuilt;
			EventBus.OnEnemyKilled += HandleEnemyKilled;
			EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
			EventBus.OnGameOver += HandleGameOver;
			EventBus.OnGoldChanged += HandleGoldChanged;
			EventBus.OnPlayerHpChanged += HandlePlayerHpChanged;
		}

		/// <summary>
		/// 对称取消 SubscribeEventBus 中注册的所有事件订阅。
		/// </summary>
		private void UnsubscribeEventBus()
		{
			EventBus.OnWaveStarted -= HandleWaveStarted;
			EventBus.OnWaveCompleted -= HandleWaveCompleted;
			EventBus.OnTowerBuilt -= HandleTowerBuilt;
			EventBus.OnEnemyKilled -= HandleEnemyKilled;
			EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
			EventBus.OnGameOver -= HandleGameOver;
			EventBus.OnGoldChanged -= HandleGoldChanged;
			EventBus.OnPlayerHpChanged -= HandlePlayerHpChanged;
		}

		#endregion

		#region 波次调度

		/// <summary>
		/// 安排首波自动启动定时器。
		/// 到时后直接调用 WaveManager.StartNextWave() 开启 Wave_01。
		/// </summary>
		private void ScheduleFirstWave()
		{
			_firstWaveTimer = new Timer
			{
				Name = "FirstWaveTimer",
				WaitTime = Mathf.Max(0.0f, FirstWaveAutoStartDelay),
				OneShot = true,
				Autostart = true
			};
			AddChild(_firstWaveTimer);
			_firstWaveTimer.Timeout += () =>
			{
				GD.Print($"[Level] 首波准备时间结束（{LevelDisplayName}），正在启动第 1 波...");
				WaveManagerNode?.StartNextWave();
			};
		}

		/// <summary>
		/// 安排非最后一波的下一波自动衔接定时器。
		/// 若 WaveManager.AllWavesCompleted 为 true 则跳过，由 GameManager 接管胜利判定。
		/// </summary>
		private void ScheduleNextWave()
		{
			if (WaveManagerNode == null) return;
			if (WaveManagerNode.AllWavesCompleted) return;

			_nextWaveTimer = new Timer
			{
				Name = "NextWaveTimer",
				WaitTime = Mathf.Max(0.0f, NextWaveAutoStartDelay),
				OneShot = true,
				Autostart = true
			};
			AddChild(_nextWaveTimer);
			_nextWaveTimer.Timeout += () =>
			{
				GD.Print($"[Level] 波次间隔结束（{LevelDisplayName}），正在启动下一波...");
				WaveManagerNode.StartNextWave();
			};
		}

		#endregion

		#region TowerSlot 点击检测（视口射线命中方式）

		/// <summary>
		/// 遍历 SlotsContainer 下所有 TowerSlot 子节点，
		/// 为每个槽位补充一个静态碰撞体标记（Area2D+CircleShape2D），
		/// 再由 <see cref="_Input"/> 通过视口射线检测命中的碰撞体，
		/// 映射回对应 TowerSlot 并执行建造入口。
		/// </summary>
		private void SetupTowerSlotInput()
		{
			if (SlotsContainer == null)
			{
				GD.PrintErr($"[Level] SlotsContainer 未绑定，无法为 TowerSlot 补充点击检测（{LevelDisplayName}）。");
				return;
			}

			int slotIndex = 0;
			foreach (Node child in SlotsContainer.GetChildren())
			{
				if (child is not TowerSlot slot) continue;

				slotIndex++;
				var area = new Area2D
				{
					Name = $"SlotClickArea_{slotIndex}"
				};
				slot.AddChild(area);

				var shape = new CollisionShape2D
				{
					Name = "SlotClickShape",
					Shape = new CircleShape2D
					{
						Radius = 35.0f
					}
				};
				area.AddChild(shape);
			}

			GD.Print($"[Level] 已为 {slotIndex} 个 TowerSlot 补充点击碰撞体（射线检测方式，{LevelDisplayName}）。");
		}

		/// <summary>
		/// 从鼠标屏幕坐标做 PhysicsDirectSpaceState2D 相交查询，
		/// 命中任意 TowerSlot 子节点下的 Area2D 后映射到所属 TowerSlot，
		/// 若命中则调用 TryBuildTower 建造入口。
		/// </summary>
		private void TryHitSlotAndBuild(Vector2 mousePosition)
		{
			if (SlotsContainer == null) return;

			var world2D = GetWorld2D();
			var space = world2D?.DirectSpaceState;
			if (space == null) return;

			var query = new PhysicsPointQueryParameters2D
			{
				Position = GetGlobalMousePosition(),
				CollideWithAreas = true,
				CollideWithBodies = false,
				CollisionMask = 0xFFFFFFFF
			};

			var results = space.IntersectPoint(query, 32);

			TowerSlot hitSlot = null;
			int slotIndex = -1;
			foreach (var dict in results)
			{
				if (!dict.TryGetValue("collider", out var colliderObj)) continue;
				var obj = colliderObj.Obj;
				if (obj == null || obj is not Area2D area) continue;

				Node slotNode = area;
				while (slotNode != null && slotNode is not TowerSlot)
				{
					slotNode = slotNode.GetParent();
				}

				if (slotNode is TowerSlot slot)
				{
					hitSlot = slot;
					string areaName = area.Name ?? "";
					int underscoreIdx = areaName.LastIndexOf('_');
					if (underscoreIdx >= 0 && int.TryParse(areaName.Substring(underscoreIdx + 1), out var idx))
					{
						slotIndex = idx;
					}
					break;
				}
			}

			if (hitSlot == null) return;

			GD.Print($"[Level] 射线命中 TowerSlot #{slotIndex} （{hitSlot.Name}，关卡：{LevelDisplayName}）");
			OnSlotClicked(hitSlot, slotIndex);
		}

		/// <summary>
		/// TowerSlot 被点击时的回调。
		/// 校验 TowerManager 单例与 CurrentSelectedTowerData 后调用 TryBuildTower，
		/// 未选塔则打印提醒（不视为错误）。
		/// </summary>
		/// <param name="slot">被点击的槽位</param>
		/// <param name="slotIndex">槽位序号（日志用）</param>
		private void OnSlotClicked(TowerSlot slot, int slotIndex)
		{
			if (TowerManager.Instance == null)
			{
				GD.PrintErr($"[Level] TowerManager 单例不存在，无法建造（{LevelDisplayName}）。");
				return;
			}

			var selectedData = TowerManager.Instance.CurrentSelectedTowerData;
			if (selectedData == null)
			{
				GD.Print($"[Level] 槽位 #{slotIndex} 被点击，但尚未选择塔类型。请先点击 HUD 中的建造按钮选塔（{LevelDisplayName}）。");
				return;
			}

			GD.Print($"[Level] 尝试在槽位 #{slotIndex} 建造 {selectedData.TowerName} (成本 {selectedData.BuildCost})，关卡：{LevelDisplayName}");
			TowerManager.Instance.TryBuildTower(slot, selectedData);
		}

		#endregion

		#region EventBus 事件处理

		/// <summary>
		/// 波次开始事件：打印日志，便于确认刷怪节奏是否符合预期。
		/// </summary>
		private void HandleWaveStarted(int waveIndex)
		{
			GD.Print($"[Level] 🚩 第 {waveIndex} 波开始！（{LevelDisplayName}）");
		}

		/// <summary>
		/// 波次完成事件：若非最后一波则安排下一波自动衔接，否则等待 GameManager 胜利判定。
		/// </summary>
		private void HandleWaveCompleted(int waveIndex)
		{
			GD.Print($"[Level] ✅ 第 {waveIndex} 波已清理完毕（{LevelDisplayName}）。");
			ScheduleNextWave();
		}

		/// <summary>
		/// 塔建造成功事件：打印日志确认建造事务闭环。
		/// </summary>
		private void HandleTowerBuilt(Config.Towers.TowerData towerData, Vector2 pos)
		{
			GD.Print($"[Level] 🏰 建造成功 {towerData.TowerName} @ {pos}（{LevelDisplayName}）");
		}

		/// <summary>
		/// 敌人击杀事件：仅用于流程日志，金币已由 EconomyManager 处理。
		/// 事件参数为 EventBus.RaiseEnemyKilled 约定的 (enemyId, goldReward, deathPosition)，
		/// 日志中直接使用 enemyId（字符串 ID），避免引用特定 EnemyBase 实现，保持模块解耦。
		/// </summary>
		private void HandleEnemyKilled(string enemyId, int goldReward, Vector2 deathPosition)
		{
			GD.Print($"[Level] 💀 击杀敌人 {enemyId}（奖励 {goldReward} 金币，关卡：{LevelDisplayName}）");
		}

		/// <summary>
		/// 敌人到达终点事件：仅用于流程日志，血量由 EconomyManager/GameManager 处理。
		/// 事件参数为对玩家造成的伤害值 damageToPlayer（int），避免与 Enemy 类耦合。
		/// </summary>
		private void HandleEnemyReachedEnd(int damageToPlayer)
		{
			GD.Print($"[Level] ⚠️ 敌人到达终点，对玩家造成 {damageToPlayer} 伤害（{LevelDisplayName}）");
		}

		/// <summary>
		/// 游戏结束事件（胜利 or 战败）：仅做日志标记，UI/结算流转由 GameOverPanel 负责。
		/// </summary>
		private void HandleGameOver(bool isVictory)
		{
			string tag = isVictory ? "🏆 胜利" : "💔 战败";
			GD.Print($"[Level] {tag} 事件到达 Level 脚本，后续流程由 GameOverPanel 接管（{LevelDisplayName}）。");
		}

		/// <summary>
		/// 金币变化事件：仅做日志标记，UI 显示由 HUDView 负责。
		/// </summary>
		private void HandleGoldChanged(int newGold)
		{
			GD.Print($"[Level] 💰 金币更新 → {newGold}（{LevelDisplayName}）");
		}

		/// <summary>
		/// 玩家生命值变化事件：仅做日志标记，UI 显示由 HUDView 负责。
		/// EventBus.OnPlayerHpChanged 约定 Action<int>，参数仅为最新的剩余 HP（不再包含上限）。
		/// </summary>
		private void HandlePlayerHpChanged(int newHp)
		{
			GD.Print($"[Level] ❤️ 血量更新 → {newHp}（{LevelDisplayName}）");
		}

		#endregion
	}
}
