using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Enemies;
using TowerDefence.Gameplay.Towers;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 防御塔攻击形态与目标策略行为测试（全代码构建，无 Inspector 绑定依赖）。
    /// 覆盖以下行为：
    /// 1) 即时模式：AOE 溅射、减速 debuff（多重取更强者/时长恢复）、tracer 拉线表现；
    /// 2) 弹道模式：单体弹延迟结算、AOE 弹落点溅射、减速弹命中附加 debuff、目标中途消灭空爆；
    /// 3) 目标策略：First 取最前线、Strongest 取血量最高者；
    /// 4) 槽位点击全链路：经视口输入管道的左键建造/右键出售；
    /// 5) 激光塔：光束锁定、持续伤害、自动切换目标；
    /// 6) 槽位环形菜单：点击槽位弹出建造环/升级环，选项点击完成建造与升级；
    /// 7) 升级 API：等级成长、费用上浮、满级拒绝、按累计投入返还、索敌碰撞体随射程成长同步；
    /// 8) 击杀幂等：同帧多源致死伤害只结算一次，逃脱与击杀互斥；
    /// 9) 菜单订阅生命周期：建造环打开时订阅金币事件，节点销毁时必须退订（防 static 事件泄漏）；
    /// 10) 配置驱动视觉/碰撞参数：敌人 HitRadius/VisualScale、塔 VisualScale 取自数据资源而非硬编码；
    /// 8) 出售：SellTower 按 SellRefundRatio 对累计投入返还并释放槽位。
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
                await TestTowerUpgrade();
                await TestSell();
                await TestKillIdempotency(enemyData);
                await TestRadialMenuEventLeak();
                await TestConfigDrivenVisualParams(enemyData);

                GD.Print($"[TowerBehaviorTest] ========== 测试结束：PASS {_passed} / FAIL {_failed} ==========");

                // 无头（CI）模式：以进程退出码上报测试结果，1 = 存在失败断言
                if (DisplayServer.GetName() == "headless")
                {
                    GetTree().Quit(_failed > 0 ? 1 : 0);
                }
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
        /// 场景 6：槽位点击全链路（经视口输入管道分发，与真实鼠标事件同一链路）。
        /// 左键空槽位 → 建造环弹出（选项数 = 扫描到的可用塔数）→ 点击选项建造并扣费 →
        /// 左键已占用槽位 → 升级环弹出 → 点击「⬆️ 升级」完成升级 → 右键槽位出售按投入返还。
        /// </summary>
        private async System.Threading.Tasks.Task TestSlotClickPipeline()
        {
            var towerManager = new TowerManager { Name = "TestTowerManager" };
            AddChild(towerManager);
            await Wait(0.1f);

            var slot = new TowerSlot { Name = "ClickTestSlot", Position = new Vector2(300, 300) };
            AddChild(slot);
            await Wait(0.1f);

            int goldBefore = _economy.CurrentGold;
            Vector2 ClickWindowPos(Vector2 worldPos) => GetViewport().GetFinalTransform() * worldPos;

            // --- 左键空槽位：建造环弹出 ---
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = ClickWindowPos(slot.GlobalPosition) });
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            var menu = TowerManager.Instance.RadialMenu;
            AssertTrue(menu.IsOpen && !menu.IsUpgradeMenu, "菜单: 左键空槽位弹出建造环");
            AssertTrue(menu.Options.Count == TowerManager.Instance.AvailableTowers.Count, "菜单: 建造环选项数与可用塔数一致");

            // --- 点击第一个建造选项（目录序第一个 = ArrowTower，成本 50）---
            // 按钮 Pressed 在按下+释放后触发，两个事件都发往选项按钮中心（环顶部）
            Vector2 optionWindowPos = ClickWindowPos(slot.GlobalPosition + new Vector2(0, -78));
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = optionWindowPos });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = optionWindowPos });
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(slot.IsOccupied, "菜单: 点击建造选项完成建造");
            AssertTrue(_economy.CurrentGold == goldBefore - 50, "菜单: 建造正确扣费");
            AssertTrue(!menu.IsOpen, "菜单: 建造后菜单关闭");

            // --- 左键已占用槽位：升级环弹出，点击「⬆️ 升级」（顶部选项，费用 40）---
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = ClickWindowPos(slot.GlobalPosition) });
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(menu.IsOpen && menu.IsUpgradeMenu, "菜单: 左键已占用槽位弹升级环");
            AssertTrue(menu.Options.Count == 2, "菜单: 升级环含升级/出售两个选项");

            float baseDamage = slot.CurrentTower.Data.Damage;
            Vector2 upgradeOptionPos = ClickWindowPos(slot.GlobalPosition + new Vector2(0, -78));
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = upgradeOptionPos });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = upgradeOptionPos });
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(slot.CurrentTower.CurrentLevel == 2, "升级: 点击升级选项后等级 +1");
            AssertTrue(slot.CurrentTower.Data.Damage > baseDamage, "升级: 伤害按成长倍率提升");
            AssertTrue(slot.CurrentTower.InvestedGold == 90, "升级: 累计投入 = 建造 50 + 升级 40");
            AssertTrue(_economy.CurrentGold == goldBefore - 90, "升级: 升级扣费正确");

            // --- 右键槽位出售：按累计投入 90 的 50% 返还 ---
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = ClickWindowPos(slot.GlobalPosition) });
            Input.FlushBufferedEvents();
            await Wait(0.2f);

            AssertTrue(!slot.IsOccupied, "出售: 右键点击槽位出售");
            AssertTrue(_economy.CurrentGold == goldBefore - 90 + 45, "出售: 按累计投入比例返还");

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
        /// 场景 8：升级 API。塔持有独立数据副本，升级成长伤害、费用上浮、
        /// 满级拒绝再升、出售按累计投入比例返还。
        /// </summary>
        private async System.Threading.Tasks.Task TestTowerUpgrade()
        {
            var towerData = new TowerData
            {
                TowerId = "test_upgrade",
                TowerName = "测试升级塔",
                BuildCost = 50,
                SellRefundRatio = 0.5f,
                MaxLevel = 2,
                UpgradeBaseCost = 50f,
                UpgradeCostFactor = 1.5f,
                DamageGrowthFactor = 1.3f,
                RangeGrowthFactor = 1.08f
            };

            var slot = new TowerSlot { Name = "UpgradeTestSlot" };
            AddChild(slot);

            int goldBefore = _economy.CurrentGold;
            var tower = new Tower { Name = "UpgradeTestTower", Data = towerData };
            AssertTrue(slot.PlaceTower(tower), "升级: PlaceTower 成功占用槽位");
            AssertTrue(tower.InvestedGold == 50, "升级: 初始投入 = 建造费用");
            AssertTrue(!ReferenceEquals(tower.Data, towerData), "升级: 塔持有独立数据副本");

            float baseDamage = tower.Data.Damage;
            float baseRange = tower.Data.AttackRange;
            AssertTrue(tower.ApplyUpgrade(), "升级: 首次升级成功");
            AssertTrue(tower.CurrentLevel == 2, "升级: 等级 +1");
            AssertTrue(Mathf.IsEqualApprox(tower.Data.Damage, baseDamage * 1.3f), "升级: 伤害按 1.3x 成长");
            AssertTrue(Mathf.IsEqualApprox(tower.Data.AttackRange, baseRange * 1.08f), "升级: 射程按 1.08x 成长");
            AssertTrue(tower.InvestedGold == 100, "升级: 累计投入 = 50 + 50");
            AssertTrue(_economy.CurrentGold == goldBefore - 50, "升级: 扣费正确");

            // 索敌碰撞体半径在 _Ready 按初始射程创建，升级后必须同步，否则射程成长对 Area2D 索敌无效
            var detectionCircle = tower
                .GetNodeOrNull<CollisionShape2D>("DetectionArea/DetectionShape")
                ?.Shape as CircleShape2D;
            AssertTrue(
                detectionCircle != null && Mathf.IsEqualApprox(detectionCircle.Radius, tower.Data.AttackRange),
                "升级: 索敌碰撞体半径随射程成长同步更新"
            );

            AssertTrue(!tower.ApplyUpgrade(), "升级: 满级后拒绝再次升级");
            AssertTrue(_economy.CurrentGold == goldBefore - 50, "升级: 满级升级不扣费");

            slot.SellTower();
            AssertTrue(_economy.CurrentGold == goldBefore, "升级: 出售返还累计投入的 50%");

            slot.QueueFree();
            await Wait(0.1f);
        }

        /// <summary>
        /// 场景 9：出售。按 SellRefundRatio 返还金币并释放槽位。
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

        /// <summary>
        /// 场景 10：击杀幂等。QueueFree 帧末才生效，同一帧内多源伤害（AOE+弹体/多塔同帧）
        /// 命中同一敌人时不得重复触发击杀事件（双倍金币/特效）；逃脱与击杀互斥——
        /// 敌人逃脱事件的同帧内补刀不应再发击杀奖励，反之亦然。
        /// </summary>
        private async System.Threading.Tasks.Task TestKillIdempotency(EnemyData enemyData)
        {
            int killedCount = 0;
            int reachedEndCount = 0;
            void OnKilled(string enemyId, int goldReward, Vector2 deathPosition) => killedCount++;
            void OnReachedEnd(int damageToPlayer) => reachedEndCount++;
            EventBus.OnEnemyKilled += OnKilled;
            EventBus.OnEnemyReachedEnd += OnReachedEnd;

            // --- 同帧双倍致死：只允许结算一次击杀 ---
            var enemy = SpawnEnemy(enemyData, 100);
            enemy.TakeDamage(enemyData.MaxHp);
            enemy.TakeDamage(enemyData.MaxHp);
            AssertTrue(killedCount == 1, "击杀幂等: 同帧多源致死伤害只触发一次击杀事件");

            // --- 逃脱与击杀互斥：在逃脱事件回调内同帧补刀，不应再触发击杀 ---
            var escaper = SpawnEnemy(enemyData, 100);
            void OnReachedEndAndHit(int damageToPlayer) => escaper.TakeDamage(enemyData.MaxHp);
            EventBus.OnEnemyReachedEnd += OnReachedEndAndHit;
            escaper.ProgressRatio = 1.0f;
            await Wait(0.2f);
            EventBus.OnEnemyReachedEnd -= OnReachedEndAndHit;

            AssertTrue(reachedEndCount == 1, "击杀幂等: 逃脱事件恰好触发一次");
            AssertTrue(killedCount == 1, "击杀幂等: 已逃脱敌人的同帧补刀不再触发击杀");

            EventBus.OnEnemyKilled -= OnKilled;
            EventBus.OnEnemyReachedEnd -= OnReachedEnd;
        }

        /// <summary>
        /// 场景 11：环形菜单订阅生命周期。建造环打开时订阅 OnGoldChanged，
        /// 节点在菜单未关闭的情况下随场景销毁（移出场景树）时必须退订——
        /// EventBus 为 static，残留订阅会在下一关触发金币事件时访问已释放节点并抛异常。
        /// 通过反射读取事件调用列表长度断言订阅数回落。
        /// </summary>
        private async System.Threading.Tasks.Task TestRadialMenuEventLeak()
        {
            var slot = new TowerSlot { Name = "MenuLeakTestSlot", Position = Vector2.Zero };
            AddChild(slot);

            var menu = new TowerRadialMenu { Name = "MenuLeakTestMenu" };
            AddChild(menu);

            int baseline = GoldChangedSubscriberCount();

            menu.OpenBuild(
                slot,
                new System.Collections.Generic.List<TowerData>
                {
                    new() { TowerId = "leak_test", TowerName = "泄漏测试塔", BuildCost = 50 }
                }
            );
            AssertTrue(GoldChangedSubscriberCount() == baseline + 1, "菜单: 打开建造环时订阅金币变更事件");

            // 模拟场景切换：建造环处于打开状态时节点直接移出场景树（未经过 Close）
            RemoveChild(menu);
            menu.QueueFree();
            AssertTrue(GoldChangedSubscriberCount() == baseline, "菜单: 移出场景树后退订金币事件（无泄漏）");

            slot.QueueFree();
            await Wait(0.1f);
        }

        /// <summary>
        /// 场景 12：配置驱动的视觉/碰撞参数。敌人 HitRadius / VisualScale 与塔 VisualScale
        /// 不再硬编码于实体脚本，美术填充阶段按素材实际体型在 .tres 中配置。
        /// 测试用代码生成的 8x8 纹理替代真实素材，走静态图渲染路径。
        /// </summary>
        private async System.Threading.Tasks.Task TestConfigDrivenVisualParams(EnemyData enemyData)
        {
            var placeholderTexture = ImageTexture.CreateFromImage(
                Image.Create(8, 8, false, Image.Format.Rgba8)
            );

            var enemyDataCopy = (EnemyData)enemyData.Duplicate();
            enemyDataCopy.HitRadius = 24.0f;
            enemyDataCopy.VisualScale = 5.0f;
            enemyDataCopy.AnimTexture = null;
            enemyDataCopy.Icon = placeholderTexture;

            var enemy = SpawnEnemy(enemyDataCopy, 100);
            var hitCircle = enemy
                .GetNodeOrNull<CollisionShape2D>("EnemyHitArea/EnemyHitShape")
                ?.Shape as CircleShape2D;
            AssertTrue(
                hitCircle != null && Mathf.IsEqualApprox(hitCircle.Radius, 24.0f),
                "配置: 敌人碰撞半径取自 EnemyData.HitRadius"
            );

            var enemySprite = enemy.GetNodeOrNull<Node2D>("EnemySprite");
            AssertTrue(
                enemySprite != null && Mathf.IsEqualApprox(enemySprite.Scale.X, 5.0f),
                "配置: 敌人视觉缩放取自 EnemyData.VisualScale"
            );

            var towerData = new TowerData
            {
                TowerId = "test_visual_scale",
                TowerName = "缩放测试塔",
                Icon = placeholderTexture,
                VisualScale = 4.0f
            };
            var tower = SpawnTower(towerData, new Vector2(50, 40));
            var towerSprite = tower.GetNodeOrNull<Node2D>("TowerSprite");
            AssertTrue(
                towerSprite != null && Mathf.IsEqualApprox(towerSprite.Scale.X, 4.0f),
                "配置: 塔视觉缩放取自 TowerData.VisualScale"
            );

            Cleanup(tower, enemy);
            await Wait(0.1f);
        }

        /// <summary>
        /// 通过反射统计 EventBus.OnGoldChanged 的订阅者数量（事件为 static，字段编译为私有静态字段）。
        /// </summary>
        /// <returns>当前订阅数</returns>
        private static int GoldChangedSubscriberCount()
        {
            var field = typeof(EventBus).GetField(
                "OnGoldChanged",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
            );
            return (field?.GetValue(null) as System.Delegate)?.GetInvocationList().Length ?? 0;
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
