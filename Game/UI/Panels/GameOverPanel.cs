using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 胜负结算 UI 面板。
    /// 监听 EventBus.OnGameOver 事件，在游戏结束时显示胜利或战败信息；
    /// 胜利时自动调用 SceneManager.UnlockNextLevel() 推进玩家解锁进度，
    /// 并根据胜负状态选择性显示"下一关"按钮（仅胜利可见），
    /// 同时提供重新开始、返回选关、退出游戏等多个场景流转出口，
    /// 构成局内战斗 → 结算 → Meta Loop 的完整架构闭环。
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
        /// 点击后恢复暂停状态并重新加载当前关卡场景，实现一局游戏的完整重置。
        /// </summary>
        [Export] public Button RestartButton { get; set; }

        /// <summary>
        /// 获取或设置"返回选关"按钮节点引用。
        /// 胜利或失败时均可见，点击后通过 SceneManager 载入选关界面。
        /// </summary>
        [Export] public Button LevelSelectButton { get; set; }

        /// <summary>
        /// 获取或设置"返回主菜单"按钮节点引用。
        /// 胜利或失败时均可见，点击后调用 SceneManager.LoadMainMenu() 回到 Meta Loop 起点。
        /// 这是关卡结束 → 主界面闭环的直接入口，也是战败/胜利后玩家的常用出口之一。
        /// </summary>
        [Export] public Button MainMenuButton { get; set; }

        /// <summary>
        /// 获取或设置"下一关"按钮节点引用。
        /// 仅在玩家胜利且存在下一已解锁关卡时可见并可交互，
        /// 点击后通过 SceneManager.LoadNextLevel() 自动载入下一关。
        /// </summary>
        [Export] public Button NextLevelButton { get; set; }

        /// <summary>
        /// 获取或设置退出游戏按钮节点引用。
        /// 点击后在编辑器模式打印提示，在打包模式下调用 Quit 退出应用程序。
        /// </summary>
        [Export] public Button QuitButton { get; set; }

        #endregion

        #region 内部状态

        /// <summary>
        /// 记录最近一次结算是否为胜利。
        /// 用于在"下一关"按钮点击时校验只有胜利方可进入下一关。
        /// </summary>
        private bool _lastIsVictory;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化面板为隐藏状态，订阅 EventBus.OnGameOver 事件，并绑定所有按钮点击回调。
        /// 关键点：立即将自身 ProcessMode 设置为 Always，并递归所有子节点同样设为 Always；
        /// 因为 HandleGameOver 触发后会调用 GetTree().Paused=true，默认 ProcessMode=Inherit 的节点无法接收 GUI 事件，
        /// 不提前设置会导致结算面板弹出后「全部按钮点不动」的同样现象。
        /// </summary>
        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            SetDescendantsProcessModeAlways(this);
            ResolveUINodeReferences();
            HidePanel();

            EventBus.OnGameOver += HandleGameOver;

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

            if (NextLevelButton != null)
            {
                NextLevelButton.Pressed += HandleNextLevelPressed;
            }

            if (QuitButton != null)
            {
                QuitButton.Pressed += HandleQuitPressed;
            }
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消 EventBus 订阅与所有按钮点击事件绑定，防止委托悬空导致的内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnGameOver -= HandleGameOver;

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

            if (NextLevelButton != null)
            {
                NextLevelButton.Pressed -= HandleNextLevelPressed;
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
        /// 根据 isVictory 参数设置标题文本：
        /// - 胜利时自动调用 SceneManager.UnlockNextLevel() 推进解锁进度，
        ///   并判断是否存在下一关场景以决定"下一关"按钮是否可交互；
        /// - 失败时隐藏"下一关"按钮，仅保留重新开始/返回选关/退出三个出口。
        /// 结算完成后立即显示面板。
        /// </summary>
        /// <param name="isVictory">true 表示玩家胜利，false 表示玩家失败</param>
        private void HandleGameOver(bool isVictory)
        {
            _lastIsVictory = isVictory;

            if (TitleLabel != null)
            {
                TitleLabel.Text = isVictory ? "胜利!" : "战败!";
            }

            if (isVictory)
            {
                GD.Print("[GameOverPanel] 检测到玩家胜利，正在调用 UnlockNextLevel 推进解锁进度...");
                SceneManager.Instance?.UnlockNextLevel();

                if (NextLevelButton != null)
                {
                    int nextIndex = (SceneManager.Instance?.CurrentLevelIndex ?? 0) + 1;
                    string nextScenePath = string.Format(SceneManager.LevelScenePathTemplate, nextIndex);
                    bool nextSceneExists = ResourceLoader.Exists(nextScenePath, "PackedScene");
                    int maxUnlocked = SceneManager.Instance?.MaxUnlockedLevel ?? 1;
                    bool canGoNext = nextSceneExists && nextIndex <= maxUnlocked;

                    NextLevelButton.Visible = true;
                    NextLevelButton.Disabled = !canGoNext;
                    NextLevelButton.Modulate = canGoNext ? Colors.White : new Color(0.5f, 0.5f, 0.55f, 0.9f);

                    if (!canGoNext)
                    {
                        NextLevelButton.Text = "🏆 已是最后一关";
                    }
                    else
                    {
                        NextLevelButton.Text = "➡️ 下一关";
                    }

                    GD.Print($"[GameOverPanel] 下一关按钮状态 → nextIndex={nextIndex} maxUnlocked={maxUnlocked} sceneExists={nextSceneExists} 可点击={canGoNext}");
                }
            }
            else
            {
                if (NextLevelButton != null)
                {
                    NextLevelButton.Visible = false;
                    NextLevelButton.Disabled = true;
                }
            }

            ShowPanel(isVictory);
        }

        /// <summary>
        /// 处理重新开始按钮点击事件。
        /// 先显式解除全局暂停，再通过 ReloadCurrentScene 重置当前关卡。
        /// </summary>
        private void HandleRestartPressed()
        {
            GD.Print("[GameOverPanel] 玩家点击「重新开始」。");
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        }

        /// <summary>
        /// 处理"返回选关"按钮点击事件。
        /// 解除全局暂停状态，并通过 SceneManager.LoadLevelSelect() 切入选关界面。
        /// </summary>
        private void HandleLevelSelectPressed()
        {
            GD.Print("[GameOverPanel] 玩家点击「返回选关」。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadLevelSelect();
        }

        /// <summary>
        /// 处理"返回主菜单"按钮点击事件。
        /// 解除全局暂停状态，并通过 SceneManager.LoadMainMenu() 回到 Meta Loop 起点。
        /// 这是关卡结束 → 主界面闭环的直接出口，无论胜负都可点击。
        /// </summary>
        private void HandleMainMenuPressed()
        {
            GD.Print("[GameOverPanel] 玩家点击「返回主菜单」，将重置 CurrentLevelIndex = 0 并切回主界面。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadMainMenu();
        }

        /// <summary>
        /// 处理"下一关"按钮点击事件。
        /// 仅在最近一次为胜利状态时执行 LoadNextLevel，避免战败后误进入下一关。
        /// </summary>
        private void HandleNextLevelPressed()
        {
            if (!_lastIsVictory)
            {
                GD.Print("[GameOverPanel] [WARN] 非胜利状态下点击「下一关」，忽略。");
                return;
            }

            GD.Print("[GameOverPanel] 玩家点击「下一关」。");
            GetTree().Paused = false;
            SceneManager.Instance?.LoadNextLevel();
        }

        /// <summary>
        /// 处理退出按钮点击事件。
        /// 在编辑器环境中打印提示，在打包环境中调用 Quit 退出应用程序。
        /// </summary>
        private void HandleQuitPressed()
        {
            GD.Print("[GameOverPanel] 玩家点击「退出游戏」。");
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
        /// 根据胜负结果选择性显示"下一关"按钮，其他按钮默认始终可见。
        /// </summary>
        /// <param name="isVictory">true 表示胜利状态，用于控制下一关按钮显隐</param>
        private void ShowPanel(bool isVictory)
        {
            Visible = true;
            if (TitleLabel != null) TitleLabel.Visible = true;
            if (RestartButton != null) RestartButton.Visible = true;
            if (LevelSelectButton != null) LevelSelectButton.Visible = true;
            if (MainMenuButton != null) MainMenuButton.Visible = true;
            if (QuitButton != null) QuitButton.Visible = true;
            if (NextLevelButton != null)
            {
                NextLevelButton.Visible = isVictory;
            }
        }

        /// <summary>
        /// 隐藏结算面板，用于初始状态或游戏重新加载前的清理。
        /// 所有 UI 子元素同时隐藏，避免节点残留导致误点击。
        /// </summary>
        private void HidePanel()
        {
            Visible = false;
            if (TitleLabel != null) TitleLabel.Visible = false;
            if (RestartButton != null) RestartButton.Visible = false;
            if (LevelSelectButton != null) LevelSelectButton.Visible = false;
            if (MainMenuButton != null) MainMenuButton.Visible = false;
            if (NextLevelButton != null) NextLevelButton.Visible = false;
            if (QuitButton != null) QuitButton.Visible = false;
        }

        #endregion

        #region 内部辅助 —— UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做 GetNodeOrNull 兜底。
        /// Godot 4.x 的 C# [Export] 属性在 .tscn 文本中以 PascalCase 直接赋值 NodePath 时，
        /// 脚本桥接层偶尔会因字段名序列化不一致而读到 null（表现为按钮有交互视觉反馈但点击逻辑完全不执行）。
        /// 兜底用相对路径解析，保证即使 .tscn 的 NodePath 没映射上也能 100% 拿到引用。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            TitleLabel ??= GetNodeOrNull<Label>("CenterContainer/VBox/TitleLabel");
            RestartButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/RestartButton");
            LevelSelectButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/LevelSelectButton");
            MainMenuButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/MainMenuButton");
            NextLevelButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/NextLevelButton");
            QuitButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/QuitButton");

            int missing = 0;
            if (TitleLabel == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: TitleLabel"); missing++; }
            if (RestartButton == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: RestartButton"); missing++; }
            if (LevelSelectButton == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: LevelSelectButton"); missing++; }
            if (MainMenuButton == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: MainMenuButton"); missing++; }
            if (NextLevelButton == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: NextLevelButton"); missing++; }
            if (QuitButton == null) { GD.PrintErr("[GameOverPanel] 兜底解析失败: QuitButton"); missing++; }

            if (missing == 0)
            {
                GD.Print("[GameOverPanel] ✅ TitleLabel + 6 个按钮引用兜底解析全部成功，Pressed 回调已可绑定。");
            }
        }

        #endregion

        #region 内部辅助 —— ProcessMode 递归设置

        /// <summary>
        /// 递归将 root 节点及所有子孙的 ProcessMode 设置为 Always。
        /// 结算面板在 HandleGameOver 后会调用 GetTree().Paused=true，
        /// 默认 ProcessMode=Inherit 的节点无法接收 _GuiInput，导致所有按钮点了没反应。
        /// 该方法递归设置 Background / CenterContainer / VBoxContainer / 所有 Label 与 Button
        /// 的 ProcessMode=Always，保证暂停态下结算面板的所有按钮交互正常工作。
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
