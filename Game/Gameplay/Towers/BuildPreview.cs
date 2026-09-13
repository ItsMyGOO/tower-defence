using Godot;
using TowerDefence.Config.Towers;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 建造预览指示器（Node2D，由 TowerManager 在 _Ready 时创建并挂载）。
    /// 选中待建造塔类型（TowerManager.CurrentSelectedTowerData 非空）时自动显示：
    /// - 鼠标位置绘制幽灵点与淡色射程圈；
    /// - 鼠标贴近空闲槽位（TowerSlot.SlotGroup 内、点击半径内）时吸附到槽位：
    ///   以槽位为圆心绘制射程圈与高亮环；
    /// - 贴近已占用槽位时改用红色高亮，提示此处无法建造；
    /// - 未贴近任何槽位时保持幽灵跟随。
    /// 视觉全部为 _Draw 占位绘制，替换正式美术时仅改动本类绘制部分。
    /// </summary>
    public partial class BuildPreview : Node2D
    {
        /// <summary>
        /// 无法建造（槽位已占用）时的高亮颜色。
        /// </summary>
        private static readonly Color BlockedColor = new(1.0f, 0.25f, 0.25f);

        /// <summary>
        /// 获取当前吸附命中的槽位（无则 null），供测试与调试读取。
        /// </summary>
        public TowerSlot HoveredSlot { get; private set; }

        /// <summary>
        /// 最近一次鼠标移动事件的视口坐标。
        /// 不使用 GetGlobalMousePosition：根视口直连屏幕时该接口读取 OS 真实光标，
        /// 无头/录屏等环境下模拟事件不会生效；从 _Input 事件中追踪在所有环境行为一致。
        /// </summary>
        private Vector2 _mouseViewportPosition = new(640.0f, 360.0f);

        /// <summary>
        /// 全局输入回调：追踪鼠标移动事件的视口坐标。
        /// </summary>
        /// <param name="event">输入事件</param>
        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion)
            {
                _mouseViewportPosition = motion.Position;
            }
        }

        /// <summary>
        /// 每帧跟随鼠标并重新判定吸附槽位；未选中塔类型时整体隐藏。
        /// </summary>
        /// <param name="delta">距上一帧经过的时间（秒）</param>
        public override void _Process(double delta)
        {
            var data = TowerManager.Instance?.CurrentSelectedTowerData;
            if (data == null)
            {
                Visible = false;
                HoveredSlot = null;
                return;
            }

            Visible = true;

            Vector2 mouseWorld = GetCanvasTransform().AffineInverse() * _mouseViewportPosition;
            GlobalPosition = mouseWorld;
            HoveredSlot = FindNearestSlot(mouseWorld);
            QueueRedraw();
        }

        /// <summary>
        /// 在槽位分组中查找距离鼠标最近且位于其点击判定半径内的槽位。
        /// </summary>
        /// <param name="mouseWorld">鼠标的世界坐标</param>
        /// <returns>命中的槽位；未命中为 null</returns>
        private TowerSlot FindNearestSlot(Vector2 mouseWorld)
        {
            TowerSlot nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Node node in GetTree().GetNodesInGroup(TowerSlot.SlotGroup))
            {
                if (node is not TowerSlot slot || !IsInstanceValid(slot)) continue;

                float distance = mouseWorld.DistanceTo(slot.GlobalPosition);
                if (distance <= slot.ClickRadius && distance < nearestDistance)
                {
                    nearest = slot;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 占位绘制：吸附时以槽位为圆心画射程圈与状态环（空闲=塔色 / 占用=红色），
        /// 未吸附时画跟随鼠标的幽灵点与淡色射程圈。
        /// </summary>
        public override void _Draw()
        {
            var data = TowerManager.Instance?.CurrentSelectedTowerData;
            if (data == null)
            {
                return;
            }

            if (HoveredSlot != null)
            {
                bool occupied = HoveredSlot.IsOccupied;
                Color ring = occupied ? BlockedColor : data.AttackColor;
                Color fill = occupied ? new Color(BlockedColor, 0.12f) : new Color(data.AttackColor, 0.25f);

                Vector2 slotLocal = ToLocal(HoveredSlot.GlobalPosition);
                DrawCircle(slotLocal, data.AttackRange, fill);
                DrawArc(slotLocal, data.AttackRange, 0.0f, Mathf.Tau, 96, ring, 2.5f);
                DrawArc(slotLocal, 30.0f, 0.0f, Mathf.Tau, 48, ring, 2.0f);
            }
            else
            {
                DrawCircle(Vector2.Zero, 6.0f, new Color(data.AttackColor, 0.7f));
                DrawArc(Vector2.Zero, data.AttackRange, 0.0f, Mathf.Tau, 96, new Color(data.AttackColor, 0.35f), 2.0f);
            }
        }
    }
}
