using System.Collections.Generic;
using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Gameplay.Enemies;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔实体节点，负责范围内索敌与周期性攻击。
    /// 以 TowerData Resource 为配置来源，在 _Ready 中动态挂载可视化、
    /// 攻击定时器与范围碰撞体组件，通过 Area2D 信号维护目标列表并执行攻击。
    /// 攻击表现按 Data.Mode 分流：Projectile 发射弹体延迟到抵达结算（箭/炮/冰霜塔），
    /// Instant 瞬间结算并绘制 tracer（激光塔规划中的基础路径）。
    /// </summary>
    public partial class Tower : Node2D
    {
        /// <summary>
        /// 获取或设置防御塔的配置数据资源。
        /// 实例化后必须在加入场景树前赋值（通过属性注入或在 Inspector 中指定）。
        /// </summary>
        [Export] public TowerData Data { get; set; }

        private Sprite2D _sprite;
        private Timer _attackTimer;
        private Area2D _detectionArea;
        private CollisionShape2D _detectionShape;

        private readonly List<Enemy> _targetsInRange = new();

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 校验 Data 配置并动态创建所有子组件（Sprite2D、Timer、Area2D），
        /// 完成信号绑定后启动攻击循环。
        /// </summary>
        public override void _Ready()
        {
            if (Data == null)
            {
                GD.PrintErr($"[Tower] TowerData 未配置！节点：{Name}");
                QueueFree();
                return;
            }

            SetupSprite();
            SetupAttackTimer();
            SetupDetectionArea();

            GD.Print($"[Tower] 初始化完成: {Data.TowerName} | 范围={Data.AttackRange} | 伤害={Data.Damage} | 间隔={Data.AttackInterval}s");
        }

        /// <summary>
        /// 创建并配置用于显示塔图标的 Sprite2D 子节点。
        /// 若 Data.Icon 为空则以占位 ColorRect 替代，便于无资源场景下的调试。
        /// </summary>
        private void SetupSprite()
        {
            _sprite = new Sprite2D
            {
                Name = "TowerSprite",
                Texture = Data.Icon
            };
            AddChild(_sprite);

            if (Data.Icon == null)
            {
                var placeholder = new ColorRect
                {
                    Size = new Vector2(40, 40),
                    Color = new Color(0.3f, 0.5f, 1.0f),
                    Position = new Vector2(-20, -20)
                };
                _sprite.AddChild(placeholder);
            }
        }

        /// <summary>
        /// 创建并配置攻击间隔定时器。
        /// WaitTime 取自 Data.AttackInterval，循环触发并自动启动，
        /// Timeout 时执行一次索敌攻击判定。
        /// </summary>
        private void SetupAttackTimer()
        {
            _attackTimer = new Timer
            {
                Name = "AttackTimer",
                WaitTime = Data.AttackInterval,
                OneShot = false,
                Autostart = true
            };
            _attackTimer.Timeout += TryAttackTarget;
            AddChild(_attackTimer);
        }

        /// <summary>
        /// 创建并配置范围检测用 Area2D 与圆形碰撞体。
        /// 碰撞半径取自 Data.AttackRange；通过监听 AreaEntered / AreaExited
        /// 信号维护当前进入攻击范围的 Enemy 集合。
        /// </summary>
        private void SetupDetectionArea()
        {
            _detectionArea = new Area2D
            {
                Name = "DetectionArea"
            };
            AddChild(_detectionArea);

            _detectionShape = new CollisionShape2D
            {
                Name = "DetectionShape",
                Shape = new CircleShape2D
                {
                    Radius = Data.AttackRange
                }
            };
            _detectionArea.AddChild(_detectionShape);

            _detectionArea.AreaEntered += OnEnemyAreaEntered;
            _detectionArea.AreaExited += OnEnemyAreaExited;
        }

        /// <summary>
        /// 当敌人的 Area2D 进入塔的攻击范围时触发。
        /// 从进入的 Area2D 向上查找 Enemy 宿主节点，加入目标列表并订阅其 TreeExiting，
        /// 以便敌人被销毁（击杀或逃脱）时能及时从列表移除。
        /// </summary>
        /// <param name="area">进入检测范围的 Area2D 节点</param>
        private void OnEnemyAreaEntered(Area2D area)
        {
            var enemy = area.GetOwnerOrNull<Enemy>() ?? area.GetParent() as Enemy;
            if (enemy == null || _targetsInRange.Contains(enemy))
            {
                return;
            }

            _targetsInRange.Add(enemy);
            enemy.TreeExiting += () => RemoveTarget(enemy);
            GD.Print($"[Tower] 目标进入范围: {enemy.Name} | 当前目标数: {_targetsInRange.Count}");
        }

        /// <summary>
        /// 当敌人的 Area2D 离开塔的攻击范围时触发。
        /// 将对应 Enemy 从目标列表中移除。
        /// </summary>
        /// <param name="area">离开检测范围的 Area2D 节点</param>
        private void OnEnemyAreaExited(Area2D area)
        {
            var enemy = area.GetOwnerOrNull<Enemy>() ?? area.GetParent() as Enemy;
            if (enemy == null)
            {
                return;
            }

            RemoveTarget(enemy);
            GD.Print($"[Tower] 目标离开范围: {enemy.Name} | 当前目标数: {_targetsInRange.Count}");
        }

        /// <summary>
        /// 即时攻击 tracer 拉线的存留时长（秒），到时淡出并销毁。
        /// </summary>
        private const float TracerDuration = 0.12f;

        /// <summary>
        /// 每次攻击间隔到点时的攻击入口。
        /// 先从目标列表中按 Data.Targeting 策略选出目标：
        /// - Mode = Projectile：发射弹体，抵达目标/落点时才结算（箭塔/炮塔/冰霜塔）；
        /// - Mode = Instant：瞬间结算并绘制 tracer 拉线（为后续激光塔"光束锁定持续伤害"预留的基础路径，
        ///   当前无内置塔使用，由行为测试覆盖）。
        /// </summary>
        private void TryAttackTarget()
        {
            PruneInvalidTargets();
            var target = SelectTarget();
            if (target == null)
            {
                return;
            }

            if (Data.Mode == AttackMode.Projectile)
            {
                LaunchProjectile(target);
                return;
            }

            switch (Data.Kind)
            {
                case TowerKind.Aoe:
                    AttackAoe(target);
                    break;
                case TowerKind.Slow:
                    AttackSlow(target);
                    break;
                default:
                    AttackSingle(target);
                    break;
            }

            SpawnTracer(target);
        }

        /// <summary>
        /// 发射一枚弹体飞向目标，伤害/debuff 延迟到弹体抵达时由 Projectile 结算。
        /// 弹体挂载到塔的父节点（塔槽）而非塔自身，出售塔不会连带回收已发射的弹体。
        /// </summary>
        /// <param name="target">锁定的目标</param>
        private void LaunchProjectile(Enemy target)
        {
            var projectile = new Projectile();
            projectile.Initialize(Data, target);

            Node host = GetParent();
            if (host == null)
            {
                host = this;
            }
            host.AddChild(projectile);
            projectile.GlobalPosition = GlobalPosition;

            GD.Print($"[Tower] 发射弹体 → {target.Name} | 弹速={Data.ProjectileSpeed}");
        }

        /// <summary>
        /// 即时命中的 tracer 表现：从塔到目标绘制一条按 TowerData.AttackColor 着色的拉线，
        /// 短暂淡出后自动销毁。仅 Mode = Instant 时调用。
        /// </summary>
        /// <param name="target">本次攻击的目标</param>
        private void SpawnTracer(Enemy target)
        {
            if (target == null || !IsInstanceValid(target))
            {
                return;
            }

            var tracer = new Line2D
            {
                Name = "AttackTracer",
                Width = 3.0f,
                DefaultColor = new Color(Data.AttackColor, 0.9f),
                Points = new[] { Vector2.Zero, target.GlobalPosition - GlobalPosition }
            };
            AddChild(tracer);

            Tween tween = CreateTween();
            tween.TweenProperty(tracer, "modulate:a", 0.0f, TracerDuration);
            tween.Finished += () =>
            {
                if (IsInstanceValid(tracer))
                {
                    tracer.QueueFree();
                }
            };
        }

        /// <summary>
        /// 清理目标列表中已被销毁的敌人实例，保证索敌只在有效实例上进行。
        /// </summary>
        private void PruneInvalidTargets()
        {
            _targetsInRange.RemoveAll(enemy => enemy == null || !IsInstanceValid(enemy));
        }

        /// <summary>
        /// 按 Data.Targeting 策略从索敌范围内选择本次攻击的目标。
        /// First 取沿路径推进最远者（最前线），Nearest 取距塔最近者，Strongest 取当前血量最高者。
        /// </summary>
        /// <returns>选中的目标；范围内无有效敌人时为 null</returns>
        private Enemy SelectTarget()
        {
            if (_targetsInRange.Count == 0)
            {
                return null;
            }

            Enemy best = _targetsInRange[0];
            float bestScore = ScoreTarget(best);

            for (int i = 1; i < _targetsInRange.Count; i++)
            {
                float score = ScoreTarget(_targetsInRange[i]);
                if (score > bestScore)
                {
                    best = _targetsInRange[i];
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>
        /// 计算候选目标在当前目标策略下的评分，评分最高者被选中。
        /// Nearest 模式以距离平方的负值参与比较，从而统一为"分高者胜"。
        /// </summary>
        /// <param name="enemy">候选目标</param>
        /// <returns>策略评分（越大越优先）</returns>
        private float ScoreTarget(Enemy enemy)
        {
            switch (Data.Targeting)
            {
                case TargetingMode.Nearest:
                    return -GlobalPosition.DistanceSquaredTo(enemy.GlobalPosition);
                case TargetingMode.Strongest:
                    return enemy.CurrentHp;
                case TargetingMode.First:
                default:
                    return enemy.ProgressRatio;
            }
        }

        /// <summary>
        /// 单体攻击：仅对目标造成 Data.Damage 伤害。
        /// </summary>
        /// <param name="target">选中的目标</param>
        private void AttackSingle(Enemy target)
        {
            target.TakeDamage(Data.Damage);
            GD.Print($"[Tower] 攻击 {target.Name} | 伤害={Data.Damage} | 目标剩余HP={target.CurrentHp:F1}");
        }

        /// <summary>
        /// 范围攻击：以目标为圆心，对 Data.AoeRadius 半径内所有索敌范围中的敌人造成伤害。
        /// </summary>
        /// <param name="target">溅射圆心的目标</param>
        private void AttackAoe(Enemy target)
        {
            float aoeRadiusSq = Data.AoeRadius * Data.AoeRadius;
            int hitCount = 0;

            for (int i = 0; i < _targetsInRange.Count; i++)
            {
                var enemy = _targetsInRange[i];
                if (enemy == null || !IsInstanceValid(enemy)) continue;

                if (enemy.GlobalPosition.DistanceSquaredTo(target.GlobalPosition) <= aoeRadiusSq)
                {
                    enemy.TakeDamage(Data.Damage);
                    hitCount++;
                }
            }

            GD.Print($"[Tower] 范围攻击 {target.Name} | 单体伤害={Data.Damage} | 溅射半径={Data.AoeRadius} | 命中数={hitCount}");
        }

        /// <summary>
        /// 减速攻击：对目标造成伤害，并附加 Data.SlowFactor 倍率、Data.SlowDuration 时长的减速 debuff。
        /// </summary>
        /// <param name="target">选中的目标</param>
        private void AttackSlow(Enemy target)
        {
            target.TakeDamage(Data.Damage);
            target.ApplySlow(Data.SlowFactor, Data.SlowDuration);
            GD.Print($"[Tower] 减速攻击 {target.Name} | 伤害={Data.Damage} | 减速至 {Data.SlowFactor:P0} 持续 {Data.SlowDuration}s | 剩余HP={target.CurrentHp:F1}");
        }

        /// <summary>
        /// 安全地从目标列表中移除指定敌人。
        /// 用于敌人死亡、逃脱或离开范围时的清理，确保列表只包含有效实例。
        /// </summary>
        /// <param name="enemy">要移除的敌人实例</param>
        private void RemoveTarget(Enemy enemy)
        {
            if (_targetsInRange.Contains(enemy))
            {
                _targetsInRange.Remove(enemy);
            }
        }
    }
}
