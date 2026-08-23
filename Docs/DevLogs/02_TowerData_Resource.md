# TowerData 数据驱动设计（Resource-driven Design）

## 一、Godot 商业化开发中的两种配置范式

在塔防等数值密集型游戏中，防御塔属性的配置方式直接决定了开发效率、迭代速度和项目可维护性。Godot 生态中存在两种主流范式：

| 范式 | 典型做法 | 适用场景 |
|------|----------|----------|
| **硬编码（Hardcoding）** | 在 C# 类中写 `public const int ArrowTowerDamage = 10;` 或 `Dictionary<string, TowerConfig>` 静态查表 | 极简 demo、Jam 参赛、原型快速验证 |
| **数据驱动（Resource-driven）** | 将配置封装为 `Resource` 子类（如 `TowerData`），在编辑器中创建 `.tres` 资源实例，代码运行时加载 | 商业化项目、需长期迭代、多人协作、需策划独立调参 |

**本项目选择 Resource-driven**，原因在下文详述。

---

## 二、硬编码（Hardcoding）的痛点

假设采用硬编码方式实现箭塔和炮塔配置：

```csharp
// ❌ 硬编码反例：所有塔的数值写死在代码里
public static class TowerHardcodes
{
    public static readonly (int cost, float range, float dmg, float interval) ArrowTower =
        (100, 150.0f, 10.0f, 1.0f);

    public static readonly (int cost, float range, float dmg, float interval) CannonTower =
        (250, 120.0f, 35.0f, 2.0f);

    // 每加一种塔就要改代码重新编译...
}
```

### 痛点 1：每一次数值调整都需要重新编译

- 策划想把箭塔伤害从 10 调到 12，必须让程序改代码 → 提交 Git → CI 构建 → 等待编译
- Godot C# 编译冷启动 + 热重载失败的概率不低，频繁的小改动会严重拖慢迭代节奏
- **商业项目中，数值平衡的试错成本是最高的**：一个塔可能要经历几十次甚至上百次调参

### 痛点 2：策划无法独立工作

- 没有程序介入，策划无法新增塔、无法修改数值、无法验证平衡性
- 程序和策划的工作流是**串行依赖**，无法并行开发，严重拖慢里程碑交付
- 团队规模 > 3 人时，这种瓶颈指数级放大

### 痛点 3：无法差异化配置（多平台 / 多渠道）

- 商业化发行常需要针对不同渠道（Steam / Epic / 移动）做差异化数值
- 针对不同难度（简单 / 普通 / 困难）、不同赛季活动，塔的属性可能完全不同
- 硬编码要写一堆 `#if` 宏或 switch 分支，很快变成"配置地狱"

### 痛点 4：缺少编辑器可视化

- 图标纹理、攻击范围预览等需要拖入图片资源的字段，硬编码根本无法表达
- 不能在 Inspector 中直接拖拽引用 Asset，只能在代码里写路径字符串（极易拼错且无重构支持）

### 痛点 5：不利于热更新

- 后续接入热更（如通过 AssetBundle / 自定义 patching 系统下发新塔配置），硬编码方案无法被外部资源覆盖
- 商业化运营中，线上平衡性 hotfix 是常规需求，硬编码会把这条路彻底堵死

---

## 三、数据驱动（Resource-driven）的架构价值

Resource-driven 的核心思想：

> **"游戏的可配置内容（塔、敌人、关卡、技能……）以 Resource 资产为唯一数据源；逻辑层只负责读取配置并执行行为，不持有任何具体数值。"**

### 1. 迭代效率：调参 = 改资源，零编译

```
策划在 Godot 编辑器中：
  双击 ArrowTower.tres → 修改 Damage = 12 → Ctrl+S 保存
  → 立即运行游戏即可看到效果
```

- **0 秒编译等待**：`.tres` 是纯文本资源，Godot 会自动 reload，无需重新编译 C#
- **完全可视化**：Inspector 中拖拽 Texture、调整滑块（`[Export(PropertyHint.Range)]`）、查看属性说明
- **所见即所得**：攻击范围数值改完立刻在预览圈里看到变化

### 2. 工作流解耦：策划和程序并行开发

```
  程序侧（写一次即可复用）              策划侧（每天都在产出）
┌──────────────────────┐            ┌──────────────────────┐
│ TowerData.cs (schema)│ ◀──────────│ ArrowTower.tres      │
│ Tower.cs   (逻辑)    │            │ CannonTower.tres     │
│ WaveManager.cs       │            │ FreezeTower.tres     │
│ ...                  │            │ Season2_FireTower.t │
└──────────────────────┘            └──────────────────────┘
```

- 程序只需要定义一次 `TowerData` "Schema"（字段结构）
- 策划在此基础上**创建任意数量的资源实例**，每新增一种塔都不需要程序参与
- 双方工作流从"串行依赖"变为"并行协作"，产能翻倍

### 3. 多配置差异化（商业发行刚需）

```
Resources/Towers/
├── Normal/
│   ├── ArrowTower.tres      (BuildCost=100, Damage=10)
│   └── CannonTower.tres
├── Easy/
│   ├── ArrowTower.tres      (BuildCost=80,  Damage=12)   ← 新手友好
│   └── CannonTower.tres
├── Hard/
│   ├── ArrowTower.tres      (BuildCost=120, Damage=8)    ← 硬核挑战
│   └── CannonTower.tres
└── Seasonal/
    └── Lunar2026_FireworkTower.tres   ← 节日活动专属塔
```

通过资源路径切换即可在**同一套代码**下加载不同配置集：
```csharp
var difficulty = GameSettings.CurrentDifficulty; // Easy / Normal / Hard
var towerData = GD.Load<TowerData>($"res://Resources/Towers/{difficulty}/ArrowTower.tres");
```

- 渠道定制包、多国版本、赛季活动都通过**新增资源目录**解决
- 不需要写任何 `if/else` 分支，代码逻辑始终保持干净

### 4. 原生编辑器能力复用

Godot 编辑器为 Resource 提供了大量开箱即用的能力，硬编码无法比拟：

| 能力 | 说明 |
|------|------|
| **资源引用** | 直接在 Inspector 中拖拽 `Texture2D`、`AudioStream`、`PackedScene`，编辑器自动管理 `.import` 和引用关系 |
| **属性提示** | 可用 `[Export(PropertyHint.Range, "0,1000,10,or_greater")]` 加滑块、`[Export(PropertyHint.File, "*.png")]` 做文件过滤 |
| **版本友好** | `.tres` 是纯文本格式，Git diff 可直接看到数值变化（"Damage 10→12"），冲突也容易解决 |
| **多语言 i18n** | `TowerName` 可配合 Godot 的 tr() 系统，或直接给不同语言目录各放一份资源 |
| **搜索与批量操作** | 编辑器文件系统面板可按类型筛选所有 `TowerData` 资源，方便批量重命名、移动、替换图标 |

### 5. 热更新与线上运营友好

商业化项目上线后，Resource-driven 模式天然支持热更：

- **方案一（覆盖替换）**：热更包下发新的 `.tres` 文件覆盖旧资源，游戏下次加载时读到新配置
- **方案二（远端下发 JSON→转 Resource）**：运营后台下发 JSON 数值表，客户端 `ResourceSaver.Save()` 生成本地 Resource 覆盖默认值
- **方案三（混合）**：基础配置打包在 `.pck` 内，线上平衡性 hotfix 通过 HTTP 下载 patch Resource

三种方案都不需要改代码、不需要重新发包，能快速响应线上平衡性问题。

---

## 四、数据驱动的代码组织原则

采用 Resource-driven 时，需严格遵守以下原则，否则会退化为"伪数据驱动"：

### ✅ 原则 1：Resource 只存纯数据，不写业务逻辑

```csharp
// ✅ 正确：TowerData 是纯数据类
[GlobalClass]
public partial class TowerData : Resource
{
    [Export] public float Damage { get; set; }
    // 没有 Attack()、FindTarget() 等方法
}

// ✅ 正确：逻辑放在 Node/Service 中，运行时读取 TowerData
public partial class Tower : Node2D
{
    [Export] public TowerData Data { get; set; }  // 配置注入

    private float _cooldown;

    public override void _Process(double delta)
    {
        _cooldown -= (float)delta;
        if (_cooldown <= 0 && TryFindTarget(out var target))
        {
            DealDamage(target, Data.Damage);   // 从 Data 读参数
            _cooldown = Data.AttackInterval;
        }
    }
}
```

### ✅ 原则 2：塔实例通过 `TowerData` 做配置化

```csharp
// 建造时：加载资源 → 注入到 Tower 节点
public void BuildTower(Vector2I gridPos, string towerId)
{
    var towerData = TowerRegistry.Get(towerId);           // 从注册表按 ID 查 Resource
    var towerScene = GD.Load<PackedScene>(towerData.ScenePath);
    var tower = towerScene.Instantiate<Tower>();
    tower.Data = towerData;                               // 注入配置
    AddChild(tower);
}
```

这样，`Tower.cs` 这一份代码就能驱动**所有类型的塔**——箭塔、炮塔、冰冻塔的差异完全体现在各自的 `TowerData.tres` 中。

### ✅ 原则 3：新增塔 = 复制已有资源 + 改数值

策划添加新塔的流程：
1. 右键 `ArrowTower.tres` → Duplicate → 命名为 `PoisonTower.tres`
2. 打开 Inspector：修改 TowerId="Poison"、TowerName="毒液塔"、Damage=5、Icon=毒液图标...
3. 注册到 `TowerRegistry`（如果是按文件夹自动扫描的话甚至连这步都省了）
4. 运行游戏，建造毒液塔 → 工作

**全程零代码改动。**

### ❌ 反模式：在 Resource 中写逻辑

```csharp
// ❌ 错误：TowerData 中混入业务逻辑
[GlobalClass]
public partial class TowerData : Resource
{
    [Export] public float Damage { get; set; }

    // ❌ 不要这样写！逻辑污染了配置层
    // 如果有 100 种塔，要么写 100 个 TowerData 子类，要么写巨型 switch
    public void DealDamage(Node target) { ... }
}
```

问题：
- 不同塔的行为差异会逼出 `TowerData_Arrow`、`TowerData_Cannon` 等子类爆炸
- 配置和逻辑再次耦合，退回"每加一种塔就要改代码编译"的老路

---

## 五、性能与内存考量（商业化项目必看）

### 1. Resource 是引用类型，相同资源共享实例

```csharp
var a = GD.Load<TowerData>("res://ArrowTower.tres");
var b = GD.Load<TowerData>("res://ArrowTower.tres");
object.ReferenceEquals(a, b); // ✅ true，同一份内存
```

- Godot 的 `ResourceLoader` 内置缓存，同一 `.tres` 不论加载多少次只占一份内存
- 场上有 100 个箭塔实例，它们共享同一个 `TowerData` 对象，内存开销极低
- **运行时绝对不要修改共享 Resource 实例的属性**（例如 `Data.Damage += 5`），会影响所有塔——如果需要"每个塔实例独立属性"（如升级加成），请在 Tower 节点上保存副本或增量值

### 2. 启动加载策略：按需加载 vs 预加载

| 策略 | 实现 | 适用场景 |
|------|------|----------|
| **预加载所有** | 启动时 `DirAccess` 扫描 Towers 目录，全部 `GD.Load` 进字典 | 塔数量 < 50 种，启动时间不敏感 |
| **按 ID 懒加载** | 首次访问时从 `res://Resources/Towers/{id}.tres` 加载并缓存 | 塔数量多（上百种），或配置分包下载 |
| **混合** | 基础塔预加载，活动塔懒加载 | 商业项目常规做法 |

商业化项目推荐"注册表 + 懒加载缓存"方案：

```csharp
public static class TowerRegistry
{
    private static readonly Dictionary<string, TowerData> _cache = new();
    private const string BasePath = "res://Game/Config/Towers/Resources/";

    public static TowerData Get(string towerId)
    {
        if (_cache.TryGetValue(towerId, out var cached)) return cached;
        var data = GD.Load<TowerData>($"{BasePath}{towerId}.tres");
        _cache[towerId] = data;
        return data;
    }
}
```

### 3. `.tres` vs `.res` 格式

| 格式 | 可读性 | 体积 | 适用 |
|------|--------|------|------|
| `.tres` (Text) | ✅ 纯文本，Git diff 友好 | 稍大 | 开发阶段、所有配置资源 |
| `.res` (Binary) | ❌ 二进制不可读 | 更小 | 发布打包时通过"转换为二进制资源"优化 |

建议开发阶段统一用 `.tres`，发布打包时 Godot 可一键批量转 `.res`，两者在代码加载接口上完全透明。

---

## 六、总结：为什么商业化项目必须选择 Resource-driven

| 维度 | 硬编码 | Resource-driven |
|------|--------|-----------------|
| **调参编译等待** | 每次改都要重新编译 C# | 保存 `.tres` 即时生效，0 等待 |
| **策划独立工作** | ❌ 必须依赖程序改代码 | ✅ 独立创建/修改资源，程序不用管 |
| **多难度/多渠道** | 大量 `#if` / switch，维护成本高 | 多份资源目录，代码零改动 |
| **新内容扩展** | 每加一种塔改代码 → 编译 → 提交 | 复制资源改数值，全程零代码 |
| **编辑器可视化** | 图标/音频等资源引用靠字符串路径 | Inspector 直接拖放，重构安全 |
| **热更新能力** | ❌ 必须重新发包 | ✅ 覆盖 `.tres` 资源即可热更 |
| **Git 协作** | 修改代码容易冲突 merge hell | `.tres` 纯文本 diff 清晰，冲突易解 |
| **单元测试** | 写死的常量难以覆盖边界值 | `new TowerData { Damage = 999 }` 任意构造测试数据 |

**一句话结论：** Resource-driven 用"一次性定义 Schema 的成本"，换取整个项目生命周期中**十倍量级的迭代效率提升**。对于需要长期运营、多人协作的商业化塔防项目，这是无可替代的架构选择。
