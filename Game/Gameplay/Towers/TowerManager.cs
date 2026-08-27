using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造管理器节点。
    /// 作为建造事务的统一入口，负责校验槽位状态、扣除金币、实例化塔预制体并挂载到槽位，
    /// 事务成功后通过 EventBus 广播 OnTowerBuilt 事件供 UI/音效等模块响应。
    /// 建议作为主场景常驻节点，配合 HUD 商店与 TowerSlot 槽位完成端到端建造流程。
    /// </summary>
    public partial class TowerManager : Node
    {
        /// <summary>
        /// 获取 TowerManager 的全局单例实例。
        /// 用于 UI 层（HUD 商店按钮）与建造槽位等模块快速访问建造管理器，
        /// 需确保场景中仅存在一个 TowerManager 实例，否则可能导致引用非预期节点。
        /// </summary>
        public static TowerManager Instance { get; private set; }

        #region 导出配置

        /// <summary>
        /// 获取或设置防御塔通用预制体场景。
        /// 该预制体的根节点需为 Tower 类型，建造时会被实例化并注入具体的 TowerData 配置。
        /// </summary>
        [Export] public PackedScene TowerBaseScene { get; set; }

        #endregion

        #region 运行时状态

        /// <summary>
        /// 获取或设置当前玩家在 UI 中选中的待建造塔数据。
        /// 为 null 表示当前未选择任何塔；HUD 商店点击后应更新此字段，
        /// 随后在玩家点击 TowerSlot 时将此值传入 TryBuildTower。
        /// </summary>
        public TowerData CurrentSelectedTowerData { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化单例引用，确保全局仅有一个 TowerManager 实例。
        /// </summary>
        public override void _Ready()
        {
            Instance = this;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 清空单例引用，避免引用已销毁节点。
        /// </summary>
        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 尝试在指定槽位上建造防御塔。
        /// 执行顺序：槽位有效性校验 → 金币校验并扣费 → 实例化塔预制体 → 挂载到槽位 → 广播建造事件。
        /// 任意一步失败均返回 false，保证事务一致性（扣费仅在后续步骤均成功时发生）。
        /// </summary>
        /// <param name="slot">目标建造槽位</param>
        /// <param name="towerData">要建造的塔配置数据</param>
        /// <returns>true 表示建造成功，false 表示任意校验或实例化失败</returns>
        public bool TryBuildTower(TowerSlot slot, TowerData towerData)
        {
            if (slot == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：slot 为 null。");
                return false;
            }

            if (towerData == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：towerData 为 null。");
                return false;
            }

            if (slot.IsOccupied)
            {
                GD.Print($"[TowerManager] 建造失败：槽位 {slot.Name} 已被占用。");
                return false;
            }

            if (EconomyManager.Instance == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：EconomyManager 实例不存在。");
                return false;
            }

            if (!EconomyManager.Instance.TrySpendGold(towerData.BuildCost))
            {
                GD.Print($"[TowerManager] 建造失败：金币不足。需要 {towerData.BuildCost}，当前 {EconomyManager.Instance.CurrentGold}。");
                return false;
            }

            Tower towerInstance = null;
            if (TowerBaseScene != null)
            {
                towerInstance = TowerBaseScene.Instantiate<Tower>();
            }
            else
            {
                GD.Print("[TowerManager] 警告：TowerBaseScene 未设置，使用动态创建 Tower 节点作为兜底。");
                towerInstance = new Tower();
            }

            towerInstance.Name = $"Tower_{towerData.TowerId}_{slot.Name}";
            towerInstance.Data = towerData;

            if (!slot.PlaceTower(towerInstance))
            {
                GD.PrintErr("[TowerManager] 建造失败：slot.PlaceTower 返回 false，返还金币。");
                EconomyManager.Instance.AddGold(towerData.BuildCost);
                towerInstance.QueueFree();
                return false;
            }

            CurrentSelectedTowerData = null;
            EventBus.RaiseTowerBuilt(towerData, slot.GlobalPosition);

            GD.Print($"[TowerManager] ✅ 建造成功！塔={towerData.TowerName} | 槽位={slot.Name} | 位置={slot.GlobalPosition} | 剩余金币={EconomyManager.Instance.CurrentGold}");
            return true;
        }

        #endregion
    }
}
