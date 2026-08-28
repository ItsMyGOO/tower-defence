using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.UI.HUD
{
    /// <summary>
    /// 局内 HUD 主界面。
    /// 负责展示玩家当前金币、剩余生命值与当前波次等核心状态信息，
    /// 并作为建塔按钮容器的挂载根节点。所有 UI 数据刷新完全通过 EventBus
    /// 订阅 OnGoldChanged / OnPlayerHpChanged / OnWaveStarted 事件驱动，
    /// 禁止直接引用 Gameplay 层的 Manager 节点，保持 UI 与业务逻辑的完全解耦。
    /// </summary>
    public partial class HUDView : CanvasLayer
    {
        #region UI 节点引用

        /// <summary>
        /// 获取或设置金币显示 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的 Label 节点，用于实时显示玩家金币数。
        /// </summary>
        [Export] public Label GoldLabel { get; set; }

        /// <summary>
        /// 获取或设置生命值显示 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的 Label 节点，用于实时显示玩家剩余 HP。
        /// </summary>
        [Export] public Label HpLabel { get; set; }

        /// <summary>
        /// 获取或设置当前波次显示 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的 Label 节点，用于显示当前进行中的波次索引。
        /// </summary>
        [Export] public Label WaveLabel { get; set; }

        /// <summary>
        /// 获取或设置建塔按钮容器节点引用。
        /// Inspector 中绑定到场景树内的 Control/Container 节点（如 HBoxContainer），
        /// 用于在 Inspector 或运行时组织多个 TowerBuildButton 子节点。
        /// </summary>
        [Export] public Control BuildButtonsContainer { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：Label/容器引用兜底解析 → 订阅 EventBus → 读取真实初始值刷新 UI。
        /// 初始值刷新优先从 EconomyManager.Instance 直接读取（避免事件竞态导致初始显示为 0），
        /// 兜底则显示预设安全默认值（金币0 / 血量0 / 波次1）。
        /// </summary>
        public override void _Ready()
        {
            ResolveUINodeReferences();

            EventBus.OnGoldChanged += HandleGoldChanged;
            EventBus.OnPlayerHpChanged += HandlePlayerHpChanged;
            EventBus.OnWaveStarted += HandleWaveStarted;

            int initialGold = EconomyManager.Instance?.CurrentGold ?? 0;
            int initialHp = EconomyManager.Instance?.CurrentHp ?? 0;
            int initialWave = 0;

            RefreshGoldLabel(initialGold);
            RefreshHpLabel(initialHp);
            RefreshWaveLabel(initialWave);

            GD.Print($"[HUDView] 初始值刷新 → 金币:{initialGold}  血量:{initialHp}  波次:{initialWave + 1}");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有 EventBus 事件订阅，防止委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnGoldChanged -= HandleGoldChanged;
            EventBus.OnPlayerHpChanged -= HandlePlayerHpChanged;
            EventBus.OnWaveStarted -= HandleWaveStarted;
        }

        #endregion

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做 GetNode 兜底。
        /// 场景中 Label/容器均挂载在 HUDView 直属的 TopBar / BottomBar 下，
        /// 用相对路径即可可靠解析，不依赖 .tscn 文本中 NodePath 的序列化结果。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            GoldLabel ??= GetNodeOrNull<Label>("TopBar/GoldLabel");
            HpLabel ??= GetNodeOrNull<Label>("TopBar/HpLabel");
            WaveLabel ??= GetNodeOrNull<Label>("TopBar/WaveLabel");
            BuildButtonsContainer ??= GetNodeOrNull<Control>("BottomBar/BuildButtons");

            int missing = 0;
            if (GoldLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: GoldLabel"); missing++; }
            if (HpLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: HpLabel"); missing++; }
            if (WaveLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: WaveLabel"); missing++; }
            if (BuildButtonsContainer == null) { GD.PrintErr("[HUDView] 兜底解析失败: BuildButtonsContainer"); missing++; }

            if (missing == 0)
            {
                GD.Print("[HUDView] ✅ 4 个 UI 节点引用兜底解析全部成功。");
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理金币变更事件。
        /// 以 "金币: {数值}" 格式刷新 GoldLabel 的显示文本。
        /// </summary>
        /// <param name="newGold">更新后的金币总数</param>
        private void HandleGoldChanged(int newGold)
        {
            RefreshGoldLabel(newGold);
        }

        /// <summary>
        /// 处理玩家生命值变更事件。
        /// 以 "血量: {数值}" 格式刷新 HpLabel 的显示文本。
        /// </summary>
        /// <param name="newHp">更新后的生命值</param>
        private void HandlePlayerHpChanged(int newHp)
        {
            RefreshHpLabel(newHp);
        }

        /// <summary>
        /// 处理波次开始事件。
        /// 以 "波次: {索引}" 格式刷新 WaveLabel 的显示文本（索引从 1 开始显示）。
        /// </summary>
        /// <param name="waveIndex">当前波次的索引（从 0 开始传入）</param>
        private void HandleWaveStarted(int waveIndex)
        {
            RefreshWaveLabel(waveIndex);
        }

        #endregion

        #region UI 刷新方法

        /// <summary>
        /// 刷新金币 Label 的显示文本。
        /// 若 GoldLabel 未绑定则静默跳过，避免空引用异常。
        /// </summary>
        /// <param name="gold">当前金币数</param>
        private void RefreshGoldLabel(int gold)
        {
            if (GoldLabel != null)
            {
                GoldLabel.Text = $"金币: {gold}";
            }
        }

        /// <summary>
        /// 刷新生命值 Label 的显示文本。
        /// 若 HpLabel 未绑定则静默跳过，避免空引用异常。
        /// </summary>
        /// <param name="hp">当前生命值</param>
        private void RefreshHpLabel(int hp)
        {
            if (HpLabel != null)
            {
                HpLabel.Text = $"血量: {hp}";
            }
        }

        /// <summary>
        /// 刷新波次 Label 的显示文本。
        /// 传入索引从 0 开始，显示时 +1 转化为玩家友好的 1-based 格式。
        /// </summary>
        /// <param name="waveIndex">当前波次索引（0-based）</param>
        private void RefreshWaveLabel(int waveIndex)
        {
            if (WaveLabel != null)
            {
                WaveLabel.Text = $"波次: {waveIndex + 1}";
            }
        }

        #endregion
    }
}
