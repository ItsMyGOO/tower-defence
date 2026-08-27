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
        /// 尝试从目标列表中攻击第一个存活敌人。
        /// 若列表为空则跳过本次攻击；否则对目标调用 TakeDamage()。
        /// </summary>
        private void TryAttackTarget()
        {
            if (_targetsInRange.Count == 0)
            {
                return;
            }

            var target = _targetsInRange[0];
            if (target == null || !IsInstanceValid(target))
            {
                _targetsInRange.RemoveAt(0);
                return;
            }

            target.TakeDamage(Data.Damage);
            GD.Print($"[Tower] 攻击 {target.Name} | 伤害={Data.Damage} | 目标剩余HP={target.CurrentHp:F1}");
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
