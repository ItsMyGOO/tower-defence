using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Tests.Scenes.AudioTest
{
	/// <summary>
	/// AudioManager + EffectsManager 双管理器联动测试脚本。
	/// 在场景中布置 5 个测试按钮，点击时分别通过 EventBus 发布建塔、击杀、漏怪、胜利、失败事件，
	/// 用于验证 AudioManager 是否正确播放对应音效、EffectsManager 是否正确实例化击杀特效且零闪烁，
	/// 以及两个管理器下的子节点是否能在结束后自动回落到基准值。
	/// </summary>
	public partial class AudioTest : Node2D
	{
		#region 导出节点引用

		/// <summary>
		/// 获取或设置用于显示两个管理器子节点计数的 Label。
		/// 点击测试按钮后观察数值是否在特效/SFX 结束后回落至基准值。
		/// </summary>
		[Export] public Label NodeCounterLabel { get; set; }

		/// <summary>
		/// 获取或设置 AudioManager 节点引用，用于统计其子节点数量（基准值应为 1，仅 BGMPlayer）。
		/// </summary>
		[Export] public Node AudioManagerNode { get; set; }

		/// <summary>
		/// 获取或设置 EffectsManager 节点引用，用于统计其子节点数量（基准值应为 0，击杀特效全部回收）。
		/// </summary>
		[Export] public Node EffectsManagerNode { get; set; }

		#endregion

		#region 测试数据占位

		/// <summary>
		/// 用于触发 OnTowerBuilt 事件的占位塔数据。
		/// 仅用于测试，实际塔属性不影响音频/特效播放。
		/// </summary>
		private TowerData _dummyTowerData;

		#endregion

		#region 生命周期

		/// <summary>
		/// 节点被添加到场景树时调用。
		/// 初始化占位塔数据，并连接各按钮的 Pressed 信号到对应测试方法。
		/// </summary>
		public override void _Ready()
		{
			_dummyTowerData = new TowerData
			{
				TowerId = "TestArrowTower",
				TowerName = "测试箭塔",
				BuildCost = 50,
				Damage = 10.0f,
				AttackRange = 150.0f,
				AttackInterval = 1.0f
			};

			ConnectButton("BtnTowerBuilt", TestTowerBuilt);
			ConnectButton("BtnEnemyKilled", TestEnemyKilled);
			ConnectButton("BtnEnemyReachedEnd", TestEnemyReachedEnd);
			ConnectButton("BtnGameOverWin", TestGameOverWin);
			ConnectButton("BtnGameOverLose", TestGameOverLose);
		}

		/// <summary>
		/// 每帧更新逻辑。
		/// 刷新 AudioManager 与 EffectsManager 的子节点计数显示，便于人工验证节点回收情况。
		/// </summary>
		/// <param name="delta">距上一帧经过的时间（秒）</param>
		public override void _Process(double delta)
		{
			if (NodeCounterLabel != null)
			{
				int audioCount = AudioManagerNode != null ? AudioManagerNode.GetChildCount() : -1;
				int effectsCount = EffectsManagerNode != null ? EffectsManagerNode.GetChildCount() : -1;
				NodeCounterLabel.Text =
					$"AudioManager 子节点数: {audioCount}（基准值 1，仅 BGMPlayer）\n" +
					$"EffectsManager 子节点数: {effectsCount}（基准值 0，特效全部回收）";
			}
		}

		#endregion

		#region 按钮连接辅助

		/// <summary>
		/// 在场景树中按名称查找 Button 节点，并将其 Pressed 信号连接到指定回调。
		/// 若找不到对应节点则打印警告，避免测试场景未完整布置时崩溃。
		/// </summary>
		/// <param name="buttonName">Button 节点在场景树中的名称</param>
		/// <param name="callback">按钮点击时执行的测试方法</param>
		private void ConnectButton(string buttonName, System.Action callback)
		{
			Button btn = GetNodeOrNull<Button>($"%{buttonName}");
			if (btn == null)
			{
				GD.Print($"[AudioTest][WARN] 未找到测试按钮: {buttonName}");
				return;
			}
			btn.Pressed += callback;
		}

		#endregion

		#region 各测试项

		/// <summary>
		/// 测试防御塔建造音效。
		/// 发布 EventBus.OnTowerBuilt 事件，验证 AudioManager 是否播放 SFXBuildTower。
		/// </summary>
		private void TestTowerBuilt()
		{
			GD.Print("[AudioTest] 触发 TestTowerBuilt —— AudioManager 应播放建塔音效");
			Vector2 dummyPos = new Vector2(400, 300);
			EventBus.RaiseTowerBuilt(_dummyTowerData, dummyPos);
		}

		/// <summary>
		/// 测试敌人击杀音效 + 击杀特效。
		/// 发布 EventBus.OnEnemyKilled 事件，验证 AudioManager 播放音效、EffectsManager 在屏幕中央实例化特效，
		/// 且特效生命周期结束后无最后一帧闪烁并自动销毁。
		/// </summary>
		private void TestEnemyKilled()
		{
			GD.Print("[AudioTest] 触发 TestEnemyKilled —— 应播放击杀音效 + 屏幕中央出现粒子特效（零闪烁）");
			Vector2 deathPos = new Vector2(640, 360);
			EventBus.RaiseEnemyKilled("TestSlime", 10, deathPos);
		}

		/// <summary>
		/// 测试敌人漏怪（到达终点）音效。
		/// 发布 EventBus.OnEnemyReachedEnd 事件，验证 AudioManager 是否播放 SFXEnemyReachedEnd。
		/// </summary>
		private void TestEnemyReachedEnd()
		{
			GD.Print("[AudioTest] 触发 TestEnemyReachedEnd —— AudioManager 应播放扣血/漏怪音效");
			EventBus.RaiseEnemyReachedEnd(5);
		}

		/// <summary>
		/// 测试玩家胜利结算音效 + BGM 淡出。
		/// 发布 EventBus.OnGameOver(true) 事件，验证 AudioManager 播放 SFXGameOverWin 且 BGM 在 1 秒内淡出。
		/// </summary>
		private void TestGameOverWin()
		{
			GD.Print("[AudioTest] 触发 TestGameOverWin —— AudioManager 应播放胜利音效 + BGM 淡出");
			EventBus.RaiseGameOver(true);
		}

		/// <summary>
		/// 测试玩家失败结算音效 + BGM 淡出。
		/// 发布 EventBus.OnGameOver(false) 事件，验证 AudioManager 播放 SFXGameOverLose 且 BGM 在 1 秒内淡出。
		/// </summary>
		private void TestGameOverLose()
		{
			GD.Print("[AudioTest] 触发 TestGameOverLose —— AudioManager 应播放失败音效 + BGM 淡出");
			EventBus.RaiseGameOver(false);
		}

		#endregion
	}
}
