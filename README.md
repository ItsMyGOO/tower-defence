# 🛡️ Project: TowerDefense-Core (Godot 4 商业化小品级塔防)

## 🚀 快速启动 (Getting Started)
* **引擎版本**：Godot 4.x .NET 版本 (建议 4.2.2 或以上)
* **SDK**：.NET 8.0 SDK
* **IDE**：推荐 Rider / VS Code

## 📌 项目简介 (Overview)
本项目是一个基于 **Godot 4.x** 开发的 2D 极简塔防切片项目。
项目的核心目标不是堆叠大量的关卡或美术资源，而是通过一个完整的塔防玩法闭环，建立符合 **Steam / Itch.io 上架发售标准** 的 Godot 商业化架构与脚手架。

本项目基于团队/个人通用模板库 (`Template`) 进行二次开发，重点验证数据驱动、事件解耦、UI/UX 交互链条及系统持久化。

---

## 🎯 核心架构与学习目标 (Architecture & Goals)

### 1. 核心玩法切片 (Core Gameplay)
* **地图与路径**：基于 `Path2D` / `PathFollow2D` 构建静态刷怪路径（关卡差异走 Level.cs 的 Export 配置）。 ✅
* **防御塔机制**：单体/AOE/减速三种攻击形态（`TowerKind`），目标选择策略（最前线/最近/血量最高，`TargetingMode`）、攻击冷却；右键出售按 `SellRefundRatio` 返还金币。 ✅
* **攻击弹道**：按 `AttackMode` 分流——弹道塔发射追踪弹体（箭/炮/冰霜），抵达才结算（AOE 落点溅射、减速弹命中附加 debuff）✅；激光塔光束锁定目标、按帧持续伤害（DPS）、自动切换目标 ✅。
* **敌人机制**：不同类型（基础/高速/高血）的波次生成（Wave Spawner）与减速 debuff；飞行敌人 🚧 规划中。
* **资源与经济**：建造消耗、击杀奖励、漏怪扣血、玩家生命值控制与胜负结算逻辑。 ✅

### 2. Godot 核心技术栈验证 (Godot Mechanics)
* **资源驱动开发 (Resource-driven Design)** ✅：
  * 使用 Custom Resource (`.tres`) 定义防御塔属性（攻击力、攻速、范围、攻击形态、目标策略、减速/溅射参数、出售返还）与敌人数据。
  * 使用 Custom Resource 组织关卡波次（Wave Spec）。升级树 🚧 规划中。
* **架构解耦 (Decoupling Pattern)** ✅：
  * **Event Bus (全局信号总线)**：解耦 UI、经济系统与战场节点的直接引用。
  * **全局状态枚举**：GameManager 以 GameState 枚举控制准备期 / 波次进行中 / 胜负结算，通用 FSM 框架 🚧 规划中。
* **UI/UX & 输入链条 (Commercial Polish)**：
  * 鼠标 + 键盘（ESC 暂停）交互闭环 ✅；建造指示器（幽灵跟随、射程预览圈、槽位吸附高亮、占用红框提示、右键取消选择）✅；手柄与 UI 焦点系统 🚧 规划中。

### 3. 商业化闭环与底层框架 (Commercial Readiness)
* **数据持久化** ✅（部分）：关卡解锁进度经 ConfigFile 落盘于用户目录；音量/全屏/按键重映射与星级高分 🚧 规划中。
* **音频系统** ✅（框架）：AudioManager 为 AutoLoad 常驻单例，BGM 跨场景连续、事件驱动 SFX 动态实例化回收；音频资产 🚧 待补。
* **打包与流转** ✅：主菜单 → 选关（目录扫描自动生成按钮）→ 关卡 → 结算（重开/选关/主菜单/下一关/退出）完整 Meta Loop。

---

## 📂 目录结构 (Project Structure)

```text
res://
├── Game/                        # 游戏核心逻辑（按模块内聚）
│   ├── Core/                    # 通用底层沉淀
│   │   ├── AutoLoads/           # EventBus 全局事件总线（static class）
│   │   └── Managers/            # SceneManager / AudioManager / EffectsManager（AutoLoad，经 .tscn 包装）
│   │                            # GameManager（关卡场景内，胜负状态机）
│   ├── Config/                  # 数据驱动配置（.tres 实例 + 对应 Resource 类）
│   │   ├── Towers/              # ArrowTower(箭弹) / CannonTower(溅射) / FrostTower(减速) / LaserTower(光束)
│   │   ├── Enemies/             # BasicSlime / FastGoblin / TankOrc
│   │   └── Waves/               # Wave_01..04 波次配置
│   ├── Gameplay/                # 核心玩法层（按业务模块划分）
│   │   ├── Towers/              # Tower / Projectile / BuildPreview / TowerManager / TowerSlot（左键建造、右键出售）
│   │   ├── Enemies/             # Enemy（PathFollow2D，减速 debuff）
│   │   ├── Waves/               # WaveManager（波次调度与存活追踪）
│   │   ├── Map/                 # Level 通用关卡控制器 + Level_01/02.tscn（差异全走 Export）
│   │   ├── Economy/             # EconomyManager（金币/HP/胜负结算）
│   │   └── Effects/             # EnemyDeathEffect.tscn 击杀粒子特效
│   ├── Scenes/                  # 共享实体场景 (EnemyBase / TowerBase / HUDView / 面板)
│   └── UI/                      # UI 模块
│       ├── HUD/                 # HUDView + TowerBuildButton（建造栏）
│       ├── Panels/              # UIPanelBase 基类 / PauseMenuPanel / GameOverPanel
│       ├── MainMenu/            # 主菜单
│       └── LevelSelect/         # 选关界面（目录扫描动态生成关卡按钮）
│
├── Docs/                        # 设计文档与 DevLogs
├── Tests/                       # 自驱动测试场景（无头可跑，结果走日志断言）+ 测试用 .tres
└── addons/                      # 插件（暂空）
```
