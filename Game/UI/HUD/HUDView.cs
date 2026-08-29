using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;
using TowerDefence.UI.Panels;

namespace TowerDefence.UI.HUD
{
    /// <summary>
    /// 局内 HUD 主界面。
    /// 负责展示玩家当前金币、剩余生命值与当前波次等核心状态信息，
    /// 并作为建塔按钮容器的挂载根节点。HUDView 新增暂停交互：
    /// 右上角 PauseButton 或玩家按 ESC 键 → 切换 PauseMenuPanel 暂停遮罩，
    /// 暂停面板提供「返回主菜单」作为关卡中途 → 主界面的闭环出口。
    /// 所有 UI 数据刷新完全通过 EventBus 订阅 OnGoldChanged / OnPlayerHpChanged / OnWaveStarted 事件驱动，
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

        /// <summary>
        /// 获取或设置 HUD 右上角的暂停按钮节点引用。
        /// 点击后调用 PauseMenuPanel.TogglePause() 切换暂停状态。
        /// </summary>
        [Export] public Button PauseButton { get; set; }

        /// <summary>
        /// 获取或设置暂停菜单面板实例引用。
        /// HUDView._Ready 时自动实例化 PauseMenuPanel.tscn 并作为子节点挂载，
        /// 也允许从 Inspector 手动传入已预挂载的实例。
        /// </summary>
        [Export] public PauseMenuPanel PauseMenu { get; set; }

        #endregion

        #region 常量 —— 暂停菜单 PackedScene 路径

        /// <summary>
        /// PauseMenuPanel 打包场景资源路径。
        /// 若 HUDView 未通过 Export 预挂载 PauseMenu，则在 _Ready 时自动实例化该场景。
        /// </summary>
        private const string PauseMenuPanelScenePath = "res://Game/Scenes/PauseMenuPanel.tscn";

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：Label/容器/暂停按钮 + 暂停菜单引用兜底解析 → 绑定 PauseButton 点击回调 →
        /// 实例化 PauseMenuPanel（如未预挂载）→ 订阅 EventBus → 读取真实初始值刷新 UI。
        /// 初始值刷新优先从 EconomyManager.Instance 直接读取（避免事件竞态导致初始显示为 0），
        /// 兜底则显示预设安全默认值（金币0 / 血量0 / 波次1）。
        /// </summary>
        public override void _Ready()
        {
            ResolveUINodeReferences();
            EnsurePauseMenuPanelReady();

            if (PauseButton != null)
            {
                PauseButton.Pressed += HandlePauseButtonPressed;
            }

            EventBus.OnGoldChanged += HandleGoldChanged;
            EventBus.OnPlayerHpChanged += HandlePlayerHpChanged;
            EventBus.OnWaveStarted += HandleWaveStarted;
            EventBus.OnGameOver += HandleGameOver;

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
        /// 取消所有 EventBus 事件订阅与 PauseButton 点击回调，防止委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            if (PauseButton != null)
            {
                PauseButton.Pressed -= HandlePauseButtonPressed;
            }

            EventBus.OnGoldChanged -= HandleGoldChanged;
            EventBus.OnPlayerHpChanged -= HandlePlayerHpChanged;
            EventBus.OnWaveStarted -= HandleWaveStarted;
            EventBus.OnGameOver -= HandleGameOver;
        }

        /// <summary>
        /// 全局未处理输入回调：检测 ESC 键按下切换暂停菜单。
        /// 使用 _UnhandledInput（而非 _Input）保证 HUD 按钮点击事件已被消费的情况下，
        /// 仍可在任意空闲时刻响应 ESC；GameOver 后面板已弹出则忽略 ESC，避免两个遮罩叠加。
        /// </summary>
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
            {
                TogglePauseMenu();
            }
        }

        #endregion

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做 GetNode 兜底。
        /// 场景中 Label/容器均挂载在 HUDView 直属的 TopBar / BottomBar 下，
        /// 暂停按钮位于 TopBar 最右侧（PauseButton），暂停菜单可能预挂载或由 EnsurePauseMenuPanelReady 懒实例化。
        /// 用相对路径即可可靠解析，不依赖 .tscn 文本中 NodePath 的序列化结果。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            GoldLabel ??= GetNodeOrNull<Label>("TopBar/GoldLabel");
            HpLabel ??= GetNodeOrNull<Label>("TopBar/HpLabel");
            WaveLabel ??= GetNodeOrNull<Label>("TopBar/WaveLabel");
            PauseButton ??= GetNodeOrNull<Button>("TopBar/PauseButton");
            BuildButtonsContainer ??= GetNodeOrNull<Control>("BottomBar/BuildButtons");

            int missing = 0;
            if (GoldLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: GoldLabel"); missing++; }
            if (HpLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: HpLabel"); missing++; }
            if (WaveLabel == null) { GD.PrintErr("[HUDView] 兜底解析失败: WaveLabel"); missing++; }
            if (PauseButton == null) { GD.PrintErr("[HUDView] 兜底解析失败: PauseButton"); missing++; }
            if (BuildButtonsContainer == null) { GD.PrintErr("[HUDView] 兜底解析失败: BuildButtonsContainer"); missing++; }

            if (missing == 0)
            {
                GD.Print("[HUDView] ✅ 5 个 UI 节点引用兜底解析全部成功。");
            }
        }

        /// <summary>
        /// 确保暂停菜单面板实例存在：
        /// - 若通过 Export 已预挂载 PauseMenu 则直接复用；
        /// - 否则从 PauseMenuPanelScenePath 实例化并 AddChild 到 HUDView 下。
        /// 这样无论 .tscn 是否手动挂载了暂停菜单，代码路径都能工作。
        /// </summary>
        private void EnsurePauseMenuPanelReady()
        {
            if (PauseMenu != null) return;

            if (!ResourceLoader.Exists(PauseMenuPanelScenePath, "PackedScene"))
            {
                GD.PrintErr($"[HUDView] PauseMenuPanel 场景不存在 → {PauseMenuPanelScenePath}，暂停功能将不可用。");
                return;
            }

            var packed = ResourceLoader.Load<PackedScene>(PauseMenuPanelScenePath);
            PauseMenu = packed?.Instantiate<PauseMenuPanel>();
            if (PauseMenu == null)
            {
                GD.PrintErr("[HUDView] PauseMenuPanel 实例化失败，暂停功能将不可用。");
                return;
            }

            AddChild(PauseMenu);
            GD.Print("[HUDView] ✅ PauseMenuPanel 已动态实例化并挂载到 HUDView 下。");
        }

        #endregion

        #region 暂停菜单切换

        /// <summary>
        /// HUD 暂停按钮或 ESC 键调用的统一暂停切换入口。
        /// 若 PauseMenu 尚未就绪则仅打印日志，不抛异常。
        /// </summary>
        private void TogglePauseMenu()
        {
            if (PauseMenu == null)
            {
                GD.Print("[HUDView] 切换暂停请求被忽略：PauseMenuPanel 尚未就绪。");
                return;
            }

            PauseMenu.TogglePause();
        }

        /// <summary>
        /// HUD 右上角暂停按钮点击回调。
        /// 转发到 TogglePauseMenu 统一入口，保持 ESC 与按钮的行为一致。
        /// </summary>
        private void HandlePauseButtonPressed()
        {
            GD.Print("[HUDView] 玩家点击 HUD 暂停按钮。");
            TogglePauseMenu();
        }

        /// <summary>
        /// 游戏结束（胜利/失败）事件回调：
        /// 强制关闭暂停面板（避免结算与暂停两个遮罩叠加），并确保 SetPaused 不再保留为 true，
        /// 为后续 GameOverPanel 的「返回主菜单」等流转出口做准备。
        /// </summary>
        private void HandleGameOver(bool isVictory)
        {
            PauseMenu?.ForceClose();
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
