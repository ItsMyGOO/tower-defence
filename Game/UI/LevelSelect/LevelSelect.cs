using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.LevelSelect
{
    /// <summary>
    /// 选关界面控制器。
    /// 挂载到 LevelSelect.tscn 根节点，启动时扫描关卡场景目录（Level_XX.tscn 命名约定）
    /// 动态生成关卡按钮——新增关卡只需放入符合命名约定的场景文件，无需改动任何代码；
    /// 再根据 SceneManager.MaxUnlockedLevel 决定各按钮可用状态：已解锁可点击，未解锁置灰加锁。
    /// 同时提供返回主菜单按钮，构成菜单 → 选关 → 关卡的完整流转链路。
    /// </summary>
    public partial class LevelSelect : Control
    {
        #region UI 节点引用

        /// <summary>
        /// 获取或设置"返回主菜单"按钮节点引用。
        /// 点击后通过 SceneManager 切回主菜单。
        /// </summary>
        [Export] public Button BackButton { get; set; }

        #endregion

        #region 常量 —— 关卡场景约定

        /// <summary>
        /// 关卡场景所在目录（Level_XX.tscn 命名约定）。
        /// </summary>
        private const string LevelMapDir = "res://Game/Gameplay/Map";

        /// <summary>
        /// 关卡场景文件名匹配规则。
        /// 兼容编辑器环境的 Level_01.tscn 与导出包内重映射的 Level_01.tscn.remap。
        /// </summary>
        private static readonly Regex LevelSceneRegex = new(@"^Level_(\d{2})\.tscn(\.remap)?$", RegexOptions.Compiled);

        #endregion

        #region 内部状态 —— 按钮与委托引用缓存

        /// <summary>
        /// 关卡按钮点击回调的委托引用缓存。
        /// 键为关卡序号（从 1 开始），值为绑定时创建的 Action 委托实例。
        /// 由于 C# lambda 每次求值都会生成新的委托实例，必须在解绑时使用与绑定时完全相同的对象，
        /// 否则 Godot.NativeCalls.Disconnect 会因找不到精确 callable 匹配而抛出 "Attempt to disconnect a nonexistent connection" 错误。
        /// </summary>
        private readonly Dictionary<int, Action> _levelButtonHandlerCache = new();

        /// <summary>
        /// 动态生成的关卡按钮集合，键为关卡序号。
        /// </summary>
        private readonly Dictionary<int, Button> _levelButtons = new();

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：UI 节点引用兜底解析 → 绑定返回按钮 → 扫描目录动态生成关卡按钮并绑定回调 → 刷新按钮状态。
        /// </summary>
        public override void _Ready()
        {
            ResolveUINodeReferences();

            if (BackButton != null)
            {
                BackButton.Pressed += HandleBackPressed;
            }

            BuildLevelButtons();
            RefreshButtonStates();

            GD.Print("[LevelSelect] ✅ 选关界面加载完成。");
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有按钮点击事件绑定，防止委托悬空。
        /// </summary>
        public override void _ExitTree()
        {
            if (BackButton != null)
            {
                BackButton.Pressed -= HandleBackPressed;
            }

            foreach (KeyValuePair<int, Action> pair in _levelButtonHandlerCache)
            {
                if (_levelButtons.TryGetValue(pair.Key, out Button button) && button != null)
                {
                    button.Pressed -= pair.Value;
                }
            }

            _levelButtonHandlerCache.Clear();
            _levelButtons.Clear();
        }

        #endregion

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为 Export 的 UI 节点引用做相对路径兜底赋值。
        /// 保证节点树层级固定的情况下，即使 .tscn 文本序列化 NodePath 丢失仍可正常工作。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            BackButton ??= GetNodeOrNull<Button>("TopBar/BackButton");

            if (BackButton == null)
            {
                GD.PrintErr("[LevelSelect] 兜底解析失败: BackButton");
            }
        }

        #endregion

        #region 关卡按钮动态生成与绑定

        /// <summary>
        /// 扫描关卡场景目录，为每个符合 Level_XX.tscn 命名约定的关卡生成一个按钮并绑定点击回调。
        /// 场景文件不存在于目录中的关卡不会出现占位按钮——放入新关卡场景即可自动出现在选关界面。
        /// </summary>
        private void BuildLevelButtons()
        {
            var grid = GetNodeOrNull<GridContainer>("CenterContainer/GridContainer");
            if (grid == null)
            {
                GD.PrintErr("[LevelSelect] 兜底解析失败: GridContainer，无法生成关卡按钮。");
                return;
            }

            List<int> levelIndices = DiscoverLevelIndices();

            foreach (int levelIndex in levelIndices)
            {
                var button = new Button
                {
                    Name = $"LevelButton_{levelIndex:D2}",
                    Text = $"第 {levelIndex} 关",
                    CustomMinimumSize = new Vector2(220, 180),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill
                };
                button.AddThemeFontSizeOverride("font_size", 22);

                Action handler = () => HandleLevelPressed(levelIndex);
                _levelButtonHandlerCache[levelIndex] = handler;
                button.Pressed += handler;

                grid.AddChild(button);
                _levelButtons[levelIndex] = button;
            }

            if (levelIndices.Count == 0)
            {
                GD.PrintErr($"[LevelSelect] {LevelMapDir} 下未发现任何 Level_XX.tscn 关卡场景。");
            }
            else
            {
                GD.Print($"[LevelSelect] 按目录扫描动态生成 {levelIndices.Count} 个关卡按钮: {string.Join(", ", levelIndices)}。");
            }
        }

        /// <summary>
        /// 扫描关卡场景目录，收集所有符合命名约定的关卡序号并升序返回。
        /// </summary>
        /// <returns>升序关卡序号列表（从 1 开始）</returns>
        private static List<int> DiscoverLevelIndices()
        {
            var indices = new List<int>();

            using var dir = DirAccess.Open(LevelMapDir);
            if (dir == null)
            {
                GD.PrintErr($"[LevelSelect] 无法打开关卡目录 {LevelMapDir}（错误: {DirAccess.GetOpenError()}）。");
                return indices;
            }

            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (!string.IsNullOrEmpty(fileName))
            {
                if (!dir.CurrentIsDir())
                {
                    Match match = LevelSceneRegex.Match(fileName);
                    if (match.Success)
                    {
                        int index = int.Parse(match.Groups[1].Value);
                        if (!indices.Contains(index))
                        {
                            indices.Add(index);
                        }
                    }
                }

                fileName = dir.GetNext();
            }
            dir.ListDirEnd();

            indices.Sort();
            return indices;
        }

        #endregion

        #region 按钮状态刷新

        /// <summary>
        /// 根据 SceneManager.MaxUnlockedLevel 刷新所有关卡按钮的可用状态。
        /// - 已解锁（levelIndex &lt;= MaxUnlockedLevel）：按钮可点击，正常显示；
        /// - 未解锁（levelIndex &gt; MaxUnlockedLevel）：按钮禁用，文本追加 🔒 并灰化。
        /// </summary>
        private void RefreshButtonStates()
        {
            int maxUnlocked = SceneManager.Instance?.MaxUnlockedLevel ?? 1;
            GD.Print($"[LevelSelect] 当前最大解锁关卡 = {maxUnlocked}，正在刷新按钮状态...");

            foreach (KeyValuePair<int, Button> pair in _levelButtons)
            {
                ApplyButtonState(pair.Value, pair.Key, maxUnlocked);
            }
        }

        /// <summary>
        /// 对单个关卡按钮应用可用状态与显示文本。
        /// </summary>
        /// <param name="button">目标按钮节点</param>
        /// <param name="levelIndex">按钮对应的关卡序号</param>
        /// <param name="maxUnlocked">当前最大解锁关卡序号</param>
        private static void ApplyButtonState(Button button, int levelIndex, int maxUnlocked)
        {
            if (levelIndex <= maxUnlocked)
            {
                button.Disabled = false;
                button.Text = $"第 {levelIndex} 关";
                button.Modulate = Colors.White;
            }
            else
            {
                button.Disabled = true;
                button.Text = $"第 {levelIndex} 关  🔒";
                button.Modulate = new Color(0.45f, 0.45f, 0.5f, 0.9f);
            }
        }

        #endregion

        #region 按钮事件处理

        /// <summary>
        /// 处理"返回主菜单"按钮点击事件。
        /// 通过 SceneManager 单例载入主菜单场景。
        /// </summary>
        private void HandleBackPressed()
        {
            GD.Print("[LevelSelect] 玩家点击「返回主菜单」。");
            SceneManager.Instance?.LoadMainMenu();
        }

        /// <summary>
        /// 处理关卡按钮点击事件。
        /// 调用 SceneManager.LoadLevel(levelIndex) 加载指定关卡场景。
        /// </summary>
        /// <param name="levelIndex">目标关卡序号（从 1 开始）</param>
        private void HandleLevelPressed(int levelIndex)
        {
            GD.Print($"[LevelSelect] 玩家点击「关卡 {levelIndex}」，正在载入场景...");
            SceneManager.Instance?.LoadLevel(levelIndex);
        }

        #endregion
    }
}
