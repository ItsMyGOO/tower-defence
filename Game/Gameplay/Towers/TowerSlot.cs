using Godot;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造槽位节点。
    /// 用于标记地图上可放置防御塔的位置，维护占用状态与当前已建造的塔引用；
    /// 同时自持点击检测：_Ready 时为自身挂载 Area2D 圆形碰撞体，玩家左键点击槽位即向
    /// TowerManager 发起建造请求，无需外层关卡脚本代为做射线查询。
    /// 占用后拒绝重复建造。
    /// </summary>
    public partial class TowerSlot : Node2D
    {
        /// <summary>
        /// 获取或设置槽位点击检测半径（像素）。
        /// Area2D 圆形碰撞体按该半径创建，略大于槽位视觉块（50x50）以保证点击手感。
        /// </summary>
        [Export] public float ClickRadius { get; set; } = 35.0f;

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
        /// 节点被添加到场景树时调用。
        /// 创建槽位自有的点击检测碰撞体并绑定输入回调。
        /// </summary>
        public override void _Ready()
        {
            SetupClickArea();
        }

        /// <summary>
        /// 创建并配置点击检测用 Area2D 与圆形碰撞体。
        /// 依赖 Godot 物理拾取（enable_object_picking，默认开启）将鼠标事件分发到本槽位。
        /// </summary>
        private void SetupClickArea()
        {
            var clickArea = new Area2D
            {
                Name = "SlotClickArea"
            };
            AddChild(clickArea);

            clickArea.InputEvent += OnClickAreaInputEvent;

            var shape = new CollisionShape2D
            {
                Name = "SlotClickShape",
                Shape = new CircleShape2D
                {
                    Radius = ClickRadius
                }
            };
            clickArea.AddChild(shape);
        }

        /// <summary>
        /// 槽位点击碰撞体的输入回调。
        /// 仅响应未被暂停打断的鼠标左键按下事件；命中后进入建造请求入口。
        /// </summary>
        /// <param name="viewport">分发事件的视口（忽略）</param>
        /// <param name="event">输入事件</param>
        /// <param name="shapeIdx">命中碰撞形状索引（忽略）</param>
        private void OnClickAreaInputEvent(Node viewport, InputEvent @event, long shapeIdx)
        {
            if (GetTree().Paused) return;
            if (@event is not InputEventMouseButton mouseBtn) return;
            if (mouseBtn.ButtonIndex != MouseButton.Left || !mouseBtn.Pressed) return;

            RequestBuild();
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
