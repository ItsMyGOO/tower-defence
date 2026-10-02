using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.UI.Panels;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// GameManager 与 GameOverPanel 自动测试场景控制器（全代码构建，无 Inspector 绑定依赖）。
    /// 通过 EventBus 直接发布胜负事件，验证：
    /// 1) 失败路径：RaiseGameOver(false) → 状态切换 GameLose、GetTree().Paused 冻结时钟、
    ///    结算面板弹出「战败!」标题且隐藏「下一关」按钮；
    /// 2) 胜利路径：RaiseGameOver(true) → 状态切换 GameWin、面板弹出「胜利!」标题、
    ///    「下一关」按钮可见且可用（Level_01 场景存在且默认已解锁）；
    /// 3) 结算面板复用正式 Game/Scenes/GameOverPanel.tscn 实例，覆盖真实 UI 链路。
    /// 两阶段各自构建全新的 GameManager / GameOverPanel 实例，避免上一阶段的 GameOver 状态锁死。
    /// 全部断言打印 ✅/❌ 与汇总；无头模式以退出码上报结果。
    /// </summary>
    public partial class GameManagerTest : Node2D
    {
        /// <summary>
        /// 正式结算面板场景路径（测试直接实例化，避免内嵌副本与真实面板结构漂移）。
        /// </summary>
        private const string GameOverPanelScenePath = "res://Game/Scenes/GameOverPanel.tscn";

        private int _passed;
        private int _failed;

        /// <summary>
        /// 节点就绪后启动异步测试序列。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[GameManagerTest] ========== GameManager 测试启动 ==========");
            _ = RunAllAsync();
        }

        /// <summary>
        /// 测试主序列：失败路径 → 胜利路径 → 输出汇总。
        /// </summary>
        private async System.Threading.Tasks.Task RunAllAsync()
        {
            try
            {
                await TestLoseFlow();
                await TestWinFlow();

                GD.Print($"[GameManagerTest] ========== 测试结束：PASS {_passed} / FAIL {_failed} ==========");

                // 无头（CI）模式：以进程退出码上报测试结果，1 = 存在失败断言
                if (DisplayServer.GetName() == "headless")
                {
                    await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
                    GetTree().Quit(_failed > 0 ? 1 : 0);
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[GameManagerTest] ❌ 测试序列异常中止: {ex}");

                if (DisplayServer.GetName() == "headless")
                {
                    GetTree().Quit(1);
                }
            }
        }

        /// <summary>
        /// 阶段一：模拟玩家失败（HP 归零链路的等效事件）。
        /// </summary>
        private async System.Threading.Tasks.Task TestLoseFlow()
        {
            var (gameManager, panel) = await BuildGameFlowNodes("失败阶段");

            EventBus.RaiseGameOver(false);
            await Wait(0.1f);

            AssertTrue(
                gameManager.CurrentState == GameManager.GameState.GameLose,
                "失败: GameManager 状态切换为 GameLose"
            );
            AssertTrue(GetTree().Paused, "失败: GetTree().Paused 冻结局内时钟");
            AssertTrue(panel.Visible, "失败: 结算面板弹出");
            AssertTrue(panel.TitleLabel != null && panel.TitleLabel.Text == "战败!", "失败: 标题显示「战败!」");
            AssertTrue(
                panel.NextLevelButton == null || !panel.NextLevelButton.Visible,
                "失败: 战败时隐藏「下一关」按钮"
            );

            await TeardownGameFlowNodes(gameManager, panel);
        }

        /// <summary>
        /// 阶段二：模拟玩家胜利。重建全新实例，避免上一阶段 GameOver 状态锁死。
        /// </summary>
        private async System.Threading.Tasks.Task TestWinFlow()
        {
            var (gameManager, panel) = await BuildGameFlowNodes("胜利阶段");

            EventBus.RaiseGameOver(true);
            await Wait(0.1f);

            AssertTrue(
                gameManager.CurrentState == GameManager.GameState.GameWin,
                "胜利: GameManager 状态切换为 GameWin"
            );
            AssertTrue(GetTree().Paused, "胜利: GetTree().Paused 冻结局内时钟");
            AssertTrue(panel.Visible, "胜利: 结算面板弹出");
            AssertTrue(panel.TitleLabel != null && panel.TitleLabel.Text == "胜利!", "胜利: 标题显示「胜利!」");
            AssertTrue(
                panel.NextLevelButton != null && panel.NextLevelButton.Visible && !panel.NextLevelButton.Disabled,
                "胜利: 「下一关」按钮可见且可用（Level_01 已解锁）"
            );

            await TeardownGameFlowNodes(gameManager, panel);
        }

        /// <summary>
        /// 构建一阶段所需的 GameManager 与正式 GameOverPanel 实例，等待各自 _Ready 完成。
        /// </summary>
        /// <param name="phaseName">阶段名（日志与节点命名用）</param>
        private async System.Threading.Tasks.Task<(GameManager GameManager, GameOverPanel Panel)> BuildGameFlowNodes(
            string phaseName
        )
        {
            var packed = ResourceLoader.Load<PackedScene>(GameOverPanelScenePath);
            var panel = packed?.Instantiate<GameOverPanel>();
            AssertTrue(panel != null, $"{phaseName}: 实例化正式 GameOverPanel.tscn");

            var gameManager = new GameManager { Name = $"TestGameManager_{phaseName}" };
            AddChild(gameManager);
            if (panel != null)
            {
                AddChild(panel);
            }

            await Wait(0.1f);
            return (gameManager, panel);
        }

        /// <summary>
        /// 释放本阶段节点并恢复时钟；等待 _ExitTree 完成 EventBus 退订后再进入下一阶段。
        /// </summary>
        private async System.Threading.Tasks.Task TeardownGameFlowNodes(GameManager gameManager, GameOverPanel panel)
        {
            GetTree().Paused = false;
            gameManager?.QueueFree();
            panel?.QueueFree();
            await Wait(0.1f);
        }

        /// <summary>
        /// 等待指定秒数（真实时间）。
        /// </summary>
        /// <param name="seconds">等待时长（秒）</param>
        private async System.Threading.Tasks.Task Wait(float seconds)
        {
            await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        }

        /// <summary>
        /// 记录一条断言结果。
        /// </summary>
        /// <param name="condition">断言条件</param>
        /// <param name="name">断言名称（日志用）</param>
        private void AssertTrue(bool condition, string name)
        {
            if (condition)
            {
                _passed++;
                GD.Print($"[GameManagerTest] ✅ PASS {name}");
            }
            else
            {
                _failed++;
                GD.PrintErr($"[GameManagerTest] ❌ FAIL {name}");
            }
        }
    }
}
