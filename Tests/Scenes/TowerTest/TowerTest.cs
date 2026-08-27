using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Enemies;
using TowerDefence.Gameplay.Towers;

namespace TowerDefence.Tests.Scenes
{
	/// <summary>
	/// 防御塔索敌与攻击测试场景控制器。
	/// 负责在测试场景中初始化 Path2D 路径、放置测试防御塔、周期性生成敌人，
	/// 并通过订阅 EventBus 事件验证敌人进入范围后塔攻击、击杀与离开范围停止攻击的完整逻辑。
	/// </summary>
	public partial class TowerTest : Node2D
	{
		/// <summary>
		/// 获取或设置测试场景中的路径节点。
		/// 敌人将沿此 Path2D 定义的曲线移动，路径需穿过塔的攻击范围以验证索敌逻辑。
		/// </summary>
		[Export] public Path2D EnemyPath { get; set; }

		/// <summary>
		/// 获取或设置测试用敌人数据资源。
		/// Inspector 中绑定 Tests/Data/Enemies/Test_SlimeData.tres。
		/// </summary>
		[Export] public EnemyData TestEnemyData { get; set; }

		/// <summary>
		/// 获取或设置测试用防御塔数据资源。
		/// Inspector 中绑定 Tests/Data/Towers/Test_ArrowTower.tres。
		/// </summary>
		[Export] public TowerData TestTowerData { get; set; }

		/// <summary>
		/// 获取或设置防御塔放置的世界坐标位置。
		/// </summary>
		[Export] public Vector2 TowerPosition { get; set; } = new Vector2(400, 300);

		/// <summary>
		/// 获取或设置敌人生成间隔（秒）。
		/// </summary>
		[Export] public float SpawnInterval { get; set; } = 2.5f;

		/// <summary>
		/// 获取或设置最大生成敌人数。
		/// </summary>
		[Export] public int MaxSpawnCount { get; set; } = 6;

		private int _spawnedCount;
		private Timer _spawnTimer;
		private Tower _testTower;

		/// <summary>
		/// 节点进入场景树时调用。
		/// 完成事件订阅、校验节点绑定、放置防御塔并启动敌人生成定时器。
		/// </summary>
		public override void _Ready()
		{
			GD.Print("[TowerTest] ========== 防御塔测试场景启动 ==========");

			SubscribeEvents();
			ValidateBindings();
			SpawnTower();
			SetupSpawnTimer();

			GD.Print($"[TowerTest] 路径绑定: {(EnemyPath != null ? "OK" : "MISSING")}");
			GD.Print($"[TowerTest] 敌人数据: {(TestEnemyData != null ? TestEnemyData.EnemyName : "MISSING")}");
			GD.Print($"[TowerTest] 塔数据: {(TestTowerData != null ? TestTowerData.TowerName : "MISSING")}");
			GD.Print($"[TowerTest] 塔位置: {TowerPosition} | 攻击范围: {TestTowerData?.AttackRange}");
			GD.Print($"[TowerTest] 计划生成 {MaxSpawnCount} 个敌人，间隔 {SpawnInterval}s");
		}

		/// <summary>
		/// 节点从场景树移除时调用。
		/// 取消订阅所有事件以避免内存泄漏。
		/// </summary>
		public override void _ExitTree()
		{
			UnsubscribeEvents();
		}

		/// <summary>
		/// 订阅 EventBus 全局事件，用于验证敌人生命周期与塔攻击的端到端回调。
		/// </summary>
		private void SubscribeEvents()
		{
			EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
			EventBus.OnEnemyKilled += HandleEnemyKilled;
		}

		/// <summary>
		/// 取消订阅 EventBus 全局事件。
		/// </summary>
		private void UnsubscribeEvents()
		{
			EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
			EventBus.OnEnemyKilled -= HandleEnemyKilled;
		}

		/// <summary>
		/// 校验 Inspector 中必填节点的绑定状态。
		/// </summary>
		private void ValidateBindings()
		{
			if (EnemyPath == null)
			{
				GD.PrintErr("[TowerTest] EnemyPath 未绑定！请在 Inspector 中绑定场景内的 Path2D 节点。");
			}

			if (TestEnemyData == null)
			{
				GD.PrintErr("[TowerTest] TestEnemyData 未绑定！请绑定 Tests/Data/Enemies/Test_SlimeData.tres。");
			}

			if (TestTowerData == null)
			{
				GD.PrintErr("[TowerTest] TestTowerData 未绑定！请绑定 Tests/Data/Towers/Test_ArrowTower.tres。");
			}
		}

		/// <summary>
		/// 在场景中实例化并放置测试防御塔。
		/// 动态创建 Tower 节点，注入 TestTowerData 并放置到 TowerPosition。
		/// </summary>
		private void SpawnTower()
		{
			if (TestTowerData == null)
			{
				return;
			}

			_testTower = new Tower
			{
				Name = "TestTower",
				Position = TowerPosition,
				Data = TestTowerData
			};
			AddChild(_testTower);

			var rangeMarker = new Node2D
			{
				Name = "RangeMarker"
			};
			_testTower.AddChild(rangeMarker);
		}

		/// <summary>
		/// 创建并配置敌人生成定时器。
		/// </summary>
		private void SetupSpawnTimer()
		{
			_spawnTimer = new Timer
			{
				WaitTime = SpawnInterval,
				OneShot = false,
				Autostart = true
			};
			_spawnTimer.Timeout += SpawnEnemy;
			AddChild(_spawnTimer);

			SpawnEnemy();
		}

		/// <summary>
		/// 生成单个测试敌人并挂载到 Path2D 下。
		/// 动态创建 Enemy 节点并附加占位视觉方块，便于观察路径与索敌效果。
		/// </summary>
		private void SpawnEnemy()
		{
			if (_spawnedCount >= MaxSpawnCount)
			{
				_spawnTimer.Stop();
				GD.Print($"[TowerTest] 已达到最大生成数 {MaxSpawnCount}，停止生成。");
				return;
			}

			if (EnemyPath == null || TestEnemyData == null)
			{
				GD.PrintErr("[TowerTest] 生成敌人失败：必要绑定缺失。");
				return;
			}

			var enemy = new Enemy
			{
				Name = $"Enemy_{_spawnedCount + 1}"
			};

			var visual = new ColorRect
			{
				Size = new Vector2(20, 20),
				Color = new Color(0.2f, 0.8f, 0.2f),
				Position = new Vector2(-10, -10)
			};
			enemy.AddChild(visual);

			enemy.Data = TestEnemyData;
			EnemyPath.AddChild(enemy);

			_spawnedCount++;
			GD.Print($"[TowerTest] 生成敌人 #{_spawnedCount}: {enemy.Name} (HP={TestEnemyData.MaxHp}, Speed={TestEnemyData.MoveSpeed})");
		}

		/// <summary>
		/// 处理敌人到达路径尽头事件。
		/// </summary>
		/// <param name="damageToPlayer">该敌人对玩家造成的伤害值</param>
		private void HandleEnemyReachedEnd(int damageToPlayer)
		{
			GD.Print($"[TowerTest] ✅ OnEnemyReachedEnd 事件触发！敌人逃脱，对玩家造成伤害: {damageToPlayer}");
		}

		/// <summary>
		/// 处理敌人被击杀事件。
		/// </summary>
		/// <param name="enemyId">被击杀敌人的资源 ID</param>
		/// <param name="goldReward">击杀奖励金币数</param>
		private void HandleEnemyKilled(string enemyId, int goldReward)
		{
			GD.Print($"[TowerTest] ✅ OnEnemyKilled 事件触发！敌人: {enemyId}, 奖励金币: {goldReward}");
		}
	}
}
