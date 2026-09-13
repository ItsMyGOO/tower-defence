using System.Collections.Generic;
using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 塔槽位环形菜单（由 TowerManager 持有唯一实例，点击槽位时在槽位位置弹出）。
    /// - 空槽位 → 建造环：环绕槽位排列所有可用塔类型（TowerManager 按目录扫描自动发现），
    ///   金币不足的选项自动置灰；悬停选项时以槽位为圆心绘制该塔的射程预览圈；
    /// - 已占用槽位 → 升级环：「⬆️ 升级」（满级/金币不足自动置灰）与「💰 出售」（按累计投入返还）。
    /// 点击菜单选项以外的任意位置（含其他槽位、空白处）关闭菜单；
    /// 选项按钮的点击在 GUI 阶段被消费，不会触发关闭。
    /// </summary>
    public partial class TowerRadialMenu : Node2D
    {
        /// <summary>
        /// 菜单选项到槽位中心的排布半径（像素）。
        /// </summary>
        private const float OptionOrbitRadius = 78.0f;

        /// <summary>
        /// 单个选项按钮的边长（像素）。
        /// </summary>
        private const float OptionSize = 68.0f;

        /// <summary>
        /// 获取当前绑定的槽位（菜单关闭时为 null）。
        /// </summary>
        public TowerSlot BoundSlot { get; private set; }

        /// <summary>
        /// 获取当前菜单是否处于打开状态。
        /// </summary>
        public bool IsOpen => BoundSlot != null;

        /// <summary>
        /// 获取当前打开的菜单类型：true = 升级环，false = 建造环。
        /// </summary>
        public bool IsUpgradeMenu { get; private set; }

        /// <summary>
        /// 获取当前菜单的选项按钮列表（含禁用项），供测试与调试读取。
        /// </summary>
        public IReadOnlyList<Button> Options => _options;

        private readonly List<Button> _options = new();

        /// <summary>
        /// 当前悬停的建造选项塔数据（用于射程预览）；升级环或无悬停时为 null。
        /// </summary>
        private TowerData _hoveredPreviewData;

        /// <summary>
        /// 打开建造环：以槽位为圆心排列所有可用塔类型。
        /// </summary>
        /// <param name="slot">目标空槽位</param>
        /// <param name="availableTowers">可用塔配置列表（TowerManager 扫描所得）</param>
        public void OpenBuild(TowerSlot slot, IReadOnlyList<TowerData> availableTowers)
        {
            OpenAt(slot, upgradeMode: false);

            for (int i = 0; i < availableTowers.Count; i++)
            {
                TowerData data = availableTowers[i];
                Button option = CreateOption(i, availableTowers.Count, $"{data.TowerName}\n{data.BuildCost}G");
                option.Pressed += () => OnBuildOptionPressed(data);
                option.MouseEntered += () => SetHoveredPreview(data);
                option.MouseExited += () => SetHoveredPreview(null);
                RefreshAffordState(option, data.BuildCost);
            }

            SubscribeGoldRefresh();
        }

        /// <summary>
        /// 打开升级环：提供「⬆️ 升级」与「💰 出售」两个选项。
        /// </summary>
        /// <param name="slot">目标已占用槽位</param>
        public void OpenUpgrade(TowerSlot slot)
        {
            OpenAt(slot, upgradeMode: true);

            Tower tower = slot.CurrentTower;
            int upgradeCost = tower?.GetNextUpgradeCost() ?? -1;
            string upgradeText = tower != null && tower.IsMaxLevel
                ? "⬆️ 已满级"
                : $"⬆️ 升级\n{upgradeCost}G";
            Button upgradeOption = CreateOption(0, 2, upgradeText);
            upgradeOption.Pressed += () => OnUpgradeOptionPressed();
            if (tower != null && !tower.IsMaxLevel)
            {
                RefreshAffordState(upgradeOption, upgradeCost);
            }

            int refund = tower == null
                ? 0
                : (int)Mathf.Floor(tower.InvestedGold * tower.Data.SellRefundRatio);
            Button sellOption = CreateOption(1, 2, $"💰 出售\n+{refund}G");
            sellOption.Pressed += OnSellOptionPressed;
        }

        /// <summary>
        /// 关闭菜单并清理选项。
        /// </summary>
        public void Close()
        {
            if (!IsOpen) return;

            EventBus.OnGoldChanged -= RefreshAffordStates;
            BoundSlot = null;
            Visible = false;
            ClearOptions();
            GD.Print("[TowerRadialMenu] 菜单已关闭。");
        }

        /// <summary>
        /// 点击菜单选项以外的任意鼠标按键（GUI 未消费时到达）→ 关闭菜单。
        /// 选项按钮自身的点击在 GUI 阶段被消费，不会进入本回调。
        /// </summary>
        /// <param name="event">输入事件</param>
        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsOpen) return;
            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                GetViewport().SetInputAsHandled();
                Close();
            }
        }

        #region 内部 —— 菜单构建与选项事件

        /// <summary>
        /// 菜单打开的公共入口：定位到槽位、重置状态。
        /// </summary>
        private void OpenAt(TowerSlot slot, bool upgradeMode)
        {
            if (IsOpen)
            {
                Close();
            }

            BoundSlot = slot;
            IsUpgradeMenu = upgradeMode;
            GlobalPosition = slot.GlobalPosition;
            Visible = true;
            GD.Print($"[TowerRadialMenu] 在槽位 {slot.Name} 打开{(upgradeMode ? "升级" : "建造")}菜单。");
        }

        /// <summary>
        /// 创建一个环形排布的选项按钮并加入菜单。
        /// </summary>
        /// <param name="index">选项序号（从 0 起）</param>
        /// <param name="totalCount">选项总数（决定角度分布）</param>
        /// <param name="text">按钮显示文本（名称/费用）</param>
        /// <returns>创建的按钮</returns>
        private Button CreateOption(int index, int totalCount, string text)
        {
            float angle = Mathf.Tau * index / Mathf.Max(1, totalCount) - Mathf.Pi / 2.0f;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OptionOrbitRadius;

            var option = new Button
            {
                Name = $"Option_{index}",
                Text = text,
                CustomMinimumSize = new Vector2(OptionSize, OptionSize),
                Position = offset - new Vector2(OptionSize, OptionSize) / 2.0f,
                Size = new Vector2(OptionSize, OptionSize)
            };
            option.AddThemeFontSizeOverride("font_size", 14);
            option.AddThemeColorOverride("font_color", Colors.White);

            AddChild(option);
            _options.Add(option);
            return option;
        }

        /// <summary>
        /// 清空全部选项按钮。
        /// </summary>
        private void ClearOptions()
        {
            foreach (Button option in _options)
            {
                if (IsInstanceValid(option))
                {
                    option.QueueFree();
                }
            }

            _options.Clear();
            _hoveredPreviewData = null;
            QueueRedraw();
        }

        /// <summary>
        /// 建造选项点击：关闭菜单后交给 TowerManager 执行建造事务（再次校验金币与占用）。
        /// </summary>
        /// <param name="data">选中的塔配置</param>
        private void OnBuildOptionPressed(TowerData data)
        {
            TowerSlot slot = BoundSlot;
            Close();
            GD.Print($"[TowerRadialMenu] 玩家选择建造 {data.TowerName}（{slot.Name}）。");
            TowerManager.Instance?.TryBuildTower(slot, data);
        }

        /// <summary>
        /// 升级选项点击：关闭菜单后交给塔执行升级事务。
        /// </summary>
        private void OnUpgradeOptionPressed()
        {
            Tower tower = BoundSlot?.CurrentTower;
            Close();
            tower?.ApplyUpgrade();
        }

        /// <summary>
        /// 出售选项点击：关闭菜单后交给槽位执行出售事务。
        /// </summary>
        private void OnSellOptionPressed()
        {
            TowerSlot slot = BoundSlot;
            Close();
            slot?.SellTower();
        }

        /// <summary>
        /// 设置射程预览的塔数据（选项悬停联动）并重绘。
        /// </summary>
        /// <param name="data">悬停选项的塔配置；移出时为 null</param>
        private void SetHoveredPreview(TowerData data)
        {
            _hoveredPreviewData = data;
            QueueRedraw();
        }

        /// <summary>
        /// 订阅金币变更事件，实时刷新建造/升级选项的可负担置灰状态。
        /// </summary>
        private void SubscribeGoldRefresh()
        {
            EventBus.OnGoldChanged -= RefreshAffordStates;
            EventBus.OnGoldChanged += RefreshAffordStates;
        }

        /// <summary>
        /// 按当前金币刷新各选项的禁用状态（建造环按建造费用，升级环按升级费用）。
        /// </summary>
        /// <param name="currentGold">最新金币数</param>
        private void RefreshAffordStates(int currentGold)
        {
            if (!IsOpen) return;

            var economy = Gameplay.Economy.EconomyManager.Instance;
            int gold = economy?.CurrentGold ?? currentGold;

            foreach (Button option in _options)
            {
                if (option.Disabled) continue;
                if (option.Name == "Option_Sell") continue;

                int cost = ExtractCostFromText(option.Text);
                if (cost > 0)
                {
                    option.Disabled = gold < cost;
                }
            }
        }

        /// <summary>
        /// 打开选项时按费用设置初始禁用状态。
        /// </summary>
        /// <param name="option">目标选项按钮</param>
        /// <param name="cost">所需费用</param>
        private static void RefreshAffordState(Button option, int cost)
        {
            var economy = Gameplay.Economy.EconomyManager.Instance;
            option.Disabled = economy != null && economy.CurrentGold < cost;
        }

        /// <summary>
        /// 从选项文本中提取费用数字（形如 "名称\n50G" / "⬆️ 升级\n75G"），无费用语义返回 0。
        /// </summary>
        /// <param name="text">按钮文本</param>
        /// <returns>费用数值；无法解析返回 0</returns>
        private static int ExtractCostFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int newline = text.LastIndexOf('\n');
            string lastLine = newline >= 0 ? text[(newline + 1)..] : text;
            string digits = System.Text.RegularExpressions.Regex.Match(lastLine, @"\d+").Value;

            return int.TryParse(digits, out int cost) ? cost : 0;
        }

        #endregion

        #region 占位绘制 —— 射程预览

        /// <summary>
        /// 占位绘制：建造环悬停选项时以槽位为圆心绘制该塔射程预览圈（塔色半透明填充 + 描边）。
        /// </summary>
        public override void _Draw()
        {
            if (!IsOpen || IsUpgradeMenu || _hoveredPreviewData == null)
            {
                return;
            }

            Color main = _hoveredPreviewData.AttackColor;
            DrawCircle(Vector2.Zero, _hoveredPreviewData.AttackRange, new Color(main, 0.2f));
            DrawArc(Vector2.Zero, _hoveredPreviewData.AttackRange, 0.0f, Mathf.Tau, 96, new Color(main, 0.8f), 2.5f);
        }

        #endregion
    }
}
