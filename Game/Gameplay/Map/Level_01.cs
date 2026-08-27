using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.Gameplay.Towers;
using TowerDefence.Gameplay.Waves;

namespace TowerDefence.Gameplay.Map
{
    /// <summary>
    /// 第一关主场景控制器。
    /// 负责串联 Level_01.tscn 中所有子系统（WaveManager / TowerManager / EconomyManager / GameManager），
    /// 在 _Ready 中延迟触发首波刷怪，并通过订阅 EventBus 实现波次间自动衔接；
    /// 同时为地图上的 TowerSlot 槽位补充点击检测与建造事务调用入口，
    /// 确保从选塔 → 点槽位 → 扣费 → 建塔 → 刷怪 → 击杀 → 经济回流 → GameOver 判定的完整垂直切片闭环。
    /// </summary>
    public partial class Level_01 : Node2D
    {
        #region 导出节点引用

        /// <summary>
        /// 获取或设置波次管理器节点引用。
        /// Inspector 中绑定到场景树内 Systems/WaveManager 节点。
        /// </summary>
        [Export] public WaveManager WaveManagerNode { get; set; }

        /// <summary>
        /// 获取或设置游戏流程管理器节点引用。
        /// Inspector 中绑定到场景树内 Systems/GameManager 节点。
        /// </summary>
        [Export] public GameManager GameManagerNode { get; set; }

        /// <summary>
        /// 获取或设置塔槽位容器节点引用。
        /// Inspector 中绑定到场景树内 Slots 节点，用于遍历其下所有 TowerSlot 子节点。
        /// </summary>
        [Export] public Node2D SlotsContainer { get; set; }

        /// <summary>
        /// 获取或设置首波自动启动的延迟秒数。
        /// 玩家进入场景后先有短暂准备布防时间，随后自动开启 Wave_01。
        /// </summary>
        [Export] public float FirstWaveAutoStartDelay { get; set; } = 3.0f;

        /// <summary>
        /// 获取或设置波次完成后自动开启下一波的延迟秒数。
        /// 非最后一波完成后，此时长过后自动调用 StartNextWave()，实现关卡无缝衔接。
        /// </summary>
        [Export] public float NextWaveAutoStartDelay { get; set; } = 3.0f;

        #endregion

        #region 内部字段

        /// <summary>
        /// 首波启动定时器。
        /// </summary>
        private Timer _firstWaveTimer;

        /// <summary>
        /// 下一波衔接定时器。
        /// </summary>
        private Timer _nextWaveTimer;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：事件订阅 → 槽位点击检测补充 → 启动首波倒计时。
        /// </summary>
        public override void _Ready()
        {
            SubscribeEventBus();
            SetupTowerSlotInput();
            ScheduleFirstWave();

            GD.Print("[Level_01] ✅ 第一关加载完成，等待首波刷怪...");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有 EventBus 订阅，防止委托悬空。
        /// </summary>
        public override void _ExitTree()
        {
            UnsubscribeEventBus();
        }

        #endregion

        #region 事件订阅与取消

        /// <summary>
        /// 订阅 EventBus 中本场景关心的事件。
        /// 主要用于波次完成后自动衔接下一波，以及打印关键流程日志便于排查。
        /// </summary>
        private void SubscribeEventBus()
        {
            EventBus.OnWaveStarted += HandleWaveStarted;
            EventBus.OnWaveCompleted += HandleWaveCompleted;
            EventBus.OnTowerBuilt += HandleTowerBuilt;
            EventBus.OnEnemyKilled += HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
            EventBus.OnGameOver += HandleGameOver;
            EventBus.OnGoldChanged += HandleGoldChanged;
            EventBus.OnPlayerHpChanged += HandlePlayerHpChanged;
        }

        /// <summary>
        /// 对称取消 SubscribeEventBus 中注册的所有事件订阅。
        /// </summary>
        private void UnsubscribeEventBus()
        {
            EventBus.OnWaveStarted -= HandleWaveStarted;
            EventBus.OnWaveCompleted -= HandleWaveCompleted;
            EventBus.OnTowerBuilt -= HandleTowerBuilt;
            EventBus.OnEnemyKilled -= HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
            EventBus.OnGameOver -= HandleGameOver;
            EventBus.OnGoldChanged -= HandleGoldChanged;
            EventBus.OnPlayerHpChanged -= HandlePlayerHpChanged;
        }

        #endregion

        #region 波次调度

        /// <summary>
        /// 安排首波自动启动定时器。
        /// 到时后直接调用 WaveManager.StartNextWave() 开启 Wave_01。
        /// </summary>
        private void ScheduleFirstWave()
        {
            _firstWaveTimer = new Timer
            {
                Name = "FirstWaveTimer",
                WaitTime = Mathf.Max(0.0f, FirstWaveAutoStartDelay),
                OneShot = true,
                Autostart = true
            };
            AddChild(_firstWaveTimer);
            _firstWaveTimer.Timeout += () =>
            {
                GD.Print($"[Level_01] 首波准备时间结束，正在启动第 1 波...");
                WaveManagerNode?.StartNextWave();
            };
        }

        /// <summary>
        /// 安排非最后一波的下一波自动衔接定时器。
        /// 若 WaveManager.AllWavesCompleted 为 true 则跳过，由 GameManager 接管胜利判定。
        /// </summary>
        private void ScheduleNextWave()
        {
            if (WaveManagerNode == null) return;
            if (WaveManagerNode.AllWavesCompleted) return;

            _nextWaveTimer = new Timer
            {
                Name = "NextWaveTimer",
                WaitTime = Mathf.Max(0.0f, NextWaveAutoStartDelay),
                OneShot = true,
                Autostart = true
            };
            AddChild(_nextWaveTimer);
            _nextWaveTimer.Timeout += () =>
            {
                GD.Print($"[Level_01] 波次间隔结束，正在启动下一波...");
                WaveManagerNode.StartNextWave();
            };
        }

        #endregion

        #region TowerSlot 点击检测补充

        /// <summary>
        /// 遍历 SlotsContainer 下所有 TowerSlot 子节点，
        /// 为每个槽位补充 Area2D+CircleShape2D 碰撞体并订阅 input_event 信号，
        /// 实现鼠标左键点击时调用 TowerManager.TryBuildTower 的建造入口。
        /// 槽位本身只维护占用状态，不直接参与建造事务，保持单一职责。
        /// </summary>
        private void SetupTowerSlotInput()
        {
            if (SlotsContainer == null)
            {
                GD.PrintErr("[Level_01] SlotsContainer 未绑定，无法为 TowerSlot 补充点击检测。");
                return;
            }

            int slotIndex = 0;
            foreach (Node child in SlotsContainer.GetChildren())
            {
                if (child is not TowerSlot slot) continue;

                slotIndex++;
                AddSlotClickDetector(slot, slotIndex);
            }

            GD.Print($"[Level_01] 已为 {slotIndex} 个 TowerSlot 补充点击建造检测。");
        }

        /// <summary>
        /// 为单个 TowerSlot 添加可点击的 Area2D 碰撞体。
        /// 半径固定 30 像素，可覆盖 ColorRect 占位视觉与鼠标点击容差。
        /// 点击时若玩家已通过 HUD 选中塔类型（TowerManager.CurrentSelectedTowerData != null），
        /// 则调用 TowerManager.TryBuildTower 执行建造事务。
        /// </summary>
        /// <param name="slot">目标槽位节点</param>
        /// <param name="slotIndex">槽位序号，用于日志打印</param>
        private void AddSlotClickDetector(TowerSlot slot, int slotIndex)
        {
            var area = new Area2D
            {
                Name = "SlotClickArea"
            };
            slot.AddChild(area);

            var shape = new CollisionShape2D
            {
                Name = "SlotClickShape",
                Shape = new CircleShape2D
                {
                    Radius = 30.0f
                }
            };
            area.AddChild(shape);

            area.InputEvent += (viewport, @event, shapeIdx) =>
            {
                if (@event is InputEventMouseButton mouseBtn
                    && mouseBtn.ButtonIndex == MouseButton.Left
                    && mouseBtn.Pressed)
                {
                    OnSlotClicked(slot, slotIndex);
                }
            };
        }

        /// <summary>
        /// TowerSlot 被点击时的回调。
        /// 校验 TowerManager 单例与 CurrentSelectedTowerData 后调用 TryBuildTower，
        /// 未选塔则打印提醒（不视为错误）。
        /// </summary>
        /// <param name="slot">被点击的槽位</param>
        /// <param name="slotIndex">槽位序号（日志用）</param>
        private void OnSlotClicked(TowerSlot slot, int slotIndex)
        {
            if (TowerManager.Instance == null)
            {
                GD.PrintErr("[Level_01] TowerManager 单例不存在，无法建造。");
                return;
            }

            var selectedData = TowerManager.Instance.CurrentSelectedTowerData;
            if (selectedData == null)
            {
                GD.Print($"[Level_01] 槽位 #{slotIndex} 被点击，但尚未选择塔类型。请先点击 HUD 中的建造按钮选塔。");
                return;
            }

            GD.Print($"[Level_01] 尝试在槽位 #{slotIndex} 建造 {selectedData.TowerName} (成本 {selectedData.BuildCost})");
            TowerManager.Instance.TryBuildTower(slot, selectedData);
        }

        #endregion

        #region EventBus 事件处理

        /// <summary>
        /// 波次开始事件：打印日志，便于确认刷怪节奏是否符合预期。
        /// </summary>
        private void HandleWaveStarted(int waveIndex)
        {
            GD.Print($"[Level_01] 🚩 第 {waveIndex} 波开始！");
        }

        /// <summary>
        /// 波次完成事件：若非最后一波则安排下一波自动衔接，否则等待 GameManager 胜利判定。
        /// </summary>
        private void HandleWaveCompleted(int waveIndex)
        {
            GD.Print($"[Level_01] ✅ 第 {waveIndex} 波已清理完毕。");
            ScheduleNextWave();
        }

        /// <summary>
        /// 塔建造成功事件：打印日志确认建造事务闭环。
        /// </summary>
        private void HandleTowerBuilt(Config.Towers.TowerData towerData, Vector2 pos)
        {
            GD.Print($"[Level_01] 🏰 建造成功 {towerData.TowerName} @ {pos}");
        }

        /// <summary>
        /// 敌人击杀事件：仅用于流程日志，金币已由 EconomyManager 处理。
        /// </summary>
        private void HandleEnemyKilled(string enemyId, int goldReward)
        {
            GD.Print($"[Level_01] 💀 敌人被击杀: {enemyId} | 奖励金币 +{goldReward}");
        }

        /// <summary>
        /// 敌人逃脱事件：仅用于流程日志，扣血已由 EconomyManager 处理。
        /// </summary>
        private void HandleEnemyReachedEnd(int damageToPlayer)
        {
            GD.Print($"[Level_01] ⚠️ 敌人逃脱！玩家受到伤害 {damageToPlayer}");
        }

        /// <summary>
        /// 游戏结束事件：打印胜负结果日志。
        /// </summary>
        private void HandleGameOver(bool isVictory)
        {
            GD.Print($"[Level_01] 🏁 游戏结束 → {(isVictory ? "胜利！" : "失败")}");
        }

        /// <summary>
        /// 金币变更日志回调，便于排查经济回流是否正常。
        /// </summary>
        private void HandleGoldChanged(int newGold)
        {
            GD.Print($"[Level_01] 💰 金币变化: {newGold}");
        }

        /// <summary>
        /// 玩家生命值变更日志回调。
        /// </summary>
        private void HandlePlayerHpChanged(int newHp)
        {
            GD.Print($"[Level_01] ❤️ 玩家血量变化: {newHp}");
        }

        #endregion
    }
}
