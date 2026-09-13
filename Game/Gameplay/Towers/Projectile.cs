using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Gameplay.Enemies;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔弹道飞行物（Mode = Projectile 时由 Tower 发射）。
    /// 追踪目标敌人飞行，抵达后按塔配置结算：
    /// - AoeRadius &gt; 0：在弹体落点按半径对范围内所有敌人结算（溅射）；
    /// - AoeRadius == 0：仅结算锁定的存活目标，目标中途被消灭则弹体空爆失效。
    /// 受击者一律受伤害；减速塔（Kind = Slow）的受击者同时附加减速 debuff。
    /// 视觉为按 TowerData.AttackColor 绘制的占位圆，替换精灵图时仅改动本类的视觉部分。
    /// </summary>
    public partial class Projectile : Node2D
    {
        /// <summary>
        /// 占位弹体的绘制半径（像素）。
        /// </summary>
        private const float PlaceholderRadius = 5.0f;

        /// <summary>
        /// 弹体纹理存在时为 false：改用 Sprite2D 显示弹体贴图而非占位圆。
        /// </summary>
        private bool _usePlaceholderVisual = true;

        private TowerData _data;
        private Enemy _target;
        private Vector2 _lastTargetPosition;

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
        /// 配置并激活弹体。必须在 AddChild 之前调用。
        /// </summary>
        /// <param name="data">发射塔的配置数据（伤害/溅射半径/减速/弹速/颜色）</param>
        /// <param name="target">锁定的目标敌人</param>
        public void Initialize(TowerData data, Enemy target)
        {
            _data = data;
            _target = target;
            _lastTargetPosition = target.GlobalPosition;
        }

        /// <summary>
        /// 每帧朝目标当前位置推进；目标中途被销毁则继续飞向最后已知位置。
        /// 到达后立即结算并自我销毁。
        /// </summary>
        /// <param name="delta">距上一帧经过的时间（秒）</param>
        public override void _Process(double delta)
        {
            if (_data == null)
            {
                QueueFree();
                return;
            }

            if (_target != null && IsInstanceValid(_target))
            {
                _lastTargetPosition = _target.GlobalPosition;
            }

            float step = _data.ProjectileSpeed * (float)delta;
            Vector2 toDestination = _lastTargetPosition - GlobalPosition;

            if (toDestination.Length() <= step)
            {
                GlobalPosition = _lastTargetPosition;
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
        /// 弹体抵达结算：受击集合由 AoeRadius 决定（&gt;0 时为落点半径内的所有敌人，否则为存活目标本身），
        /// 对每个受击者按塔的 Kind 结算伤害与减速；无有效受击者则空爆失效。随后销毁自身。
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
            else if (_target != null && IsInstanceValid(_target))
            {
                ApplyHit(_target);
                hitCount = 1;
                GD.Print($"[Projectile] 命中 {_target.Name} | 伤害={_data.Damage} | 剩余HP={_target.CurrentHp:F1}");
            }
            else
            {
                GD.Print("[Projectile] 目标已消失，弹体空爆失效。");
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
