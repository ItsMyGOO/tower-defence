using Godot;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 胜负结算 UI 面板。
    /// 监听 EventBus.OnGameOver 事件，在游戏结束时显示胜利或战败信息，
    /// 并提供重新开始与退出（主菜单）两个操作按钮。
    /// 面板默认隐藏，收到游戏结束事件后自动可见并置顶。
    /// </summary>
    public partial class GameOverPanel : CanvasLayer
    {
        #region UI 节点引用

        /// <summary>
        /// 获取或设置胜负结果标题 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的 Label 节点，用于显示"胜利!"或"战败!"文本。
        /// </summary>
        [Export] public Label TitleLabel { get; set; }

        /// <summary>
        /// 获取或设置重新开始按钮节点引用。
        /// Inspector 中绑定到场景树内对应的 Button 节点，点击后重置游戏并重新加载当前场景。
        /// </summary>
        [Export] public Button RestartButton { get; set; }

        /// <summary>
        /// 获取或设置退出/主菜单按钮节点引用。
        /// Inspector 中绑定到场景树内对应的 Button 节点，点击后退出游戏或返回主菜单。
        /// </summary>
        [Export] public Button QuitButton { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化面板为隐藏状态，订阅 EventBus.OnGameOver 事件，并绑定按钮点击回调。
        /// </summary>
        public override void _Ready()
        {
            HidePanel();

            EventBus.OnGameOver += HandleGameOver;

            if (RestartButton != null)
            {
                RestartButton.Pressed += HandleRestartPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed += HandleQuitPressed;
            }
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消 EventBus 订阅与按钮点击事件绑定，防止委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnGameOver -= HandleGameOver;

            if (RestartButton != null)
            {
                RestartButton.Pressed -= HandleRestartPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed -= HandleQuitPressed;
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理游戏结束事件。
        /// 根据 isVictory 参数设置标题文本，并显示结算面板。
        /// </summary>
        /// <param name="isVictory">true 表示玩家胜利，false 表示玩家失败</param>
        private void HandleGameOver(bool isVictory)
        {
            if (TitleLabel != null)
            {
                TitleLabel.Text = isVictory ? "胜利!" : "战败!";
            }

            ShowPanel();
        }

        /// <summary>
        /// 处理重新开始按钮点击事件。
        /// 恢复游戏暂停状态并重新加载当前场景，实现一局游戏的完整重置。
        /// </summary>
        private void HandleRestartPressed()
        {
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }

        /// <summary>
        /// 处理退出按钮点击事件。
        /// 在编辑器环境中打印提示，在打包环境中调用 Quit 退出应用程序。
        /// 若后续接入主菜单场景，可在此处切换至主菜单场景。
        /// </summary>
        private void HandleQuitPressed()
        {
            GetTree().Paused = false;

            if (OS.HasFeature("editor"))
            {
                GD.Print("[GameOverPanel] 编辑器模式下已请求退出游戏（实际在打包版会退出应用）。");
            }
            else
            {
                GetTree().Quit();
            }
        }

        #endregion

        #region 面板显隐控制

        /// <summary>
        /// 显示结算面板并将其置于 UI 最顶层。
        /// </summary>
        private void ShowPanel()
        {
            Visible = true;
            if (TitleLabel != null) TitleLabel.Visible = true;
            if (RestartButton != null) RestartButton.Visible = true;
            if (QuitButton != null) QuitButton.Visible = true;
        }

        /// <summary>
        /// 隐藏结算面板，用于初始状态或游戏重新加载前的清理。
        /// </summary>
        private void HidePanel()
        {
            Visible = false;
            if (TitleLabel != null) TitleLabel.Visible = false;
            if (RestartButton != null) RestartButton.Visible = false;
            if (QuitButton != null) QuitButton.Visible = false;
        }

        #endregion
    }
}
