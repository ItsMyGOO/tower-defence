using Godot;

namespace TowerDefence.Config.Enemies
{
    /// <summary>
    /// 敌人配置数据资源。
    /// 以 Godot Resource 形式存储敌人的各项属性，是数据驱动架构的核心配置资产。
    /// 游戏逻辑层通过加载该资源实例读取敌人的配置，禁止在代码中硬编码敌人属性。
    /// </summary>
    [GlobalClass]
    public partial class EnemyData : Resource
    {
        /// <summary>
        /// 获取或设置敌人的唯一标识符。
        /// 用于在事件总线、存档、配置表等场景中精确索引某一种敌人。
        /// </summary>
        [Export] public string EnemyId { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置敌人的显示名称。
        /// 用于 UI 展示（敌人信息面板、击杀提示等），支持本地化占位。
        /// </summary>
        [Export] public string EnemyName { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置敌人的精灵纹理。
        /// 可以是整张单帧图，也可以是 AtlasTexture（取行走动画条中的某一帧）。
        /// 为 null 时敌人使用场景中的色块占位视觉。
        /// </summary>
        [Export] public Texture2D Icon { get; set; }

        /// <summary>
        /// 获取或设置行走动画条纹理（横向等宽帧条）。
        /// 非 null 时敌人以 AnimatedSprite2D 循环播放（优先级高于 Icon 静态显示）。
        /// </summary>
        [Export] public Texture2D AnimTexture { get; set; }

        /// <summary>
        /// 获取或设置行走动画条的帧数（条宽 / 帧数 = 单帧宽）。
        /// </summary>
        [Export] public int AnimFrames { get; set; } = 4;

        /// <summary>
        /// 获取或设置行走动画播放帧率（FPS）。
        /// </summary>
        [Export] public float AnimFps { get; set; } = 7.0f;

        /// <summary>
        /// 获取或设置敌人的最大生命值。
        /// 敌人出生时的初始生命值上限；受治疗、护盾等 Buff 影响时，实际当前 HP 在 Enemy 节点上保存。
        /// </summary>
        [Export] public float MaxHp { get; set; } = 100.0f;

        /// <summary>
        /// 获取或设置敌人的移动速度（单位：像素/秒）。
        /// 在 PathFollow2D 上沿路径前进时，每帧增加的 Progress 值基于该速度除以路径总长度换算。
        /// </summary>
        [Export] public float MoveSpeed { get; set; } = 100.0f;

        /// <summary>
        /// 获取或设置击杀该敌人后奖励给玩家的金币数量。
        /// 必须为非负整数；敌人死亡时通过 EventBus.OnEnemyKilled 事件传递该值。
        /// </summary>
        [Export] public int RewardGold { get; set; } = 10;

        /// <summary>
        /// 获取或设置敌人走到路径尽头时对玩家造成的生命值伤害。
        /// 当敌人未被击杀而成功逃脱时，通过 EventBus.OnEnemyReachedEnd 事件传递该伤害值。
        /// </summary>
        [Export] public int DamageToPlayer { get; set; } = 1;
    }
}
