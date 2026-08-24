using Godot;
using TowerDefence.Config.Enemies;

namespace TowerDefence.Config.Waves
{
    /// <summary>
    /// 波次配置数据资源。
    /// 以 Godot Resource 形式存储每一波敌人的生成规则，是数据驱动架构的核心配置资产。
    /// 波次管理器通过加载该资源实例读取波次配置，禁止在代码中硬编码刷怪逻辑。
    /// </summary>
    [GlobalClass]
    public partial class WaveData : Resource
    {
        /// <summary>
        /// 获取或设置当前波次的序号。
        /// 用于在事件总线、UI 显示等场景中标识波次；建议从 1 开始递增。
        /// </summary>
        [Export] public int WaveIndex { get; set; } = 1;

        /// <summary>
        /// 获取或设置该波次生成的敌人类型列表。
        /// 列表中元素的顺序即为刷怪顺序；同一敌人类型可重复出现以实现连续刷同种敌人。
        /// </summary>
        [Export] public Godot.Collections.Array<EnemyData> EnemyTypes { get; set; } = new();

        /// <summary>
        /// 获取或设置相邻两次刷怪之间的时间间隔（单位：秒）。
        /// 波次管理器内部使用定时器按此间隔依次从 EnemyTypes 中实例化敌人。
        /// </summary>
        [Export] public float SpawnInterval { get; set; } = 1.0f;

        /// <summary>
        /// 获取或设置波次正式开始前的准备时间（单位：秒）。
        /// 在 StartNextWave() 被调用后，管理器会等待此时长再开始按 SpawnInterval 刷怪；
        /// 常用于给玩家预留布防时间，或在 UI 上显示波次预告倒计时。
        /// </summary>
        [Export] public float DelayBeforeStart { get; set; } = 3.0f;
    }
}
