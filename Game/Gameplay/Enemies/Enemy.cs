using Godot;
using TowerDefence.Config.Enemies;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Gameplay.Enemies
{
    /// <summary>
    /// 敌人实体节点，沿 Path2D 定义的路径移动并处理受击与销毁逻辑。
    /// 必须作为 Path2D 的子节点挂载，由波次管理器在运行时实例化并注入 EnemyData 配置。
    /// 运行时动态挂载 Area2D 碰撞体，使防御塔能通过范围检测锁定并攻击该敌人。
    /// </summary>
    public partial class Enemy : PathFollow2D
    {
        /// <summary>
        /// 获取或设置当前敌人的配置数据资源。
        /// 实例化后必须在加入场景树前赋值（通过属性注入或在 Inspector 中指定）。
        /// </summary>
        [Export] public EnemyData Data { get; set; }

        /// <summary>
        /// 获取当前敌人的剩余生命值。
        /// 初始化时取 Data.MaxHp，受击后递减；当该值小于等于 0 时触发击杀流程。
        /// </summary>
        public float CurrentHp { get; private set; }

        /// <summary>
        /// 获取当前移动速度倍率（1.0 = 原速，0.5 = 减半）。
        /// 由减速 debuff 驱动，供调试与行为测试断言。
        /// </summary>
        public float SpeedFactor => _speedFactor;

        private Area2D _hitArea;
        private CollisionShape2D _hitShape;

        /// <summary>
        /// 当前移动速度倍率；多重减速取更强者（更小倍率），时长结束恢复 1.0。
        /// </summary>
        private float _speedFactor = 1.0f;

        /// <summary>
        /// 减速 debuff 剩余持续时间（秒）。
        /// </summary>
        private float _slowRemaining;

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 完成生命值初始化、校验 Data 配置，以及动态创建索敌碰撞体。
        /// </summary>
        public override void _Ready()
        {
            if (Data == null)
            {
                GD.PrintErr($"[Enemy] EnemyData 未配置！节点：{Name}");
                QueueFree();
                return;
            }

            CurrentHp = Data.MaxHp;
            Progress = 0.0f;

            // PathFollow2D 的 Loop 默认为 true：不显式关闭时敌人走到路径终点会绕回起点循环移动，
            // ProgressRatio 永远到不了 1.0，漏怪扣血与波次完成判定全部失效。
            Loop = false;

            SetupHitArea();
        }

        /// <summary>
        /// 创建并配置用于防御塔索敌检测的 Area2D 与圆形碰撞体。
        /// 碰撞半径采用固定值 16 像素（适配 ColorRect 占位视觉），
        /// 使 Tower 的 DetectionArea 能够通过 Area 信号捕获该敌人。
        /// </summary>
        private void SetupHitArea()
        {
            _hitArea = new Area2D
            {
                Name = "EnemyHitArea"
            };
            AddChild(_hitArea);

            _hitShape = new CollisionShape2D
            {
                Name = "EnemyHitShape",
                Shape = new CircleShape2D
                {
                    Radius = 16.0f
                }
            };
            _hitArea.AddChild(_hitShape);
        }

        /// <summary>
        /// 每帧更新逻辑。
        /// 驱动减速 debuff 计时，沿路径按当前速度倍率推进 Progress，并检测是否已到达路径尽头。
        /// </summary>
        /// <param name="delta">距上一帧经过的时间（秒）</param>
        public override void _Process(double delta)
        {
            if (Data == null) return;

            if (_slowRemaining > 0.0f)
            {
                _slowRemaining -= (float)delta;
                if (_slowRemaining <= 0.0f)
                {
                    _slowRemaining = 0.0f;
                    _speedFactor = 1.0f;
                }
            }

            Progress += Data.MoveSpeed * _speedFactor * (float)delta;

            if (ProgressRatio >= 1.0f)
            {
                EventBus.RaiseEnemyReachedEnd(Data.DamageToPlayer);
                QueueFree();
            }
        }

        /// <summary>
        /// 对敌人施加减速 debuff。
        /// 多重减速的语义：倍率取更强者（更小值），持续时间取新时长与剩余时长中的更长者——
        /// 弱而短的减速不会提前结束强而长的减速，但可以增强其倍率。
        /// </summary>
        /// <param name="factor">减速后的速度倍率，约定 [0.05, 1.0]，越小减速越强</param>
        /// <param name="duration">减速持续时间（秒），传入非正数时忽略</param>
        public void ApplySlow(float factor, float duration)
        {
            if (duration <= 0.0f) return;

            factor = Mathf.Clamp(factor, 0.05f, 1.0f);
            _speedFactor = Mathf.Min(_speedFactor, factor);
            _slowRemaining = Mathf.Max(_slowRemaining, duration);
        }

        /// <summary>
        /// 对敌人造成伤害并扣除当前生命值。
        /// 扣血后若 HP 小于等于 0，将触发击杀事件并销毁自身节点。
        /// </summary>
        /// <param name="damage">本次伤害的数值（非负浮点数）；负值会被截断为 0</param>
        public void TakeDamage(float damage)
        {
            if (Data == null) return;
            if (damage < 0.0f) damage = 0.0f;

            CurrentHp -= damage;

            if (CurrentHp <= 0.0f)
            {
                EventBus.RaiseEnemyKilled(Data.EnemyId, Data.RewardGold, GlobalPosition);
                QueueFree();
            }
        }
    }
}
