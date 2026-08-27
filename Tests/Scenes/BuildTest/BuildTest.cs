using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;
using TowerDefence.Gameplay.Towers;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 防御塔建造系统测试场景控制器。
    /// 负责在测试场景中初始化 EconomyManager、TowerManager 与多个 TowerSlot 槽位，
    /// 通过顺序调用 TryBuildTower 模拟金币充足建塔成功、金币不足建造失败、
    /// 已占用槽位重复建造被拒绝三种核心场景，并通过日志与 EventBus 订阅验证结果。
    /// </summary>
    public partial class BuildTest : Node2D
    {
        /// <summary>
        /// 获取或设置测试用防御塔数据资源。
        /// Inspector 中绑定 Tests/Data/Towers/Test_ArrowTower.tres。
        /// </summary>
        [Export] public TowerData TestTowerData { get; set; }

        /// <summary>
        /// 获取或设置经济管理器节点引用。
        /// 用于提供金币消耗入口与状态校验，场景内需绑定 EconomyManager 实例。
        /// </summary>
        [Export] public EconomyManager EconomyManagerNode { get; set; }

        /// <summary>
        /// 获取或设置防御塔建造管理器节点引用。
        /// 统一调用 TryBuildTower 接口执行建造事务。
        /// </summary>
        [Export] public TowerManager TowerManagerNode { get; set; }

        /// <summary>
        /// 获取或设置第一个建造槽位。
        /// </summary>
        [Export] public TowerSlot Slot1 { get; set; }

        /// <summary>
        /// 获取或设置第二个建造槽位。
        /// </summary>
        [Export] public TowerSlot Slot2 { get; set; }

        /// <summary>
        /// 获取或设置第三个建造槽位（预留用于金币不足场景）。
        /// </summary>
        [Export] public TowerSlot Slot3 { get; set; }

        private int _buildSuccessCount;
        private int _buildFailCount;

        /// <summary>
        /// 节点进入场景树时调用。
        /// 订阅 EventBus 建造事件、校验绑定、依次触发三个测试场景。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[BuildTest] ========== 防御塔建造系统测试启动 ==========");

            SubscribeEvents();
            ValidateBindings();
            CallDeferred(nameof(RunTestCases));
        }

        /// <summary>
        /// 节点从场景树移除时调用。
        /// 取消订阅所有事件以避免内存泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// 订阅 EventBus 中的 OnTowerBuilt 事件，用于接收建造成功通知并计数。
        /// </summary>
        private void SubscribeEvents()
        {
            EventBus.OnTowerBuilt += HandleTowerBuilt;
            EventBus.OnGoldChanged += HandleGoldChanged;
        }

        /// <summary>
        /// 取消订阅 EventBus 事件。
        /// </summary>
        private void UnsubscribeEvents()
        {
            EventBus.OnTowerBuilt -= HandleTowerBuilt;
            EventBus.OnGoldChanged -= HandleGoldChanged;
        }

        /// <summary>
        /// 校验所有 Inspector 绑定是否完整。
        /// </summary>
        private void ValidateBindings()
        {
            GD.Print($"[BuildTest] TowerData: {(TestTowerData != null ? TestTowerData.TowerName : "MISSING")} | 建造成本: {TestTowerData?.BuildCost}");
            GD.Print($"[BuildTest] EconomyManager: {(EconomyManagerNode != null ? "OK" : "MISSING")} | 初始金币: {EconomyManagerNode?.InitialGold}");
            GD.Print($"[BuildTest] TowerManager: {(TowerManagerNode != null ? "OK" : "MISSING")}");
            GD.Print($"[BuildTest] Slot1: {(Slot1 != null ? Slot1.Name : "MISSING")}");
            GD.Print($"[BuildTest] Slot2: {(Slot2 != null ? Slot2.Name : "MISSING")}");
            GD.Print($"[BuildTest] Slot3: {(Slot3 != null ? Slot3.Name : "MISSING")}");

            if (TestTowerData == null || EconomyManagerNode == null || TowerManagerNode == null
                || Slot1 == null || Slot2 == null || Slot3 == null)
            {
                GD.PrintErr("[BuildTest] ❌ 存在未绑定的必填节点/资源，测试将中止。");
            }
        }

        /// <summary>
        /// 依次运行三个测试用例。
        /// 1. 正常建造：Slot1 在金币充足且未占用时应成功。
        /// 2. 重复建造：Slot1 再次建造应被拒绝。
        /// 3. 金币不足：Slot3 在建完 Slot1+Slot2 后若金币不足应失败。
        /// </summary>
        private void RunTestCases()
        {
            GD.Print("[BuildTest] ---- 测试用例 1：正常建造（Slot1，金币充足、未占用） ----");
            bool case1 = TowerManagerNode.TryBuildTower(Slot1, TestTowerData);
            GD.Print($"[BuildTest] 用例 1 结果: {(case1 ? "✅ PASS" : "❌ FAIL")}");
            GD.Print($"[BuildTest] Slot1.IsOccupied = {Slot1.IsOccupied} | Slot1.CurrentTower = {(Slot1.CurrentTower != null ? Slot1.CurrentTower.Name : "null")}");

            GD.Print("[BuildTest] ---- 测试用例 2：重复建造（Slot1，已占用） ----");
            bool case2 = TowerManagerNode.TryBuildTower(Slot1, TestTowerData);
            GD.Print($"[BuildTest] 用例 2 结果: {(!case2 ? "✅ PASS (拒绝重复建造)" : "❌ FAIL (不应允许重复建造)")}");
            GD.Print($"[BuildTest] Slot1.IsOccupied = {Slot1.IsOccupied}");

            GD.Print("[BuildTest] ---- 测试用例 3：建造 Slot2（验证多槽位） ----");
            bool case3 = TowerManagerNode.TryBuildTower(Slot2, TestTowerData);
            GD.Print($"[BuildTest] 用例 3 结果: {(case3 ? "✅ PASS" : "❌ FAIL")}");
            GD.Print($"[BuildTest] Slot2.IsOccupied = {Slot2.IsOccupied}");

            GD.Print("[BuildTest] ---- 测试用例 4：尝试建造 Slot3（若当前金币 >= 成本则 PASS 并标记；若不足则 PASS 拒绝建造） ----");
            int currentGold = EconomyManager.Instance?.CurrentGold ?? 0;
            int cost = TestTowerData.BuildCost;
            GD.Print($"[BuildTest] 当前金币: {currentGold} | 建造成本: {cost}");
            bool case4 = TowerManagerNode.TryBuildTower(Slot3, TestTowerData);
            if (currentGold >= cost)
            {
                GD.Print($"[BuildTest] 用例 4 结果: {(case4 ? "✅ PASS (金币充足，建造成功)" : "❌ FAIL (金币充足但建造失败)")}");
            }
            else
            {
                GD.Print($"[BuildTest] 用例 4 结果: {(!case4 ? "✅ PASS (金币不足，正确拒绝建造)" : "❌ FAIL (金币不足却建造成功)")}");
            }

            GD.Print("[BuildTest] ========== 测试结束，汇总统计 ==========");
            GD.Print($"[BuildTest] 建造成功事件触发次数: {_buildSuccessCount}");
            GD.Print($"[BuildTest] 建造失败次数（TryBuildTower 返回 false）: {_buildFailCount}");
        }

        /// <summary>
        /// 处理防御塔建造成功事件。
        /// </summary>
        /// <param name="towerData">建造的塔数据</param>
        /// <param name="buildPosition">建造位置</param>
        private void HandleTowerBuilt(TowerData towerData, Vector2 buildPosition)
        {
            _buildSuccessCount++;
            GD.Print($"[BuildTest] ✅ OnTowerBuilt 事件触发！塔={towerData.TowerName} | 位置={buildPosition}");
        }

        /// <summary>
        /// 处理金币变更事件，仅用于日志观察。
        /// </summary>
        /// <param name="newGold">更新后的金币总数</param>
        private void HandleGoldChanged(int newGold)
        {
            GD.Print($"[BuildTest] 💰 当前金币变更: {newGold}");
        }
    }
}
