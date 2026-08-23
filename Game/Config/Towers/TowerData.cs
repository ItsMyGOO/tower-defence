using Godot;

namespace TowerDefence.Config.Towers
{
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
        /// 用于商店按钮、建造预览、塔信息面板等 UI 场景。
        /// </summary>
        [Export] public Texture2D Icon { get; set; }

        /// <summary>
        /// 获取或设置建造该防御塔所需消耗的金币数量。
        /// 必须为非负整数；游戏逻辑层在放置塔前需校验玩家金币是否充足。
        /// </summary>
        [Export] public int BuildCost { get; set; } = 100;

        /// <summary>
        /// 获取或设置防御塔的攻击范围（世界坐标单位，像素）。
        /// 用于寻敌范围判定以及编辑器中绘制攻击范围预览圈。
        /// </summary>
        [Export] public float AttackRange { get; set; } = 150.0f;

        /// <summary>
        /// 获取或设置防御塔每次攻击造成的基础伤害值。
        /// 实际伤害结算需在逻辑层结合目标护甲、减伤 Buff 等因素计算。
        /// </summary>
        [Export] public float Damage { get; set; } = 10.0f;

        /// <summary>
        /// 获取或设置防御塔两次攻击之间的间隔时间（单位：秒）。
        /// 值越小塔的攻速越快；游戏逻辑层使用计时器或累加 delta 判定是否可再次攻击。
        /// </summary>
        [Export] public float AttackInterval { get; set; } = 1.0f;
    }
}
