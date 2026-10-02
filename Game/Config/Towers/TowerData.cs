using Godot;

namespace TowerDefence.Config.Towers
{
    /// <summary>
    /// 防御塔的攻击形态。
    /// </summary>
    public enum TowerKind
    {
        /// <summary>单体攻击：每次攻击仅命中选中的目标。</summary>
        Single,

        /// <summary>范围攻击：命中目标以及目标周围 AoeRadius 半径内的所有敌人。</summary>
        Aoe,

        /// <summary>减速攻击：命中目标并附加 SlowFactor 倍率的减速 debuff。</summary>
        Slow
    }

    /// <summary>
    /// 防御塔的目标选择策略。
    /// </summary>
    public enum TargetingMode
    {
        /// <summary>最前线：优先攻击沿路径推进最远的目标。</summary>
        First,

        /// <summary>最近：优先攻击距离塔最近的目标。</summary>
        Nearest,

        /// <summary>最强：优先攻击当前血量最高的目标。</summary>
        Strongest
    }

    /// <summary>
    /// 防御塔的攻击表现与结算方式。
    /// </summary>
    public enum AttackMode
    {
        /// <summary>即时命中：攻击瞬间结算伤害/debuff，并绘制塔到目标的 tracer 拉线表现。</summary>
        Instant,

        /// <summary>弹道攻击：发射飞行弹体，抵达目标（或落点）时才结算伤害/debuff；
        /// AOE 塔在弹体落点按 AoeRadius 溅射，与命中瞬间目标是否存活无关。</summary>
        Projectile,

        /// <summary>光束攻击：锁定目标持续跟踪并按帧结算每秒伤害（Damage 即 DPS），
        /// 目标死亡或离开范围后自动切换/断束，锁定期间常驻光束 Line2D 表现。</summary>
        Beam
    }

    /// <summary>
    /// 防御塔配置数据资源。
    /// 以 Godot Resource 形式存储塔的各项属性，是数据驱动架构的核心配置资产。
    /// 游戏逻辑层通过加载该资源实例读取塔的配置，禁止在代码中硬编码塔属性。
    /// </summary>
    [GlobalClass]
    public partial class TowerData : Resource
    {
        /// <summary>
        /// 获取或设置防御塔的唯一标识符。
        /// 用于在事件总线、存档、配置表等场景中精确索引某一种塔。
        /// </summary>
        [Export] public string TowerId { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置防御塔的显示名称。
        /// 用于 UI 展示（商店、塔信息面板等），支持本地化占位。
        /// </summary>
        [Export] public string TowerName { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置防御塔的图标纹理。
        /// 用于塔身显示（商店按钮、建造预览、塔信息面板等 UI 场景）。
        /// 像素风素材建议配合最近邻过滤放大使用。
        /// </summary>
        [Export] public Texture2D Icon { get; set; }

        /// <summary>
        /// 获取或设置弹道攻击（Mode = Projectile）的弹体纹理。
        /// 为 null 时弹体使用 AttackColor 绘制的占位圆；非空时弹体旋转朝向飞行方向。
        /// </summary>
        [Export] public Texture2D ProjectileIcon { get; set; }

        /// <summary>
        /// 获取或设置防御塔的攻击形态（单体/范围/减速）。
        /// </summary>
        [Export] public TowerKind Kind { get; set; } = TowerKind.Single;

        /// <summary>
        /// 获取或设置目标选择策略（最前线/最近/最强）。
        /// </summary>
        [Export] public TargetingMode Targeting { get; set; } = TargetingMode.First;

        /// <summary>
        /// 获取或设置攻击表现与结算方式（即时 tracer / 飞行弹体）。
        /// </summary>
        [Export] public AttackMode Mode { get; set; } = AttackMode.Instant;

        /// <summary>
        /// 获取或设置弹体飞行速度（像素/秒，Mode = Projectile 时生效）。
        /// </summary>
        [Export] public float ProjectileSpeed { get; set; } = 400.0f;

        /// <summary>
        /// 获取或设置单体弹（AoeRadius = 0）的落点命中容差（像素）：
        /// 目标偏离落点超过该值即打空。美术填充阶段按弹体/目标体型调整。
        /// </summary>
        [Export] public float ProjectileHitTolerance { get; set; } = 26.0f;

        /// <summary>
        /// 获取或设置弹体贴图（ProjectileIcon）的视觉缩放倍数（像素素材最近邻放大）。
        /// </summary>
        [Export] public float ProjectileVisualScale { get; set; } = 2.0f;

        /// <summary>
        /// 获取或设置塔身图标（Icon）的视觉缩放倍数（像素素材最近邻放大，整数倍最锐利）。
        /// </summary>
        [Export] public float VisualScale { get; set; } = 3.0f;

        /// <summary>
        /// 获取或设置攻击表现颜色：即时模式为 tracer 拉线颜色，弹道模式为弹体占位圆颜色。
        /// 纯配置驱动的占位视觉，替换精灵图后仍可复用为粒子/弹体主色。
        /// </summary>
        [Export] public Color AttackColor { get; set; } = Colors.White;

        /// <summary>
        /// 获取或设置建造该防御塔所需消耗的金币数量。
        /// 必须为非负整数；游戏逻辑层在放置塔前需校验玩家金币是否充足。
        /// </summary>
        [Export] public int BuildCost { get; set; } = 100;

        /// <summary>
        /// 获取或设置出售该防御塔时返还的金币比例（相对 BuildCost）。
        /// 0.5 表示出售返还一半造价；实际返还金额向下取整。
        /// </summary>
        [Export] public float SellRefundRatio { get; set; } = 0.5f;

        /// <summary>
        /// 获取或设置防御塔的攻击范围（世界坐标单位，像素）。
        /// 用于寻敌范围判定以及编辑器中绘制攻击范围预览圈。
        /// </summary>
        [Export] public float AttackRange { get; set; } = 150.0f;

        /// <summary>
        /// 获取或设置防御塔每次攻击造成的基础伤害值。
        /// Mode = Beam 时该值表示每秒伤害（DPS），按帧持续结算；其余模式为单次攻击伤害。
        /// 实际伤害结算需在逻辑层结合目标护甲、减伤 Buff 等因素计算。
        /// </summary>
        [Export] public float Damage { get; set; } = 10.0f;

        /// <summary>
        /// 获取或设置防御塔两次攻击之间的间隔时间（单位：秒）。
        /// 值越小塔的攻速越快；游戏逻辑层使用计时器或累加 delta 判定是否可再次攻击。
        /// </summary>
        [Export] public float AttackInterval { get; set; } = 1.0f;

        /// <summary>
        /// 获取或设置范围攻击（Kind = Aoe）的溅射半径（像素）。
        /// 以被选中目标为圆心，该半径内的所有在索敌范围内的敌人都会受到伤害。
        /// </summary>
        [Export] public float AoeRadius { get; set; } = 80.0f;

        /// <summary>
        /// 获取或设置减速攻击（Kind = Slow）命中后目标移动速度的剩余倍率。
        /// 0.5 表示减速至一半速度；取值范围约定 [0.05, 1.0]，多重减速取更强者。
        /// </summary>
        [Export] public float SlowFactor { get; set; } = 0.5f;

        /// <summary>
        /// 获取或设置减速效果的持续时间（单位：秒）。
        /// 时长结束后目标恢复原速；再次命中会刷新持续时间。
        /// </summary>
        [Export] public float SlowDuration { get; set; } = 2.0f;

        /// <summary>
        /// 获取或设置光束攻击（Mode = Beam）的 Line2D 表现宽度（像素）。
        /// </summary>
        [Export] public float BeamWidth { get; set; } = 4.0f;

        /// <summary>
        /// 获取或设置该塔的最高等级（1 表示不可升级）。
        /// </summary>
        [Export] public int MaxLevel { get; set; } = 1;

        /// <summary>
        /// 获取或设置 1 级升 2 级的基础费用；后续等级费用按 UpgradeCostFactor 逐级上浮。
        /// </summary>
        [Export] public float UpgradeBaseCost { get; set; } = 50.0f;

        /// <summary>
        /// 获取或设置升级费用的逐级倍率（2→3 级费用 = 基础费用 × 倍率）。
        /// </summary>
        [Export] public float UpgradeCostFactor { get; set; } = 1.5f;

        /// <summary>
        /// 获取或设置每级伤害成长倍率（升级后伤害 = 当前伤害 × 该倍率）。
        /// </summary>
        [Export] public float DamageGrowthFactor { get; set; } = 1.3f;

        /// <summary>
        /// 获取或设置每级射程成长倍率（升级后射程 = 当前射程 × 该倍率）。
        /// </summary>
        [Export] public float RangeGrowthFactor { get; set; } = 1.08f;
    }
}
