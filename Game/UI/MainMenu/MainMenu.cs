using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.MainMenu
{
    /// <summary>
    /// 主菜单场景控制器。
    /// 挂载到 MainMenu.tscn 根节点，负责连接游戏标题、开始按钮、退出按钮的用户交互，
    /// 点击"开始游戏"时通过 SceneManager 单例切入选关界面，
    /// 点击"退出游戏"时根据运行环境打印提示或调用 GetTree().Quit() 退出应用。
    /// </summary>
    public partial class MainMenu : Control
    {
        #region UI 节点引用

        /// <summary>
        /// 获取或设置游戏标题 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的标题 Label，用于显示游戏名称。
        /// </summary>
        [Export] public Label TitleLabel { get; set; }

        /// <summary>
        /// 获取或设置"开始游戏"按钮节点引用。
        /// 点击后通过 SceneManager 进入选关界面。
        /// </summary>
        [Export] public Button StartButton { get; set; }

        /// <summary>
        /// 获取或设置"退出游戏"按钮节点引用。
        /// 点击后在编辑器中打印提示，在打包版中退出应用。
        /// </summary>
        [Export] public Button QuitButton { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：UI 节点引用兜底解析 → 绑定按钮点击回调 → 打印加载日志。
        /// </summary>
        public override void _Ready()
        {
            ResolveUINodeReferences();

            if (StartButton != null)
            {
                StartButton.Pressed += HandleStartPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed += HandleQuitPressed;
            }

            GD.Print("[MainMenu] ✅ 主菜单加载完成。");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有按钮点击事件绑定，防止委托悬空。
        /// </summary>
        public override void _ExitTree()
        {
            if (StartButton != null)
            {
                StartButton.Pressed -= HandleStartPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed -= HandleQuitPressed;
            }
        }

        #endregion

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做相对路径兜底赋值。
        /// 当 .tscn 文本序列化的 NodePath 在不同 Godot 版本解析差异导致绑定时，
        /// 只要保持节点树层级不变，即可通过 GetNode 可靠获取引用。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            TitleLabel ??= GetNodeOrNull<Label>("CenterContainer/VBoxContainer/TitleLabel");
            StartButton ??= GetNodeOrNull<Button>("CenterContainer/VBoxContainer/StartButton");
            QuitButton ??= GetNodeOrNull<Button>("CenterContainer/VBoxContainer/QuitButton");

            int missing = 0;
            if (TitleLabel == null) { GD.PrintErr("[MainMenu] 兜底解析失败: TitleLabel"); missing++; }
            if (StartButton == null) { GD.PrintErr("[MainMenu] 兜底解析失败: StartButton"); missing++; }
            if (QuitButton == null) { GD.PrintErr("[MainMenu] 兜底解析失败: QuitButton"); missing++; }

            if (missing == 0)
            {
                GD.Print("[MainMenu] ✅ 3 个 UI 节点引用兜底解析全部成功。");
            }
        }

        #endregion

        #region 按钮事件处理

        /// <summary>
        /// 处理"开始游戏"按钮点击事件。
        /// 通过 SceneManager 单例载入选关界面。
        /// </summary>
        private void HandleStartPressed()
        {
            GD.Print("[MainMenu] 玩家点击「开始游戏」，进入选关界面...");
            SceneManager.Instance?.LoadLevelSelect();
        }

        /// <summary>
        /// 处理"退出游戏"按钮点击事件。
        /// 编辑器模式下打印提示（避免误退出 IDE），打包模式下直接调用 Quit 退出应用程序。
        /// </summary>
        private void HandleQuitPressed()
        {
            GD.Print("[MainMenu] 玩家点击「退出游戏」。");

            if (OS.HasFeature("editor"))
            {
                GD.Print("[MainMenu] 编辑器模式下已请求退出游戏（实际在打包版会退出应用）。");
            }
            else
            {
                GetTree().Quit();
            }
        }

        #endregion
    }
}
