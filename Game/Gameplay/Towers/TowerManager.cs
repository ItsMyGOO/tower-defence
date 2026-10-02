using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;
using TowerDefence.Gameplay.Economy;

namespace TowerDefence.Gameplay.Towers
{
    /// <summary>
    /// 防御塔建造管理器节点。
    /// 作为建造事务的统一入口，负责校验槽位状态、扣除金币、实例化塔预制体并挂载到槽位，
    /// 事务成功后通过 EventBus 广播 OnTowerBuilt 事件供 UI/音效等模块响应。
    /// 同时持有唯一的槽位环形菜单：玩家点击槽位即弹出——空槽位为建造环（可用塔按
    /// Config/Towers 目录扫描自动发现），已占用槽位为升级/出售环。
    /// </summary>
    public partial class TowerManager : Node
    {
        /// <summary>
        /// 获取 TowerManager 的全局单例实例。
        /// 用于 UI 层与建造槽位等模块快速访问建造管理器，
        /// 需确保场景中仅存在一个 TowerManager 实例，否则可能导致引用非预期节点。
        /// </summary>
        public static TowerManager Instance { get; private set; }

        /// <summary>
        /// 可用塔配置所在目录（按目录扫描自动发现，新增塔 .tres 无需改代码）。
        /// </summary>
        private const string TowerConfigDir = "res://Game/Config/Towers";

        /// <summary>
        /// 塔配置文件名匹配规则，兼容导出包内重映射的 .tres.remap。
        /// </summary>
        private static readonly Regex TowerConfigRegex = new(@"^[\w\-]+\.tres(\.remap)?$", RegexOptions.Compiled);

        #region 导出配置

        /// <summary>
        /// 获取或设置防御塔通用预制体场景。
        /// 该预制体的根节点需为 Tower 类型，建造时会被实例化并注入具体的 TowerData 配置。
        /// </summary>
        [Export] public PackedScene TowerBaseScene { get; set; }

        #endregion

        #region 运行时状态

        /// <summary>
        /// 槽位环形菜单唯一实例。
        /// </summary>
        private TowerRadialMenu _radialMenu;

        /// <summary>
        /// 获取槽位环形菜单实例（供测试与调试读取菜单状态）。
        /// </summary>
        public TowerRadialMenu RadialMenu => _radialMenu;

        /// <summary>
        /// 扫描 Config/Towers 目录所得的可用塔配置缓存。
        /// </summary>
        private List<TowerData> _availableTowers;

        /// <summary>
        /// 获取扫描到的可用塔配置列表（惰性扫描，失败时为空列表）。
        /// </summary>
        public IReadOnlyList<TowerData> AvailableTowers
        {
            get
            {
                _availableTowers ??= DiscoverTowers();
                return _availableTowers;
            }
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化单例引用并创建槽位环形菜单子节点。
        /// </summary>
        public override void _Ready()
        {
            Instance = this;

            _radialMenu = new TowerRadialMenu { Name = "TowerRadialMenu" };
            AddChild(_radialMenu);
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 清空单例引用，避免引用已销毁节点。
        /// </summary>
        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region 槽位菜单管理

        /// <summary>
        /// 在指定槽位打开环形菜单：空槽位弹建造环，已占用槽位弹升级/出售环。
        /// 由 TowerSlot 左键点击触发。
        /// </summary>
        /// <param name="slot">被点击的槽位</param>
        public void OpenSlotMenu(TowerSlot slot)
        {
            if (slot == null)
            {
                GD.PrintErr("[TowerManager] OpenSlotMenu 失败：slot 为 null。");
                return;
            }

            if (slot.IsOccupied)
            {
                _radialMenu.OpenUpgrade(slot);
            }
            else
            {
                _radialMenu.OpenBuild(slot, AvailableTowers);
            }
        }

        /// <summary>
        /// 扫描塔配置目录，加载所有 TowerData 资源并按文件名排序。
        /// </summary>
        /// <returns>可用塔配置列表</returns>
        private static List<TowerData> DiscoverTowers()
        {
            var towers = new List<TowerData>();
            var fileNames = new List<string>();

            using var dir = DirAccess.Open(TowerConfigDir);
            if (dir == null)
            {
                GD.PrintErr($"[TowerManager] 无法打开塔配置目录 {TowerConfigDir}（错误: {DirAccess.GetOpenError()}）。");
                return towers;
            }

            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (!string.IsNullOrEmpty(fileName))
            {
                if (!dir.CurrentIsDir() && TowerConfigRegex.IsMatch(fileName))
                {
                    fileNames.Add(fileName);
                }

                fileName = dir.GetNext();
            }
            dir.ListDirEnd();

            fileNames.Sort();

            foreach (string fileName2 in fileNames)
            {
                string path = $"{TowerConfigDir}/{fileName2}";
                var data = ResourceLoader.Load<TowerData>(path);
                if (data != null)
                {
                    towers.Add(data);
                }
                else
                {
                    GD.PrintErr($"[TowerManager] 塔配置加载失败或类型不符: {path}");
                }
            }

            GD.Print($"[TowerManager] ✅ 从 {TowerConfigDir} 扫描到 {towers.Count} 种可用塔。");
            return towers;
        }

        #endregion

        #region 公共接口 —— 建造事务

        /// <summary>
        /// 尝试在指定槽位上建造防御塔。
        /// 执行顺序：槽位有效性校验 → 金币校验并扣费 → 实例化塔预制体 → 挂载到槽位 → 广播建造事件。
        /// 任意一步失败均返回 false，保证事务一致性（扣费仅在后续步骤均成功时发生）。
        /// </summary>
        /// <param name="slot">目标建造槽位</param>
        /// <param name="towerData">要建造的塔配置数据</param>
        /// <returns>true 表示建造成功，false 表示任意校验或实例化失败</returns>
        public bool TryBuildTower(TowerSlot slot, TowerData towerData)
        {
            if (slot == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：slot 为 null。");
                return false;
            }

            if (towerData == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：towerData 为 null。");
                return false;
            }

            if (slot.IsOccupied)
            {
                GD.Print($"[TowerManager] 建造失败：槽位 {slot.Name} 已被占用。");
                return false;
            }

            if (EconomyManager.Instance == null)
            {
                GD.PrintErr("[TowerManager] 建造失败：EconomyManager 实例不存在。");
                return false;
            }

            if (!EconomyManager.Instance.TrySpendGold(towerData.BuildCost))
            {
                GD.Print($"[TowerManager] 建造失败：金币不足。需要 {towerData.BuildCost}，当前 {EconomyManager.Instance.CurrentGold}。");
                return false;
            }

            Tower towerInstance = null;
            if (TowerBaseScene != null)
            {
                towerInstance = TowerBaseScene.Instantiate<Tower>();
            }
            else
            {
                GD.Print("[TowerManager] 警告：TowerBaseScene 未设置，使用动态创建 Tower 节点作为兜底。");
                towerInstance = new Tower();
            }

            towerInstance.Name = $"Tower_{towerData.TowerId}_{slot.Name}";
            towerInstance.Data = towerData;

            if (!slot.PlaceTower(towerInstance))
            {
                GD.PrintErr("[TowerManager] 建造失败：slot.PlaceTower 返回 false，返还金币。");
                EconomyManager.Instance.AddGold(towerData.BuildCost);
                towerInstance.QueueFree();
                return false;
            }

            EventBus.RaiseTowerBuilt(towerData, slot.GlobalPosition);

            GD.Print($"[TowerManager] ✅ 建造成功！塔={towerData.TowerName} | 槽位={slot.Name} | 位置={slot.GlobalPosition} | 剩余金币={EconomyManager.Instance.CurrentGold}");
            return true;
        }

        #endregion
    }
}
