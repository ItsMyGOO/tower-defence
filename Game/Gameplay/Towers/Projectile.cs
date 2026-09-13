using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Gameplay.Enemies;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔弹道飞行物（Mode = Projectile 时由 Tower 发射）。
    /// 经典塔防的落点弹道：发射瞬间按目标当前速度预判拦截点，随后直飞该固定落点——
    /// 不追踪目标，目标中途变向/被减速会导致偏差。
    /// 抵达落点时按塔配置结算：
    /// - AoeRadius &gt; 0：在落点按半径对范围内所有敌人结算（溅射）；
    /// - AoeRadius == 0：仅当目标位于落点命中容差内时结算伤害（单体塔可打空）；
    /// 减速塔（Kind = Slow）对受击者额外附加减速 debuff。
    /// 视觉为按 TowerData.AttackColor 绘制的占位圆，配置 ProjectileIcon 时改用精灵图。
    /// </summary>
    public partial class Projectile : Node2D
    {
        /// <summary>
        /// 占位弹体的绘制半径（像素）。
        /// </summary>
        private const float PlaceholderRadius = 5.0f;

        /// <summary>
        /// 单体弹的落点命中容差（像素）：目标偏离落点超过该值即打空。
        /// </summary>
        private const float HitTolerance = 26.0f;

        /// <summary>
        /// 弹体纹理存在时为 false：改用 Sprite2D 显示弹体贴图而非占位圆。
        /// </summary>
        private bool _usePlaceholderVisual = true;

        private TowerData _data;
        private Enemy _target;
        private Vector2 _destination;

        /// <summary>
        /// 配置并激活弹体。必须在 AddChild 之前调用。
        /// 按目标当前速度迭代预判拦截点作为固定飞行目的地。
        /// </summary>
        /// <param name="data">发射塔的配置数据（伤害/溅射半径/减速/弹速/颜色）</param>
        /// <param name="target">锁定的目标敌人</param>
        /// <param name="origin">发射起点（塔的世界坐标）</param>
        public void Initialize(TowerData data, Enemy target, Vector2 origin)
        {
            _data = data;
            _target = target;
            _destination = PredictImpact(data, target, origin);
        }

        /// <summary>
        /// 弹体视觉初始化：配置了 ProjectileIcon 时创建 Sprite2D（最近邻过滤放大），
        /// 否则保留 _Draw 占位圆。
        /// </summary>
        public override void _Ready()
        {
            if (_data?.ProjectileIcon != null)
            {
                _usePlaceholderVisual = false;

                var sprite = new Sprite2D
                {
                    Name = "ProjectileSprite",
                    Texture = _data.ProjectileIcon,
                    TextureFilter = TextureFilterEnum.Nearest,
                    Scale = new Vector2(2.0f, 2.0f)
                };
                AddChild(sprite);
            }
        }

        /// <summary>
        /// 按目标当前速度迭代预判拦截点：
        /// 飞行时间 = 距离 / 弹速，落点 = 目标当前位置 + 目标速度 × 飞行时间，迭代三次收敛。
        /// </summary>
        /// <param name="data">塔配置（取弹速）</param>
        /// <param name="target">目标敌人</param>
        /// <param name="origin">发射起点</param>
        /// <returns>预判落点（世界坐标）</returns>
        private static Vector2 PredictImpact(TowerData data, Enemy target, Vector2 origin)
        {
            Vector2 targetPosition = target.GlobalPosition;
            Vector2 targetVelocity = target.Velocity;
            float flightTime = 0.0f;

            for (int i = 0; i < 3; i++)
            {
                Vector2 predicted = targetPosition + targetVelocity * flightTime;
                flightTime = origin.DistanceTo(predicted) / Mathf.Max(1.0f, data.ProjectileSpeed);
            }

            return targetPosition + targetVelocity * flightTime;
        }

        /// <summary>
        /// 每帧朝固定落点直线推进，途中旋转朝向飞行方向；抵达后立即结算并自我销毁。
        /// 不追踪目标——落点在发射瞬间即已确定。
        /// </summary>
        /// <param name="delta">距上一帧经过的时间（秒）</param>
        public override void _Process(double delta)
        {
            if (_data == null)
            {
                QueueFree();
                return;
            }

            float step = _data.ProjectileSpeed * (float)delta;
            Vector2 toDestination = _destination - GlobalPosition;

            if (toDestination.Length() <= step)
            {
                GlobalPosition = _destination;
                Impact();
                return;
            }

            GlobalPosition += toDestination.Normalized() * step;
            Rotation = toDestination.Angle();
        }

        /// <summary>
        /// 占位视觉：无弹体纹理时按 TowerData.AttackColor 绘制实心圆。
        /// </summary>
        public override void _Draw()
        {
            if (_usePlaceholderVisual)
            {
                DrawCircle(Vector2.Zero, PlaceholderRadius, _data?.AttackColor ?? Colors.White);
            }
        }

        /// <summary>
        /// 弹体抵达落点结算：受击集合由 AoeRadius 决定（&gt;0 时为落点半径内的所有敌人），
        /// 单体模式下仅当目标仍处于落点命中容差内才结算（可打空）。
        /// 受击者一律受伤害；减速塔（Kind = Slow）的受击者同时附加减速 debuff。随后销毁自身。
        /// </summary>
        private void Impact()
        {
            int hitCount = 0;

            if (_data.AoeRadius > 0.0f)
            {
                float radiusSq = _data.AoeRadius * _data.AoeRadius;

                foreach (Node node in GetTree().GetNodesInGroup(Enemy.EnemyGroup))
                {
                    if (node is not Enemy enemy || !IsInstanceValid(enemy)) continue;

                    if (enemy.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= radiusSq)
                    {
                        ApplyHit(enemy);
                        hitCount++;
                    }
                }

                GD.Print($"[Projectile] 落点结算 | 位置={GlobalPosition} | 半径={_data.AoeRadius} | 命中={hitCount}");
            }
            else if (_target != null && IsInstanceValid(_target)
                     && _target.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= HitTolerance * HitTolerance)
            {
                ApplyHit(_target);
                hitCount = 1;
                GD.Print($"[Projectile] 命中 {_target.Name} | 伤害={_data.Damage} | 剩余HP={_target.CurrentHp:F1}");
            }
            else
            {
                GD.Print($"[Projectile] 弹体落空（目标偏离落点超过 {HitTolerance}px）| 位置={GlobalPosition}");
            }

            QueueFree();
        }

        /// <summary>
        /// 对单个受击者结算弹体效果：按塔伤害扣血，减速塔（Kind = Slow）附加减速 debuff。
        /// </summary>
        /// <param name="enemy">受击的敌人</param>
        private void ApplyHit(Enemy enemy)
        {
            enemy.TakeDamage(_data.Damage);

            if (_data.Kind == TowerKind.Slow && _data.SlowDuration > 0.0f)
            {
                enemy.ApplySlow(_data.SlowFactor, _data.SlowDuration);
            }
        }
    }
}
