using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Config.Towers;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Enemies;
using TowerDefence.Gameplay.Towers;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 防御塔攻击形态与目标策略行为测试（全代码构建，无 Inspector 绑定依赖）。
    /// 覆盖两种攻击交付模式与四种行为：
    /// 1) 即时模式：AOE 溅射、减速 debuff（多重取更强者/时长恢复）、tracer 拉线表现；
    /// 2) 弹道模式：单体弹延迟结算、AOE 弹落点溅射、减速弹命中附加 debuff、目标中途消灭空爆；
    /// 3) 目标策略：First 取最前线、Strongest 取血量最高者；
    /// 4) 出售：SellTower 按 SellRefundRatio 返还金币并释放槽位。
    /// 全部断言结果打印 ✅/❌ 与最终汇总，无头模式可直接运行。
    /// </summary>
    public partial class TowerBehaviorTest : Node2D
    {
        private int _passed;
        private int _failed;

        private Path2D _path;
        private EconomyManager _economy;

        /// <summary>
        /// 节点就绪后启动异步测试序列。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[TowerBehaviorTest] ========== 塔行为测试启动 ==========");
            _ = RunAllAsync();
        }

        /// <summary>
        /// 测试主序列：依次执行 AOE / 减速 / 目标策略 / 出售四个场景并输出汇总。
        /// </summary>
        private async System.Threading.Tasks.Task RunAllAsync()
        {
            try
            {
                var enemyData = ResourceLoader.Load<EnemyData>("res://Tests/Data/Enemies/Test_SlimeData.tres");
                if (enemyData == null)
                {
                    GD.PrintErr("[TowerBehaviorTest] ❌ 无法加载 Test_SlimeData.tres，测试中止。");
                    return;
                }

                SetupEconomy();
                SetupPath();

                await TestAoeSplash(enemyData);
                await TestSlowDebuff(enemyData);
                await TestTargetingModes(enemyData);
                await TestProjectileDelivery(enemyData);
                await TestTracerVisual(enemyData);
                await TestSlotClickPipeline();
                await TestBeamLaser(enemyData);
                await TestSell();

                GD.Print($"[TowerBehaviorTest] ========== 测试结束：PASS {_passed} / FAIL {_failed} ==========");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[TowerBehaviorTest] ❌ 测试序列异常中止: {ex}");
            }
        }

        #region 测试场景

        /// <summary>
        /// 场景 1：AOE 溅射。两个相邻敌人都应被一次攻击命中。
        /// </summary>
        private async System.Threading.Tasks.Task TestAoeSplash(EnemyData enemyData)
        {
            var e1 = SpawnEnemy(enemyData, 100);
            var e2 = SpawnEnemy(enemyData, 130);

            var data = new TowerData
            {
                TowerId = "test_aoe",
                TowerName = "测试AOE塔",
                Kind = TowerKind.Aoe,
                Targeting = TargetingMode.First,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 0.1f,
                AoeRadius = 50f
            };
            var tower = SpawnTower(data, new Vector2(115, 40));

            await Wait(0.6f);

            AssertTrue(e1.CurrentHp < enemyData.MaxHp, "AOE: 目标(最前线)被命中");
            AssertTrue(e2.CurrentHp < enemyData.MaxHp, "AOE: 溅射半径内相邻敌人被命中");

            Cleanup(tower, e1, e2);
        }

        /// <summary>
        /// 场景 2：减速 debuff。命中减速 → 时长结束恢复 → 多重减速取更强者。
        /// </summary>
        private async System.Threading.Tasks.Task TestSlowDebuff(EnemyData enemyData)
        {
            var enemy = SpawnEnemy(enemyData, 100);

            var data = new TowerData
            {
                TowerId = "test_slow",
                TowerName = "测试减速塔",
                Kind = TowerKind.Slow,
                Targeting = TargetingMode.First,
                AttackRange = 500f,
                Damage = 5f,
                AttackInterval = 0.1f,
                SlowFactor = 0.5f,
                SlowDuration = 1.0f
            };
            var tower = SpawnTower(data, new Vector2(100, 40));

            await Wait(0.5f);
            AssertTrue(Mathf.IsEqualApprox(enemy.SpeedFactor, 0.5f), "减速: 命中后速度倍率降至 SlowFactor");
            AssertTrue(enemy.CurrentHp < enemyData.MaxHp, "减速: 命中同时造成伤害");

            tower.QueueFree();
            await Wait(0.1f);

            enemy.ApplySlow(0.8f, 5.0f);
            AssertTrue(Mathf.IsEqualApprox(enemy.SpeedFactor, 0.5f), "减速: 多重减速保留更强者(0.5 < 0.8)");

            // 时长语义：取新时长与剩余时长的更长者（当前剩余 5s），弱短减速不提前结束强长减速
            enemy.ApplySlow(0.3f, 0.4f);
            AssertTrue(Mathf.IsEqualApprox(enemy.SpeedFactor, 0.3f), "减速: 更强的减速(0.3)生效");

            await Wait(0.8f);
            AssertTrue(Mathf.IsEqualApprox(enemy.SpeedFactor, 0.3f), "减速: 弱短减速(0.4s)不提前结束强长减速(5s)");

            await Wait(4.6f);
            AssertTrue(Mathf.IsEqualApprox(enemy.SpeedFactor, 1.0f), "减速: 时长结束后恢复原速");

            Cleanup(enemy);
        }

        /// <summary>
        /// 场景 3：目标策略。First 取推进最远者，Strongest 取当前血量最高者。
        /// Nearest 与 First/Strongest 共用同一评分选择机制，仅评分函数不同，不做时序敏感断言。
        /// </summary>
        private async System.Threading.Tasks.Task TestTargetingModes(EnemyData enemyData)
        {
            // --- First：推进最远的敌人被优先攻击 ---
            var eFront = SpawnEnemy(enemyData, 200);
            var eBack = SpawnEnemy(enemyData, 100);

            var firstData = new TowerData
            {
                TowerId = "test_first",
                TowerName = "测试最前线塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.First,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 0.1f
            };
            var firstTower = SpawnTower(firstData, new Vector2(150, 40));

            await Wait(0.4f);
            AssertTrue(eFront.CurrentHp < enemyData.MaxHp, "目标策略 First: 推进最远者被攻击");
            AssertTrue(Mathf.IsEqualApprox(eBack.CurrentHp, enemyData.MaxHp), "目标策略 First: 后方敌人未被攻击");
            firstTower.QueueFree();

            // --- Strongest：血量最高的敌人被优先攻击（eFront 已被上一座塔打伤）---
            var strongestData = new TowerData
            {
                TowerId = "test_strongest",
                TowerName = "测试最强塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.Strongest,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 0.1f
            };
            var strongestTower = SpawnTower(strongestData, new Vector2(150, 40));

            await Wait(0.4f);
            float frontHpBefore = eFront.CurrentHp;
            AssertTrue(eBack.CurrentHp < enemyData.MaxHp, "目标策略 Strongest: 血量最高者(满血)被攻击");
            AssertTrue(Mathf.IsEqualApprox(eFront.CurrentHp, frontHpBefore), "目标策略 Strongest: 血量低者未被继续攻击");

            Cleanup(strongestTower, eFront, eBack);
        }

        /// <summary>
        /// 场景 4：弹道攻击（Mode = Projectile，三座正式塔的实际交付方式）。
        /// 单体弹延迟结算、AOE 弹落点溅射、减速弹命中附加 debuff、目标中途消灭后弹体空爆。
        /// </summary>
        private async System.Threading.Tasks.Task TestProjectileDelivery(EnemyData enemyData)
        {
            // --- 单体弹（箭塔形态）：伤害延迟到弹体命中 ---
            var e1 = SpawnEnemy(enemyData, 100);
            var arrowData = new TowerData
            {
                TowerId = "test_arrow_proj",
                TowerName = "测试箭弹塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Projectile,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 0.1f,
                ProjectileSpeed = 600f
            };
            var arrowTower = SpawnTower(arrowData, new Vector2(100, 40));

            // 首发于 0.1s 发射、约 0.17s 命中：0.12s 处弹体在飞、伤害尚未结算
            await Wait(0.12f);
            AssertTrue(Mathf.IsEqualApprox(e1.CurrentHp, enemyData.MaxHp), "弹道: 单体弹发射瞬间不结算伤害");
            await Wait(0.5f);
            AssertTrue(e1.CurrentHp < enemyData.MaxHp, "弹道: 单体弹命中后结算伤害");

            arrowTower.QueueFree();
            Cleanup(e1);

            // --- AOE 弹（炮塔形态）：落点溅射命中相邻敌人 ---
            var e2 = SpawnEnemy(enemyData, 100);
            var e3 = SpawnEnemy(enemyData, 130);
            var cannonData = new TowerData
            {
                TowerId = "test_cannon_proj",
                TowerName = "测试炮弹塔",
                Kind = TowerKind.Aoe,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Projectile,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 0.1f,
                AoeRadius = 50f,
                ProjectileSpeed = 500f
            };
            var cannonTower = SpawnTower(cannonData, new Vector2(115, 40));

            await Wait(0.6f);
            AssertTrue(e2.CurrentHp < enemyData.MaxHp && e3.CurrentHp < enemyData.MaxHp, "弹道: AOE 弹落点溅射命中相邻敌人");

            Cleanup(cannonTower, e2, e3);

            // --- 减速弹（冰霜塔形态）：命中附加减速 debuff ---
            var e4 = SpawnEnemy(enemyData, 100);
            var frostData = new TowerData
            {
                TowerId = "test_frost_proj",
                TowerName = "测试冰弹塔",
                Kind = TowerKind.Slow,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Projectile,
                AttackRange = 500f,
                Damage = 5f,
                AttackInterval = 0.1f,
                SlowFactor = 0.5f,
                SlowDuration = 2.0f,
                ProjectileSpeed = 500f
            };
            var frostTower = SpawnTower(frostData, new Vector2(100, 40));

            await Wait(0.6f);
            AssertTrue(Mathf.IsEqualApprox(e4.SpeedFactor, 0.5f), "弹道: 减速弹命中后附加减速");

            Cleanup(frostTower, e4);

            // --- 空爆：目标在弹体飞行途中被消灭，流程不异常 ---
            var e5 = SpawnEnemy(enemyData, 100);
            var fizzData = new TowerData
            {
                TowerId = "test_fizz",
                TowerName = "测试空爆塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Projectile,
                AttackRange = 500f,
                Damage = 10f,
                AttackInterval = 1.0f,
                ProjectileSpeed = 300f
            };
            var fizzTower = SpawnTower(fizzData, new Vector2(100, 40));

            await Wait(0.15f);
            e5.TakeDamage(99999f);
            await Wait(0.8f);
            AssertTrue(!IsInstanceValid(e5), "弹道: 目标飞行途中被消灭后弹体空爆无异常");

            Cleanup(fizzTower);
        }

        /// <summary>
        /// 场景 5：即时模式 tracer 表现。攻击时应存在 Line2D 拉线子节点（激光塔预留路径的基础设施）。
        /// </summary>
        private async System.Threading.Tasks.Task TestTracerVisual(EnemyData enemyData)
        {
            var enemy = SpawnEnemy(enemyData, 100);
            var data = new TowerData
            {
                TowerId = "test_tracer",
                TowerName = "测试即时塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Instant,
                AttackRange = 500f,
                Damage = 5f,
                AttackInterval = 0.1f
            };
            var tower = SpawnTower(data, new Vector2(100, 40));

            // 0.1s/0.2s 两次攻击，0.2s 的 tracer 存活至约 0.32s，0.25s 处必存在
            await Wait(0.25f);

            bool hasTracer = false;
            foreach (Node child in tower.GetChildren())
            {
                if (child is Line2D)
                {
                    hasTracer = true;
                    break;
                }
            }

            AssertTrue(hasTracer, "即时模式: 攻击时生成 tracer 拉线子节点");

            Cleanup(tower, enemy);
        }

        /// <summary>
        /// 场景 6：槽位点击全链路（经视口 push_input 分发，与真实鼠标事件同一链路，
        /// 回归「点击建造位置无反应」缺陷——占位 Control 吞事件导致物理拾取失效的问题）。
        /// </summary>
        private async System.Threading.Tasks.Task TestSlotClickPipeline()
        {
            var towerManager = new TowerManager { Name = "TestTowerManager" };
            AddChild(towerManager);

            var towerData = new TowerData
            {
                TowerId = "test_click",
                TowerName = "测试点击塔",
                BuildCost = 50,
                SellRefundRatio = 0.5f
            };
            TowerManager.Instance.CurrentSelectedTowerData = towerData;

            var slot = new TowerSlot { Name = "ClickTestSlot", Position = new Vector2(300, 300) };
            AddChild(slot);
            await Wait(0.1f);

            int goldBefore = _economy.CurrentGold;

            // 事件坐标是窗口坐标：引擎分发到 _Input 前会经 GetFinalTransform 的逆变换
            // 转为视口坐标（无头窗口存在内容缩放，真机 1280x720 下为恒等），故此处需正向变换反推窗口坐标
            Vector2 slotWindowPos = GetViewport().GetFinalTransform() * slot.GlobalPosition;

            var press = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = slotWindowPos
            };
            Input.ParseInputEvent(press);
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(slot.IsOccupied, "点击: 左键点击槽位完成建造");
            AssertTrue(_economy.CurrentGold == goldBefore - 50, "点击: 建造正确扣费");

            var rightPress = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = true,
                Position = slotWindowPos
            };
            Input.ParseInputEvent(rightPress);
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(!slot.IsOccupied, "点击: 右键点击槽位出售");
            AssertTrue(_economy.CurrentGold == goldBefore - 50 + 25, "点击: 出售返还金币");

            slot.QueueFree();
            await Wait(0.1f);
        }

        /// <summary>
        /// 场景 7：激光塔（Mode = Beam）。光束锁定最前线目标持续伤害，
        /// 击杀后自动切换到下一个目标，锁定期间存在光束 Line2D。
        /// </summary>
        private async System.Threading.Tasks.Task TestBeamLaser(EnemyData enemyData)
        {
            var e1 = SpawnEnemy(enemyData, 100);
            var e2 = SpawnEnemy(enemyData, 60);

            var data = new TowerData
            {
                TowerId = "test_beam",
                TowerName = "测试激光塔",
                Kind = TowerKind.Single,
                Targeting = TargetingMode.First,
                Mode = AttackMode.Beam,
                AttackRange = 500f,
                Damage = 100f,
                AttackInterval = 0.5f,
                BeamWidth = 4.0f
            };
            var tower = SpawnTower(data, new Vector2(130, 40));

            await Wait(0.3f);
            AssertTrue(ReferenceEquals(tower.BeamTarget, e1), "激光: 光束锁定推进最远的目标");
            AssertTrue(e1.CurrentHp < enemyData.MaxHp && e1.CurrentHp > 0, "激光: 持续伤害进行中（非瞬杀）");
            AssertTrue(HasBeamLine(tower), "激光: 锁定期间存在光束 Line2D");

            // DPS 100 对 100 血敌人约 1.0s 击杀；随后光束应自动切换到 60 血的第二个目标
            await Wait(1.2f);
            AssertTrue(!IsInstanceValid(e1), "激光: 持续伤害击杀首个目标");
            AssertTrue(ReferenceEquals(tower.BeamTarget, e2), "激光: 目标死亡后自动切换至下一个");

            await Wait(1.5f);
            AssertTrue(!IsInstanceValid(e2), "激光: 切换后继续击杀下一目标");

            Cleanup(tower);
        }

        /// <summary>
        /// 检查塔下是否存在可见的光束 Line2D 子节点。
        /// </summary>
        /// <param name="tower">目标塔</param>
        /// <returns>存在可见光束返回 true</returns>
        private static bool HasBeamLine(Tower tower)
        {
            foreach (Node child in tower.GetChildren())
            {
                if (child is Line2D line && line.Visible)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 场景 8：出售。按 SellRefundRatio 返还金币并释放槽位。
        /// </summary>
        private async System.Threading.Tasks.Task TestSell()
        {
            var towerData = new TowerData
            {
                TowerId = "test_sell",
                TowerName = "测试出售塔",
                BuildCost = 50,
                SellRefundRatio = 0.5f
            };

            var slot = new TowerSlot { Name = "SellTestSlot" };
            AddChild(slot);

            int goldBefore = _economy.CurrentGold;
            var tower = new Tower { Name = "SellTestTower", Data = towerData };
            AssertTrue(slot.PlaceTower(tower), "出售: PlaceTower 成功占用槽位");

            slot.SellTower();

            AssertTrue(_economy.CurrentGold == goldBefore + 25, "出售: 按 SellRefundRatio 返还金币 (+25)");
            AssertTrue(!slot.IsOccupied, "出售: 槽位恢复空闲");

            slot.QueueFree();
            await Wait(0.1f);
        }

        #endregion

        #region 构建与断言辅助

        /// <summary>
        /// 创建经济管理器节点（无关卡驱动场景的兜底实例）。
        /// </summary>
        private void SetupEconomy()
        {
            _economy = new EconomyManager
            {
                Name = "TestEconomy",
                InitialGold = 100,
                InitialHp = 10
            };
            AddChild(_economy);
        }

        /// <summary>
        /// 构建一条 600 像素长的水平直线路径。
        /// </summary>
        private void SetupPath()
        {
            _path = new Path2D { Name = "TestPath" };
            var curve = new Curve2D();
            curve.AddPoint(Vector2.Zero);
            curve.AddPoint(new Vector2(600, 0));
            _path.Curve = curve;
            AddChild(_path);
        }

        /// <summary>
        /// 在路径的指定进度处生成一个测试敌人。
        /// </summary>
        /// <param name="data">敌人配置数据</param>
        /// <param name="progress">初始路径进度（像素）</param>
        /// <returns>生成的敌人实例</returns>
        private Enemy SpawnEnemy(EnemyData data, float progress)
        {
            var enemy = new Enemy { Name = $"TestEnemy_{progress:F0}", Data = data };
            _path.AddChild(enemy);
            enemy.Progress = progress;
            return enemy;
        }

        /// <summary>
        /// 在指定位置生成一座测试防御塔。
        /// </summary>
        /// <param name="data">塔配置数据</param>
        /// <param name="position">塔的世界坐标位置</param>
        /// <returns>生成的塔实例</returns>
        private Tower SpawnTower(TowerData data, Vector2 position)
        {
            var tower = new Tower { Name = $"TestTower_{data.TowerId}", Data = data };
            AddChild(tower);
            tower.Position = position;
            return tower;
        }

        /// <summary>
        /// 等待指定秒数（真实时间）。
        /// </summary>
        /// <param name="seconds">等待时长</param>
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
                GD.Print($"[TowerBehaviorTest] ✅ PASS {name}");
            }
            else
            {
                _failed++;
                GD.PrintErr($"[TowerBehaviorTest] ❌ FAIL {name}");
            }
        }

        /// <summary>
        /// 释放本轮测试使用的节点。
        /// </summary>
        /// <param name="nodes">待释放节点</param>
        private void Cleanup(params Node[] nodes)
        {
            foreach (Node node in nodes)
            {
                if (node != null && IsInstanceValid(node))
                {
                    node.QueueFree();
                }
            }

            _ = Wait(0.1f);
        }

        #endregion
    }
}
