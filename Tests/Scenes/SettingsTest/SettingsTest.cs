using Godot;
using TowerDefence.Core.Managers;

namespace TowerDefence.Tests.Scenes
{
    /// <summary>
    /// 设置系统测试场景控制器（全代码构建，无 Inspector 绑定依赖）。
    /// 覆盖以下行为：
    /// 1) SettingsManager AutoLoad 单例就绪，默认主音量合法；
    /// 2) 设置主音量立即应用到 AudioServer 的 Master 总线（线性→dB）；
    /// 3) 超界输入被钳制到 [0, 1]；
    /// 4) 持久化 round-trip：写盘 → 修改内存值 → 重读恢复（注入测试路径，不污染真实存档）。
    /// 全部断言结果打印 ✅/❌ 与最终汇总；无头模式以进程退出码上报结果。
    /// </summary>
    public partial class SettingsTest : Node2D
    {
        private int _passed;
        private int _failed;

        /// <summary>
        /// 节点就绪后启动测试序列。
        /// </summary>
        public override void _Ready()
        {
            GD.Print("[SettingsTest] ========== 设置系统测试启动 ==========");
            _ = RunAllAsync();
        }

        /// <summary>
        /// 测试主序列：依次执行单例/默认值、总线应用、边界钳制、持久化 round-trip 并输出汇总。
        /// </summary>
        private async System.Threading.Tasks.Task RunAllAsync()
        {
            try
            {
                var settings = SettingsManager.Instance;
                AssertTrue(settings != null, "单例: SettingsManager AutoLoad 就绪");
                AssertTrue(
                    settings != null && settings.MasterVolume > 0.0f && settings.MasterVolume <= 1.0f,
                    "默认值: 主音量位于 (0, 1] 区间"
                );

                // --- 设置音量立即应用到 Master 总线 ---
                const float testPathVolume = 0.5f;
                settings?.SetMasterVolume(testPathVolume, persist: false);
                int masterBus = AudioServer.GetBusIndex("Master");
                AssertTrue(
                    settings != null
                        && Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(masterBus), Mathf.LinearToDb(testPathVolume)),
                    "应用: 0.5 线性音量写入 Master 总线 dB"
                );

                // --- 超界钳制 ---
                settings?.SetMasterVolume(1.7f, persist: false);
                AssertTrue(
                    settings != null && Mathf.IsEqualApprox(settings.MasterVolume, 1.0f),
                    "边界: 超界输入被钳制到 [0, 1]"
                );

                // --- 持久化 round-trip（注入测试路径，不碰真实存档） ---
                const string testPath = "user://SettingsTest.cfg";
                settings?.SetMasterVolume(0.35f, persist: false);
                settings?.SaveToDisk(testPath);
                settings?.SetMasterVolume(0.9f, persist: false);
                settings?.LoadFromDisk(testPath);
                AssertTrue(
                    settings != null && Mathf.IsEqualApprox(settings.MasterVolume, 0.35f),
                    "持久化: 写盘 → 改值 → 重读恢复"
                );

                GD.Print($"[SettingsTest] ========== 测试结束：PASS {_passed} / FAIL {_failed} ==========");

                // 无头（CI）模式：以进程退出码上报测试结果，1 = 存在失败断言
                if (DisplayServer.GetName() == "headless")
                {
                    await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
                    GetTree().Quit(_failed > 0 ? 1 : 0);
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[SettingsTest] ❌ 测试序列异常中止: {ex}");

                if (DisplayServer.GetName() == "headless")
                {
                    GetTree().Quit(1);
                }
            }
        }

        /// <summary>
        /// 记录一条断言结果。
        /// </summary>
        /// <param name="condition">断言条件</param>
        /// <param name="name">断言名称（日志用）</param>
        private void AssertTrue(bool condition, string name)
        {
            if (condition)
            {
                _passed++;
                GD.Print($"[SettingsTest] ✅ PASS {name}");
            }
            else
            {
                _failed++;
                GD.PrintErr($"[SettingsTest] ❌ FAIL {name}");
            }
        }
    }
}
