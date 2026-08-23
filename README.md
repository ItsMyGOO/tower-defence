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
* **地图与路径**：基于 Godot `TileMapLayer` + `Path2D` / `PathFollow2D` 构建动态/静态刷怪路径。
* **防御塔机制**：单体/AOE/减速塔，具备范围检测、目标选择（最近/血量最高/最前线）及攻击冷却。
* **敌人机制**：不同类型（基础/高速/高血/飞行）的敌人波次生成（Wave Spawner）。
* **资源与经济**：建造消耗、击杀奖励、玩家生命值控制与结算逻辑。

### 2. Godot 核心技术栈验证 (Godot Mechanics)
* **资源驱动开发 (Resource-driven Design)**：
  * 使用 Custom Resource (`.tres`) 定义防御塔属性（攻击力、攻速、范围、升级树）与敌人数据。
  * 使用 Custom Resource 组织关卡波次（Wave Spec）。
* **架构解耦 (Decoupling Pattern)**：
  * **Event Bus (全局信号总线)**：解耦 UI、经济系统与战场节点的直接引用。
  * **自定义状态机 (FSM)**：控制游戏状态（准备期、波次进行中、暂停、胜负结算）。
* **UI/UX & 输入链条 (Commercial Polish)**：
  * 完整支持 **键盘 + 鼠标** 与 **手柄 (Gamepad)** 无缝切换及 UI 焦点控制 (Focus System)。
  * 交互式建造指示器（拖拽/点击放置、范围预览、无法建造区域红框高亮）。

### 3. 商业化闭环与底层框架 (Commercial Readiness)
* **数据持久化 (Save/Load System)**：
  * 基于 `FileAccess` / JSON 实现游戏配置保存（主音量、BGM/SFX 独立调节、全屏/窗口模式、按键重映射）。
  * 关卡关卡星级/高分记录落盘。
* **音频系统**：基于 AudioStreamPlayer 的音效池管理与 BGM 平滑切换。
* **打包与流转**：包含异步加载（`ResourceLoader`）、Pause 暂停菜单、胜利/失败重新开始流转。

---

## 📂 目录结构预览 (Project Structure)

```text
res://
├── Game/                        # 游戏核心逻辑（按模块内聚）
│   ├── Core/                    # 通用底层沉淀（跨项目复用）
│   │   ├── AutoLoads/           # 全局单例（EventBus, SoundManager 等）
│   │   ├── SaveSystem/          # 存档与配置持久化
│   │   ├── FSM/                 # 通用有限状态机框架
│   │   └── Extensions/          # C# / Godot 扩展工具类
│   │
│   ├── Gameplay/                # 核心玩法层（按业务模块划分）
│   │   ├── Towers/              # 塔模块 (Tower.tscn, Tower.cs, TowerData.cs)
│   │   ├── Enemies/             # 敌人模块 (Enemy.tscn, Enemy.cs, EnemyData.cs)
│   │   ├── Waves/               # 波次管理与 Spawner (WaveManager.cs, WaveData.cs)
│   │   ├── Map/                 # 地图与路径 (TileMap, Path2D 逻辑)
│   │   └── Economy/             # 经济/血量/胜负结算逻辑
│   │
│   ├── UI/                      # UI 模块
│   │   ├── Common/              # 通用组件 (Buttons, Dialogs, Focus)
│   │   ├── HUD/                 # 局内 HUD (BuildMenu, WaveBar, HPBar)
│   │   ├── Menus/               # 主菜单、Pause 菜单、Settings 菜单
│   │   └── Styles/              # UI 主题与样式 (Control Themes, Fonts)
│   │
│   ├── Config/                  # 数据定义与配置文件 (.tres 数据实例)
│   │   ├── Towers/              # 塔的具体数值配置实例
│   │   ├── Enemies/             # 敌人的数值配置实例
│   │   └── Waves/               # 关卡波次配置实例
│   │
│   ├── Art/                     # 美术资源 (按类型或模块存放)
│   │   ├── Textures/
│   │   ├── Shaders/
│   │   └── TileMaps/
│   │
│   └── Audio/                   # 音频资源与 Bus 配置
│       ├── BGM/
│       ├── SFX/
│       └── DefaultBusLayout.tres
│
├── Docs/                        # 设计文档与 API 说明
├── Tests/                       # 单元测试 / 实验性测试场景 (GdUnit4 或自研)
└── addons/                      # Godot 插件 (如 GodotSteam 等)
