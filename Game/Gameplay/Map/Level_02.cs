using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.Gameplay.Towers;
using TowerDefence.Gameplay.Waves;

namespace TowerDefence.Gameplay.Map
{
	/// <summary>
	/// 第二关主场景控制器。
	/// 负责串联 Level_02.tscn 中所有子系统（WaveManager / TowerManager / EconomyManager / GameManager），
	/// 在 _Ready 中延迟触发首波刷怪，并通过订阅 EventBus 实现波次间自动衔接；
	/// 同时在 _Input 中通过射线命中检测 TowerSlot 点击，触发建造事务调用入口，
	/// 作为 SceneManager 关卡流转的第二关占位场景，确保从第一关通关后"下一关"按钮可正常切入。
	/// </summary>
	public partial class Level_02 : Node2D
	{
		#region 导出节点引用

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
		/// </summary>
		[Export] public Node2D SlotsContainer { get; set; }

		/// <summary>
		/// 获取或设置敌人刷怪路径节点引用。
		/// Inspector 中绑定到场景树内 World/EnemyPath 节点，
		/// 用于在 _Ready 时动态构建折线式 Curve2D 控制点。
		/// </summary>
		[Export] public Path2D EnemyPathNode { get; set; }

		/// <summary>
		/// 获取或设置首波自动启动的延迟秒数。
		/// 第二关默认设置为 2.5 秒，节奏略快于第一关以体现进阶难度。
		/// </summary>
		[Export] public float FirstWaveAutoStartDelay { get; set; } = 2.5f;

		/// <summary>
		/// 获取或设置波次完成后自动开启下一波的延迟秒数。
		/// 第二关默认设置为 2.5 秒，节奏略快于第一关。
		/// </summary>
		[Export] public float NextWaveAutoStartDelay { get; set; } = 2.5f;

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
		/// 依次执行：节点引用兜底 → 构建敌人折线路径 → 事件订阅 → 槽位点击检测补充 → 启动首波倒计时。
		/// </summary>
		public override void _Ready()
		{
			ResolveNodeReferences();
			SetupEnemyPath();
			SubscribeEventBus();
			SetupTowerSlotInput();
			ScheduleFirstWave();

			GD.Print("[Level_02] ✅ 第二关加载完成，等待首波刷怪...");
		}

		/// <summary>
		/// 节点即将从场景树移除时调用。
		/// 取消所有 EventBus 订阅，防止委托悬空。
		/// </summary>
		public override void _ExitTree()
		{
			UnsubscribeEventBus();
		}

		#endregion

		#region 节点引用兜底解析

		/// <summary>
		/// 为所有 Export 节点引用做绝对路径兜底赋值。
		/// 保持与 Level_01 一致的节点树层级结构（World / Slots / Systems 三层根），
		/// 通过 GetNode&lt;&gt; 可靠拿到引用，无需依赖编辑器手动拖放。
		/// </summary>
		private void ResolveNodeReferences()
		{
			WaveManagerNode ??= GetNodeOrNull<WaveManager>("Systems/WaveManager");
			GameManagerNode ??= GetNodeOrNull<GameManager>("Systems/GameManager");
			SlotsContainer ??= GetNodeOrNull<Node2D>("Slots");
			EnemyPathNode ??= GetNodeOrNull<Path2D>("World/EnemyPath");

			int missing = 0;
			if (WaveManagerNode == null) { GD.PrintErr("[Level_02] 兜底解析失败: WaveManagerNode"); missing++; }
			if (GameManagerNode == null) { GD.PrintErr("[Level_02] 兜底解析失败: GameManagerNode"); missing++; }
			if (SlotsContainer == null) { GD.PrintErr("[Level_02] 兜底解析失败: SlotsContainer"); missing++; }
			if (EnemyPathNode == null) { GD.PrintErr("[Level_02] 兜底解析失败: EnemyPathNode"); missing++; }

			if (missing == 0)
			{
				GD.Print("[Level_02] ✅ 4 个关键节点引用兜底解析全部成功。");
			}
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
		/// 到时后直接调用 WaveManager.StartNextWave() 开启首波。
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
				GD.Print($"[Level_02] 首波准备时间结束，正在启动第 1 波...");
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
				GD.Print($"[Level_02] 波次间隔结束，正在启动下一波...");
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
				GD.PrintErr("[Level_02] SlotsContainer 未绑定，无法为 TowerSlot 补充点击检测。");
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

			GD.Print($"[Level_02] 已为 {slotIndex} 个 TowerSlot 补充点击碰撞体（射线检测方式）。");
		}

		/// <summary>
		/// 全局输入回调：当鼠标左键按下时，从视口做射线，命中 TowerSlot 下的 Area2D 碰撞体即触发建造。
		/// </summary>
		public override void _Input(InputEvent @event)
		{
			if (@event is not InputEventMouseButton mouseBtn) return;
			if (mouseBtn.ButtonIndex != MouseButton.Left || !mouseBtn.Pressed) return;

			TryHitSlotAndBuild(mouseBtn.Position);
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

			GD.Print($"[Level_02] 射线命中 TowerSlot #{slotIndex} （{hitSlot.Name}）");
			OnSlotClicked(hitSlot, slotIndex);
		}

		/// <summary>
		/// TowerSlot 被点击时的回调。
		/// 校验 TowerManager 单例与 CurrentSelectedTowerData 后调用 TryBuildTower，
		/// 未选塔则打印提醒（不视为错误）。
		/// </summary>
		private void OnSlotClicked(TowerSlot slot, int slotIndex)
		{
			if (TowerManager.Instance == null)
			{
				GD.PrintErr("[Level_02] TowerManager 单例不存在，无法建造。");
				return;
			}

			var selectedData = TowerManager.Instance.CurrentSelectedTowerData;
			if (selectedData == null)
			{
				GD.Print($"[Level_02] 槽位 #{slotIndex} 被点击，但尚未选择塔类型。请先点击 HUD 中的建造按钮选塔。");
				return;
			}

			GD.Print($"[Level_02] 尝试在槽位 #{slotIndex} 建造 {selectedData.TowerName} (成本 {selectedData.BuildCost})");
			TowerManager.Instance.TryBuildTower(slot, selectedData);
		}

		#endregion

		#region EventBus 事件处理

		/// <summary>
		/// 波次开始事件：打印日志，便于确认刷怪节奏是否符合预期。
		/// </summary>
		private void HandleWaveStarted(int waveIndex)
		{
			GD.Print($"[Level_02] 🚩 第 {waveIndex} 波开始！");
		}

		/// <summary>
		/// 波次完成事件：若非最后一波则安排下一波自动衔接，否则等待 GameManager 胜利判定。
		/// </summary>
		private void HandleWaveCompleted(int waveIndex)
		{
			GD.Print($"[Level_02] ✅ 第 {waveIndex} 波已清理完毕。");
			ScheduleNextWave();
		}

		/// <summary>
		/// 塔建造成功事件：打印日志确认建造事务闭环。
		/// </summary>
		private void HandleTowerBuilt(Config.Towers.TowerData towerData, Vector2 pos)
		{
			GD.Print($"[Level_02] 🏰 建造成功 {towerData.TowerName} @ {pos}");
		}

		/// <summary>
		/// 敌人击杀事件：仅用于流程日志，金币已由 EconomyManager 处理。
		/// </summary>
		private void HandleEnemyKilled(string enemyId, int goldReward, Vector2 deathPosition)
		{
			GD.Print($"[Level_02] 💀 敌人被击杀: {enemyId} | 奖励金币 +{goldReward} @ {deathPosition}");
		}

		/// <summary>
		/// 敌人逃脱事件：仅用于流程日志，扣血已由 EconomyManager 处理。
		/// </summary>
		private void HandleEnemyReachedEnd(int damageToPlayer)
		{
			GD.Print($"[Level_02] ⚠️ 敌人逃脱！玩家受到伤害 {damageToPlayer}");
		}

		/// <summary>
		/// 游戏结束事件：打印胜负结果日志。
		/// </summary>
		private void HandleGameOver(bool isVictory)
		{
			GD.Print($"[Level_02] 🏁 游戏结束 → {(isVictory ? "胜利！" : "失败")}");
		}

		/// <summary>
		/// 金币变更日志回调，便于排查经济回流是否正常。
		/// </summary>
		private void HandleGoldChanged(int newGold)
		{
			GD.Print($"[Level_02] 💰 金币变化: {newGold}");
		}

		/// <summary>
		/// 玩家生命值变更日志回调。
		/// </summary>
		private void HandlePlayerHpChanged(int newHp)
		{
			GD.Print($"[Level_02] ❤️ 玩家血量变化: {newHp}");
		}

		#endregion

		#region 敌人路径动态构建

		/// <summary>
		/// 在运行时动态为 EnemyPathNode.Curve 添加 6 个折线控制点。
		/// 第二关路径形状相比第一关略作调整：增加一处反向折线拐点形成"S"形迂回，
		/// 整体路径长度更长、节奏略快，体现"进阶挑战"的差异化定位。
		/// 路径形状（相对 EnemyPath 的 position 偏移量 (80,320)）：
		///   (0,0) → (320,0) → (320,-200) → (640,-200) → (640,200) → (960,200) → (960,-80) → (1280,-80)
		/// </summary>
		private void SetupEnemyPath()
		{
			if (EnemyPathNode == null)
			{
				GD.PrintErr("[Level_02] EnemyPathNode 未绑定，无法构建刷怪路径。");
				return;
			}

			var curve = EnemyPathNode.Curve ?? new Curve2D();
			curve.ClearPoints();

			Vector2[] waypoints =
			{
				new(0, 0),
				new(320, 0),
				new(320, -200),
				new(640, -200),
				new(640, 200),
				new(960, 200),
				new(960, -80),
				new(1280, -80)
			};

			foreach (var wp in waypoints)
			{
				curve.AddPoint(wp);
			}

			EnemyPathNode.Curve = curve;
			GD.Print($"[Level_02] 已构建 {waypoints.Length} 个控制点的敌人折线路径（S 形进阶版本）。");
		}

		#endregion
	}
}
