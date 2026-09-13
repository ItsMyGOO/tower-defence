using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造槽位节点。
    /// 用于标记地图上可放置防御塔的位置，维护占用状态与当前已建造的塔引用；
    /// 左键点击槽位向 TowerManager 请求弹出环形菜单（空槽位 = 建造环 / 已占用 = 升级出售环），
    /// 右键点击已占用的槽位直接出售塔位上的塔（按 TowerData.SellRefundRatio 对累计投入返还金币）。
    /// 点击采用 _Input + 画布坐标距离判定：地图上的占位 Control（如全屏 MapBackground）
    /// 会在 GUI 阶段吞掉鼠标事件，物理拾取与 _UnhandledInput 都收不到，
    /// 而 _Input 在 GUI 之前分发，是最可靠的入口。
    /// </summary>
    public partial class TowerSlot : Node2D
    {
        /// <summary>
        /// 所有塔槽位所在的场景树分组名。
        /// 供建造预览吸附、右键取消选择判定等按位置查询槽位的模块使用。
        /// </summary>
        public const string SlotGroup = "tower_slots";

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
        /// 节点被添加到场景树时调用：加入 tower_slots 分组，供建造预览吸附、
        /// 右键取消选择判定等模块按位置查询槽位。
        /// </summary>
        public override void _Ready()
        {
            AddToGroup(SlotGroup);
        }

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

            if (!ContainsViewportPosition(mouseBtn.Position)) return;

            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                TowerManager.Instance?.OpenSlotMenu(this);
            }
            else
            {
                SellTower();
            }

            // 标记已消费：阻止本次点击继续流入 _UnhandledInput 阶段，
            // 否则环形菜单的「点击外部关闭」逻辑会在菜单打开的同一事件里立刻将其关闭
            GetViewport().SetInputAsHandled();
        }

        /// <summary>
        /// 判断视口坐标是否落在本槽位的点击判定半径内。
        /// 事件坐标是视口坐标，经画布变换逆矩阵换算为世界坐标（兼容未来加入相机）。
        /// 供本类点击判定与 TowerManager 的右键取消选择判定共用。
        /// </summary>
        /// <param name="viewportPosition">视口坐标</param>
        /// <returns>在判定半径内返回 true</returns>
        public bool ContainsViewportPosition(Vector2 viewportPosition)
        {
            Vector2 worldPosition = GetCanvasTransform().AffineInverse() * viewportPosition;
            return worldPosition.DistanceTo(GlobalPosition) <= ClickRadius;
        }

        /// <summary>
        /// 出售当前槽位上的防御塔：按 TowerData.SellRefundRatio 对累计投入
        /// （建造费用 + 历次升级费用）返还金币、释放塔节点并清空占用状态，
        /// 随后通过 EventBus 广播 OnTowerSold 事件。
        /// 右键点击槽位或经升级环「💰 出售」选项触发；槽位为空时仅提示，不视为错误。
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
            int refund = (int)Mathf.Floor(CurrentTower.InvestedGold * CurrentTower.Data.SellRefundRatio);
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

            CurrentTower = towerInstance;
            IsOccupied = true;

            towerInstance.Position = Vector2.Zero;
            AddChild(towerInstance);

            GD.Print($"[TowerSlot] 槽位 {Name} 成功放置防御塔: {towerInstance.Name}");
            return true;
        }
    }
}
