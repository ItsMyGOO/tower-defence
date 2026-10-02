using Godot;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Core.Managers;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Waves;

namespace TowerDefence.Gameplay.Map
{
	/// <summary>
	/// 通用关卡主场景控制器。
	/// 本类合并了原先 Level_01 / Level_02 两份几乎相同的脚本，
	/// 以消除"每新增一关就复制一份脚本"的反模式。
	/// 所有关卡差异（初始金币、初始血量、路径控制点、首波延迟、波次间隔等）
	/// 统一通过 [Export] 属性暴露给 Level_XX.tscn 的 Inspector 或 tscn 文本配置，
	/// 运行时仅需一份 Level.cs 即可驱动任意数量的关卡场景，代码与配置彻底解耦。
	///
	/// 职责边界：槽位点击检测由 TowerSlot 自持（Area2D + 物理拾取），
	/// 经济初始值由本类在 _Ready 中通过 EconomyManager.ResetEconomy 统一下发，
	/// 本类只负责关卡编排 —— 路径构建、波次调度与关键流程日志。
	///
	/// 通用生命周期：
	/// _Ready → 1. 节点引用兜底 2. SetupEnemyPath 根据 PathControlPoints 生成 Curve2D
	///      → 3. 订阅 EventBus（波次完成自动衔接下一波）
	///      → 4. ScheduleFirstWave 首波延迟后自动启动 Wave_01
	///      → 5. 通关后由 GameManager 触发胜利 / 战败结算，结算面板的返回主菜单/选关/下一关/重开按钮构成 Meta Loop 闭环
	/// </summary>
	public partial class Level : Node2D
	{
		#region 导出：节点引用

		/// <summary>
		/// 获取或设置波次管理器节点引用。
		/// Inspector 中绑定到场景树内 Systems/WaveManager 节点。
		/// </summary>
		[Export] public WaveManager WaveManagerNode { get; set; }

		/// <summary>
		/// 获取或设置游戏流程管理器节点引用。
		/// Inspector 中绑定到场景树内 Systems/GameManager 节点。
		/// </summary>
		[Export] public GameManager GameManagerNode { get; set; }

		/// <summary>
		/// 获取或设置敌人刷怪路径节点引用。
		/// Inspector 中绑定到场景树内 World/EnemyPath 节点，
		/// 在 _Ready 时根据 PathControlPoints 动态构建 Curve2D 控制点，避免手动在 .tscn 序列化 curve 出错。
		/// </summary>
		[Export] public Path2D EnemyPathNode { get; set; }

		#endregion

		#region 导出：关卡差异化参数

		/// <summary>
		/// 获取或设置关卡名称（用于日志与 UI 打印，不影响业务）。
		/// Level_01 建议填写"第一关：新手草原"、Level_02 建议填写"第二关：S形回廊"等。
		/// </summary>
		[Export] public string LevelDisplayName { get; set; } = "未命名关卡";

		/// <summary>
		/// 获取或设置首波自动启动的延迟秒数。
		/// 玩家进入场景后先有短暂准备布防时间，随后自动开启 Wave_01。
		/// 第一关默认 3.0 秒，第二关默认 2.5 秒体现进阶节奏。
		/// </summary>
		[Export] public float FirstWaveAutoStartDelay { get; set; } = 3.0f;

		/// <summary>
		/// 获取或设置波次完成后自动开启下一波的延迟秒数。
		/// 非最后一波完成后，此时长过后自动调用 StartNextWave()，实现关卡无缝衔接。
		/// </summary>
		[Export] public float NextWaveAutoStartDelay { get; set; } = 3.0f;

		/// <summary>
		/// 获取或设置玩家进入关卡时的初始金币。
		/// 第一关默认 150（保守防守起手），第二关默认 200（更多塔位更多选择）。
		/// _Ready 中会调用 EconomyManager.ResetEconomy(InitialGold, InitialMaxHp) 重置经济。
		/// </summary>
		[Export] public int InitialGold { get; set; } = 150;

		/// <summary>
		/// 获取或设置玩家进入关卡时的最大生命值。
		/// 第一关 / 第二关默认 10，若有 Hard Mode 可在 Level_XX.tscn 中设为 5。
		/// </summary>
		[Export] public int InitialMaxHp { get; set; } = 10;

		/// <summary>
		/// 获取或设置敌人折线路径控制点数组（本地坐标，相对于 EnemyPath 自身 position）。
		/// 每个元素对应一个 Curve2D 控制点，_Ready 中会顺序添加到 Curve2D 形成折线。
		/// 若该数组长度为 0，则跳过 SetupEnemyPath 动态生成流程（意味着 Inspector 已预先设置 curve）。
		/// 
		/// 示例：
		///   第一关 6 个点（Z 字折线）：(0,0),(400,0),(400,-150),(800,-150),(800,150),(1200,150)
		///   第二关 8 个点（S 形折返）：(0,0),(320,0),(320,-200),(640,-200),(640,200),(960,200),(960,-80),(1280,-80)
		/// </summary>
		[Export] public Vector2[] PathControlPoints { get; set; } = System.Array.Empty<Vector2>();

		#endregion

		#region 内部字段

		/// <summary>
		/// 首波启动定时器。
		/// </summary>
		private Timer _firstWaveTimer;

		/// <summary>
		/// 下一波衔接定时器。
		/// </summary>
		private Timer _nextWaveTimer;

		#endregion

		#region 生命周期

	/// <summary>
	/// 节点被添加到场景树时调用。
	/// 依次执行：节点引用兜底解析 → 构建敌人折线路径 → 订阅波次事件 → 启动首波倒计时。
	/// 本方法对 Level_01 / Level_02 完全通用，所有差异走 Export 参数。
	/// </summary>
	public override void _Ready()
	{
		ResolveNodeReferences();
		SetupEnemyPath();
		InitializeEconomy();
		SubscribeEventBus();
		ScheduleFirstWave();

		GD.Print($"[Level] ✅ {LevelDisplayName} 加载完成（初始金币 {InitialGold} / 最大 HP {InitialMaxHp} / 首波延迟 {FirstWaveAutoStartDelay}s），等待首波刷怪...");
	}

	/// <summary>
	/// 节点即将从场景树移除时调用。
	/// 取消所有 EventBus 订阅，防止委托悬空。
	/// </summary>
	public override void _ExitTree()
	{
		UnsubscribeEventBus();
	}

	#endregion

		#region 节点引用兜底解析

	/// <summary>
	/// 为所有 Export 节点引用做绝对路径兜底赋值。
	/// 避免 .tscn 文本格式解析差异导致 Inspector 绑定丢失（NodePath 未正确反序列化），
	/// 只要 Level_XX.tscn 统一遵循 World / Systems 两层根结构（EnemyPath 位于 World/EnemyPath），
	/// 就可以通过 GetNode&lt;&gt; 可靠地拿到引用，无需依赖编辑器手动拖放。
	/// </summary>
	private void ResolveNodeReferences()
	{
		WaveManagerNode ??= GetNodeOrNull<WaveManager>("Systems/WaveManager");
		GameManagerNode ??= GetNodeOrNull<GameManager>("Systems/GameManager");
		EnemyPathNode ??= GetNodeOrNull<Path2D>("World/EnemyPath");

		int missing = 0;
		if (WaveManagerNode == null) { GD.PrintErr($"[Level] 兜底解析失败: WaveManagerNode ({LevelDisplayName})"); missing++; }
		if (GameManagerNode == null) { GD.PrintErr($"[Level] 兜底解析失败: GameManagerNode ({LevelDisplayName})"); missing++; }
		if (EnemyPathNode == null) { GD.PrintErr($"[Level] 兜底解析失败: EnemyPathNode ({LevelDisplayName})"); missing++; }

		if (missing == 0)
		{
			GD.Print($"[Level] ✅ 3 个关键节点引用兜底解析全部成功（{LevelDisplayName}）。");
		}
	}

	#endregion

	#region 差异化初始化：路径

	/// <summary>
	/// 根据 Export 的 <see cref="PathControlPoints"/> 数组顺序构建 EnemyPathNode.Curve。
	/// 先清掉原有控制点（可能是 Level_XX.tscn 残留的 SubResource("Curve2D_X")）再重新添加，
	/// 保证所有关卡的路径都从 Level.cs 的统一入口生成，后续仅需改 tscn 中的 PathControlPoints Export 值即可。
	/// 若数组为空（长度 0）则视为手动保留 Inspector 配置，不做任何修改。
	/// </summary>
	private void SetupEnemyPath()
	{
		if (EnemyPathNode == null) return;
		if (PathControlPoints == null || PathControlPoints.Length == 0)
		{
			GD.Print($"[Level] PathControlPoints 为空，跳过动态路径生成，使用 EnemyPath 原有 curve（{LevelDisplayName}）。");
			return;
		}

		var curve = new Curve2D();
		foreach (Vector2 point in PathControlPoints)
		{
			curve.AddPoint(point);
		}

		EnemyPathNode.Curve = curve;
		GD.Print($"[Level] 动态构建敌人折线路径完成，共 {PathControlPoints.Length} 个控制点（{LevelDisplayName}）。");
	}

	/// <summary>
	/// 用本关 Export 的 InitialGold / InitialMaxHp 重置经济管理器。
	/// EconomyManager 自身的同名 Export 仅作为无关卡驱动时（测试场景等）的兜底默认值，
	/// 两处配置此前并未打通（Level 的 InitialMaxHp 实际从未生效），现统一以关卡配置为准。
	/// 重置后立即广播金币/血量事件，保证 HUD 首帧即显示本关初始值。
	/// </summary>
	private void InitializeEconomy()
	{
		if (EconomyManager.Instance == null)
		{
			GD.PrintErr("[Level] EconomyManager 单例不存在，跳过经济初始化。");
			return;
		}

		EconomyManager.Instance.ResetEconomy(InitialGold, InitialMaxHp);
		GD.Print($"[Level] 经济初始化 → 金币 {InitialGold} / HP {InitialMaxHp}（{LevelDisplayName}）。");
	}

	#endregion

		#region 事件订阅与取消

	/// <summary>
	/// 订阅 EventBus 中本场景关心的事件。
	/// 核心用途是波次完成后自动衔接下一波；经济/建造/击杀等事件的展示与结算
	/// 分别由 HUDView、EconomyManager、GameManager 等专职模块订阅，本类不再重复记录日志。
	/// </summary>
	private void SubscribeEventBus()
	{
		EventBus.OnWaveStarted += HandleWaveStarted;
		EventBus.OnWaveCompleted += HandleWaveCompleted;
	}

	/// <summary>
	/// 对称取消 SubscribeEventBus 中注册的所有事件订阅。
	/// </summary>
	private void UnsubscribeEventBus()
	{
		EventBus.OnWaveStarted -= HandleWaveStarted;
		EventBus.OnWaveCompleted -= HandleWaveCompleted;
	}

		#endregion

		#region 波次调度

		/// <summary>
		/// 安排首波自动启动定时器。
		/// 到时后直接调用 WaveManager.StartNextWave() 开启 Wave_01。
		/// </summary>
		private void ScheduleFirstWave()
		{
			_firstWaveTimer = new Timer
			{
				Name = "FirstWaveTimer",
				WaitTime = Mathf.Max(0.0f, FirstWaveAutoStartDelay),
				OneShot = true,
				Autostart = true
			};
			AddChild(_firstWaveTimer);
			_firstWaveTimer.Timeout += () =>
			{
				GD.Print($"[Level] 首波准备时间结束（{LevelDisplayName}），正在启动第 1 波...");
				WaveManagerNode?.StartNextWave();
			};
		}

		/// <summary>
		/// 安排非最后一波的下一波自动衔接定时器。
		/// 若 WaveManager.AllWavesCompleted 为 true 则跳过，由 GameManager 接管胜利判定。
		/// </summary>
		private void ScheduleNextWave()
		{
			if (WaveManagerNode == null) return;
			if (WaveManagerNode.AllWavesCompleted) return;

			_nextWaveTimer = new Timer
			{
				Name = "NextWaveTimer",
				WaitTime = Mathf.Max(0.0f, NextWaveAutoStartDelay),
				OneShot = true,
				Autostart = true
			};
			AddChild(_nextWaveTimer);
			_nextWaveTimer.Timeout += () =>
			{
				GD.Print($"[Level] 波次间隔结束（{LevelDisplayName}），正在启动下一波...");
				WaveManagerNode.StartNextWave();
			};
		}

		#endregion

	#region EventBus 事件处理

	/// <summary>
	/// 波次开始事件：打印日志，便于确认刷怪节奏是否符合预期。
	/// </summary>
	private void HandleWaveStarted(int waveIndex)
	{
		GD.Print($"[Level] 🚩 第 {waveIndex} 波开始！（{LevelDisplayName}）");
	}

	/// <summary>
	/// 波次完成事件：若非最后一波则安排下一波自动衔接，否则等待 GameManager 胜利判定。
	/// </summary>
	private void HandleWaveCompleted(int waveIndex)
	{
		GD.Print($"[Level] ✅ 第 {waveIndex} 波已清理完毕（{LevelDisplayName}）。");
		ScheduleNextWave();
	}

	#endregion
	}
}
