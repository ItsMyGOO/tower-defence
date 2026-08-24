using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Enemies;

namespace TowerDefence.Tests.Scenes
{
	/// <summary>
	/// 敌人路径测试场景控制器。
	/// 负责在测试场景中初始化 Path2D 路径、生成测试敌人、
	/// 并通过订阅 EventBus 事件验证敌人移动与生命周期逻辑。
	/// </summary>
	public partial class EnemyTest : Node2D
	{
		/// <summary>
		/// 获取或设置测试场景中的路径节点。
		/// 敌人将沿此 Path2D 定义的曲线移动，Inspector 中绑定场景内的 Path2D 节点。
		/// </summary>
		[Export] public Path2D EnemyPath { get; set; }

		/// <summary>
		/// 获取或设置测试用敌人数据资源。
		/// Inspector 中绑定 Tests/Data/Enemies/Test_SlimeData.tres。
		/// </summary>
		[Export] public EnemyData TestEnemyData { get; set; }

		/// <summary>
		/// 获取或设置敌人预制体（PackedScene）。
		/// 如果为空，将在代码中动态创建 Enemy 节点用于快速测试。
		/// </summary>
		[Export] public PackedScene EnemyPrefab { get; set; }

		/// <summary>
		/// 获取或设置敌人生成间隔（秒）。
		/// </summary>
		[Export] public float SpawnInterval { get; set; } = 2.0f;

		/// <summary>
		/// 获取或设置最大生成敌人数。
		/// </summary>
		[Export] public int MaxSpawnCount { get; set; } = 5;

		private int _spawnedCount;
		private Timer _spawnTimer;

		/// <summary>
		/// 节点进入场景树时调用。
		/// 完成事件订阅、校验节点绑定、并启动敌人生成定时器。
		/// </summary>
		public override void _Ready()
		{
			GD.Print("[EnemyTest] ========== 测试场景启动 ==========");

			SubscribeEvents();
			ValidateBindings();
			SetupSpawnTimer();

			GD.Print($"[EnemyTest] 敌人路径绑定: {(EnemyPath != null ? "OK" : "MISSING")}");
			GD.Print($"[EnemyTest] 敌人数据绑定: {(TestEnemyData != null ? TestEnemyData.EnemyName : "MISSING")}");
			GD.Print($"[EnemyTest] 计划生成 {MaxSpawnCount} 个敌人，间隔 {SpawnInterval}s");
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
		/// 订阅 EventBus 全局事件，用于验证敌人生命周期回调。
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
		/// 若 Path2D 缺失则向控制台输出警告。
		/// </summary>
		private void ValidateBindings()
		{
			if (EnemyPath == null)
			{
				GD.PrintErr("[EnemyTest] EnemyPath 未绑定！请在 Inspector 中绑定场景内的 Path2D 节点。");
			}

			if (TestEnemyData == null)
			{
				GD.PrintErr("[EnemyTest] TestEnemyData 未绑定！请绑定 Tests/Data/Enemies/Test_SlimeData.tres。");
			}
		}

		/// <summary>
		/// 创建并配置敌人生成定时器。
		/// 定时器将按 SpawnInterval 间隔触发，直到达到 MaxSpawnCount。
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
		/// 优先使用 EnemyPrefab 实例化，否则动态创建 Enemy 节点。
		/// </summary>
		private void SpawnEnemy()
		{
			if (_spawnedCount >= MaxSpawnCount)
			{
				_spawnTimer.Stop();
				GD.Print($"[EnemyTest] 已达到最大生成数 {MaxSpawnCount}，停止生成。");
				return;
			}

			if (EnemyPath == null || TestEnemyData == null)
			{
				GD.PrintErr("[EnemyTest] 生成敌人失败：必要绑定缺失。");
				return;
			}

			Enemy enemy;
			if (EnemyPrefab != null)
			{
				enemy = EnemyPrefab.Instantiate<Enemy>();
			}
			else
			{
				enemy = new Enemy
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
			}

			enemy.Data = TestEnemyData;
			EnemyPath.AddChild(enemy);

			_spawnedCount++;
			GD.Print($"[EnemyTest] 生成敌人 #{_spawnedCount}: {enemy.Name} (HP={TestEnemyData.MaxHp}, Speed={TestEnemyData.MoveSpeed})");
		}

		/// <summary>
		/// 处理敌人到达路径尽头事件。
		/// 通过 GD.Print 输出日志，便于在控制台验证事件是否正确触发。
		/// </summary>
		/// <param name="damageToPlayer">该敌人对玩家造成的伤害值</param>
		private void HandleEnemyReachedEnd(int damageToPlayer)
		{
			GD.Print($"[EnemyTest] ✅ OnEnemyReachedEnd 事件触发！对玩家造成伤害: {damageToPlayer}");
		}

		/// <summary>
		/// 处理敌人被击杀事件。
		/// 通过 GD.Print 输出日志，便于在控制台验证事件是否正确触发。
		/// </summary>
		/// <param name="enemyId">被击杀敌人的资源 ID</param>
		/// <param name="goldReward">击杀奖励金币数</param>
		private void HandleEnemyKilled(string enemyId, int goldReward)
		{
			GD.Print($"[EnemyTest] ✅ OnEnemyKilled 事件触发！敌人: {enemyId}, 奖励金币: {goldReward}");
		}
	}
}
