# TowerData 生成提示词与设计要点

## 生成提示词（Prompt）

```
请帮我完成《TowerDefense-Core》项目的第二个核心模块：防御塔配置数据 (TowerData.cs)

创建数据 Resource：在 Game/Config/Towers/ 目录下创建 TowerData.cs 脚本：
- 继承自 Resource，并在类名上方加上 [GlobalClass] 属性。
- 包含以下暴露给编辑器的字段 ([Export]):
  - TowerId (string, 防御塔唯一ID)
  - TowerName (string, 显示名称)
  - Icon (Texture2D, 图标)
  - BuildCost (int, 建造消耗金币)
  - AttackRange (float, 攻击范围)
  - Damage (float, 基础攻击力)
  - AttackInterval (float, 攻击间隔秒数)
- 添加必要的 C# XML 中文注释。

要求：
1. 所有公共类、接口、方法、属性、枚举必须包含清晰的 C# XML 中文注释。
2. 注释需说明用途、参数含义、返回值、异常情况等关键信息。
3. 命名空间：TowerDefence.Config.Towers
4. 所有 [Export] 属性需提供合理的默认值，避免编辑器中字段为空导致空引用异常。
5. 遵循数据驱动设计原则：配置数据仅存储纯数据，不包含任何游戏逻辑。
```

## 设计要点记录

### 1. [GlobalClass] 属性的作用

**核心作用：让自定义 C# Resource 类在 Godot 编辑器中被正确识别和序列化。**

| 作用点 | 说明 |
|--------|------|
| **编辑器识别** | 不加 `[GlobalClass]` 时，Godot 无法在"新建资源"对话框中列出该自定义类，也无法在 Inspector 的类型下拉中看到它 |
| **类型注册** | 将类注册到 Godot 的全局 ClassDB 中，使其与 GDScript 自定义 class_name 等效 |
| **序列化支持** | 确保 `.tres` / `.res` 资源文件保存/加载时能正确映射类型；缺少此属性会导致加载时类型丢失或 fallback 到基类 `Resource` |
| **拖放关联** | 在场景编辑器中将脚本拖到 Resource 字段时，Godot 能识别类型兼容关系 |

**不加 [GlobalClass] 的后果：**
- 只能通过 C# 代码 `new TowerData()` 构造，无法在编辑器中创建资源实例
- `[Export] public TowerData Tower { get; set; }` 字段在 Inspector 中无法赋值资源
- 保存的 `.tres` 文件中 `script` 引用可能失效，加载时类型不匹配

### 2. 字段设计说明

| 字段名 | 类型 | 默认值 | 设计意图 |
|--------|------|--------|----------|
| TowerId | string | `""` | 塔的逻辑唯一标识，用于事件参数、存档键、配置表查找；不使用显示名做 ID 是为了支持重命名和本地化 |
| TowerName | string | `""` | UI 展示用，和 TowerId 分离以便独立做 i18n 本地化 |
| Icon | Texture2D | `null` | 图标纹理允许 null（图标未就绪时不 crash），逻辑层使用前需判空或给默认占位图 |
| BuildCost | int | `100` | 金币消耗用整数（符合塔防游戏惯例），非负校验放在业务逻辑层 |
| AttackRange | float | `150.0f` | 世界坐标单位（像素），与 Godot 2D 坐标系一致；绘制攻击范围圆时直接使用该值做半径 |
| Damage | float | `10.0f` | 用 float 支持护甲减伤、暴击倍率等小数伤害结算；最终显示给玩家时可以取整 |
| AttackInterval | float | `1.0f` | 两次攻击的冷却时间（秒）；攻速 Buff 通过缩短该值实现 |

### 3. 数据驱动 vs 纯数据原则

- **纯数据（Plain Data）**：`TowerData` 只存静态属性，不包含 `Attack()`, `FindTarget()` 等逻辑方法
- **逻辑与配置分离**：战斗逻辑放在 `Tower` 节点脚本或 `TowerCombatService` 服务类中，通过读取 `TowerData` 实例获取参数
- **优势**：同一份代码可通过不同 `TowerData` 资源实例驱动多种塔（箭塔、炮塔、冰冻塔等），无需为每种塔写子类

### 4. 命名空间与目录映射

- **命名空间**：`TowerDefence.Config.Towers`
- **目录映射**：`Game/Config/Towers/TowerData.cs`
- **后续扩展**：同一目录下可新增 `TowerUpgradeData.cs`（升级曲线）、`TowerProjectileData.cs`（弹道配置）等相关资源类

### 5. 默认值策略

所有值类型属性设置了合理默认值（BuildCost=100 而非 0），目的是：
1. 防止策划在编辑器新建资源时忘记填写导致数值异常
2. 单元测试中直接 `new TowerData()` 即可获得可用的默认配置
3. 给策划一个"基准参考值"，围绕该值上下调整平衡性
