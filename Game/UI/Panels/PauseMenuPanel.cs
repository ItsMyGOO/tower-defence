using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 局内暂停菜单面板。
    /// 挂载为 HUDView 的子节点，由 HUD 右上角的 PauseButton 或玩家按 ESC 键切换显隐。
    /// 提供「继续游戏」作为本面板特有出口；「重新开始本关」「返回选关」「返回主菜单」「退出游戏」
    /// 四条流转出口由基类 <see cref="UIPanelBase"/> 统一提供，
    /// 构成关卡中途 → 主界面的完整闭环：战斗中按 ESC → 暂停面板 → 🏠 返回主菜单 → 重开 Meta Loop。
    /// </summary>
    public partial class PauseMenuPanel : UIPanelBase
    {
        #region 特有 UI 节点引用

        /// <summary>
        /// 获取或设置"继续游戏"按钮节点引用。
        /// 点击后隐藏面板并将 GetTree().Paused 置为 false，回归正常战斗节奏。
        /// </summary>
        [Export] public Button ResumeButton { get; set; }

        /// <summary>
        /// 获取或设置主音量滑条节点引用。
        /// 拖动实时经 SettingsManager 应用到 Master 总线并持久化；初始值从存档恢复。
        /// </summary>
        [Export] public HSlider MasterVolumeSlider { get; set; }

        /// <summary>
        /// 音量行容器（Label + Slider），随面板显隐整体切换可见性。
        /// </summary>
        private Control _volumeRow;

        #endregion

        #region 生命周期

        /// <summary>
        /// 基类共享初始化完成后的附加初始化：
        /// 将暂停遮罩置于 Layer 5（高于 HUD，也不会遮挡结算面板），解析 ResumeButton 并绑定回调。
        /// 关键的暂停态可交互配置（ProcessMode 递归 Always）已由基类完成，
        /// 否则 GetTree().Paused = true 时所有按钮的 GUI 输入会被 Godot 跳过。
        /// ESC 键关闭暂停由本类 _UnhandledInput 接管（Paused 期间 HUDView 的 _UnhandledInput 不会触发）。
        /// </summary>
        protected override void OnPanelReady()
        {
            Layer = 5;

            if (ResumeButton != null)
            {
                ResumeButton.Pressed += HandleResumePressed;
            }

            if (MasterVolumeSlider != null)
            {
                // 初始值从存档恢复（不触发 ValueChanged），拖动时实时应用并写盘
                MasterVolumeSlider.SetValueNoSignal(SettingsManager.Instance?.MasterVolume * 100.0f ?? 80.0f);
                MasterVolumeSlider.ValueChanged += HandleMasterVolumeChanged;
            }

            GD.Print("[PauseMenuPanel] ✅ 暂停菜单已就绪，玩家按 ESC 或点击 HUD 暂停按钮可显隐。");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 先解绑"继续游戏"按钮与音量滑条，再交由基类解绑共享按钮。
        /// </summary>
        public override void _ExitTree()
        {
            if (ResumeButton != null)
            {
                ResumeButton.Pressed -= HandleResumePressed;
            }

            if (MasterVolumeSlider != null)
            {
                MasterVolumeSlider.ValueChanged -= HandleMasterVolumeChanged;
            }

            base._ExitTree();
        }

        /// <summary>
        /// 全局未处理输入回调：暂停期间监听 ESC 键，关闭暂停菜单并恢复时钟。
        /// Paused = true 时 HUDView（默认 ProcessMode = Inherit）的 _UnhandledInput 会被 Godot 跳过，
        /// 所以暂停期间 ESC 关闭必须由 ProcessMode = Always 的 PauseMenuPanel 自身负责，
        /// 否则会出现「打开暂停后按 ESC 没反应，玩家只能点继续按钮才能恢复」的死锁。
        /// </summary>
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
            {
                if (Visible)
                {
                    GD.Print("[PauseMenuPanel] ⏸️ 玩家按 ESC 关闭暂停菜单。");
                    HidePanelAndResume();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        #endregion

        #region 节点引用解析

        /// <summary>
        /// 解析本面板特有的 ResumeButton 与音量滑条引用。
        /// </summary>
        protected override void ResolveExtraUINodeReferences()
        {
            ResumeButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/ResumeButton");
            CheckResolved(ResumeButton, nameof(ResumeButton));

            MasterVolumeSlider ??= GetNodeOrNull<HSlider>("CenterContainer/VBox/VolumeRow/MasterVolumeSlider");
            CheckResolved(MasterVolumeSlider, nameof(MasterVolumeSlider));

            _volumeRow ??= GetNodeOrNull<Control>("CenterContainer/VBox/VolumeRow");
        }

        /// <summary>
        /// 隐藏面板：除共享四按钮外，同步隐藏"继续游戏"按钮与音量行。
        /// </summary>
        protected override void HideExtraNodes()
        {
            SetButtonVisible(ResumeButton, false);

            if (_volumeRow != null)
            {
                _volumeRow.Visible = false;
            }
        }

        #endregion

        #region 公共 API —— 暂停切换

        /// <summary>
        /// 切换暂停菜单显示状态：
        /// - 隐藏中 → 显示面板并 Paused = true，冻结局内时钟；
        /// - 显示中 → 隐藏面板并 Paused = false，解冻战斗。
        /// HUD 的 PauseButton 与 ESC 键均通过此统一入口切换，避免出现"面板隐藏但时钟仍暂停"的错位。
        /// </summary>
        public void TogglePause()
        {
            if (Visible)
            {
                HidePanelAndResume();
            }
            else
            {
                ShowPanelAndPause();
            }
        }

        /// <summary>
        /// 显式关闭暂停菜单并恢复时钟（GameOver 触发时由 HUDView 调用，避免结算后还残留暂停遮罩）。
        /// </summary>
        public void ForceClose()
        {
            if (Visible)
            {
                HidePanelAndResume();
            }
        }

        #endregion

        #region 按钮事件处理

        /// <summary>
        /// 处理"继续游戏"按钮点击事件。
        /// 隐藏面板并解冻时钟，等效于再次按 ESC。
        /// </summary>
        private void HandleResumePressed()
        {
            GD.Print("[PauseMenuPanel] 玩家点击「继续游戏」。");
            HidePanelAndResume();
        }

        /// <summary>
        /// 处理主音量滑条值变更事件。
        /// 将 0-100 滑条值换算为线性音量交给 SettingsManager（应用总线 + 写盘）。
        /// </summary>
        /// <param name="value">滑条当前值（0-100）</param>
        private void HandleMasterVolumeChanged(double value)
        {
            SettingsManager.Instance?.SetMasterVolume((float)value / 100.0f);
        }

        #endregion

        #region 面板显隐控制（内部）

        /// <summary>
        /// 显示暂停遮罩、音量行并设置全局暂停时钟。
        /// </summary>
        private void ShowPanelAndPause()
        {
            ShowPanel();
            SetButtonVisible(ResumeButton, true);

            if (_volumeRow != null)
            {
                _volumeRow.Visible = true;
            }

            GetTree().Paused = true;
            GD.Print("[PauseMenuPanel] ⏸️ 暂停菜单开启（Paused = true）");
        }

        /// <summary>
        /// 隐藏暂停遮罩并解除全局暂停时钟。
        /// </summary>
        private void HidePanelAndResume()
        {
            Visible = false;
            GetTree().Paused = false;
            HidePanel();
            GD.Print("[PauseMenuPanel] ▶️ 暂停菜单关闭（Paused = false）");
        }

        #endregion
    }
}
