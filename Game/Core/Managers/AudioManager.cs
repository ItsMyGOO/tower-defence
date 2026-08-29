using System.Collections.Generic;
using Godot;
using TowerDefence.Config.Towers;
using TowerDefence.Core.AutoLoads;

namespace TowerDefence.Core.Managers
{
    /// <summary>
    /// 全局音频管理器（单一职责版）。
    /// 仅负责背景音乐循环播放、事件驱动的一次性音效动态实例化与自动回收；
    /// 视觉粒子特效、震屏等反馈由 EffectsManager 独立管理，避免职责混杂。
    /// 建议挂载到主场景 Systems 节点下，所有音频完全通过 EventBus 解耦触发，
    /// 与 Gameplay、经济、UI、特效等模块无硬编码引用。
    /// </summary>
    public partial class AudioManager : Node
    {
        #region 导出配置 —— 音频资源

        /// <summary>
        /// 获取或设置背景音乐音频流资源。
        /// 在 _Ready 中若已配置则自动开始循环播放，不受暂停菜单影响。
        /// </summary>
        [Export] public AudioStream BGMStream { get; set; }

        /// <summary>
        /// 获取或设置防御塔建造成功时的一次性音效。
        /// 由 EventBus.OnTowerBuilt 事件触发播放。
        /// </summary>
        [Export] public AudioStream SFXBuildTower { get; set; }

        /// <summary>
        /// 获取或设置敌人被击杀时的一次性音效。
        /// 由 EventBus.OnEnemyKilled 事件触发播放。
        /// </summary>
        [Export] public AudioStream SFXEnemyKilled { get; set; }

        /// <summary>
        /// 获取或设置敌人到达路径尽头（漏怪/扣血）时的一次性音效。
        /// 由 EventBus.OnEnemyReachedEnd 事件触发播放。
        /// </summary>
        [Export] public AudioStream SFXEnemyReachedEnd { get; set; }

        /// <summary>
        /// 获取或设置玩家胜利（全部波次清理完毕）时的一次性结算音效。
        /// 由 EventBus.OnGameOver(true) 事件触发播放。
        /// </summary>
        [Export] public AudioStream SFXGameOverWin { get; set; }

        /// <summary>
        /// 获取或设置玩家失败（HP 归零）时的一次性结算音效。
        /// 由 EventBus.OnGameOver(false) 事件触发播放。
        /// </summary>
        [Export] public AudioStream SFXGameOverLose { get; set; }

        #endregion

        #region 运行时内部节点

        /// <summary>
        /// 背景音乐专用 AudioStreamPlayer。
        /// 节点生命周期与 AudioManager 一致，常驻不销毁，仅切换 Stream 与播放/停止。
        /// </summary>
        private AudioStreamPlayer _bgmPlayer;

        /// <summary>
        /// 当前动态实例化的 SFX AudioStreamPlayer 集合，用于调试追踪与强制回收。
        /// 正常情况下每个 SFX 播放器在 finished 信号后自行 QueueFree，
        /// 仅在 AudioManager 销毁时通过该集合兜底释放。
        /// </summary>
        private readonly List<AudioStreamPlayer> _activeSfxPlayers = new List<AudioStreamPlayer>();

        #endregion

        #region 生命周期

        /// <summary>
        /// 节点被添加到场景树时调用。
        /// 创建内部 BGM 播放器并启动背景音乐，随后订阅 EventBus 中与音频触发相关的事件。
        /// </summary>
        public override void _Ready()
        {
            SetupBGMPlayer();

            EventBus.OnTowerBuilt += HandleTowerBuilt;
            EventBus.OnEnemyKilled += HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd += HandleEnemyReachedEnd;
            EventBus.OnGameOver += HandleGameOver;
        }

        /// <summary>
        /// 节点即将从场景树移除时调用。
        /// 取消所有 EventBus 订阅，停止 BGM，并强制回收所有尚未自然结束的 SFX 播放器，防止委托悬空与节点泄漏。
        /// </summary>
        public override void _ExitTree()
        {
            EventBus.OnTowerBuilt -= HandleTowerBuilt;
            EventBus.OnEnemyKilled -= HandleEnemyKilled;
            EventBus.OnEnemyReachedEnd -= HandleEnemyReachedEnd;
            EventBus.OnGameOver -= HandleGameOver;

            if (_bgmPlayer != null)
            {
                _bgmPlayer.Stop();
            }

            foreach (AudioStreamPlayer player in _activeSfxPlayers)
            {
                if (IsInstanceValid(player))
                {
                    player.Stop();
                    player.QueueFree();
                }
            }
            _activeSfxPlayers.Clear();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 创建并配置常驻的 BGM AudioStreamPlayer 子节点。
        /// 若 BGMStream 已在 Inspector 中配置，则立即以循环模式开始播放。
        /// </summary>
        private void SetupBGMPlayer()
        {
            _bgmPlayer = new AudioStreamPlayer
            {
                Name = "BGMPlayer",
                Bus = "Master",
                Stream = BGMStream
            };
            AddChild(_bgmPlayer);

            if (BGMStream != null)
            {
                _bgmPlayer.VolumeDb = -6.0f;
                _bgmPlayer.Play();
            }
        }

        #endregion

        #region SFX 动态播放与回收

        /// <summary>
        /// 动态实例化一个一次性 AudioStreamPlayer 播放指定音效。
        /// 播放完毕（finished 信号）后自动 QueueFree 回收节点并从活动集合中移除。
        /// 若传入的 AudioStream 为 null 则直接静默返回，保证缺失资源时游戏不报错。
        /// </summary>
        /// <param name="stream">需要播放的音效音频流；为 null 时不执行任何操作</param>
        private void PlayOneShotSFX(AudioStream stream)
        {
            if (stream == null) return;

            AudioStreamPlayer player = new AudioStreamPlayer
            {
                Name = $"SFX_{stream.ResourceName ?? "Unknown"}",
                Bus = "Master",
                Stream = stream
            };
            AddChild(player);
            _activeSfxPlayers.Add(player);

            player.Finished += () =>
            {
                _activeSfxPlayers.Remove(player);
                if (IsInstanceValid(player))
                {
                    player.QueueFree();
                }
            };

            player.Play();
        }

        #endregion

        #region EventBus 事件处理

        /// <summary>
        /// 处理防御塔建造成功事件。
        /// 播放 SFXBuildTower 一次性音效（若已配置）。
        /// </summary>
        /// <param name="towerData">建造的塔数据资源（仅用于匹配事件签名，本模块不使用）</param>
        /// <param name="buildPosition">建造的世界坐标位置（仅用于匹配事件签名，本模块不使用）</param>
        private void HandleTowerBuilt(TowerData towerData, Vector2 buildPosition)
        {
            PlayOneShotSFX(SFXBuildTower);
        }

        /// <summary>
        /// 处理敌人被击杀事件。
        /// 播放 SFXEnemyKilled 音效；视觉特效由 EffectsManager 通过同一事件独立触发，两者完全解耦。
        /// </summary>
        /// <param name="enemyId">被击杀敌人的资源标识符（仅用于匹配事件签名，本模块不使用）</param>
        /// <param name="goldReward">击杀该敌人获得的金币奖励（仅用于匹配事件签名，本模块不使用）</param>
        /// <param name="deathPosition">敌人被击杀时的世界坐标（仅用于匹配事件签名，本模块不使用）</param>
        private void HandleEnemyKilled(string enemyId, int goldReward, Vector2 deathPosition)
        {
            PlayOneShotSFX(SFXEnemyKilled);
        }

        /// <summary>
        /// 处理敌人到达路径尽头（漏怪/扣血）事件。
        /// 播放 SFXEnemyReachedEnd 一次性音效（若已配置）。
        /// </summary>
        /// <param name="damageToPlayer">该敌人对玩家造成的生命值伤害（仅用于匹配事件签名，本模块不使用）</param>
        private void HandleEnemyReachedEnd(int damageToPlayer)
        {
            PlayOneShotSFX(SFXEnemyReachedEnd);
        }

        /// <summary>
        /// 处理游戏结束（胜负结算）事件。
        /// 根据 isVictory 参数选择播放 SFXGameOverWin 或 SFXGameOverLose 音效；
        /// 同时淡出当前 BGM（通过线性 Tween 降低 VolumeDb 至静音后停止）。
        /// </summary>
        /// <param name="isVictory">true 表示玩家胜利，false 表示玩家失败</param>
        private void HandleGameOver(bool isVictory)
        {
            if (isVictory)
            {
                PlayOneShotSFX(SFXGameOverWin);
            }
            else
            {
                PlayOneShotSFX(SFXGameOverLose);
            }

            if (_bgmPlayer != null && _bgmPlayer.Playing)
            {
                float startVolume = _bgmPlayer.VolumeDb;
                Tween tween = CreateTween();
                tween.TweenProperty(_bgmPlayer, "volume_db", -80.0f, 1.0)
                    .SetTrans(Tween.TransitionType.Linear)
                    .SetEase(Tween.EaseType.InOut);
                tween.Finished += () =>
                {
                    if (IsInstanceValid(_bgmPlayer))
                    {
                        _bgmPlayer.Stop();
                        _bgmPlayer.VolumeDb = startVolume;
                    }
                };
            }
        }

        #endregion
    }
}
