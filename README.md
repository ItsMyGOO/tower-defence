# godot-game-template v1.0

一个用于创建新游戏项目的 **Godot 4 + C#** 极简 GitHub 模板仓库。

本仓库是一个模板，而非游戏玩法库。它的设计范围仅限于项目结构、Git 默认配置、文档以及基础的 Godot 项目文件。

## 目录结构

```text
Game/
├── Gameplay/
├── UI/
├── Scenes/
├── Config/
├── Art/
└── Audio/
Docs/
Tests/
addons/
```

以上就是完整的模板目录设计。

## 各目录用途

- `Game/Gameplay/`：存放下游项目中与游戏玩法相关的代码和资源。
- `Game/UI/`：存放下游项目中与 UI 相关的场景、脚本和资源。
- `Game/Scenes/`：存放下游项目中的游戏场景。
- `Game/Config/`：存放下游项目中的配置资源。
- `Game/Art/`：存放下游项目中的视觉美术资源。
- `Game/Audio/`：存放下游项目中的音频资源。
- `Docs/`：项目文档。
- `Tests/`：项目测试。
- `addons/`：Godot 插件。

## 模板边界

请勿向本仓库添加演示内容、示例玩法、可复用系统或示例游戏实体。

请基于此模板创建新的游戏仓库，然后在新仓库中添加实际的游戏内容。

---

# godot-game-template v1.0 (English)

A minimal **Godot 4 + C#** GitHub Template Repository for creating new game projects.

This repository is a template, not a gameplay library. It intentionally stops at project structure, Git defaults, documentation, and basic Godot project files.

## Directory Layout

```text
Game/
├── Gameplay/
├── UI/
├── Scenes/
├── Config/
├── Art/
└── Audio/
Docs/
Tests/
addons/
```

That is the complete template directory design.

## What Belongs Here

- `Game/Gameplay/`: game-specific gameplay code and assets in downstream projects.
- `Game/UI/`: game-specific UI scenes, scripts, and assets in downstream projects.
- `Game/Scenes/`: game-specific scenes in downstream projects.
- `Game/Config/`: game-specific configuration resources in downstream projects.
- `Game/Art/`: game-specific visual assets in downstream projects.
- `Game/Audio/`: game-specific audio assets in downstream projects.
- `Docs/`: project documentation.
- `Tests/`: project tests.
- `addons/`: Godot plugins.

## Template Boundaries

Do not add demo content, sample gameplay, reusable systems, or example game entities to this repository.

Create new game repositories from this template, then add the actual game content in the new repository.
