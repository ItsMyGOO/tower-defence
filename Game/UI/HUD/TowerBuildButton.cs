using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Towers;

namespace TowerDefence.UI.HUD
{
    /// <summary>
    /// 防御塔建造按钮控件。
    /// 绑定具体的 TowerData 配置，通过订阅 EventBus.OnGoldChanged 实时响应玩家金币变化，
    /// 当金币不足以支付建造成本时自动禁用按钮（变灰），金币充足时恢复启用。
    /// 点击按钮时将关联的 TowerData 设置到 TowerManager.CurrentSelectedTowerData，
    /// 供后续放置预览或槽位建造流程使用。
    /// </summary>
    public partial class TowerBuildButton : Button
    {
        /// <summary>
        /// 获取或设置该按钮所绑定的防御塔配置数据。
        /// Inspector 中需绑定具体的 TowerData Resource（如 Tests/Data/Towers/Test_ArrowTower.tres）。
        /// </summary>
        [Export] public TowerData Data { get; set; }

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 订阅 EventBus.OnGoldChanged 事件以实时同步按钮可用状态，
        /// 并根据当前金币数进行一次初始状态刷新。
        /// </summary>
        public override void _Ready()
        {
            EventBus.OnGoldChanged += HandleGoldChanged;
            Pressed += HandlePressed;

            RefreshButtonState(EconomyManager.Instance?.CurrentGold ?? 0);
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消 EventBus 与自身信号的订阅，避免委托悬空导致内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnGoldChanged -= HandleGoldChanged;
            Pressed -= HandlePressed;
        }

        /// <summary>
        /// 处理金币变更事件。
        /// 根据最新金币数与当前塔建造成本对比，刷新按钮的禁用/启用状态。
        /// </summary>
        /// <param name="newGold">玩家更新后的金币总数</param>
        private void HandleGoldChanged(int newGold)
        {
            RefreshButtonState(newGold);
        }

        /// <summary>
        /// 根据金币数量刷新按钮可用状态。
        /// 金币 >= 建造成本时按钮可用（Disabled = false），否则禁用并变灰。
        /// </summary>
        /// <param name="currentGold">当前玩家金币数</param>
        private void RefreshButtonState(int currentGold)
        {
            if (Data == null)
            {
                Disabled = true;
                return;
            }

            int cost = Data.BuildCost;
            Disabled = currentGold < cost;
        }

        /// <summary>
        /// 处理按钮点击事件。
        /// 将本按钮绑定的 TowerData 设置为 TowerManager 当前选中的待建造塔类型，
        /// 供后续玩家点击 TowerSlot 时执行建造事务。
        /// </summary>
        private void HandlePressed()
        {
            if (Data == null)
            {
                GD.PrintErr("[TowerBuildButton] 点击失败：Data 未绑定 TowerData 资源。");
                return;
            }

            if (TowerManager.Instance == null)
            {
                GD.PrintErr("[TowerBuildButton] 点击失败：TowerManager 单例实例不存在。");
                return;
            }

            TowerManager.Instance.CurrentSelectedTowerData = Data;
            GD.Print($"[TowerBuildButton] ✅ 已选中待建造塔：{Data.TowerName} (成本 {Data.BuildCost})");
        }
    }
}
