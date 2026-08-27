using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.Gameplay.Waves;
using TowerDefence.UI.Panels;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// GameManager 与 GameOverPanel 测试场景控制器。
    /// 通过键盘输入模拟两套胜负条件：
    /// 1) 按 F 键：模拟玩家生命值清零，触发失败结算面板；
    /// 2) 按 V 键：模拟最后一波敌人全部清理完毕，触发胜利结算面板。
    /// 验证 GameManager 正确切换状态、GetTree().Paused 冻结局内时钟，
    /// 以及 GameOverPanel 的重新开始按钮能重置并重新加载场景。
    /// </summary>
    public partial class GameManagerTest : Node2D
    {
        #region 节点引用

        /// <summary>
        /// 获取或设置测试场景内嵌的 GameManager 节点引用。
        /// Inspector 中绑定到场景内的 GameManager 子节点。
        /// </summary>
        [Export] public GameManager GameManager { get; set; }

        /// <summary>
        /// 获取或设置测试场景内嵌的 WaveManager 节点引用。
        /// Inspector 中绑定到场景内的 WaveManager 子节点，用于模拟最后一波完成状态。
        /// </summary>
        [Export] public WaveManager WaveManager { get; set; }

        /// <summary>
        /// 获取或设置测试场景内嵌的 GameOverPanel 节点引用。
        /// Inspector 中绑定到场景内的 GameOverPanel 子节点。
        /// </summary>
        [Export] public GameOverPanel GameOverPanel { get; set; }

        #endregion

        #region 运行时状态

        /// <summary>
        /// 记录是否已触发过失败事件，避免重复触发导致状态切换异常。
        /// </summary>
        private bool _loseFired;

        /// <summary>
        /// 记录是否已触发过胜利事件，避免重复触发。
        /// </summary>
        private bool _winFired;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点进入场景树时调用。
        /// 校验节点绑定，订阅 EventBus.OnGameOver 用于日志输出，并打印操作提示。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[GameManagerTest] ========== GameManager 测试启动 ==========");

            ValidateBindings();
            SubscribeEvents();

            GD.Print("[GameManagerTest] 操作说明:");
            GD.Print("[GameManagerTest]   - 按 [F] 键: 模拟玩家血量归零 → 触发失败结算");
            GD.Print("[GameManagerTest]   - 按 [V] 键: 模拟最后一波通关 → 触发胜利结算");
            GD.Print("[GameManagerTest]   - 结算面板弹出后验证: 游戏已暂停 (GetTree().Paused == true)");
            GD.Print("[GameManagerTest]   - 点击 '重新开始' 按钮: 场景应重新加载并恢复到未暂停状态");
            GD.Print($"[GameManagerTest] 初始 GameState: {GameManager?.CurrentState.ToString() ?? "未绑定"}");
            GD.Print($"[GameManagerTest] 初始 Paused: {GetTree().Paused}");
        }

        /// <summary>
        /// 节点从场景树移除时调用。
        /// 取消订阅所有 EventBus 事件以避免内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// 每帧处理输入事件。
        /// 监听 F 键触发失败模拟，V 键触发胜利模拟。
        /// </summary>
        /// <param name="event">当前帧的输入事件</param>
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;

            switch (keyEvent.Keycode)
            {
                case Key.F:
                    SimulatePlayerLose();
                    break;

                case Key.V:
                    SimulatePlayerWin();
                    break;
            }
        }

        #endregion

        #region 测试方法

        /// <summary>
        /// 模拟玩家失败：通过 EventBus 发布 OnGameOver(false)，
        /// 模拟玩家生命值已被 EconomyManager 扣至零的场景。
        /// </summary>
        private void SimulatePlayerLose()
        {
            if (_loseFired)
            {
                GD.Print("[GameManagerTest] [跳过] 失败事件已触发过，按 '重新开始' 重置场景后可再次测试。");
                return;
            }

            GD.Print("[GameManagerTest] --- 模拟玩家失败：触发 EventBus.RaiseGameOver(false) ---");
            _loseFired = true;
            EventBus.RaiseGameOver(false);
        }

        /// <summary>
        /// 模拟玩家胜利：通过 EventBus 发布 OnGameOver(true) 直接触发胜利结算。
        /// 由于测试场景中 WaveManager 未配置实际 Waves（即 Waves.Count == 0），
        /// GameManager 通过 WaveCompleted 判定胜利的真实业务路径需配合 WaveData 资源配置才能生效，
        /// 因此测试场景直接发布 OnGameOver(true) 以验证：状态切换为 GameWin、
        /// GetTree().Paused 冻结时钟、GameOverPanel 弹出"胜利!"标题三条核心链路的正确性。
        /// 真实业务场景中胜利由 GameManager 监听最后一波 OnWaveCompleted 自动触发。
        /// </summary>
        private void SimulatePlayerWin()
        {
            if (_winFired)
            {
                GD.Print("[GameManagerTest] [跳过] 胜利事件已触发过，按 '重新开始' 重置场景后可再次测试。");
                return;
            }

            GD.Print("[GameManagerTest] --- 模拟玩家胜利：发布 EventBus.RaiseGameOver(true) ---");
            _winFired = true;
            EventBus.RaiseGameOver(true);
        }

        #endregion

        #region 内部辅助方法

        /// <summary>
        /// 校验 Inspector 中各节点是否已绑定，并打印缺失警告。
        /// </summary>
        private void ValidateBindings()
        {
            if (GameManager == null)
            {
                GD.PrintErr("[GameManagerTest] ⚠ GameManager 未绑定！胜利判定闭环将无法由 GameManager 驱动。");
            }

            if (WaveManager == null)
            {
                GD.PrintErr("[GameManagerTest] ⚠ WaveManager 未绑定！无法模拟最后一波完成的胜利路径。");
            }

            if (GameOverPanel == null)
            {
                GD.PrintErr("[GameManagerTest] ⚠ GameOverPanel 未绑定！无法看到结算面板 UI 弹出效果。");
            }
        }

        /// <summary>
        /// 订阅 EventBus 事件，用于打印游戏状态变化的日志。
        /// </summary>
        private void SubscribeEvents()
        {
            EventBus.OnGameOver += HandleGameOver;
        }

        /// <summary>
        /// 取消订阅 EventBus 事件。
        /// </summary>
        private void UnsubscribeEvents()
        {
            EventBus.OnGameOver -= HandleGameOver;
        }

        /// <summary>
        /// 处理游戏结束事件回调。
        /// 打印当前 GameState 与 Paused 状态快照，验证 GameManager 的状态切换与时钟冻结是否生效。
        /// </summary>
        /// <param name="isVictory">true 表示胜利，false 表示失败</param>
        private void HandleGameOver(bool isVictory)
        {
            string result = isVictory ? "胜利" : "失败";
            GD.Print($"[GameManagerTest] ✅ OnGameOver 事件触发！结果: {result}");
            GD.Print($"[GameManagerTest]   GameManager.CurrentState = {GameManager?.CurrentState.ToString() ?? "未绑定"}");
            GD.Print($"[GameManagerTest]   GetTree().Paused = {GetTree().Paused} (预期: True)");
        }

        #endregion
    }
}
