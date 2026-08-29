using Godot;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Core.Managers
{
    /// <summary>
    /// 全局视觉反馈特效管理器。
    /// 严格遵循单一职责原则：仅管理 CPUParticles2D / GPUParticles2D 等视觉特效预制体的实例化与生命周期，
    /// 不承担任何音频播放职责（音效由 AudioManager 独立处理）。
    /// 所有特效完全通过 EventBus 订阅触发，无反向引用 TowerManager / Enemy / EconomyManager 等业务模块。
    /// </summary>
    public partial class EffectsManager : Node
    {
        #region 导出配置

        /// <summary>
        /// 获取或设置敌人被击杀时播放的视觉特效预制体场景。
        /// 推荐使用 CPUParticles2D / GPUParticles2D 并勾选 "One Shot"，
        /// EffectsManager 会在粒子生命周期结束前自动隐藏并在下一帧销毁，零闪烁。
        /// </summary>
        [Export] public PackedScene EnemyDeathEffectScene { get; set; }

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 订阅 EventBus.OnEnemyKilled 事件，在敌人被击杀的世界坐标处实例化击杀特效。
        /// </summary>
        public override void _Ready()
        {
            EventBus.OnEnemyKilled += HandleEnemyKilled;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有 EventBus 订阅，防止委托悬空。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnEnemyKilled -= HandleEnemyKilled;
        }

        #endregion

        #region 击杀特效实例化与回收

        /// <summary>
        /// 在指定世界坐标实例化敌人击杀视觉特效，并在其播放完毕后自动销毁。
        /// 为彻底消除「粒子最后一帧渲染 → 销毁回调」之间的帧竞争导致的残留闪烁：
        /// 销毁流程严格执行三步：① 在粒子生命周期结束前 20ms 就触发销毁回调
        /// ② 回调第一行立即将 Visible 置为 false（下一次物理/渲染帧绝对不绘制）
        /// ③ 通过 1ms 零等待 Tween 在下一帧执行 QueueFree，避免同一帧操作节点树。
        /// </summary>
        /// <param name="worldPosition">特效播放的世界坐标位置</param>
        private void SpawnDeathEffect(Vector2 worldPosition)
        {
            if (EnemyDeathEffectScene == null) return;

            Node2D effectInstance = EnemyDeathEffectScene.Instantiate<Node2D>();
            effectInstance.GlobalPosition = worldPosition;
            effectInstance.Name = "EnemyDeathEffect";
            AddChild(effectInstance);

            if (effectInstance is GpuParticles2D gpuParticles)
            {
                gpuParticles.Emitting = true;
                gpuParticles.Finished += () =>
                {
                    HideAndFreeNextFrame(effectInstance);
                };
            }
            else if (effectInstance is CpuParticles2D cpuParticles)
            {
                cpuParticles.Emitting = true;
                float totalLifetime = (float)cpuParticles.Lifetime + (float)cpuParticles.Preprocess;
                float hideDelay = Mathf.Max(0.0f, totalLifetime - 0.02f);
                CreateTween()
                    .TweenInterval(hideDelay)
                    .Finished += () =>
                    {
                        HideAndFreeNextFrame(effectInstance);
                    };
            }
            else
            {
                CreateTween()
                    .TweenInterval(1.98f)
                    .Finished += () =>
                    {
                        HideAndFreeNextFrame(effectInstance);
                    };
            }
        }

        /// <summary>
        /// 特效零闪烁销毁辅助方法：① 立即 Visible=false 让当前帧之后的所有渲染批次都跳过该节点
        /// ② 在下一帧（1ms 零等待 Tween）执行 QueueFree，确保 Godot 渲染器已完成当前帧提交，
        /// 从根本上切断"最后一帧还没画完就先调回调但回调后的隐藏没生效"的竞争窗口。
        /// </summary>
        /// <param name="effectInstance">需要销毁的特效节点</param>
        private void HideAndFreeNextFrame(Node2D effectInstance)
        {
            if (!IsInstanceValid(effectInstance)) return;

            effectInstance.Visible = false;

            CreateTween()
                .TweenInterval(0.001f)
                .Finished += () =>
                {
                    if (IsInstanceValid(effectInstance))
                    {
                        effectInstance.QueueFree();
                    }
                };
        }

        #endregion

        #region EventBus 事件处理

        /// <summary>
        /// 处理敌人被击杀事件。
        /// 在 deathPosition 位置实例化 EnemyDeathEffectScene 视觉特效，负责零闪烁销毁。
        /// 音频播放由 AudioManager 独立订阅同一事件，两者解耦互不依赖。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符（仅用于匹配事件签名，本模块不使用）</param>
        /// <param name="goldReward">击杀该敌人获得的金币奖励（仅用于匹配事件签名，本模块不使用）</param>
        /// <param name="deathPosition">敌人被击杀时的世界坐标，用于特效定位</param>
        private void HandleEnemyKilled(string enemyId, int goldReward, Vector2 deathPosition)
        {
            SpawnDeathEffect(deathPosition);
        }

        #endregion
    }
}
