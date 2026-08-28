using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Config.Waves;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Enemies;

namespace TowerDefence.Gameplay.Waves
{
    /// <summary>
    /// 波次管理器节点。
    /// 负责按 WaveData 配置定时刷怪、将敌人挂载至 Path2D 路径系统，
    /// 并结合 EventBus 的敌人事件追踪存活敌人数量，完成波次开始/结束的状态闭环。
    /// 建议作为游戏主场景的常驻节点，由游戏流程控制器调用 StartNextWave()。
    /// </summary>
    public partial class WaveManager : Node
    {
        #region 导出配置

        /// <summary>
        /// 获取或设置所有波次的配置数据数组。
        /// 数组顺序即为游戏进行时的波次顺序；通过 StartNextWave() 按序读取。
        /// </summary>
        [Export] public Godot.Collections.Array<WaveData> Waves { get; set; } = new();

        /// <summary>
        /// 获取或设置敌人基础场景预制体。
        /// 刷怪时 Instantiate 此 PackedScene，并将 EnemyData 注入到 Enemy 节点的 Data 属性。
        /// 该场景的根节点必须是 Enemy（继承自 PathFollow2D）。
        /// </summary>
        [Export] public PackedScene EnemyBaseScene { get; set; }

        /// <summary>
        /// 获取或设置敌人沿其移动的目标路径节点。
        /// 新生成的敌人节点会被 AddChild 到此 Path2D 下，从而自动继承 PathFollow2D 的路径跟随能力。
        /// </summary>
        [Export] public Path2D TargetPath { get; set; }

        #endregion

        #region 运行时状态

        /// <summary>
        /// 获取当前已进行到的波次索引（对应 Waves 数组的下标）。
        /// 初始值为 -1 表示尚未开始任何波次；第一次调用 StartNextWave() 后变为 0。
        /// </summary>
        public int CurrentWaveIndex { get; private set; } = -1;

        /// <summary>
        /// 获取当前正在处理的 WaveData 配置引用。
        /// 若尚未开始或已全部结束则为 null。
        /// </summary>
        public WaveData CurrentWave { get; private set; }

        /// <summary>
        /// 获取当前波次中尚未生成的敌人剩余数量。
        /// 结合 AliveEnemyCount 一起判定波次是否已完成。
        /// </summary>
        public int RemainingSpawnCount { get; private set; }

        /// <summary>
        /// 获取当前波次中已生成但尚未清除（死亡+逃跑）的敌人存活数量。
        /// 由 EventBus.OnEnemyKilled 与 OnEnemyReachedEnd 事件递减。
        /// </summary>
        public int AliveEnemyCount { get; private set; }

        /// <summary>
        /// 获取一个值，指示当前波次是否正在进行中（刷怪未完成或仍有存活敌人）。
        /// </summary>
        public bool IsWaveActive { get; private set; }

        /// <summary>
        /// 获取一个值，指示所有波次是否都已完成。
        /// </summary>
        public bool AllWavesCompleted => CurrentWaveIndex >= Waves.Count - 1 && !IsWaveActive;

        #endregion

        #region 内部字段

        /// <summary>
        /// 内部刷怪定时器。
        /// 波次准备阶段结束后启动，按 CurrentWave.SpawnInterval 触发 Timeout 事件来生成下一个敌人。
        /// </summary>
        private Timer _spawnTimer;

        /// <summary>
        /// 波次准备阶段（DelayBeforeStart）的一次性定时器。
        /// 到时后自动启动 _spawnTimer 并发布波次开始事件。
        /// </summary>
        private Timer _delayTimer;

        /// <summary>
        /// 当前波次 EnemyTypes 列表中，下一个应生成的敌人数组下标。
        /// </summary>
        private int _enemyCursor;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化定时器并订阅 EventBus 中的敌人事件，用于追踪存活敌人数量。
        /// </summary>
        public override void _Ready()
        {
            _delayTimer = new Timer
            {
                OneShot = true,
                Name = "WaveDelayTimer"
            };
            AddChild(_delayTimer);
            _delayTimer.Timeout += OnDelayTimerTimeout;

            _spawnTimer = new Timer
            {
                OneShot = false,
                Name = "SpawnTimer"
            };
            AddChild(_spawnTimer);
            _spawnTimer.Timeout += OnSpawnTimerTimeout;

            EventBus.OnEnemyKilled += HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消 EventBus 订阅，避免委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnEnemyKilled -= HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 启动下一波敌人。
        /// 读取 Waves 数组中 CurrentWaveIndex + 1 对应的 WaveData，等待 DelayBeforeStart 秒后开始刷怪，
        /// 并通过 EventBus.OnWaveStarted 广播波次开始事件。
        /// </summary>
        /// <returns>若成功启动下一波则返回 true；若已没有更多波次或配置不合法则返回 false。</returns>
        public bool StartNextWave()
        {
            if (IsWaveActive)
            {
                GD.Print("[WARN][WaveManager] 当前波次尚未完成，无法开启下一波。");
                return false;
            }

            int nextIndex = CurrentWaveIndex + 1;
            if (nextIndex >= Waves.Count)
            {
                GD.Print("[WaveManager] 所有波次已完成。");
                return false;
            }

            CurrentWaveIndex = nextIndex;
            CurrentWave = Waves[nextIndex];

            if (CurrentWave == null)
            {
                GD.PrintErr($"[WaveManager] Waves[{nextIndex}] 为空，跳过该波次。");
                return false;
            }
            if (TargetPath == null)
            {
                GD.PrintErr("[WaveManager] TargetPath 未配置，无法刷怪。");
                return false;
            }
            if (EnemyBaseScene == null)
            {
                GD.PrintErr("[WaveManager] EnemyBaseScene 未配置，无法刷怪。");
                return false;
            }

            IsWaveActive = true;
            _enemyCursor = 0;
            RemainingSpawnCount = CurrentWave.EnemyTypes.Count;
            AliveEnemyCount = 0;

            if (CurrentWave.DelayBeforeStart > 0.0f)
            {
                _delayTimer.WaitTime = CurrentWave.DelayBeforeStart;
                _delayTimer.Start();
            }
            else
            {
                OnDelayTimerTimeout();
            }

            return true;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 波次准备阶段定时器到时回调。
        /// 发布 OnWaveStarted 事件，并按 SpawnInterval 启动刷怪定时器。
        /// </summary>
        private void OnDelayTimerTimeout()
        {
            if (CurrentWave == null) return;

            EventBus.RaiseWaveStarted(CurrentWave.WaveIndex);

            if (CurrentWave.EnemyTypes.Count == 0)
            {
                GD.Print($"[WARN][WaveManager] 波次 {CurrentWave.WaveIndex} 的 EnemyTypes 为空。");
                CheckWaveCompleted();
                return;
            }

            SpawnEnemy();

            _spawnTimer.WaitTime = Mathf.Max(0.01f, CurrentWave.SpawnInterval);
            _spawnTimer.Start();
        }

        /// <summary>
        /// 刷怪定时器超时回调。
        /// 生成 EnemyTypes 列表中的下一个敌人；若已生成完毕则停止定时器并立即检查波次完成条件。
        /// </summary>
        private void OnSpawnTimerTimeout()
        {
            if (_enemyCursor < CurrentWave.EnemyTypes.Count)
            {
                SpawnEnemy();
            }
            else
            {
                _spawnTimer.Stop();
                CheckWaveCompleted();
            }
        }

        /// <summary>
        /// 实例化一个敌人节点并挂载到 TargetPath 下。
        /// 从 CurrentWave.EnemyTypes[_enemyCursor] 读取 EnemyData，注入到 Enemy.Data 属性。
        /// </summary>
        private void SpawnEnemy()
        {
            if (CurrentWave == null || EnemyBaseScene == null || TargetPath == null) return;

            EnemyData enemyData = CurrentWave.EnemyTypes[_enemyCursor];
            if (enemyData == null)
            {
                GD.PrintErr($"[WaveManager] EnemyTypes[{_enemyCursor}] 为空，跳过该敌人。");
                _enemyCursor++;
                RemainingSpawnCount--;
                return;
            }

            Node instance = EnemyBaseScene.Instantiate();
            if (instance is not Enemy enemyNode)
            {
                GD.PrintErr("[WaveManager] EnemyBaseScene 根节点类型不是 Enemy。");
                instance.QueueFree();
                _enemyCursor++;
                RemainingSpawnCount--;
                return;
            }

            enemyNode.Data = enemyData;
            enemyNode.Name = $"Enemy_{CurrentWave.WaveIndex}_{_enemyCursor}";

            TargetPath.AddChild(enemyNode);

            AliveEnemyCount++;
            _enemyCursor++;
            RemainingSpawnCount--;
        }

        /// <summary>
        /// 敌人被击杀时从存活计数中扣除 1 并检查波次是否结束。
        /// 注意：这里不关心具体是哪种敌人，只关心存活数量的变化。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符（忽略）</param>
        /// <param name="goldReward">击杀奖励金币（忽略）</param>
        /// <param name="deathPosition">敌人被击杀时的世界坐标（忽略）</param>
        private void HandleEnemyKilled(string enemyId, int goldReward, Vector2 deathPosition)
        {
            if (!IsWaveActive) return;
            if (AliveEnemyCount > 0) AliveEnemyCount--;
            CheckWaveCompleted();
        }

        /// <summary>
        /// 敌人到达终点（逃跑）时从存活计数中扣除 1 并检查波次是否结束。
        /// </summary>
        private void HandleEnemyReachedEnd(int damageToPlayer)
        {
            if (!IsWaveActive) return;
            if (AliveEnemyCount > 0) AliveEnemyCount--;
            CheckWaveCompleted();
        }

        /// <summary>
        /// 检查当前波次是否已完成。
        /// 判定条件：刷怪列表已全部生成（RemainingSpawnCount == 0）且没有存活敌人（AliveEnemyCount == 0）。
        /// 满足后发布 OnWaveCompleted 事件并重置运行时状态。
        /// </summary>
        private void CheckWaveCompleted()
        {
            if (!IsWaveActive) return;
            if (RemainingSpawnCount > 0 || AliveEnemyCount > 0) return;

            IsWaveActive = false;
            _spawnTimer.Stop();
            _delayTimer.Stop();

            int finishedWaveIndex = CurrentWave?.WaveIndex ?? (CurrentWaveIndex + 1);
            EventBus.RaiseWaveCompleted(finishedWaveIndex);
        }

        #endregion
    }
}
