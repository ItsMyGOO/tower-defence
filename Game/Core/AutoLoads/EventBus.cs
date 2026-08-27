using System;
using Godot;

namespace TowerDefence.Core.AutoLoads
{
    /// <summary>
    /// 全局事件总线，用于模块间的松耦合通信。
    /// 所有业务模块通过发布/订阅事件进行交互，禁止直接跨模块引用。
    /// </summary>
    public static class EventBus
    {
        #region 经济与玩家

        /// <summary>
        /// 当玩家金币数量发生变化时触发。
        /// </summary>
        /// <param name="newGold">更新后的金币总数</param>
        public static event Action<int> OnGoldChanged;

        /// <summary>
        /// 当玩家生命值发生变化时触发。
        /// </summary>
        /// <param name="newHp">更新后的生命值</param>
        public static event Action<int> OnPlayerHpChanged;

        #endregion

        #region 防御塔

        /// <summary>
        /// 当防御塔被成功放置到地图上时触发。
        /// </summary>
        /// <param name="gridPosition">防御塔所在的网格坐标</param>
        /// <param name="towerId">防御塔的资源标识符或配置 ID</param>
        public static event Action<Vector2I, string> OnTowerPlaced;

        /// <summary>
        /// 当防御塔被出售（移除）时触发。
        /// </summary>
        /// <param name="gridPosition">被出售防御塔所在的网格坐标</param>
        public static event Action<Vector2I> OnTowerSold;

        /// <summary>
        /// 当防御塔建造成功时触发。
        /// </summary>
        /// <param name="towerData">建造的塔数据资源</param>
        /// <param name="buildPosition">建造的世界坐标位置</param>
        public static event Action<Config.Towers.TowerData, Vector2> OnTowerBuilt;

        #endregion

        #region 敌人与波次

        /// <summary>
        /// 当敌人被击杀时触发。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符或配置 ID</param>
        /// <param name="goldReward">击杀该敌人获得的金币奖励</param>
        public static event Action<string, int> OnEnemyKilled;

        /// <summary>
        /// 当敌人沿路径移动至尽头（未被击杀、成功逃脱）时触发。
        /// </summary>
        /// <param name="damageToPlayer">该敌人对玩家造成的生命值扣除量</param>
        public static event Action<int> OnEnemyReachedEnd;

        /// <summary>
        /// 当新一波敌人开始生成时触发。
        /// </summary>
        /// <param name="waveIndex">当前波次的索引（从 0 或 1 开始，依游戏规则而定）</param>
        public static event Action<int> OnWaveStarted;

        /// <summary>
        /// 当一波敌人全部清理完毕（波次完成）时触发。
        /// </summary>
        /// <param name="waveIndex">已完成波次的索引</param>
        public static event Action<int> OnWaveCompleted;

        #endregion

        #region 局内胜负

        /// <summary>
        /// 当一局游戏结束（胜利或失败）时触发。
        /// </summary>
        /// <param name="isVictory">true 表示玩家胜利，false 表示玩家失败</param>
        public static event Action<bool> OnGameOver;

        #endregion

        #region 发布方法

        /// <summary>
        /// 发布金币变更事件。
        /// </summary>
        /// <param name="newGold">更新后的金币总数</param>
        public static void RaiseGoldChanged(int newGold) => OnGoldChanged?.Invoke(newGold);

        /// <summary>
        /// 发布玩家生命值变更事件。
        /// </summary>
        /// <param name="newHp">更新后的生命值</param>
        public static void RaisePlayerHpChanged(int newHp) => OnPlayerHpChanged?.Invoke(newHp);

        /// <summary>
        /// 发布防御塔放置事件。
        /// </summary>
        /// <param name="gridPosition">防御塔所在的网格坐标</param>
        /// <param name="towerId">防御塔的资源标识符或配置 ID</param>
        public static void RaiseTowerPlaced(Vector2I gridPosition, string towerId) => OnTowerPlaced?.Invoke(gridPosition, towerId);

        /// <summary>
        /// 发布防御塔出售事件。
        /// </summary>
        /// <param name="gridPosition">被出售防御塔所在的网格坐标</param>
        public static void RaiseTowerSold(Vector2I gridPosition) => OnTowerSold?.Invoke(gridPosition);

        /// <summary>
        /// 发布防御塔建造成功事件。
        /// </summary>
        /// <param name="towerData">建造的塔数据资源</param>
        /// <param name="buildPosition">建造的世界坐标位置</param>
        public static void RaiseTowerBuilt(Config.Towers.TowerData towerData, Vector2 buildPosition) => OnTowerBuilt?.Invoke(towerData, buildPosition);

        /// <summary>
        /// 发布敌人击杀事件。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符或配置 ID</param>
        /// <param name="goldReward">击杀该敌人获得的金币奖励</param>
        public static void RaiseEnemyKilled(string enemyId, int goldReward) => OnEnemyKilled?.Invoke(enemyId, goldReward);

        /// <summary>
        /// 发布敌人走到路径尽头事件。
        /// </summary>
        /// <param name="damageToPlayer">该敌人对玩家造成的生命值扣除量</param>
        public static void RaiseEnemyReachedEnd(int damageToPlayer) => OnEnemyReachedEnd?.Invoke(damageToPlayer);

        /// <summary>
        /// 发布波次开始事件。
        /// </summary>
        /// <param name="waveIndex">当前波次的索引</param>
        public static void RaiseWaveStarted(int waveIndex) => OnWaveStarted?.Invoke(waveIndex);

        /// <summary>
        /// 发布波次完成事件。
        /// </summary>
        /// <param name="waveIndex">已完成波次的索引</param>
        public static void RaiseWaveCompleted(int waveIndex) => OnWaveCompleted?.Invoke(waveIndex);

        /// <summary>
        /// 发布游戏结束事件。
        /// </summary>
        /// <param name="isVictory">true 表示胜利，false 表示失败</param>
        public static void RaiseGameOver(bool isVictory) => OnGameOver?.Invoke(isVictory);

        #endregion
    }
}
