using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 经济与生命值系统测试场景控制器。
    /// 负责模拟敌人击杀与到达终点事件，通过 GD.Print 输出验证：
    /// 1) 金币增加是否正确；2) 生命值扣减是否正确；3) HP 归零是否触发 GameOver。
    /// 场景中内嵌 EconomyManager 节点，通过暴露字段配置初始状态便于覆盖多组测试用例。
    /// </summary>
    public partial class EconomyTest : Node2D
    {
        /// <summary>
        /// 获取或设置测试场景内嵌的 EconomyManager 节点引用。
        /// Inspector 中绑定到场景内的 EconomyManager 子节点。
        /// </summary>
        [Export] public EconomyManager Economy { get; set; }

        /// <summary>
        /// 获取或设置测试用：单次敌人击杀的金币奖励金额。
        /// </summary>
        [Export] public int TestKillReward { get; set; } = 15;

        /// <summary>
        /// 获取或设置测试用：单次敌人逃脱对玩家造成的生命值伤害。
        /// </summary>
        [Export] public int TestEscapeDamage { get; set; } = 2;

        /// <summary>
        /// 获取或设置模拟测试事件之间的时间间隔（秒）。
        /// </summary>
        [Export] public float TestStepInterval { get; set; } = 1.5f;

        private int _testStep;
        private Timer _testTimer;
        private bool _gameOverFired;

        /// <summary>
        /// 节点进入场景树时调用。
        /// 完成事件订阅、校验绑定、启动测试流程定时器，并打印初始状态快照。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[EconomyTest] ========== 经济系统测试启动 ==========");

            SubscribeEvents();
            ValidateBindings();
            SetupTestTimer();

            GD.Print($"[EconomyTest] 初始金币: {Economy?.CurrentGold ?? -1}");
            GD.Print($"[EconomyTest] 初始 HP: {Economy?.CurrentHp ?? -1}");
            GD.Print($"[EconomyTest] 测试参数: 击杀奖励={TestKillReward}, 逃脱伤害={TestEscapeDamage}, 步长={TestStepInterval}s");
            GD.Print("[EconomyTest] 开始按步执行测试事件...");
        }

        /// <summary>
        /// 节点从场景树移除时调用。
        /// 取消订阅所有事件以避免内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// 订阅 EventBus 全局事件，用于验证金币/HP 变更及 GameOver 触发。
        /// </summary>
        private void SubscribeEvents()
        {
            EventBus.OnGoldChanged += HandleGoldChanged;
            EventBus.OnPlayerHpChanged += HandlePlayerHpChanged;
            EventBus.OnGameOver += HandleGameOver;
        }

        /// <summary>
        /// 取消订阅 EventBus 全局事件。
        /// </summary>
        private void UnsubscribeEvents()
        {
            EventBus.OnGoldChanged -= HandleGoldChanged;
            EventBus.OnPlayerHpChanged -= HandlePlayerHpChanged;
            EventBus.OnGameOver -= HandleGameOver;
        }

        /// <summary>
        /// 校验 Inspector 中 EconomyManager 节点是否已绑定。
        /// </summary>
        private void ValidateBindings()
        {
            if (Economy == null)
            {
                GD.PrintErr("[EconomyTest] EconomyManager 未绑定！请在 Inspector 中绑定场景内的 EconomyManager 节点。");
            }
        }

        /// <summary>
        /// 创建并配置测试步长定时器，按 TestStepInterval 间隔依次触发各类模拟事件。
        /// </summary>
        private void SetupTestTimer()
        {
            _testStep = 0;
            _gameOverFired = false;

            _testTimer = new Timer
            {
                WaitTime = TestStepInterval,
                OneShot = false,
                Autostart = true
            };
            _testTimer.Timeout += RunNextTestStep;
            AddChild(_testTimer);
        }

        /// <summary>
        /// 按顺序执行下一个测试步骤，模拟敌人事件并验证 EconomyManager 的响应。
        /// 测试顺序：击杀 -> 击杀 -> 尝试消费(不足) -> 击杀 -> 消费(足够) -> 逃脱*N -> 验证 GameOver。
        /// </summary>
        private void RunNextTestStep()
        {
            _testStep++;

            switch (_testStep)
            {
                case 1:
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 模拟敌人击杀 (奖励 {TestKillReward} 金币) ---");
                    EventBus.RaiseEnemyKilled("TestEnemy_A", TestKillReward);
                    break;

                case 2:
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 再次模拟敌人击杀 (奖励 {TestKillReward} 金币) ---");
                    EventBus.RaiseEnemyKilled("TestEnemy_B", TestKillReward);
                    break;

                case 3:
                    int tooMuch = (Economy?.CurrentGold ?? 0) + 999;
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 尝试消费 {tooMuch} 金币 (预期失败) ---");
                    bool spendFail = Economy != null && Economy.TrySpendGold(tooMuch);
                    GD.Print($"[EconomyTest]   TrySpendGold({tooMuch}) 返回: {spendFail} (预期 False)");
                    break;

                case 4:
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 第三次模拟敌人击杀 (奖励 {TestKillReward} 金币) ---");
                    EventBus.RaiseEnemyKilled("TestEnemy_C", TestKillReward);
                    break;

                case 5:
                    int affordable = (Economy?.CurrentGold ?? 0) / 2;
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 尝试消费 {affordable} 金币 (预期成功) ---");
                    bool spendOk = Economy != null && Economy.TrySpendGold(affordable);
                    GD.Print($"[EconomyTest]   TrySpendGold({affordable}) 返回: {spendOk} (预期 True)");
                    GD.Print($"[EconomyTest]   CanAfford({affordable + 1}) 返回: {Economy?.CanAfford(affordable + 1)}");
                    break;

                case 6:
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 模拟敌人逃脱 (扣 {TestEscapeDamage} HP) ---");
                    EventBus.RaiseEnemyReachedEnd(TestEscapeDamage);
                    break;

                case 7:
                    int bigDamage = (Economy?.CurrentHp ?? 0) + 5;
                    GD.Print($"[EconomyTest] --- Step {_testStep}: 模拟大伤害敌人逃脱 (扣 {bigDamage} HP, 预期触发 GameOver) ---");
                    EventBus.RaiseEnemyReachedEnd(bigDamage);
                    break;

                case 8:
                    GD.Print($"[EconomyTest] --- Step {_testStep}: GameOver 后再次尝试击杀/逃脱 (预期无变更) ---");
                    EventBus.RaiseEnemyKilled("TestEnemy_AfterOver", 9999);
                    EventBus.RaiseEnemyReachedEnd(9999);
                    break;

                default:
                    GD.Print("[EconomyTest] ========== 测试流程结束，停止定时器 ==========");
                    GD.Print($"[EconomyTest] 最终状态: 金币={Economy?.CurrentGold ?? -1}, HP={Economy?.CurrentHp ?? -1}, GameOver触发={_gameOverFired}");
                    _testTimer?.Stop();
                    break;
            }
        }

        /// <summary>
        /// 处理金币变更事件。
        /// </summary>
        /// <param name="newGold">更新后的金币总数</param>
        private void HandleGoldChanged(int newGold)
        {
            GD.Print($"[EconomyTest] ✅ OnGoldChanged 事件触发！当前金币: {newGold}");
        }

        /// <summary>
        /// 处理玩家生命值变更事件。
        /// </summary>
        /// <param name="newHp">更新后的生命值</param>
        private void HandlePlayerHpChanged(int newHp)
        {
            GD.Print($"[EconomyTest] ✅ OnPlayerHpChanged 事件触发！当前 HP: {newHp}");
        }

        /// <summary>
        /// 处理游戏结束事件。
        /// </summary>
        /// <param name="isVictory">true 表示胜利，false 表示失败</param>
        private void HandleGameOver(bool isVictory)
        {
            _gameOverFired = true;
            string result = isVictory ? "胜利" : "失败";
            GD.Print($"[EconomyTest] ✅ OnGameOver 事件触发！结果: {result} (预期为 失败/False)");
        }
    }
}
