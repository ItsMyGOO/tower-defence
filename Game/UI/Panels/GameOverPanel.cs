using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.Panels
{
    /// <summary>
    /// 胜负结算 UI 面板。
    /// 监听 EventBus.OnGameOver 事件，在游戏结束时显示胜利或战败信息；
    /// 胜利时自动调用 SceneManager.UnlockNextLevel() 推进玩家解锁进度，
    /// 并根据胜负状态选择性显示"下一关"按钮（仅胜利可见）。
    /// 重新开始、返回选关、返回主菜单、退出游戏四条流转出口由基类 <see cref="UIPanelBase"/> 提供，
    /// 与暂停菜单共用同一套导航逻辑，构成局内战斗 → 结算 → Meta Loop 的完整架构闭环。
    /// </summary>
    public partial class GameOverPanel : UIPanelBase
    {
        #region 特有 UI 节点引用

        /// <summary>
        /// 获取或设置胜负结果标题 Label 节点引用。
        /// Inspector 中绑定到场景树内对应的 Label 节点，用于显示"胜利!"或"战败!"文本。
        /// </summary>
        [Export] public Label TitleLabel { get; set; }

        /// <summary>
        /// 获取或设置"下一关"按钮节点引用。
        /// 仅在玩家胜利且存在下一已解锁关卡时可见并可交互，
        /// 点击后通过 SceneManager.LoadNextLevel() 自动载入下一关。
        /// </summary>
        [Export] public Button NextLevelButton { get; set; }

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
        /// 基类共享初始化完成后的附加初始化：
        /// 订阅 EventBus.OnGameOver 并绑定"下一关"按钮回调。
        /// 关键的暂停态可交互配置（ProcessMode 递归 Always）已由基类完成，
        /// 否则 HandleGameOver 触发 GetTree().Paused = true 后结算面板按钮将全部点不动。
        /// </summary>
        protected override void OnPanelReady()
        {
            if (NextLevelButton != null)
            {
                NextLevelButton.Pressed += HandleNextLevelPressed;
            }

            EventBus.OnGameOver += HandleGameOver;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 先解绑"下一关"按钮与 EventBus 订阅，再交由基类解绑共享按钮。
        /// </summary>
        public override void _ExitTree()
        {
            if (NextLevelButton != null)
            {
                NextLevelButton.Pressed -= HandleNextLevelPressed;
            }

            EventBus.OnGameOver -= HandleGameOver;
            base._ExitTree();
        }

        #endregion

        #region 节点引用解析与显隐

        /// <summary>
        /// 解析本面板特有的 TitleLabel 与 NextLevelButton 引用。
        /// </summary>
        protected override void ResolveExtraUINodeReferences()
        {
            TitleLabel ??= GetNodeOrNull<Label>("CenterContainer/VBox/TitleLabel");
            NextLevelButton ??= GetNodeOrNull<Button>("CenterContainer/VBox/NextLevelButton");

            int missing = CheckResolved(TitleLabel, nameof(TitleLabel));
            missing += CheckResolved(NextLevelButton, nameof(NextLevelButton));

            if (missing == 0)
            {
                GD.Print("[GameOverPanel] ✅ TitleLabel + NextLevelButton 引用兜底解析成功。");
            }
        }

        /// <summary>
        /// 隐藏面板：除共享四按钮外，同步隐藏标题与"下一关"按钮。
        /// </summary>
        protected override void HideExtraNodes()
        {
            SetButtonVisible(NextLevelButton, false);
            if (TitleLabel != null) TitleLabel.Visible = false;
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

                    NextLevelButton.Text = canGoNext ? "➡️ 下一关" : "🏆 已是最后一关";

                    GD.Print($"[GameOverPanel] 下一关按钮状态 → nextIndex={nextIndex} maxUnlocked={maxUnlocked} sceneExists={nextSceneExists} 可点击={canGoNext}");
                }
            }
            else
            {
                SetButtonVisible(NextLevelButton, false);
            }

            ShowPanel();
            if (TitleLabel != null) TitleLabel.Visible = true;
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

        #endregion
    }
}
