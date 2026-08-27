using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Waves;

namespace TowerDefence.Core.Managers
{
    /// <summary>
    /// 游戏主流程管理器。
    /// 负责维护全局 GameState（准备中/波次进行中/胜利/失败），监听 EventBus 中的波次完成与游戏结束事件，
    /// 在最后一波清理完毕后自动触发胜利判定，并在任何胜负条件达成时通过 GetTree().Paused 冻结局内时钟。
    /// 建议作为游戏主场景的常驻根节点，由场景层级挂载并通过 [Export] 关联 WaveManager。
    /// </summary>
    public partial class GameManager : Node
    {
        #region 游戏状态枚举

        /// <summary>
        /// 一局游戏的主状态枚举。
        /// 用于 GameManager 维护当前游戏阶段，便于后续 UI 高亮、暂停菜单逻辑等进行分支判定。
        /// </summary>
        public enum GameState
        {
            /// <summary>
            /// 准备阶段。游戏已加载但首波尚未开始，玩家可进行建塔等准备操作。
            /// </summary>
            Preparation,

            /// <summary>
            /// 波次进行中。敌人正在生成或场上仍有存活敌人，战斗逻辑持续运行。
            /// </summary>
            InWave,

            /// <summary>
            /// 玩家胜利。所有波次已清理完毕且无存活敌人。
            /// </summary>
            GameWin,

            /// <summary>
            /// 玩家失败。玩家生命值归零或满足其他失败条件。
            /// </summary>
            GameLose
        }

        #endregion

        #region 导出配置

        /// <summary>
        /// 获取或设置波次管理器节点引用。
        /// Inspector 中绑定到场景树内的 WaveManager 节点，用于查询 AllWavesCompleted 与 AliveEnemyCount
        /// 以判定玩家是否达成胜利条件。
        /// </summary>
        [Export] public WaveManager WaveManager { get; set; }

        #endregion

        #region 运行时状态

        /// <summary>
        /// 获取当前游戏主状态。
        /// 通过 GameState 枚举维护，外部仅可读，状态切换完全由内部事件驱动。
        /// </summary>
        public GameState CurrentState { get; private set; } = GameState.Preparation;

        /// <summary>
        /// 获取一个值，指示当前游戏是否已经结束（胜利或失败任一状态）。
        /// 在游戏结束后拒绝重复触发结算，避免面板反复弹出与多次暂停。
        /// </summary>
        public bool IsGameOver => CurrentState == GameState.GameWin || CurrentState == GameState.GameLose;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 确保游戏初始处于非暂停状态，并订阅 EventBus 中与胜负判定相关的事件。
        /// </summary>
        public override void _Ready()
        {
            GetTree().Paused = false;

            EventBus.OnWaveStarted += HandleWaveStarted;
            EventBus.OnWaveCompleted += HandleWaveCompleted;
            EventBus.OnGameOver += HandleGameOver;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有 EventBus 订阅，防止委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnWaveStarted -= HandleWaveStarted;
            EventBus.OnWaveCompleted -= HandleWaveCompleted;
            EventBus.OnGameOver -= HandleGameOver;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理波次开始事件。
        /// 将游戏状态从 Preparation 切换为 InWave，标记战斗阶段开始。
        /// </summary>
        /// <param name="waveIndex">当前启动波次的索引</param>
        private void HandleWaveStarted(int waveIndex)
        {
            if (IsGameOver) return;

            CurrentState = GameState.InWave;
        }

        /// <summary>
        /// 处理波次完成事件。
        /// 在确认是最后一波（WaveManager.AllWavesCompleted == true）且场上无存活敌人时，
        /// 发布 OnGameOver(true) 事件触发玩家胜利结算。
        /// 非最后一波则将状态临时切回 Preparation，等待玩家启动下一波或自动开启。
        /// </summary>
        /// <param name="waveIndex">已完成波次的索引</param>
        private void HandleWaveCompleted(int waveIndex)
        {
            if (IsGameOver) return;

            if (WaveManager != null && WaveManager.AllWavesCompleted && WaveManager.AliveEnemyCount == 0)
            {
                CurrentState = GameState.GameWin;
                EventBus.RaiseGameOver(true);
                return;
            }

            CurrentState = GameState.Preparation;
        }

        /// <summary>
        /// 处理游戏结束事件。
        /// 根据 isVictory 参数设置对应 GameWin/GameLose 状态，并通过 GetTree().Paused = true
        /// 冻结局内逻辑时钟（停止 _Process 与动画，保留 UI 交互），确保玩家点击结算按钮前游戏世界不再变化。
        /// 该方法为幂等：若已处于 GameOver 状态则直接返回，避免多次重复暂停。
        /// </summary>
        /// <param name="isVictory">true 表示玩家胜利，false 表示玩家失败</param>
        private void HandleGameOver(bool isVictory)
        {
            if (IsGameOver) return;

            CurrentState = isVictory ? GameState.GameWin : GameState.GameLose;
            GetTree().Paused = true;
        }

        #endregion
    }
}
