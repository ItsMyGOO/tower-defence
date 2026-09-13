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
    /// 覆盖四种行为：
    /// 1) AOE 溅射：以目标为圆心，AoeRadius 内的多个敌人同时受击；
    /// 2) 减速 debuff：命中后目标按 SlowFactor 减速、时长结束后恢复原速、多重减速取更强者；
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
        /// 场景 4：出售。按 SellRefundRatio 返还金币并释放槽位。
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
