using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 暂停菜单与结算面板的公共基类。
    /// 两类面板都要在 GetTree().Paused = true 的全局暂停态下保持 GUI 可交互，
    /// 且共享「重新开始本关 / 返回选关 / 返回主菜单 / 退出游戏」四条场景流转出口，
    /// 故将 ProcessMode 配置、节点引用兜底解析与公共导航回调上收至此处，子类仅保留差异逻辑。
    /// 注意：共享按钮 Export 声明在基类上，但属性名与历史 .tscn 保持一致，NodePath 绑定不受影响。
    /// </summary>
    public abstract partial class UIPanelBase : CanvasLayer
    {
        #region 共享 UI 节点引用

        /// <summary>
        /// 获取或设置"重新开始本关"按钮节点引用。
        /// 点击后取消全局暂停并 ReloadCurrentScene，实现一局关卡的完整重置。
        /// </summary>
        [Export] public Button RestartButton { get; set; }

        /// <summary>
        /// 获取或设置"返回选关"按钮节点引用。
        /// 点击后取消全局暂停并通过 SceneManager.LoadLevelSelect() 回到选关界面。
        /// </summary>
        [Export] public Button LevelSelectButton { get; set; }

        /// <summary>
        /// 获取或设置"返回主菜单"按钮节点引用。
        /// 点击后取消全局暂停并通过 SceneManager.LoadMainMenu() 回到 Meta Loop 起点。
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
        /// 依次执行：暂停态可交互配置 → UI 节点引用兜底解析（基类共享 + 子类扩展）→ 隐藏面板 →
        /// 绑定共享按钮回调 → <see cref="OnPanelReady"/> 子类附加初始化。
        /// </summary>
        public override void _Ready()
        {
            ConfigurePauseProofProcessMode();
            ResolveUINodeReferences();
            HidePanel();
            BindSharedButtons();
            OnPanelReady();
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 解绑共享按钮回调；子类通过 override 补充自身的事件/按钮解绑。
        /// </summary>
        public override void _ExitTree()
        {
            if (RestartButton != null) RestartButton.Pressed -= HandleRestartPressed;
            if (LevelSelectButton != null) LevelSelectButton.Pressed -= HandleLevelSelectPressed;
            if (MainMenuButton != null) MainMenuButton.Pressed -= HandleMainMenuPressed;
            if (QuitButton != null) QuitButton.Pressed -= HandleQuitPressed;
        }

        #endregion

        #region 子类扩展点

        /// <summary>
        /// 子类附加初始化钩子：绑定自身特有按钮、订阅 EventBus 等。
        /// 在基类共享初始化（ProcessMode / 引用解析 / 显隐 / 共享按钮绑定）完成后调用。
        /// </summary>
        protected virtual void OnPanelReady()
        {
        }

        /// <summary>
        /// 子类特有 UI 节点引用的兜底解析钩子（如 TitleLabel、ResumeButton 等）。
        /// 在基类共享引用解析完成后调用，解析不到时用 <see cref="CheckResolved"/> 上报计数。
        /// </summary>
        protected virtual void ResolveExtraUINodeReferences()
        {
        }

        /// <summary>
        /// 隐藏面板时对子类特有节点的额外显隐清理（共享四按钮由基类统一处理）。
        /// </summary>
        protected virtual void HideExtraNodes()
        {
        }

        #endregion

        #region 暂停态可交互配置

        /// <summary>
        /// 递归将自身与所有子孙节点 ProcessMode 设为 Always。
        /// GetTree().Paused = true 时，默认 ProcessMode = Inherit 的 Control 节点不会收到
        /// _GuiInput 与 Pressed 信号，表现为「暂停后按钮点了没反应」；因此面板整棵子树必须 Always。
        /// </summary>
        protected void ConfigurePauseProofProcessMode()
        {
            ProcessMode = ProcessModeEnum.Always;
            SetDescendantsProcessModeAlways(this);
        }

        /// <summary>
        /// 递归将 root 节点及所有子孙的 ProcessMode 设置为 Always。
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

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为基类共享按钮做 GetNodeOrNull 兜底，随后调用 <see cref="ResolveExtraUINodeReferences"/>。
        /// 面板结构统一为 CenterContainer/VBox/&lt;按钮名&gt;，相对路径即可可靠解析，
        /// 不依赖 .tscn 文本中 NodePath 的序列化结果。
        /// </summary>
        protected virtual void ResolveUINodeReferences()
        {
            RestartButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/RestartButton");
            LevelSelectButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/LevelSelectButton");
            MainMenuButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/MainMenuButton");
            QuitButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/QuitButton");

            ResolveExtraUINodeReferences();

            int missing = 0;
            missing += CheckResolved(RestartButton, nameof(RestartButton));
            missing += CheckResolved(LevelSelectButton, nameof(LevelSelectButton));
            missing += CheckResolved(MainMenuButton, nameof(MainMenuButton));
            missing += CheckResolved(QuitButton, nameof(QuitButton));

            if (missing == 0)
            {
                GD.Print($"[{GetType().Name}] ✅ 共享按钮引用兜底解析全部成功。");
            }
        }

        /// <summary>
        /// 检查节点引用是否解析成功，失败时打印错误日志并计 1 个缺失。
        /// </summary>
        /// <param name="node">待检查的节点引用</param>
        /// <param name="nodeName">节点属性名（用于日志定位）</param>
        /// <returns>解析成功返回 0，失败返回 1</returns>
        protected int CheckResolved(Node node, string nodeName)
        {
            if (node != null) return 0;

            GD.PrintErr($"[{GetType().Name}] 兜底解析失败: {nodeName}");
            return 1;
        }

        #endregion

        #region 共享按钮绑定与导航回调

        /// <summary>
        /// 绑定四个共享导航按钮的 Pressed 回调。
        /// </summary>
        private void BindSharedButtons()
        {
            if (RestartButton != null) RestartButton.Pressed += HandleRestartPressed;
            if (LevelSelectButton != null) LevelSelectButton.Pressed += HandleLevelSelectPressed;
            if (MainMenuButton != null) MainMenuButton.Pressed += HandleMainMenuPressed;
            if (QuitButton != null) QuitButton.Pressed += HandleQuitPressed;
        }

        /// <summary>
        /// 处理"重新开始本关"按钮点击事件。
        /// 先显式解除全局暂停，再 ReloadCurrentScene，防止下一局开局时钟保持 Paused = true 的死锁。
        /// </summary>
        private void HandleRestartPressed()
        {
            GD.Print($"[{GetType().Name}] 玩家点击「重新开始本关」。");
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }

        /// <summary>
        /// 处理"返回选关"按钮点击事件。
        /// 解除全局暂停后通过 SceneManager.LoadLevelSelect() 切入选关界面。
        /// </summary>
        private void HandleLevelSelectPressed()
        {
            GD.Print($"[{GetType().Name}] 玩家点击「返回选关」。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadLevelSelect();
        }

        /// <summary>
        /// 处理"返回主菜单"按钮点击事件。
        /// 解除全局暂停后通过 SceneManager.LoadMainMenu() 回到主菜单并重置 CurrentLevelIndex。
        /// </summary>
        private void HandleMainMenuPressed()
        {
            GD.Print($"[{GetType().Name}] 玩家点击「返回主菜单」，将重置 CurrentLevelIndex = 0 并切回主界面。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadMainMenu();
        }

        /// <summary>
        /// 处理"退出游戏"按钮点击事件。
        /// 编辑器环境中打印提示，打包环境中调用 Quit 退出应用程序。
        /// </summary>
        private void HandleQuitPressed()
        {
            GD.Print($"[{GetType().Name}] 玩家点击「退出游戏」。");
            GetTree().Paused = false;

            if (OS.HasFeature("editor"))
            {
                GD.Print($"[{GetType().Name}] 编辑器模式下已请求退出游戏（实际在打包版会退出应用）。");
            }
            else
            {
                GetTree().Quit();
            }
        }

        #endregion

        #region 面板显隐辅助

        /// <summary>
        /// 隐藏面板：CanvasLayer 与共享四按钮同时隐藏，避免节点残留导致误点击。
        /// 子类通过 override 并调用 base.HidePanel() 补充特有节点的隐藏。
        /// </summary>
        protected virtual void HidePanel()
        {
            Visible = false;
            SetButtonVisible(RestartButton, false);
            SetButtonVisible(LevelSelectButton, false);
            SetButtonVisible(MainMenuButton, false);
            SetButtonVisible(QuitButton, false);
            HideExtraNodes();
        }

        /// <summary>
        /// 将面板置于可见状态并显示共享四按钮；子类自行控制特有节点。
        /// </summary>
        protected void ShowPanel()
        {
            Visible = true;
            SetButtonVisible(RestartButton, true);
            SetButtonVisible(LevelSelectButton, true);
            SetButtonVisible(MainMenuButton, true);
            SetButtonVisible(QuitButton, true);
        }

        /// <summary>
        /// 设置按钮可见性的空安全辅助方法。
        /// </summary>
        /// <param name="button">目标按钮，允许为 null</param>
        /// <param name="visible">目标可见状态</param>
        protected static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.Visible = visible;
            }
        }

        #endregion
    }
}
