using System;
using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Towers;
using TowerDefence.UI.HUD;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 基础 UI 框架与局内 HUD 界面测试场景控制器。
    /// 动态创建 HUDView、EconomyManager、TowerManager 与测试按钮，
    /// 通过按钮手动触发金币/血量增减与波次开始事件，验证：
    /// 1) HUD Label 能实时刷新金币、血量、波次；
    /// 2) 金币/血量/波次事件驱动 HUD 实时刷新。
    /// </summary>
    public partial class UITest : Node2D
    {
        #region 导出配置

        /// <summary>
        /// 获取或设置测试用防御塔数据资源。
        /// Inspector 中绑定 Tests/Data/Towers/Test_ArrowTower.tres（成本 50 金币）。
        /// </summary>
        [Export] public TowerData TestTowerData { get; set; }

        /// <summary>
        /// 获取或设置初始金币数量。
        /// 用于验证金币不足/充足两种状态下按钮的禁用与启用切换。
        /// </summary>
        [Export] public int InitialGold { get; set; } = 30;

        /// <summary>
        /// 获取或设置初始血量。
        /// </summary>
        [Export] public int InitialHp { get; set; } = 20;

        #endregion

        #region 运行时引用

        private EconomyManager _economyManager;
        private TowerManager _towerManager;
        private HUDView _hudView;
            #endregion

        #region 生命周期

        /// <summary>
        /// 节点进入场景树时调用。
        /// 依次创建 EconomyManager、TowerManager、HUDView
        /// 以及测试用控制按钮，并订阅事件用于日志打印。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[UITest] ========== HUD 界面与建造按钮测试启动 ==========");

            CreateEconomyManager();
            CreateTowerManager();
            CreateHUDView();
            CreateTestControlButtons();

            SubscribeTestEvents();
            ValidateBindings();
            _ = RunAutoChecksAsync();
        }

        /// <summary>
        /// 节点从场景树移除时调用。
        /// 取消测试事件订阅，避免内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            UnsubscribeTestEvents();
        }

        #endregion

        #region 节点创建

        /// <summary>
        /// 创建并配置 EconomyManager 子节点。
        /// 设置初始金币与血量，并加入场景树。
        /// </summary>
        private void CreateEconomyManager()
        {
            _economyManager = new EconomyManager
            {
                Name = "EconomyManager",
                InitialGold = InitialGold,
                InitialHp = InitialHp
            };
            AddChild(_economyManager);
        }

        /// <summary>
        /// 创建并配置 TowerManager 子节点。
        /// 用于接收 TowerBuildButton 点击后设置的选中塔数据。
        /// </summary>
        private void CreateTowerManager()
        {
            _towerManager = new TowerManager
            {
                Name = "TowerManager"
            };
            AddChild(_towerManager);
        }

        /// <summary>
        /// 创建 HUDView 主界面及其内部 Label / PauseButton。
        /// 子节点先于 HUDView 进树构建（命名与真实 HUDView.tscn 一致：TopBar/...），
        /// HUDView._Ready 的兜底解析即可拿到全部引用——无需事后属性注入，
        /// 与真实关卡场景走完全相同的初始化链路。
        /// </summary>
        private void CreateHUDView()
        {
            _hudView = new HUDView
            {
                Name = "HUDView"
            };

            var topBar = new HBoxContainer
            {
                Name = "TopBar",
                OffsetTop = 10,
                OffsetLeft = 10
            };

            var goldLabel = new Label { Name = "GoldLabel" };
            var hpLabel = new Label { Name = "HpLabel" };
            var waveLabel = new Label { Name = "WaveLabel" };
            var pauseButton = new Button { Name = "PauseButton", Text = "⏸" };

            goldLabel.AddThemeFontSizeOverride("font_size", 18);
            hpLabel.AddThemeFontSizeOverride("font_size", 18);
            waveLabel.AddThemeFontSizeOverride("font_size", 18);

            goldLabel.AddThemeColorOverride("font_color", Colors.Gold);
            hpLabel.AddThemeColorOverride("font_color", Colors.Red);
            waveLabel.AddThemeColorOverride("font_color", Colors.Cyan);

            topBar.AddChild(goldLabel);
            topBar.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
            topBar.AddChild(hpLabel);
            topBar.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
            topBar.AddChild(waveLabel);
            topBar.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
            topBar.AddChild(pauseButton);

            _hudView.AddChild(topBar);
            AddChild(_hudView);
        }

        /// <summary>
        /// 创建测试控制按钮区。
        /// 包含增加金币、减少金币、扣除血量、模拟波次开始四个按钮，
        /// 用于手动触发各类事件并验证 HUD 刷新与按钮状态。
        /// </summary>
        private void CreateTestControlButtons()
        {
            var controlPanel = new VBoxContainer
            {
                Name = "TestControlPanel",
                OffsetTop = 120,
                OffsetLeft = 10
            };
            _hudView.AddChild(controlPanel);

            AddTestButton(controlPanel, "增加金币 +100", Colors.Green, () =>
            {
                _economyManager?.AddGold(100);
            });

            AddTestButton(controlPanel, "减少金币 -80", Colors.Orange, () =>
            {
                if (_economyManager != null)
                {
                    _economyManager.TrySpendGold(80);
                }
            });

            AddTestButton(controlPanel, "扣血 -3", Colors.Red, () =>
            {
                EventBus.RaiseEnemyReachedEnd(3);
            });

            AddTestButton(controlPanel, "开始下一波", Colors.Cyan, () =>
            {
                int nextWave = _currentWave + 1;
                EventBus.RaiseWaveStarted(nextWave);
                GD.Print($"[UITest] 模拟波次开始：第 {nextWave} 波 (WaveIndex {nextWave})");
            });

            var hint = new Label
            {
                Text = "\n提示：点击地图槽位弹出环形菜单建造/升级/出售防御塔",
                AutowrapMode = TextServer.AutowrapMode.Word,
                CustomMinimumSize = new Vector2(350, 0)
            };
            hint.AddThemeFontSizeOverride("font_size", 13);
            controlPanel.AddChild(hint);
        }

        /// <summary>
        /// 辅助方法：向指定容器中添加一个带颜色与点击回调的测试按钮。
        /// </summary>
        /// <param name="container">目标容器（如 VBoxContainer）</param>
        /// <param name="text">按钮显示文本</param>
        /// <param name="color">按钮文本颜色</param>
        /// <param name="onClick">按钮点击时的回调</param>
        private void AddTestButton(Container container, string text, Color color, Action onClick)
        {
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(200, 36)
            };
            btn.AddThemeColorOverride("font_color", color);
            btn.Pressed += onClick;
            container.AddChild(btn);
        }

        #endregion

        #region 测试事件订阅与验证

        private int _currentWave = -1;
        private int _passed;
        private int _failed;

        /// <summary>
        /// 自动断言序列：通过经济/事件接口驱动 HUD 刷新并校验 Label 文本。
        /// 编辑器内仍可配合右侧测试按钮做手动交互验证。
        /// </summary>
        private async System.Threading.Tasks.Task RunAutoChecksAsync()
        {
            try
            {
                await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);

                AssertTrue(_hudView?.GoldLabel?.Text == "金币: 30", "HUD: 初始金币显示 30");
                AssertTrue(_hudView?.HpLabel?.Text == "血量: 20", "HUD: 初始血量显示 20");
                AssertTrue(_hudView?.PauseButton != null, "HUD: PauseButton 兜底解析成功");

                _economyManager?.AddGold(100);
                AssertTrue(_hudView?.GoldLabel?.Text == "金币: 130", "HUD: 金币事件驱动刷新 (+100)");

                EventBus.RaiseEnemyReachedEnd(3);
                AssertTrue(_hudView?.HpLabel?.Text == "血量: 17", "HUD: 血量事件驱动刷新 (-3)");

                EventBus.RaiseWaveStarted(4);
                AssertTrue(_hudView?.WaveLabel?.Text == "波次: 4", "HUD: 波次事件驱动刷新");

                GD.Print($"[UITest] ========== 测试结束：PASS {_passed} / FAIL {_failed} ==========");

                // 无头（CI）模式：以进程退出码上报测试结果，1 = 存在失败断言
                if (DisplayServer.GetName() == "headless")
                {
                    await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
                    GetTree().Quit(_failed > 0 ? 1 : 0);
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[UITest] ❌ 测试序列异常中止: {ex}");

                if (DisplayServer.GetName() == "headless")
                {
                    GetTree().Quit(1);
                }
            }
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
                GD.Print($"[UITest] ✅ PASS {name}");
            }
            else
            {
                _failed++;
                GD.PrintErr($"[UITest] ❌ FAIL {name}");
            }
        }

        /// <summary>
        /// 订阅 EventBus 事件用于日志输出，便于在控制台验证事件流。
        /// </summary>
        private void SubscribeTestEvents()
        {
            EventBus.OnGoldChanged += TestHandleGoldChanged;
            EventBus.OnPlayerHpChanged += TestHandleHpChanged;
            EventBus.OnWaveStarted += TestHandleWaveStarted;
        }

        /// <summary>
        /// 取消测试事件订阅。
        /// </summary>
        private void UnsubscribeTestEvents()
        {
            EventBus.OnGoldChanged -= TestHandleGoldChanged;
            EventBus.OnPlayerHpChanged -= TestHandleHpChanged;
            EventBus.OnWaveStarted -= TestHandleWaveStarted;
        }

        /// <summary>
        /// 验证关键节点是否正确创建并绑定。
        /// </summary>
        private void ValidateBindings()
        {
            GD.Print($"[UITest] TestTowerData: {(TestTowerData != null ? TestTowerData.TowerName : "MISSING")} | BuildCost={TestTowerData?.BuildCost}");
            GD.Print($"[UITest] EconomyManager: {(_economyManager != null ? "OK" : "MISSING")} | InitialGold={InitialGold}");
            GD.Print($"[UITest] TowerManager: {(_towerManager != null ? "OK" : "MISSING")}");
            GD.Print($"[UITest] HUDView: {(_hudView != null ? "OK" : "MISSING")}");
            GD.Print($"[UITest] GoldLabel: {(_hudView?.GoldLabel != null ? "OK" : "MISSING")}");
            GD.Print($"[UITest] HpLabel: {(_hudView?.HpLabel != null ? "OK" : "MISSING")}");
            GD.Print($"[UITest] WaveLabel: {(_hudView?.WaveLabel != null ? "OK" : "MISSING")}");
            GD.Print($"[UITest] 初始金币: {_economyManager?.CurrentGold} (塔成本: {TestTowerData?.BuildCost})");
            GD.Print("[UITest] 请使用界面右侧的测试按钮进行手动验证。");
        }

        private void TestHandleGoldChanged(int newGold)
        {
            GD.Print($"[UITest] 💰 OnGoldChanged -> {newGold}");
        }

        private void TestHandleHpChanged(int newHp)
        {
            GD.Print($"[UITest] ❤️  OnPlayerHpChanged -> {newHp}");
        }

        private void TestHandleWaveStarted(int waveIndex)
        {
            _currentWave = waveIndex;
            GD.Print($"[UITest] 🌊 OnWaveStarted -> 波次索引 {waveIndex}");
        }

        #endregion
    }
}
