using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 局内暂停菜单面板。
    /// 挂载为 HUDView 的子节点，由 HUD 右上角的 PauseButton 或玩家按 ESC 键切换显隐。
    /// 提供「继续游戏」「重新开始本关」「返回选关」「返回主菜单」「退出游戏」5 条流转出口，
    /// 构成关卡中途 → 主界面的完整闭环：玩家在战斗中随时按 ESC → 打开暂停面板 → 点击 🏠 返回主菜单
    /// → SceneManager.LoadMainMenu() 重置 CurrentLevelIndex 为 0 并切回主菜单，完成 Meta Loop 的反向出口。
    /// </summary>
    public partial class PauseMenuPanel : CanvasLayer
    {
        #region UI 节点引用

        /// <summary>
        /// 获取或设置"继续游戏"按钮节点引用。
        /// 点击后将 GetTree().Paused 置为 false 并隐藏面板，回归正常战斗节奏。
        /// </summary>
        [Export] public Button ResumeButton { get; set; }

        /// <summary>
        /// 获取或设置"重新开始本关"按钮节点引用。
        /// 点击后先取消暂停再 ReloadCurrentScene，实现一局关卡的完整重置。
        /// </summary>
        [Export] public Button RestartButton { get; set; }

        /// <summary>
        /// 获取或设置"返回选关"按钮节点引用。
        /// 点击后取消暂停并调用 SceneManager.LoadLevelSelect() 回到选关界面。
        /// </summary>
        [Export] public Button LevelSelectButton { get; set; }

        /// <summary>
        /// 获取或设置"返回主菜单"按钮节点引用。
        /// 点击后取消暂停并调用 SceneManager.LoadMainMenu() 回到 Meta Loop 起点，
        /// 这是关卡中途 → 主界面闭环的核心入口，保证玩家无需通关或战败也能回到主菜单。
        /// </summary>
        [Export] public Button MainMenuButton { get; set; }

        /// <summary>
        /// 获取或设置"退出游戏"按钮节点引用。
        /// 编辑器模式下仅打印提示，打包版本调用 GetTree().Quit() 退出应用。
        /// </summary>
        [Export] public Button QuitButton { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化面板为隐藏状态，绑定 5 个按钮的点击回调，并在暂停菜单 Layer 1
        ///（避免被 HUD CanvasLayer 遮挡，也不会遮挡 GameOverPanel 结算）。
        /// 关键点：立即将自身 ProcessMode 设置为 Always，并递归所有子节点同样设为 Always；
        /// 因为 GetTree().Paused=true 时默认 ProcessMode=Inherit 的节点 GUI 输入与信号会被跳过，
        /// 不提前设置会导致暂停菜单打开后「全部按钮点不动」的现象。
        /// ESC 键关闭暂停也直接由此类 _UnhandledInput 接管（Paused 期间 HUDView._UnhandledInput 不会触发）。
        /// </summary>
        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            SetDescendantsProcessModeAlways(this);
            Layer = 5;
            ResolveUINodeReferences();
            HidePanel();

            if (ResumeButton != null)
            {
                ResumeButton.Pressed += HandleResumePressed;
            }

            if (RestartButton != null)
            {
                RestartButton.Pressed += HandleRestartPressed;
            }

            if (LevelSelectButton != null)
            {
                LevelSelectButton.Pressed += HandleLevelSelectPressed;
            }

            if (MainMenuButton != null)
            {
                MainMenuButton.Pressed += HandleMainMenuPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed += HandleQuitPressed;
            }

            GD.Print("[PauseMenuPanel] ✅ 暂停菜单已就绪，玩家按 ESC 或点击 HUD 暂停按钮可显隐。");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有按钮点击事件绑定，防止委托悬空。
        /// </summary>
        public override void _ExitTree()
        {
            if (ResumeButton != null)
            {
                ResumeButton.Pressed -= HandleResumePressed;
            }

            if (RestartButton != null)
            {
                RestartButton.Pressed -= HandleRestartPressed;
            }

            if (LevelSelectButton != null)
            {
                LevelSelectButton.Pressed -= HandleLevelSelectPressed;
            }

            if (MainMenuButton != null)
            {
                MainMenuButton.Pressed -= HandleMainMenuPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed -= HandleQuitPressed;
            }
        }

        /// <summary>
        /// 全局未处理输入回调：暂停期间监听 ESC 键，释放后关闭暂停菜单并恢复时钟。
        /// Paused=true 时 HUDView（默认 ProcessMode=Inherit）的 _UnhandledInput 会被 Godot 跳过，
        /// 所以暂停期间 ESC 关闭必须由 ProcessMode=Always 的 PauseMenuPanel 自身负责，
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

        #region 公共 API —— 暂停切换

        /// <summary>
        /// 切换暂停菜单显示状态：
        /// - 隐藏中 → 显示面板并 SetPaused(true)，冻结局内时钟；
        /// - 显示中 → 隐藏面板并 SetPaused(false)，解冻战斗。
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
        /// 显式关闭暂停菜单并恢复时钟（GameOver 触发时从 Level 根节点调用，避免结算后还残留暂停遮罩）。
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
        /// 处理"重新开始本关"按钮点击事件。
        /// 先解冻时钟再 ReloadCurrentScene，防止下一局开局时钟保持 Paused=true 的死锁。
        /// </summary>
        private void HandleRestartPressed()
        {
            GD.Print("[PauseMenuPanel] 玩家点击「重新开始本关」。");
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }

        /// <summary>
        /// 处理"返回选关"按钮点击事件。
        /// 解冻时钟后调用 SceneManager.LoadLevelSelect()。
        /// </summary>
        private void HandleLevelSelectPressed()
        {
            GD.Print("[PauseMenuPanel] 玩家点击「返回选关」。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadLevelSelect();
        }

        /// <summary>
        /// 处理"返回主菜单"按钮点击事件。
        /// 解冻时钟后调用 SceneManager.LoadMainMenu()，将 CurrentLevelIndex 重置为 0 并切回主菜单场景，
        /// 完成关卡中途 → 主界面的 Meta Loop 闭环出口。
        /// </summary>
        private void HandleMainMenuPressed()
        {
            GD.Print("[PauseMenuPanel] 玩家点击「返回主菜单」（关卡中途出口），CurrentLevelIndex 将重置为 0。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadMainMenu();
        }

        /// <summary>
        /// 处理"退出游戏"按钮点击事件。
        /// 编辑器模式仅打印日志，打包模式调用 Quit。
        /// </summary>
        private void HandleQuitPressed()
        {
            GD.Print("[PauseMenuPanel] 玩家点击「退出游戏」。");
            GetTree().Paused = false;

            if (OS.HasFeature("editor"))
            {
                GD.Print("[PauseMenuPanel] 编辑器模式下已请求退出游戏（实际在打包版会退出应用）。");
            }
            else
            {
                GetTree().Quit();
            }
        }

        #endregion

        #region 面板显隐控制（内部）

        /// <summary>
        /// 显示暂停遮罩并设置全局暂停时钟。
        /// </summary>
        private void ShowPanelAndPause()
        {
            Visible = true;
            GetTree().Paused = true;
            if (ResumeButton != null) ResumeButton.Visible = true;
            if (RestartButton != null) RestartButton.Visible = true;
            if (LevelSelectButton != null) LevelSelectButton.Visible = true;
            if (MainMenuButton != null) MainMenuButton.Visible = true;
            if (QuitButton != null) QuitButton.Visible = true;
            GD.Print("[PauseMenuPanel] ⏸️ 暂停菜单开启（SetPaused = true）");
        }

        /// <summary>
        /// 隐藏暂停遮罩并解除全局暂停时钟。
        /// </summary>
        private void HidePanelAndResume()
        {
            Visible = false;
            GetTree().Paused = false;
            HidePanel();
            GD.Print("[PauseMenuPanel] ▶️ 暂停菜单关闭（SetPaused = false）");
        }

        /// <summary>
        /// 隐藏面板所有子元素（仅做子节点 Visible 清理，不触碰 Paused 状态）。
        /// </summary>
        private void HidePanel()
        {
            Visible = false;
            if (ResumeButton != null) ResumeButton.Visible = false;
            if (RestartButton != null) RestartButton.Visible = false;
            if (LevelSelectButton != null) LevelSelectButton.Visible = false;
            if (MainMenuButton != null) MainMenuButton.Visible = false;
            if (QuitButton != null) QuitButton.Visible = false;
        }

        #endregion

        #region 内部辅助 —— UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做 GetNodeOrNull 兜底。
        /// Godot 4.x 的 C# [Export] 属性在 .tscn 文本中以 PascalCase 直接赋值 NodePath 时，
        /// 脚本桥接层偶尔会因字段名序列化不一致而读到 null（表现为按钮有交互但点击逻辑不执行）。
        /// 兜底用相对路径解析，保证即使 .tscn 的 NodePath 没映射上也能 100% 拿到引用。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            ResumeButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/ResumeButton");
            RestartButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/RestartButton");
            LevelSelectButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/LevelSelectButton");
            MainMenuButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/MainMenuButton");
            QuitButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/QuitButton");

            int missing = 0;
            if (ResumeButton == null) { GD.PrintErr("[PauseMenuPanel] 兜底解析失败: ResumeButton"); missing++; }
            if (RestartButton == null) { GD.PrintErr("[PauseMenuPanel] 兜底解析失败: RestartButton"); missing++; }
            if (LevelSelectButton == null) { GD.PrintErr("[PauseMenuPanel] 兜底解析失败: LevelSelectButton"); missing++; }
            if (MainMenuButton == null) { GD.PrintErr("[PauseMenuPanel] 兜底解析失败: MainMenuButton"); missing++; }
            if (QuitButton == null) { GD.PrintErr("[PauseMenuPanel] 兜底解析失败: QuitButton"); missing++; }

            if (missing == 0)
            {
                GD.Print("[PauseMenuPanel] ✅ 5 个按钮引用兜底解析全部成功，Pressed 回调已可绑定。");
            }
        }

        #endregion

        #region 内部辅助 —— ProcessMode 递归设置

        /// <summary>
        /// 递归将 root 节点及所有子孙的 ProcessMode 设置为 Always。
        /// 暂停菜单 / 结算面板在 GetTree().Paused=true 时必须保持 GUI 输入事件正常分发，
        /// 否则所有 Button 的 _GuiInput 与 Pressed 信号都不会触发，表现为「按钮点了没反应」。
        /// 仅设置 CanvasLayer 根节点 ProcessMode=Always 不够：
        /// Godot 要求每个接收 GUI 输入的 Control 子节点自身 ProcessMode 也要为 Always（或 WhenPaused），
        /// 否则 Viewport 的 GUI 事件阶段不会派发到对应 Control。
        /// </summary>
        /// <param name="root">遍历起点（含自身）</param>
        private void SetDescendantsProcessModeAlways(Node root)
        {
            if (root == null) return;
            root.ProcessMode = ProcessModeEnum.Always;
            int count = root.GetChildCount();
            for (int i = 0; i < count; i++)
            {
                SetDescendantsProcessModeAlways(root.GetChild(i));
            }
        }

        #endregion
    }
}
