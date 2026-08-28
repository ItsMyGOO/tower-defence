using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.UI.LevelSelect
{
    /// <summary>
    /// 选关界面控制器。
    /// 挂载到 LevelSelect.tscn 根节点，根据 SceneManager.MaxUnlockedLevel
    /// 决定各关卡按钮的可用状态：已解锁关卡可点击加载，未解锁关卡自动置灰禁用并显示锁图标。
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

        /// <summary>
        /// 获取或设置第 1 关选择按钮。
        /// </summary>
        [Export] public Button LevelButton_01 { get; set; }

        /// <summary>
        /// 获取或设置第 2 关选择按钮。
        /// </summary>
        [Export] public Button LevelButton_02 { get; set; }

        /// <summary>
        /// 获取或设置第 3 关选择按钮（占位，暂未实现，保持禁用）。
        /// </summary>
        [Export] public Button LevelButton_03 { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 依次执行：UI 节点引用兜底解析 → 绑定按钮点击回调 → 根据解锁进度刷新按钮状态。
        /// </summary>
        public override void _Ready()
        {
            ResolveUINodeReferences();

            if (BackButton != null)
            {
                BackButton.Pressed += HandleBackPressed;
            }

            BindLevelButton(LevelButton_01, 1);
            BindLevelButton(LevelButton_02, 2);
            BindLevelButton(LevelButton_03, 3);

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

            UnbindLevelButton(LevelButton_01, 1);
            UnbindLevelButton(LevelButton_02, 2);
            UnbindLevelButton(LevelButton_03, 3);
        }

        #endregion

        #region UI 节点引用兜底解析

        /// <summary>
        /// 为所有 Export 的 UI 节点引用做相对路径兜底赋值。
        /// 保证节点树层级固定的情况下，即使 .tscn 文本序列化 NodePath 丢失仍可正常工作。
        /// </summary>
        private void ResolveUINodeReferences()
        {
            BackButton ??= GetNodeOrNull<Button>("TopBar/BackButton");
            LevelButton_01 ??= GetNodeOrNull<Button>("CenterContainer/GridContainer/LevelButton_01");
            LevelButton_02 ??= GetNodeOrNull<Button>("CenterContainer/GridContainer/LevelButton_02");
            LevelButton_03 ??= GetNodeOrNull<Button>("CenterContainer/GridContainer/LevelButton_03");

            int missing = 0;
            if (BackButton == null) { GD.PrintErr("[LevelSelect] 兜底解析失败: BackButton"); missing++; }
            if (LevelButton_01 == null) { GD.PrintErr("[LevelSelect] 兜底解析失败: LevelButton_01"); missing++; }
            if (LevelButton_02 == null) { GD.PrintErr("[LevelSelect] 兜底解析失败: LevelButton_02"); missing++; }
            if (LevelButton_03 == null) { GD.PrintErr("[LevelSelect] 兜底解析失败: LevelButton_03"); missing++; }

            if (missing == 0)
            {
                GD.Print("[LevelSelect] ✅ 4 个 UI 节点引用兜底解析全部成功。");
            }
        }

        #endregion

        #region 关卡按钮绑定与解绑

        /// <summary>
        /// 为单个关卡按钮绑定点击回调。
        /// 点击时通过 SceneManager.LoadLevel(levelIndex) 进入对应关卡。
        /// </summary>
        /// <param name="button">目标按钮节点</param>
        /// <param name="levelIndex">按钮对应的关卡序号（从 1 开始）</param>
        private void BindLevelButton(Button button, int levelIndex)
        {
            if (button == null) return;
            button.Pressed += () => HandleLevelPressed(levelIndex);
        }

        /// <summary>
        /// 对称解绑关卡按钮的点击回调，防止委托悬空。
        /// </summary>
        /// <param name="button">目标按钮节点</param>
        /// <param name="levelIndex">按钮对应的关卡序号（日志用）</param>
        private void UnbindLevelButton(Button button, int levelIndex)
        {
            if (button == null) return;
            button.Pressed -= () => HandleLevelPressed(levelIndex);
        }

        #endregion

        #region 按钮状态刷新

        /// <summary>
        /// 根据 SceneManager.MaxUnlockedLevel 刷新所有关卡按钮的可用状态。
        /// - 已解锁（levelIndex <= MaxUnlockedLevel）：按钮 Disabled = false，正常显示；
        /// - 未解锁（levelIndex > MaxUnlockedLevel）：按钮 Disabled = true，文本追加 🔒 并灰化。
        /// 同时检查对应关卡场景文件是否存在，不存在的场景即使已解锁也标记为占位状态。
        /// </summary>
        private void RefreshButtonStates()
        {
            int maxUnlocked = SceneManager.Instance?.MaxUnlockedLevel ?? 1;
            GD.Print($"[LevelSelect] 当前最大解锁关卡 = {maxUnlocked}，正在刷新按钮状态...");

            ApplyButtonState(LevelButton_01, 1, maxUnlocked, "第 1 关 · 新手教学");
            ApplyButtonState(LevelButton_02, 2, maxUnlocked, "第 2 关 · 进阶挑战");
            ApplyButtonState(LevelButton_03, 3, maxUnlocked, "第 3 关 · 敬请期待");
        }

        /// <summary>
        /// 对单个关卡按钮应用可用状态与显示文本。
        /// 先校验关卡场景文件是否存在，不存在则标记为占位；
        /// 再根据解锁序号决定按钮是否可点击，未解锁时追加锁图标。
        /// </summary>
        /// <param name="button">目标按钮节点</param>
        /// <param name="levelIndex">按钮对应的关卡序号</param>
        /// <param name="maxUnlocked">当前最大解锁关卡序号</param>
        /// <param name="baseText">按钮基础显示文本</param>
        private void ApplyButtonState(Button button, int levelIndex, int maxUnlocked, string baseText)
        {
            if (button == null) return;

            string scenePath = string.Format(SceneManager.LevelScenePathTemplate, levelIndex);
            bool sceneExists = ResourceLoader.Exists(scenePath, "PackedScene");

            if (!sceneExists)
            {
                button.Disabled = true;
                button.Text = $"{baseText}  🚧 (场景未制作)";
                button.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                GD.Print($"[LevelSelect] 关卡 {levelIndex} 场景文件不存在，标记为占位。");
                return;
            }

            if (levelIndex <= maxUnlocked)
            {
                button.Disabled = false;
                button.Text = baseText;
                button.Modulate = Colors.White;
                GD.Print($"[LevelSelect] 关卡 {levelIndex} 已解锁 → 按钮启用。");
            }
            else
            {
                button.Disabled = true;
                button.Text = $"{baseText}  🔒";
                button.Modulate = new Color(0.45f, 0.45f, 0.5f, 0.9f);
                GD.Print($"[LevelSelect] 关卡 {levelIndex} 未解锁 → 按钮禁用并加锁。");
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
