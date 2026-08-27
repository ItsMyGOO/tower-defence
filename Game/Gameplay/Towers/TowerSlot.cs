using Godot;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造槽位节点。
    /// 用于标记地图上可放置防御塔的位置，维护占用状态与当前已建造的塔引用，
    /// 通过 PlaceTower() 方法接收外部建造管理器分配的塔实例，并在占用后拒绝重复建造。
    /// </summary>
    public partial class TowerSlot : Node2D
    {
        /// <summary>
        /// 获取一个值，指示当前槽位是否已被防御塔占用。
        /// 仅通过 PlaceTower() 内部赋值，外部只可读取，防止非法篡改状态。
        /// </summary>
        public bool IsOccupied { get; private set; } = false;

        /// <summary>
        /// 获取当前槽位上已建造的防御塔实例引用。
        /// 未建造时为 null，可用于 UI 选中高亮、塔升级/出售查询等后续功能扩展。
        /// </summary>
        public Tower CurrentTower { get; private set; }

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
