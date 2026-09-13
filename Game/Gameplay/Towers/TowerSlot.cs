using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造槽位节点。
    /// 用于标记地图上可放置防御塔的位置，维护占用状态与当前已建造的塔引用；
    /// 同时自持点击检测：左键点击槽位向 TowerManager 发起建造请求，
    /// 右键点击已占用的槽位则出售塔位上的塔（按 TowerData.SellRefundRatio 返还金币）。
    /// 点击采用 _Input + 画布坐标距离判定：地图上的占位 Control（如全屏 MapBackground）
    /// 会在 GUI 阶段吞掉鼠标事件，物理拾取与 _UnhandledInput 都收不到，
    /// 而 _Input 在 GUI 之前分发，是最可靠的入口。
    /// </summary>
    public partial class TowerSlot : Node2D
    {
        /// <summary>
        /// 获取或设置槽位点击判定半径（像素），略大于槽位视觉块（50x50）以保证点击手感。
        /// </summary>
        [Export] public float ClickRadius { get; set; } = 35.0f;

        /// <summary>
        /// 获取一个值，指示当前槽位是否已被防御塔占用。
        /// 仅通过 PlaceTower()/SellTower() 内部赋值，外部只可读取，防止非法篡改状态。
        /// </summary>
        public bool IsOccupied { get; private set; } = false;

        /// <summary>
        /// 获取当前槽位上已建造的防御塔实例引用。
        /// 未建造时为 null，可用于 UI 选中高亮、塔升级/出售查询等后续功能扩展。
        /// </summary>
        public Tower CurrentTower { get; private set; }

        /// <summary>
        /// 全局输入回调：左键按下进入建造入口，右键按下进入出售入口。
        /// 全局暂停或点击位置不在槽位半径内时忽略。
        /// </summary>
        /// <param name="event">输入事件</param>
        public override void _Input(InputEvent @event)
        {
            if (GetTree().Paused) return;
            if (@event is not InputEventMouseButton mouseBtn || !mouseBtn.Pressed) return;
            if (mouseBtn.ButtonIndex != MouseButton.Left && mouseBtn.ButtonIndex != MouseButton.Right) return;

            // 事件坐标是视口坐标，经画布变换逆矩阵换算为世界坐标（兼容未来加入相机）
            Vector2 worldPosition = GetCanvasTransform().AffineInverse() * mouseBtn.Position;
            if (worldPosition.DistanceTo(GlobalPosition) > ClickRadius) return;

            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                RequestBuild();
            }
            else
            {
                SellTower();
            }
        }

        /// <summary>
        /// 建造请求入口：校验占用状态与 TowerManager 单例后，将当前选中的 TowerData
        /// 交给 TowerManager.TryBuildTower 执行建造事务；未选塔时仅提示，不视为错误。
        /// </summary>
        private void RequestBuild()
        {
            if (IsOccupied)
            {
                GD.Print($"[TowerSlot] 槽位 {Name} 已被占用，忽略建造请求。");
                return;
            }

            if (TowerManager.Instance == null)
            {
                GD.PrintErr($"[TowerSlot] TowerManager 单例不存在，无法建造（槽位 {Name}）。");
                return;
            }

            var selectedData = TowerManager.Instance.CurrentSelectedTowerData;
            if (selectedData == null)
            {
                GD.Print($"[TowerSlot] 槽位 {Name} 被点击，但尚未选择塔类型。请先点击 HUD 中的建造按钮选塔。");
                return;
            }

            GD.Print($"[TowerSlot] 槽位 {Name} 请求建造 {selectedData.TowerName} (成本 {selectedData.BuildCost})");
            TowerManager.Instance.TryBuildTower(this, selectedData);
        }

        /// <summary>
        /// 出售当前槽位上的防御塔：按 TowerData.SellRefundRatio 返还金币、
        /// 释放塔节点并清空占用状态，随后通过 EventBus 广播 OnTowerSold 事件。
        /// 右键点击槽位触发；槽位为空时仅提示，不视为错误。
        /// </summary>
        public void SellTower()
        {
            if (!IsOccupied)
            {
                GD.Print($"[TowerSlot] 槽位 {Name} 没有可出售的防御塔。");
                return;
            }

            if (CurrentTower == null || !IsInstanceValid(CurrentTower) || CurrentTower.Data == null)
            {
                GD.PrintErr($"[TowerSlot] 槽位 {Name} 的防御塔引用无效，出售失败。");
                return;
            }

            if (EconomyManager.Instance == null)
            {
                GD.PrintErr("[TowerSlot] EconomyManager 单例不存在，出售失败。");
                return;
            }

            string towerName = CurrentTower.Data.TowerName;
            int refund = (int)Mathf.Floor(CurrentTower.Data.BuildCost * CurrentTower.Data.SellRefundRatio);
            Vector2 sellPosition = GlobalPosition;

            CurrentTower.QueueFree();
            CurrentTower = null;
            IsOccupied = false;

            if (refund > 0)
            {
                EconomyManager.Instance.AddGold(refund);
            }

            EventBus.RaiseTowerSold(sellPosition);
            GD.Print($"[TowerSlot] ✅ 已出售 {towerName}，返还 {refund} 金币（槽位 {Name}）。");
        }

        /// <summary>
        /// 尝试将指定防御塔实例放置到当前槽位。
        /// 成功时同步设置 IsOccupied 与 CurrentTower，并将塔节点移动到槽位的世界坐标位置；
        /// 若槽位已占用则直接返回 false，保证单一槽位最多容纳一座塔。
        /// </summary>
        /// <param name="towerInstance">待放置的防御塔节点实例（需已配置好 TowerData）</param>
        /// <returns>true 表示放置成功，false 表示槽位已占用或参数无效</returns>
        public bool PlaceTower(Tower towerInstance)
        {
            if (IsOccupied)
            {
                GD.Print($"[TowerSlot] 槽位 {Name} 已占用，放置失败。");
                return false;
            }

            if (towerInstance == null)
            {
                GD.PrintErr($"[TowerSlot] 槽位 {Name} 放置失败：towerInstance 为 null。");
                return false;
            }

            CurrentTower = towerInstance;
            IsOccupied = true;

            towerInstance.Position = Vector2.Zero;
            AddChild(towerInstance);

            GD.Print($"[TowerSlot] 槽位 {Name} 成功放置防御塔: {towerInstance.Name}");
            return true;
        }
    }
}
