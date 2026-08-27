using Godot;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Gameplay.Economy
{
    /// <summary>
    /// 玩家经济与生命值管理器节点。
    /// 负责收拢当前金币与玩家 HP 的状态，通过订阅 EventBus 的敌人事件自动响应增减，
    /// 并在状态变化时广播对应变更事件；HP 归零则触发 GameOver 失败事件。
    /// 建议作为游戏主场景的常驻节点，可由防御塔系统、UI 层通过公共接口查询与消费金币。
    /// </summary>
    public partial class EconomyManager : Node
    {
        #region 导出配置

        /// <summary>
        /// 获取或设置游戏开局时的初始金币数量。
        /// 用于局内第一波次开始前的经济起点，建议在 Inspector 中根据难度预设调整。
        /// </summary>
        [Export] public int InitialGold { get; set; } = 100;

        /// <summary>
        /// 获取或设置游戏开局时的玩家初始生命值。
        /// 敌人到达终点会扣除该值；归零即判定为局内失败，触发 OnGameOver(false)。
        /// </summary>
        [Export] public int InitialHp { get; set; } = 20;

        #endregion

        #region 运行时状态

        /// <summary>
        /// 获取当前玩家持有的金币总数。
        /// 受敌人击杀奖励与防御塔建造/升级消费影响，变更时自动广播 OnGoldChanged。
        /// </summary>
        public int CurrentGold { get; private set; }

        /// <summary>
        /// 获取当前玩家剩余的生命值。
        /// 受敌人逃脱伤害影响，变更时自动广播 OnPlayerHpChanged；
        /// 若值小于等于 0 则同步触发 OnGameOver(false) 局内失败事件。
        /// </summary>
        public int CurrentHp { get; private set; }

        /// <summary>
        /// 获取一个值，指示当前游戏是否已结束（失败或胜利）。
        /// 用于防止 GameOver 后重复触发扣血/扣金币逻辑造成副作用。
        /// </summary>
        public bool IsGameOver { get; private set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化运行时状态（金币、HP），并订阅 EventBus 中的敌人生命周期事件。
        /// </summary>
        public override void _Ready()
        {
            CurrentGold = InitialGold;
            CurrentHp = InitialHp;
            IsGameOver = false;

            EventBus.RaiseGoldChanged(CurrentGold);
            EventBus.RaisePlayerHpChanged(CurrentHp);

            EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
            EventBus.OnEnemyKilled += HandleEnemyKilled;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消 EventBus 订阅，避免委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
            EventBus.OnEnemyKilled -= HandleEnemyKilled;
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 检查当前金币是否足以支付指定金额。
        /// 用于防御塔建造、升级等操作的前置判定，不会实际扣除金币。
        /// </summary>
        /// <param name="amount">需要检查的消费金额（应为非负整数）</param>
        /// <returns>true 表示当前金币 >= amount，可安全调用 TrySpendGold；否则返回 false</returns>
        public bool CanAfford(int amount)
        {
            if (amount < 0) amount = 0;
            return CurrentGold >= amount;
        }

        /// <summary>
        /// 尝试从当前金币中扣除指定金额。
        /// 扣除成功时自动广播 OnGoldChanged 事件；金额不足或游戏已结束时返回 false，不触发任何事件。
        /// </summary>
        /// <param name="amount">需要扣除的金币金额（应为非负整数）</param>
        /// <returns>true 表示扣除成功并已广播变更事件；false 表示金额不足或游戏已结束</returns>
        public bool TrySpendGold(int amount)
        {
            if (IsGameOver) return false;
            if (amount < 0) amount = 0;
            if (amount == 0) return true;

            if (!CanAfford(amount)) return false;

            CurrentGold -= amount;
            EventBus.RaiseGoldChanged(CurrentGold);
            return true;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 处理敌人到达路径尽头（逃脱）事件。
        /// 从玩家生命值中扣除对应伤害值，若 HP 归零则发布 GameOver 失败事件。
        /// </summary>
        /// <param name="damageToPlayer">该敌人对玩家造成的生命值伤害</param>
        private void HandleEnemyReachedEnd(int damageToPlayer)
        {
            if (IsGameOver) return;
            if (damageToPlayer <= 0) return;

            CurrentHp -= damageToPlayer;
            if (CurrentHp < 0) CurrentHp = 0;

            EventBus.RaisePlayerHpChanged(CurrentHp);
            GD.Print($"[EconomyManager] 玩家受到伤害 {damageToPlayer}，剩余 HP: {CurrentHp}");

            if (CurrentHp <= 0)
            {
                IsGameOver = true;
                EventBus.RaiseGameOver(false);
                GD.Print("[EconomyManager] 玩家生命值归零，触发 GameOver (失败)。");
            }
        }

        /// <summary>
        /// 处理敌人被击杀事件。
        /// 将该敌人的金币奖励累加到玩家当前金币，并广播金币变更事件。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符（用于日志追踪，不参与经济计算）</param>
        /// <param name="goldReward">击杀该敌人获得的金币奖励</param>
        private void HandleEnemyKilled(string enemyId, int goldReward)
        {
            if (IsGameOver) return;
            if (goldReward <= 0) return;

            CurrentGold += goldReward;
            EventBus.RaiseGoldChanged(CurrentGold);
            GD.Print($"[EconomyManager] 击杀敌人 {enemyId} 获得金币 +{goldReward}，当前金币: {CurrentGold}");
        }

        #endregion
    }
}
