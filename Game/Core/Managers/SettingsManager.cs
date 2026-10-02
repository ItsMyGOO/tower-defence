using Godot;

namespace TowerDefence.Core.Managers
{
    /// <summary>
    /// 全局设置管理器（AutoLoad 常驻单例）。
    /// 负责主音量的状态维护、AudioServer 应用与本地持久化。
    /// 与 AudioManager 职责互补：本类只管「音量多大且存得住」，不管「播什么、怎么播」。
    /// 持久化文件独立于 SceneManager 的关卡进度存档，删除任一不影响另一个。
    /// </summary>
    public partial class SettingsManager : Node
    {
        /// <summary>
        /// 获取 SettingsManager 的全局单例实例（AutoLoad）。
        /// </summary>
        public static SettingsManager Instance { get; private set; }

        /// <summary>
        /// 持久化文件路径（user:// 目录，独立于关卡进度存档 TowerDefence_Save.cfg）。
        /// </summary>
        private const string SavePath = "user://TowerDefence_Settings.cfg";

        /// <summary>
        /// ConfigFile 中音频设置的 Section / Key。
        /// </summary>
        private const string SaveSectionAudio = "Audio";
        private const string SaveKeyMasterVolume = "MasterVolume";

        /// <summary>
        /// 默认主音量（线性 0.8，约 -1.9dB，为音效峰值留出头部空间）。
        /// </summary>
        public const float DefaultMasterVolume = 0.8f;

        /// <summary>
        /// 获取当前主音量（线性，0 = 静音，1 = 满音量）。
        /// </summary>
        public float MasterVolume { get; private set; } = DefaultMasterVolume;

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 初始化单例引用，从本地存档恢复音量并应用到音频总线。
        /// </summary>
        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                GD.PrintErr("[SettingsManager] 检测到重复实例化，AutoLoad 单例仅允许存在一个 SettingsManager。");
                QueueFree();
                return;
            }

            Instance = this;
            LoadFromDisk();
            ApplyToAudioBuses();
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。清空单例引用。
        /// </summary>
        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 设置主音量：钳制到 [0, 1] 后立即应用到 Master 总线。
        /// persist = true 时同步写盘（文件极小，UI 拖动即存的成本可忽略）；
        /// persist = false 供测试与批量调用复用同一入口而不落盘。
        /// </summary>
        /// <param name="linear">线性音量（0 = 静音，1 = 满音量）</param>
        /// <param name="persist">是否立即持久化到本地存档</param>
        public void SetMasterVolume(float linear, bool persist = true)
        {
            MasterVolume = Mathf.Clamp(linear, 0.0f, 1.0f);
            ApplyToAudioBuses();

            if (persist)
            {
                SaveToDisk();
            }
        }

        /// <summary>
        /// 将当前主音量写入指定 ConfigFile 路径。
        /// 默认写正式存档路径；测试注入临时路径以隔离真实存档。
        /// </summary>
        /// <param name="path">目标存档路径（user:// 协议）</param>
        public void SaveToDisk(string path = SavePath)
        {
            using var cfg = new ConfigFile();
            cfg.SetValue(SaveSectionAudio, SaveKeyMasterVolume, MasterVolume);

            Error saveErr = cfg.Save(path);
            if (saveErr != Error.Ok)
            {
                GD.PrintErr($"[SettingsManager] 设置写入失败：{saveErr}。路径: {path}");
            }
        }

        /// <summary>
        /// 从指定 ConfigFile 路径读取主音量；文件缺失或损坏时保持当前值不抛异常。
        /// 只读值不应用总线——应用时机由调用方决定（_Ready 启动时整体应用一次）。
        /// </summary>
        /// <param name="path">来源存档路径（user:// 协议）</param>
        public void LoadFromDisk(string path = SavePath)
        {
            using var cfg = new ConfigFile();
            Error loadErr = cfg.Load(path);

            if (loadErr == Error.FileNotFound)
            {
                return;
            }

            if (loadErr != Error.Ok)
            {
                GD.Print($"[SettingsManager] 设置读取异常（{loadErr}），保持当前音量。路径: {path}");
                return;
            }

            float savedVolume = (float)cfg.GetValue(SaveSectionAudio, SaveKeyMasterVolume, DefaultMasterVolume);
            MasterVolume = Mathf.Clamp(savedVolume, 0.0f, 1.0f);
        }

        /// <summary>
        /// 将当前主音量（线性）换算为 dB 写入 Master 总线。0 音量对应 -inf dB 即静音。
        /// </summary>
        private void ApplyToAudioBuses()
        {
            int masterBus = AudioServer.GetBusIndex("Master");
            AudioServer.SetBusVolumeDb(masterBus, Mathf.LinearToDb(MasterVolume));
        }
    }
}
