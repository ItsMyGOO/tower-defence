using Godot;

namespace TowerDefence.Core.Managers
{
    /// <summary>
    /// 全局场景流转管理器。
    /// 负责主菜单、选关界面与关卡场景之间的统一切换与加载，
    /// 同时维护玩家的关卡解锁进度 MaxUnlockedLevel，通过 Godot ConfigFile 写入用户目录实现本地持久化，
    /// 构成局外 Meta Loop（菜单 → 选关 → 通关 → 解锁 → 下一关）的架构闭环。
    /// 建议配置为 AutoLoad 单例，通过 SceneManager.Instance 在任意模块访问。
    /// </summary>
    public partial class SceneManager : Node
    {
        #region 单例访问

        /// <summary>
        /// 获取 SceneManager 的全局单例实例。
        /// 要求在 project.godot 的 AutoLoad 列表中注册本类，否则为 null。
        /// </summary>
        public static SceneManager Instance { get; private set; }

        #endregion

        #region 常量 —— 场景路径

        /// <summary>
        /// 主菜单场景资源路径。
        /// </summary>
        public const string MainMenuScenePath = "res://Game/UI/MainMenu/MainMenu.tscn";

        /// <summary>
        /// 选关界面场景资源路径。
        /// </summary>
        public const string LevelSelectScenePath = "res://Game/UI/LevelSelect/LevelSelect.tscn";

        /// <summary>
        /// 关卡场景路径格式化模板，使用 {0:D2} 拼接两位序号（如 01、02）。
        /// </summary>
        public const string LevelScenePathTemplate = "res://Game/Gameplay/Map/Level_{0:D2}.tscn";

        #endregion

        #region 常量 —— 本地存档 Key 与文件路径

        /// <summary>
        /// 存储关卡解锁进度的本地存档文件名（位于 OS.GetUserDataDir() 下）。
        /// </summary>
        private const string SaveFileName = "TowerDefence_Save.cfg";

        /// <summary>
        /// 本地存档 ConfigFile 中用于存储最大解锁关卡序号的 Section/Key 组合。
        /// </summary>
        private const string SaveSectionMeta = "Meta";
        private const string SaveKeyMaxUnlockedLevel = "MaxUnlockedLevel";

        #endregion

        #region 运行时状态

        /// <summary>
        /// 获取当前已解锁的最大关卡序号（从 1 开始计数）。
        /// 默认为 1，即玩家进入游戏时至少可选择第一关。
        /// </summary>
        public int MaxUnlockedLevel { get; private set; } = 1;

        /// <summary>
        /// 获取当前正在进行或最近一次加载的关卡序号（从 1 开始）。
        /// 尚未进入任何关卡时为 0；LoadLevel / LoadNextLevel 成功后会更新此值。
        /// </summary>
        public int CurrentLevelIndex { get; private set; } = 0;

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化单例引用并通过 ConfigFile 从用户目录读取已保存的解锁进度，
        /// 若存档文件不存在或读取失败则保持默认值 1（至少解锁第一关）。
        /// </summary>
        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                GD.PrintErr("[SceneManager] 检测到重复实例化，AutoLoad 单例仅允许存在一个 SceneManager。");
                QueueFree();
                return;
            }

            Instance = this;
            LoadProgressFromDisk();
        }

        #endregion

        #region 本地存档读写（Godot ConfigFile 封装）

        /// <summary>
        /// 获取本地存档文件的完整绝对路径（拼接 OS.GetUserDataDir() 与 SaveFileName）。
        /// </summary>
        private static string GetSaveFilePath()
        {
            return System.IO.Path.Combine(OS.GetUserDataDir(), SaveFileName);
        }

        /// <summary>
        /// 从用户目录的 ConfigFile 存档中读取已保存的 MaxUnlockedLevel。
        /// 文件不存在、Section/Key 缺失或读取异常时，安全回退到默认值 1，绝不抛异常。
        /// </summary>
        private void LoadProgressFromDisk()
        {
            string savePath = GetSaveFilePath();
            using var cfg = new ConfigFile();
            Error loadErr = cfg.Load(savePath);

            if (loadErr == Error.Ok && cfg.HasSectionKey(SaveSectionMeta, SaveKeyMaxUnlockedLevel))
            {
                int savedValue = (int)cfg.GetValue(SaveSectionMeta, SaveKeyMaxUnlockedLevel, 1);
                MaxUnlockedLevel = Mathf.Max(1, savedValue);
                GD.Print($"[SceneManager] 已从本地存档恢复解锁进度 → 最大解锁关卡 = {MaxUnlockedLevel} (路径: {savePath})");
            }
            else
            {
                if (loadErr != Error.Ok && loadErr != Error.FileNotFound)
                {
                    GD.Print($"[SceneManager] 本地存档读取异常（{loadErr}），使用默认解锁进度。路径: {savePath}");
                }
                else
                {
                    GD.Print($"[SceneManager] 未检测到本地存档，使用默认解锁进度 → 最大解锁关卡 = {MaxUnlockedLevel}");
                }
            }
        }

        /// <summary>
        /// 将当前 MaxUnlockedLevel 写入用户目录的 ConfigFile 存档并立即落盘。
        /// 任何写入错误仅打印日志，不向上抛异常，保证游戏流程不受存档失败影响。
        /// </summary>
        private void SaveProgressToDisk()
        {
            string savePath = GetSaveFilePath();
            using var cfg = new ConfigFile();

            if (FileAccess.FileExists(savePath))
            {
                Error loadErr = cfg.Load(savePath);
                if (loadErr != Error.Ok && loadErr != Error.FileNotFound)
                {
                    GD.Print($"[SceneManager] 存档读取阶段异常（{loadErr}），将以空 ConfigFile 覆盖重写。");
                }
            }

            cfg.SetValue(SaveSectionMeta, SaveKeyMaxUnlockedLevel, MaxUnlockedLevel);
            Error saveErr = cfg.Save(savePath);
            if (saveErr == Error.Ok)
            {
                GD.Print($"[SceneManager] 本地存档写入成功 → 最大解锁关卡 = {MaxUnlockedLevel} (路径: {savePath})");
            }
            else
            {
                GD.PrintErr($"[SceneManager] 本地存档写入失败：{saveErr}。路径: {savePath}");
            }
        }

        #endregion

        #region 公共方法 —— 场景切换

        /// <summary>
        /// 通用场景加载方法：按指定资源路径切换场景。
        /// 切换前先解除全局暂停状态，确保下一场景从干净的非暂停时钟启动；
        /// 若路径无效则打印错误日志并直接返回，不抛异常。
        /// </summary>
        /// <param name="scenePath">目标场景的 res:// 资源路径</param>
        public void LoadScene(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                GD.PrintErr("[SceneManager] LoadScene 失败：scenePath 为空。");
                return;
            }

            if (!ResourceLoader.Exists(scenePath, "PackedScene"))
            {
                GD.PrintErr($"[SceneManager] LoadScene 失败：场景资源不存在 → {scenePath}");
                return;
            }

            GetTree().Paused = false;
            GD.Print($"[SceneManager] 正在切换场景 → {scenePath}");
            GetTree().ChangeSceneToFile(scenePath);
        }

        /// <summary>
        /// 载入主菜单场景。
        /// 加载前会将 CurrentLevelIndex 重置为 0，表示当前不在任何关卡内。
        /// </summary>
        public void LoadMainMenu()
        {
            CurrentLevelIndex = 0;
            LoadScene(MainMenuScenePath);
        }

        /// <summary>
        /// 载入选关界面场景。
        /// 选关界面会根据 MaxUnlockedLevel 决定哪些关卡按钮可交互。
        /// </summary>
        public void LoadLevelSelect()
        {
            LoadScene(LevelSelectScenePath);
        }

        /// <summary>
        /// 按序号载入指定关卡场景。
        /// 根据 LevelScenePathTemplate 模板拼接实际资源路径，
        /// 加载成功后更新 CurrentLevelIndex，供后续 UnlockNextLevel / LoadNextLevel 使用。
        /// </summary>
        /// <param name="levelIndex">目标关卡序号（从 1 开始）</param>
        public void LoadLevel(int levelIndex)
        {
            if (levelIndex < 1)
            {
                GD.PrintErr($"[SceneManager] LoadLevel 失败：无效的关卡序号 {levelIndex}，必须 >= 1。");
                return;
            }

            if (levelIndex > MaxUnlockedLevel)
            {
                GD.PrintErr($"[SceneManager] LoadLevel 失败：关卡 {levelIndex} 尚未解锁（当前最大解锁 = {MaxUnlockedLevel}）。");
                return;
            }

            string scenePath = string.Format(LevelScenePathTemplate, levelIndex);
            if (!ResourceLoader.Exists(scenePath, "PackedScene"))
            {
                GD.PrintErr($"[SceneManager] LoadLevel 失败：关卡场景不存在 → {scenePath}");
                return;
            }

            CurrentLevelIndex = levelIndex;
            LoadScene(scenePath);
        }

        /// <summary>
        /// 解锁下一关：通关当前最高关卡后，将 MaxUnlockedLevel 自增 1 并通过 ConfigFile 写入用户目录。
        /// 仅当 CurrentLevelIndex == MaxUnlockedLevel 时才会真正推进解锁进度，
        /// 多次调用或在非最高关卡通关时均安全幂等，不会重复推进进度。
        /// </summary>
        public void UnlockNextLevel()
        {
            if (CurrentLevelIndex <= 0)
            {
                GD.Print("[SceneManager] UnlockNextLevel：CurrentLevelIndex 为 0，跳过解锁。");
                return;
            }

            if (CurrentLevelIndex != MaxUnlockedLevel)
            {
                GD.Print($"[SceneManager] UnlockNextLevel：当前关卡 {CurrentLevelIndex} 并非最大已解锁 {MaxUnlockedLevel}，不推进进度。");
                return;
            }

            int newMax = MaxUnlockedLevel + 1;
            MaxUnlockedLevel = newMax;
            SaveProgressToDisk();
            GD.Print($"[SceneManager] ✅ 解锁进度已更新 → 最大解锁关卡 = {MaxUnlockedLevel}");
        }

        /// <summary>
        /// 自动载入下一关：按 CurrentLevelIndex + 1 计算目标关卡序号并调用 LoadLevel。
        /// 若未处于任何关卡或已通关全部关卡则打印提示并跳转到选关界面，保证流程始终有出口。
        /// </summary>
        public void LoadNextLevel()
        {
            if (CurrentLevelIndex <= 0)
            {
                GD.Print("[SceneManager] LoadNextLevel：未处于任何关卡，返回选关界面。");
                LoadLevelSelect();
                return;
            }

            int nextIndex = CurrentLevelIndex + 1;
            string nextScenePath = string.Format(LevelScenePathTemplate, nextIndex);

            if (nextIndex > MaxUnlockedLevel)
            {
                GD.Print($"[SceneManager] LoadNextLevel：下一关 {nextIndex} 尚未解锁，返回选关界面。");
                LoadLevelSelect();
                return;
            }

            if (!ResourceLoader.Exists(nextScenePath, "PackedScene"))
            {
                GD.Print($"[SceneManager] LoadNextLevel：下一关 {nextIndex} 场景不存在，返回选关界面。");
                LoadLevelSelect();
                return;
            }

            GD.Print($"[SceneManager] 正在载入下一关 → 关卡 {nextIndex}");
            LoadLevel(nextIndex);
        }

        #endregion
    }
}
