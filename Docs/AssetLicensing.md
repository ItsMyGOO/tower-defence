# 素材授权审计台账

> **状态：🔴 未解决 —— 商业发布阻塞项**
> 建立：2026-10-02（项目商业级审查）。决策推迟到美术填充阶段，届时按本台账逐项处理。

## 问题描述

`Game/Art/Simple Tower Defense/` 第三方像素素材包的**来源与许可证均未确认**
（包内 `ATTRIBUTION.md` 为空白占位）。项目目标是 Steam / Itch.io 商业发售，
在授权确认前，该素材包**不得进入任何公开发布构建**——包括免费 Demo 与
 itch.io 早期页面截图之外的任何公开展示。

## 当前引用清单（截至 2026-10-02）

已深度耦合进玩法配置，替换时需逐项清理：

| 素材文件 | 引用位置 |
|---|---|
| Towers/Combat Towers/spr_tower_archer.png | `Game/Config/Towers/ArrowTower.tres` (Icon) |
| Towers/Combat Towers/spr_tower_cannon.png | `Game/Config/Towers/CannonTower.tres` (Icon) |
| Towers/Combat Towers/spr_tower_ice_wizard.png | `Game/Config/Towers/FrostTower.tres` (Icon) |
| Towers/Combat Towers/spr_tower_lightning_tower.png | `Game/Config/Towers/LaserTower.tres` (Icon) |
| Towers/Combat Towers Projectiles/spr_tower_archer_projectile.png | `ArrowTower.tres` (ProjectileIcon) |
| Towers/Combat Towers Projectiles/spr_tower_cannon_projectile.png | `CannonTower.tres` (ProjectileIcon) |
| Towers/Combat Towers Projectiles/spr_tower_ice_wizard_projectile.png | `FrostTower.tres` (ProjectileIcon) |
| Enemies/spr_normal_slime.png | `Game/Config/Enemies/BasicSlime.tres` (AnimTexture) |
| Enemies/spr_goblin.png | `Game/Config/Enemies/FastGoblin.tres` (AnimTexture) |
| Enemies/spr_demon.png | `Game/Config/Enemies/TankOrc.tres` (AnimTexture) |
| Towers/Castle/spr_castle_red.png | `Level_01.tscn` / `Level_02.tscn` (CastleBase) |

注：包内 `Environment/` 目录当前**未被引用**，可随决策一并删除或保留。
代码层无需改动——`Enemy.SetupVisual` / `Tower.SetupSprite` / `Projectile` 在
纹理为 null 时自动回退占位视觉（色块/DrawCircle），替换素材零代码成本。

## 待办（美术填充阶段执行）

- [ ] 确认素材包来源与作者（下载页/商店链接，保留凭证截图）
- [ ] 确认许可证：是否允许**商业使用**、是否要求署名、是否禁止再分发
- [ ] **决策二选一**：
  - 保留 → 购买商业许可 / 补全 `Game/Art/Simple Tower Defense/ATTRIBUTION.md`，
    并在发布包根目录附 `CREDITS.txt`；
  - 替换 → 整目录删除，清空上表 .tres 的纹理引用（回退占位视觉），
    候选 CC0 素材：Kenney（kenney.nl）、itch.io 筛选 CC0。
- [ ] 若包名/文件名可检索（"Simple Tower Defense" + spr_tower_archer），
  优先用文件名反查原始商店页确认来源。

## 发布前检查（合并入发布清单）

- [ ] 构建产物不含未授权资源（配合 export_presets 的 include/exclude 过滤器）
- [ ] ATTRIBUTION / CREDITS 随包分发
