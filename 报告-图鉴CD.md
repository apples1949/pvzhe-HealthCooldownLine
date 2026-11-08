# 报告 · 图鉴 CD 显示 + 两个误显示修复（v1.27.0）

> 交付物：`mods\HealthCooldownLine`（「血条·装填倒计时」，id `healthcooldowndisplay`）**v1.27.0**
> 日期：2026-11-08　｜　组装与汇总：cdmod-owner　｜　僵尸静态表：zombie-dev-a / zombie-dev-b　｜　植物侧：shovel
> 事实源：`unpack\`（0.32 源码）逐行实读 + `研究-僵尸CD\A批-研究.md` / `B批-研究.md` / `植物侧-研究.md` + 契约 `研究-僵尸CD\00-框架与契约.md` **v1.2**

---

## 0. 一页纸结论

| 项 | 结论 |
|---|---|
| **用户报"图鉴里看不到 CD"的根因** | **不是没做**，是**图鉴里根本没有运行中的计时器**：图鉴格子只播动画（无实体）；详情面板虽实例化真角色，但 `inGame=false` + 主状态机 dispatch 关闭 ⇒ 计时器永不推进 ⇒ 读"剩余"只能得到 0/初始值。另外面板自带的「冷却时间」读的是 packet 级 `packetCooldown`（默认 5.0，僵尸不覆盖）⇒ 恒显示一个无意义的 `5.0` |
| 本轮做法 | 新增**图鉴静态 CD 通道**：在图鉴详情面板上叠加一行 `满CD：歌唱 5.0s · 施法 12.0s`（读**配置里的冷却总时长**），并**抑制**图鉴预览角色上的运行时行（避免显示"假倒计时"） |
| 新开关 | **「图鉴显示冷却」`showAlmanacCd`**（bool，默认开，group=显示内容）。与「显示僵尸冷却」**互相独立**，只有总开关能一键全灭 |
| 覆盖 | 僵尸侧静态表 **19 个僵尸 / 20 个键**（A 批 19 + B 批 18，去重后覆盖本轮点名全部）；植物侧静态表 **2 株**（毁灭向日葵 / 魅惑花盆） |
| 两个误显示 | ① 巨型雪球/巨型冰球/烈焰毒物等**投射物**不再显示血量（判据：`MoveComponent` + 类名兜底）；② 僵尸不再显示「加速 +100%」（⑨ 加成段对僵尸整体关闭） |
| 版本 | 1.26.0 → **1.27.0**（mod.json + csproj 同步；设置项 16 → **17**） |
| 构建 | 编译 **0 错误 0 警告**；`verify_build` **✅ 全绿**；`verify_pmod` **ok=8 warn=1 FAIL=0 → 通过**；DLL 特征串 **100/100 命中** |

---

## 1. 复核结论（Lead 的两条事实我逐条实读确认）

### 1.1 图鉴格子：**完全没有实体**
| 证据 | 内容 |
|---|---|
| `unpack\Prefab\GUI\DialogBox\Almanac\AlmanacZombie\AlmanacZombieWidow.cs:8` | `public partial class AlmanacZombieWidow : Control`（**不是** `TowerDefenseCharacter`） |
| `:51 Init(TowerDefensePacketConfig)` | 只做三件事：`CreateSprite()` / `FreezePreviewTree(sprite)` / `TrySubscribeCharacterSkinSwitch()` |
| `:83 CreateSprite()` | 只 `TowerDefenseManager.GetPacketSprite(config)` 造一个 `AdobeAnimateSprite`，再 `ApplyInitialArmor` / `SetAnimation` / `FreezePreviewTree` |
| `Almanac.cs:821 / :927` | `BindZombiePreview(almanacZombieWidow, _zombieLogicalConfigs[index])`，且格子走**对象池复用**（`_zombiePreviewPool`） |

⇒ 格子里**没有 `ComponentManager`、没有 timer、没有 `TowerDefenseCharacter`**，运行时的"剩余"物理上不存在。

### 1.2 图鉴详情面板：**有真角色，但计时器不跑**
| 证据（`unpack\Prefab\GUI\InformationPanel\InformationPanel.cs`） | 内容 |
|---|---|
| `:143-150` | `TowerDefenseManager.GetChacraterScene(name)` → `Instantiate()` → `currentCharcter = node as TowerDefenseCharacter` |
| `:159` | `currentCharcter.inGame = false;` |
| `:162` | `spriteNode.AddChild(currentCharcter, …)` ⇒ **真的进了场景树** |
| `:165` | `currentCharcter.SetMainStateMachineDispatchEnabled(enabled: false);` ⇒ 主状态机不派发 |
| `:236` | 面板那行「冷却时间」读 `characterConfig.packetCooldown`；基类默认 `TowerDefenseCharacterConfig.cs:83` `packetCooldown = 5.0` |

### 1.3 「我们的扫描有没有扫到面板里那个 `inGame=false` 角色？」—— **扫到了**
- `ScanRecursive` 是**整棵树**递归（`HealthCooldownLineEntry.cs` 的 `ScanRecursive`），`ScanRecursive` 里 `if (node is TowerDefenseCharacter character) { … ApplyToCharacter(character); }`，
  所以面板里那个实例**会被扫到并走 `ApplyToCharacter`**。
- 但它是 **`inGame=false`**：所有计时分支的"剩余"要么是 0、要么是初始满值（因为 `BatchUpdate` 不跑）⇒ **显示空白或显示"不动的假倒计时"**。
- ⇒ **结论：不是"没扫到"，是"计时器没启动"**。这正是用户截图里"图鉴什么都没有"的技术根因。

### 1.4 因此本轮设计（与 Lead 指示一致）
- **不做格子**（成本高、收益小，Lead 明确本轮先不做）；
- **做详情面板**：静态叠加行 `满CD：…`，数据来自配置；
- **抑制预览角色的运行时行**：`ApplyToCharacter` 新增 `if (!character.inGame) { HideAllLines(character); return; }`，
  否则图鉴里会显示"永远不动的 25.0s"这种假倒计时（比空白更误导）。

---

## 2. 实现（全部在 `HealthCooldownLineEntry.cs`，另有 3 个 partial 文件）

| 部件 | 说明 |
|---|---|
| `TickAlmanacCd()` | 每 30 帧（`AlmanacScanStride`）从 `OnProcessFrame` 调一次；放在 `_frame % ScanStride` 早退**之前**，否则会被战斗扫描节流吃掉 |
| `WalkAlmanacPanels()` | 遍历全树找 `InformationPanel`（图鉴的 `%ZombieInformationPanel` / `%PlantInformationPanel` 都是它） |
| `RefreshAlmanacPanelLabel()` | 在面板上维护一个 Label `ModAlmanacCdLabel`：底部居中、字号 13、绿字黑描边、`MouseFilter=Ignore`、锚点 `BottomWide` + 偏移 `-78/-44`（避开底部标签页按钮）；无内容则隐藏 |
| `BuildAlmanacCdText()` | `panel.currentPacketConfig` → `characterConfig.name`（charName）+ `saveKey`；僵尸走 A/B 静态表、其它走植物静态表；多行用 `  ·  ` 连接；最多 3 行（`AlmanacMaxRows`） |
| 前缀 | `AlmanacCdPrefix = "满CD："`（玩家一眼能区分"配置满值"与"战斗剩余"） |
| 开关 | 「图鉴显示冷却」`showAlmanacCd`：mod.json 设置项 + CD 面板勾选框（两条建面板路径 + 状态校准都加了）+ 宿主配置/`user://cfg` 双写 + 落盘日志 `开关[图鉴显示冷却] → …` |
| 门控 | `TickAlmanacCd` 判 `_enabled && _showAlmanacCd`；**分支里不判开关**（A/B/植物三个实现点都只填数据） |
| 异常 | A / B / 植物三处各自 try/catch + "只报一次"标志；整体另有一层 `_almanacFaultReported` |

### 2.1 三个开关的优先级
| 开关 | 管哪个面 | 默认 |
|---|---|---|
| 总开关 `enabled` | 全部（最高优先级） | 开 |
| 「显示僵尸冷却」`showZombieCd` | **战斗内**僵尸附加行 | 开 |
| 「图鉴显示冷却」`showAlmanacCd` | **图鉴/详情面板**的 `满CD：` 静态行 | 开 |

三者互相独立：战斗里关掉僵尸 CD，图鉴照样能查满 CD；反之亦然。

---

## 3. 用户点名清单 · 逐条结论（15 项 / 17 个僵尸）

> 图例：**✅静态** = 图鉴详情面板能显示静态值（本轮新增）；**✅战斗** = 战斗内能显示（新增专属分支 or 通用通道已覆盖）；**❌** = 配置里确实没有该计时。

| # | 用户点名 | 类名 / `config.name` | 图鉴静态（面板显示） | 战斗内 | 证据 |
|---|---|---|---|---|---|
| 1 | 三叶草僵尸 | `TowerDefenseZombieBalloonBlover` / `ZombieBalloonBlover` | ✅ `满CD：吹飞 8.0s` | ✅ 通用通道②（`Clover` 键，需「角色计时器其他CD」） | `…\Chapter1\Balloon\Scene\Blover\TowerDefenseZombieBalloonBlover.cs:120`（`Run("Clover",8.0)`）+ 其 ComponentSet 的 `{"Clover":8.0}` |
| 2 | 多彩气球三叶草 | `…BalloonColourBlover` / `ZombieBalloonColourBlover` | ✅ `吹飞 8.0s` | ✅ 通用通道② | `…\BalloonColour\Scene\Blover\…:137` |
| 3 | 吸金磁僵尸 | `…NormalGoldMagnet` / `ZombieNormalGoldMagnet` | ✅ `满CD：吸金 3.0s` | ✅ **新增**（`吸金 X.Xs`，读 `MagnetCoinComponent.timer/magnetTime`，循环计时；3.0s < 4.9，开「只显示 ≥5 秒」时会整行隐藏 —— 与「隐身 4.0s」同一口径） | `…\Chapter1\Normal\Scene\GoldMagnet\TowerDefenseZombieNormalGoldMagnetComponentSet.tres`（`magnetTime = 3.0`，定义默认 5f）+ `.cs:13` `GetRuntime<MagnetCoinComponent>()` |
| 4 | 僵尸坚果伽刚特尔 | `…GargantuarWallnutZ` / `ZombieGargantuarWallnutZ` | ✅ `满CD：生成 10.0s` | ✅ 通用通道②（`Spawn` 键 → 显示「生成 X.Xs」，需「生成计时」） | `…\Chapter1\Gargantuar\Scene\WallnutZ\TowerDefenseZombieGargantuarWallnutZ.cs:43-45`（`Run("Spawn",10.0)`）、`:169-175`；其 `…TimerComponentDefinition.tres` `{"Spawn":10.0}`。**"疑似被格子裁掉"= 格子 UI 裁切问题，与 CD 无关** |
| 5 | 天使僵尸（无敌） | `…ZombieAngel` / `ZombieAngel` | ✅ `满CD：复活 15.0s · 无敌 8.0s` | ✅ 新增（`复活` + `无敌` 两行） | `.cs:35` `spawnTimer = 15.0`、`:39` `invincibleTimer = 8.0`；`:118`/`:123-126` 无敌越界清除 |
| 6 | 黑客小鬼（10 秒） | `…Imphacker` / `ZombieImphacker` | ✅ `满CD：篡改 10.0s · 隐身 4.0s` | ✅ 新增（`冷却 10s` + `隐身 4s`） | `.cs:94` `_hackTimer.Start(10.0)`、`:174` `_hackEffectRemaining = 4.0` |
| 7 | 飞行器僵尸 | `…Aircraft` / `ZombieAircraft` | ✅ `满CD：推进 15.0s`（**有效值**，定义 25.0 被 `Run("Spawn",15.0)` 覆盖） | ✅ 通用通道②（`Spawn` 键 → 「生成 X.Xs」） | `.cs:132/:144` `Run("Spawn", 15.0)` |
| 8 | 传送门 | `…Portal` / `ZombiePortal` | ✅ `满CD：闭合 20.0s` | ✅ **新增**（`闭合 (time−timer)s`，关门后隐藏） | `.cs:31 timer`、`:33 time = 20.0`、`:111-120` |
| 9 | 魔化僵尸怪人 | `…MagicYeti` / `ZombieMagicYeti` | ✅ `满CD：狂暴 30.0s` | ✅ **新增**（`狂暴 (30−_aliveTime)s`；血量≤1000 会提前触发 ⇒ 提前消失属正常） | `.cs:25 RageTriggerTime = 30.0`、`:186` |
| 10 | 塞壬僵尸 | `…Siren` / `ZombieSiren` | ✅ `满CD：施法 12.0s · 歌唱 5.0s · 治疗 1.0s`（3 行） | ✅ 通用通道②（中文化后 `歌唱`/`施法`；`治疗 1.0s` 在「只显示 ≥5 秒」开启时会被滤掉） | `…SirenTimerComponentDefinition.tres` `{Cast:12, Heal:1, Sing:5}`、`.cs:84/:216` |
| 11 | 守护术士 | `…GuardMage` / `ZombieGuardMage` | ✅ `满CD：施法 10.0s` | ✅ 通用通道② | `…GuardMageTimerComponentDefinition.tres` `{Cast:10}`、`.cs:92/:145` |
| 12 | 占卜术士 | `…DivineMage` / `ZombieDivineMage` | ✅ `满CD：施法 12.0s` | ✅ 通用通道② | `…DivineMageTimerComponentDefinition.tres` `{Cast:12}`、`.cs:125/:178` |
| 13 | 唤魔使 | `…Evoker` / `ZombieEvoker` | ✅ `满CD：施法 5.0s`（**有效值**，定义 10.0 被 `Run("Cast",5.0)` 覆盖） | ✅ 通用通道② | `.cs:215/:270` `Run("Cast",5.0)` |
| 14 | 安眠术士 | `…SleepMage` / `ZombieSleepMage` | ✅ `满CD：吟唱 5.0s · 睡眠 10.0s` | ✅ 新增（`吟唱`→`睡眠`，互斥同屏 1 行） | `.cs:9 EntryCastDelay=5.0`、`:11 SleepDuration=10.0`、`:259 chantTimer>=5.0` |
| 15 | 时间术士 | `…TimeMage` / `ZombieTimeMage` | ✅ `满CD：施法 10.0s` | ✅ 新增（`冷却 10s`） | `.cs:163` `spellTimer>=10.0`、`:209` 归零 |
| 16 | 女巫僵尸 | `…Witch` / `ZombieWitch` | ✅ `满CD：投掷 10.0s` | ✅ 通用通道②（中文化后 `投掷`） | `…WitchTimerComponentDefinition.tres` `{Throw:10.0}`、`.cs:107/:178` |
| 17 | 快闪迪斯科 | `…DiscoFlash` / `ZombieDiscoFlash` | ✅ `满CD：起身 10.0s` | ✅ 新增（`起身`；闪身步是瞬发事件、滑倒概率是百分比 ⇒ 不显示） | `FlashStepComponentDefinition.cs:11 fellDuration = 10.0`；快闪舞者 `…BackupFlash` 同款 |

**统计**：图鉴静态 **✅ 17/17**（点名项全覆盖）；战斗内 **✅ 15 项可见**（其中 8 项走通用通道②）、**1 项（吸金磁）战斗内暂不可见**（原因见上，属"能做未做"）、**0 项"配置里根本没有"**。

### 3.1 关于"配置里确实没有该计时"的补充
- 本轮点名项里**没有**"配置里完全不存在"的僵尸（都能给出静态值）；
- 原始 31 个清单里唯一真·没有计时的是**磁场僵尸的「磁力持续时间」**（`open` 后无复位路径、`ChangeProjectileStateComponent` 无时长字段，见 v1.26 报告 §4.1）；
- 骷髅伽刚特尔的 30 秒复活挂在**骨堆**上（`TowerDefenseSkeleton`，`packetCooldown=30.0`），战斗内由「显示僵尸冷却」新增显示，图鉴静态写 `复活 30.0s`。

---

## 4. 两个误显示的定性与修复

### 4.1 巨型雪球 / 巨型冰球 / 烈焰毒物 **不该显示血量** —— 已修
- 真身：`unpack\Asset\Anime\Character\Item\` 下的 **移动道具/投射物**
  - 巨型雪球 = `TowerDefenseItemSnowBall`（雪球射手滚出的球；v1.23.0 已按类名排除）
  - 巨型冰球 = `TowerDefenseItemIceball`
  - 烈焰毒物 = `TowerDefenseItemPoisonMist`
- 为什么原来会显示：`IsObstacleLike()` 的判据是"非植物/非僵尸/非割草机 且 有 `HurtComponent`"，而这类道具继承通用角色组件集、带 `HurtComponent`，类名又不含 Plant/Zombie ⇒ 被当成障碍物。
- **修复（两道判据）**，改在 `IsObstacleLike()`：
  1. **类名族前缀 `TowerDefenseItem` ⇒ 不是障碍物**（`Item\` 下 17 个脚本里 16 个是该前缀，唯一例外是割草机类 `TowerDefenseReverseRake`）；\n     ⚠️ 初版曾用"有 `MoveComponent` ⇒ 不是障碍物"，被 zombie-dev-a 复核判定为**死代码**：冰球/火球挂的是 `GroundMoveComponent`（`GroundMoveComponent.cs:4`），与 `MoveComponent`（`MoveComponent.cs:5`，`ComponentBase` 体系）**无继承关系**；也不能改判 `GroundMoveComponent`（**所有僵尸都挂它**，只是被 `tn.Contains("Zombie")` 前置拦住）⇒ 已删除，改为族前缀；
  2. **类名兜底** `Iceball / IceBall / Fireball / PoisonMist / SurfWave / Spikeball / Brain`（防"组件尚未注册进运行时表"时漏判）。
- 同时保留 v1.23.0 的 `SnowBall / SoccerBall` 与 `BowlingComponent` 排除。

### 4.2 僵尸豌豆小鬼 显示「加速 +100%」 —— 已修
- 「加速/攻速/伤害/加成 buff」那一段（⑨ 段）本来是给**植物被加成**用的（buff 的 `timeScaleValue`、猫窝攻速翻倍等）。
- 僵尸命中原因：僵尸的 `FireComponent.timeScale` / `AttackComponent.attackInterval` 是**配置里的设计值**（僵尸天生射得更快，`timeScale = 2.0` ⇒ 被算成"+100%"），`spd` 直接把 `FireComponent.timeScale` 当"加速倍率"（`spd = ts`）。
- **修复**：`TryCollectInfo` 开头加 `bool isZombieBoost = IsZombieCdHost(character, charName);`，⑨ 段的**两处显示条件**改为 `!isZombieBoost && CatOn(8) && …` ⇒ **僵尸一律不显示这几类"加成"文案**（数据采集与诊断日志保留）。
- 僵尸豌豆小鬼的**正确答案**是它的 `Spawn` 计时（`{"Spawn":10.0}`）—— 战斗内由通用通道②显示「生成 X.Xs」，图鉴静态写 `生成 10.0s`。

### 4.3 三条判据 + 一处文案的**行号索引**（`HealthCooldownLineEntry.cs`）

| 判据 / 修复 | 位置 | 拦下/修好了什么 |
|---|---|---|
| `tn.Contains("SnowBall") \|\| tn.Contains("SoccerBall")`（v1.23.0） | **`:205`** | 巨型雪球 `TowerDefenseItemSnowBall`（更早命中，先于族前缀） |
| **`tn.StartsWith("TowerDefenseItem")` ⇒ 不是障碍物**（v1.27.0 新增，**替代死代码**） | **`:228`** | 巨型冰球 `ItemIceball`、烈焰毒物 `ItemPoisonMist`、巨型火球 `ItemFireball`、以及**将来任何新增 Item 系道具**（自动覆盖） |
| 类名兜底 `Iceball/Fireball/PoisonMist/SurfWave/Spikeball/Brain` | **`:236`** | 不带 `Item` 前缀的移动特效 |
| **`tn0.Contains("MegaFire")` ⇒ 不算血量**（v1.27.0 新增，**专人拦截**） | **`:350`** | **烈焰 `TowerDefenseItemMegaFire`** —— 它命中 `IsObstacleCharacter`（`:159`）的 `MegaFire` 分支，**在进入 `IsObstacleLike` 之前就被放行**，族前缀拦不到；其 15 秒 AutoDestroy「消失」计时**保留**（走通用通道②） |
| `isZombieBoost = IsZombieCdHost(...)` + `!isZombieBoost && CatOn(8)` | **`:1849` / `:3397` / `:3419`** | 僵尸不再显示「加速/攻速/伤害/加成 buff」 |
| `AddLine(lines, "减速 " + …)`（原「加速 -N%」） | **`:3413`** | 植物侧 `timeScaleValue < 1`（减速 buff）不再显示成"反向加速" |

⚠️ **闸门顺序坑（务必记档）**：`TryCollectObstacleHp` 的入口是
`if (!IsObstacleCharacter(character) && !IsObstacleLike(character)) return;` —— **任一为真就放行**。
所以当某个类被 `IsObstacleCharacter`（`:153` 的 5 类精确名单：Crater / GraveStone / TimeBomb / TimeNuke / **MegaFire**）命中时，
`IsObstacleLike` 里的**任何排除都不生效**。以后要排除这类对象，必须像烈焰这样**在闸门之前单独拦**，只改 `IsObstacleLike` 会漏。

另外两条"以后会反复被踩"的结论（已写进代码注释）：
- **不能按血量判**：`TowerDefenseCharacterConfig.cs:16` `hitpoints = 300.0` 是**基类默认值**，绝大多数道具不覆盖 ⇒ 道具与正常植物同值；`ItemPoisonMist` 还显式 `hitpoints = 1.0` ⇒ 用"没血量/血量太小"当判据必然误判。
- **不能判 `GroundMoveComponent`**：冰球/火球挂的是它，但**所有僵尸的组件集都挂了它**（`Prefab\...\ComponentSets\TowerDefenseZombieComponentSet.tres:4`），现在没炸只是因为本方法开头 `tn.Contains("Zombie")` 先拦住了 —— 隐式顺序耦合，改动极易炸。

---
#### 4.3.1 零误伤核实（zombie-dev-a 独立复验，`A批-研究.md §7` 有完整表格）

| 修复 | 零误伤证据 |
|---|---|
| `TryCollectObstacleHp` 里 `tn0.Contains("MegaFire")`（`:350`） | 全库类名/文件名含 `MegaFire` 的**只有 `TowerDefenseItemMegaFire` 一个角色类**（另一个 `MegaFirePeaSprite` 是 Sprite 类，永远走不到这条分支）；`CharacterResource.json` 里含 `MegaFire` 的键**只有 `ItemMegaFire`** 一个 ⇒ **不存在误伤面**。且它只在 `TryCollectObstacleHp` 内 `return` ⇒ **只停"血量"，不动 15s `AutoDestroy`「消失」**（那条走通用通道②） |
| `IsObstacleLike` 的 `StartsWith("TowerDefenseItem")`（`:228`） | `Item\` 下 17 个脚本里 16 个是该前缀，唯一例外 `TowerDefenseReverseRake`（不匹配 ⇒ 保持原行为）；`Item\` 里唯一有 `hitpoints` 的三个（`ItemSheild` 1000 / `ItemSoccerBall` 500 / `ItemPoisonMist` 1.0）**本来就已被别处拦掉** ⇒ 不会出现"该显示的反而不显示" |
| ⑨ 段 `减速 N%`（`:3413`） | 该行位于 `!isZombieBoost && CatOn(8)` 守卫块内 ⇒ **僵尸不会因这次改动冒出新的"减速"行**，零副作用 |

> **引用约定**：行号会随 `Entry.cs` 改动漂移，报告与文档一律用「**函数名 + 行号**」双标
> （`IsObstacleLike` / `TryCollectObstacleHp` / ⑨ 段 / `TickAlmanacCd` / `RefreshAlmanacPanelLabel`），
> 别只给裸行号；`unpack\` 下的行号是稳定的，可以单给。

---
## 5. 植物侧（shovel 交付，已并入本包）

| 植物 | 实现 | 结论 |
|---|---|---|
| 毁灭向日葵 `PlantDoomSunFlower` | 战斗内 `CollectPlantExtraLines` 加「叠种 N/2（· 产出 75）」；图鉴 `CollectAlmanacStaticCdPlant` 加 `叠种 2 层 · 产出 25→75` | 已落地，编译进包 |
| 魅惑花盆 `PlantHypnoPatch` | 图鉴静态 `生成 30.0s（需先被啃）` / `可啃 3 次` | **用户"看不到"的真因已定性**：`TowerDefensePlantHypnoPatch.cs:41` `if (… || eatTime <= 0) return;` ⇒ **被僵尸啃之前 `spawnTimer` 恒 0、`spawnPacket` 为 null**，语义上此刻不该有"生成僵尸 X.Xs"；v1.26 只在被啃后才显示是**正确行为**，但缺"待啃"提示 ⇒ 建议后续加「生成 待啃」（本轮未改，避免动 v1.26 已验证的分支） |

- 扩展点：`CollectPlantExtraLines`（战斗内，外层只判总开关，分类开关由实现方自己判）+ `CollectAlmanacStaticCdPlant`（图鉴静态），实现文件 `runtime_src\PlantCdExtra.cs`，**Entry.cs 只放声明与调用点**（已写进契约 v1.2 §11）。

---

## 6. 构建 / 打包 / 装机证据

```
[compile] RC=0   0 个错误 / 0 个警告
[package] RC=0   OK -> dist\HealthCooldownLine.pmod   大小: 153,996 字节
[install] %APPDATA%\…\Mods\HealthCooldownLine.pmod (153,996 B)
```

| 项 | 值 |
|---|---|
| DLL | `58a7a1bb2f4dad3039697fd2b891da50`　**115,200 B**（`bin/Release` == `Runtime/ModAssembly.dll` == 包内） |
| pmod | `ab6958e997dc30e30126f5dd8b09e091`　**153,996 B**（dist == 装机，sha256 亦一致） |
| `verify_build.py` | **✅ 全部通过**：编译（DLL 新于源码）/ 打包 11-08 10:21 / 装机 sha256 一致 / 包内容 **17 项设置一致** |
| `verify_pmod.py` | **ok=8 / warn=1 / FAIL=0 → 通过**（warn 是 17 个 Mod 共用的赞助码 PNG；`managed.api` 规则已于 2026-11-07 修正为接受 `runtimeApiVersion 1..3`） |
| DLL 特征串 | **100/100 命中**（`_verify_dll_owner.py`；含 `TickAlmanacCd` / `CollectAlmanacStaticCdA/B` / `showAlmanacCd` / `ModAlmanacCdLabel` / `InformationPanel` / `MoveComponent` / 全部中文标签） |
| 装机前游戏状态 | **未运行** ✅ |
| 装机侧 | `ModsCache` 无本 Mod 残留（0 条目）；`mod_boot_journal.json` 已删除/不存在 |

> 构建过程中踩到并已修复的一个坑：用 PowerShell `Set-Content -Encoding UTF8` 改 `mod.json` 会**写入 BOM** ⇒ `build_pmod.py` 的 `json.load(..., encoding="utf-8")` 抛 `Unexpected UTF-8 BOM` ⇒ `[package] RC=1`。已用 `UTF8Encoding($false)` 重写为**无 BOM**（csproj 同样处理），重打包通过。

---

## 7. 交付文件

| 文件 | 说明 |
|---|---|
| `runtime_src\HealthCooldownLineEntry.cs` | 图鉴静态通道（`TickAlmanacCd`/`WalkAlmanacPanels`/`RefreshAlmanacPanelLabel`/`BuildAlmanacCdText`）+ 新开关三处闭环 + 预览角色抑制 + 两个误显示修复 |
| `runtime_src\ZombieCdA.cs` / `ZombieCdB.cs` | A 批（战斗内 13 分支 + 图鉴静态 19 键）/ B 批（战斗内 **12** 分支 + 图鉴静态 18 僵尸 19 键） |
| `runtime_src\PlantCdExtra.cs` | 植物侧（毁灭向日葵战斗内叠加 + 植物图鉴静态） |
| `mod.json` | 新增 `showAlmanacCd`（设置项 16 → 17）；版本 **1.27.0** + 更新历史 |
| `研究-僵尸CD\00-框架与契约.md` | **v1.2**：§10 图鉴静态通道、§11 植物侧扩展点、§12 词表增补（`推进/闭合/狂暴/篡改/叠种`） |
| `研究-僵尸CD\A批-研究.md` / `B批-研究.md` / `植物侧-研究.md` | 逐僵尸/植物的静态值证据链 |
| `研究-僵尸CD\_verify_dll_owner.py` | DLL 特征串核对（按字节搜 UTF-16LE，规避 `#US` 堆奇数偏移假阴性） |
| `报告-图鉴CD.md` | 本报告 |

---

## 8. 本轮"已做 / 未做"清单（收尾对账）

### 8.1 已完成（全部在本包 v1.27.0 内）
1. **图鉴静态 CD 通道**（详情面板，`满CD：…`）+ 新开关 `showAlmanacCd`；
2. **抑制图鉴预览角色的运行时行**（`!character.inGame` 早退）；
3. **用户点名 17 项**静态值全覆盖（吸金磁磁战斗内分支也已补：`吸金 X.Xs`）；
4. **误显示 ①**：巨型雪球 / 巨型冰球 / 烈焰毒物 / 巨型火球 不再显示血量（`:205` / `:228` / `:350`）；
5. **误显示 ②**：僵尸不再显示「加速/攻速/伤害/加成」（`:1849` + 两处 `!isZombieBoost`）；
6. **顺手修**：植物侧「加速 -50%」→「减速 -50%」（`:3413`）；
7. 植物侧（shovel）：毁灭向日葵叠种行、魅惑花盆未啃态「生成 待啃」。

### 8.2 未做 / 下轮建议
| # | 项 | 状态与理由 |
|---|---|---|
| 1 | **图鉴格子通道**（`AlmanacZombieWidow` 角上叠字） | **本轮不做**（Lead 决定：格子无实体、成本高，详情面板已覆盖需求）；静态表 `CollectAlmanacStaticCdA/B/Plant` 可直接复用，做时只需换渲染位置 |
| 2 | **haste 白名单** | **未做**：现在"任何带 `timeScaleValue` 的 buff"都当速度倍率，减速 buff 已被上面第 6 条救回文案，但更稳的做法是只认 haste 白名单（`Coffee`/`MagicRootHaste`/`OverallHaste`/`PoisonBottleHaste`/`SirenHaste`/`IceSunHaste`/`KangarooRageHaste`）；属结构性改动 |
| 3 | **②.9c 段反射加固** | **未做**（shovel 建议）：`GetField(..., DeclaredOnly)` 换 `FindFieldAlong`、`Contains("HypnoPatch")` 换逐字相等、`30.0`/`3` 抽常量注明 `:46`/`:106` |
| 4 | **真机验收** | **未做**：本轮为开发+组装，图鉴叠加行的**位置/不遮挡**只能靠代码推断（锚点 `BottomWide`、偏移 −78/−44）；真机若位置不合适，改两个常量即可 |

### 8.3 口径备忘（避免以后争议）
- `吸金 3.0s` **照实传 3.0**（Lead 2026-11-08 拍板）：与 `隐身 4.0s` 同一口径 —— 「只显示 ≥5 秒」默认**关闭**，平时可见；用户主动开过滤才隐藏短 CD，属既有语义，不为单个僵尸破例。
- 图鉴静态行**一律 `AddLine`（Total=0）** ⇒ 天然不参与「只显示 ≥5 秒」过滤（它没有"剩余"概念）。
- 面板自带「冷却时间 5.0」是 `packetCooldown` 基类默认值，**不是**技能 CD，我们不覆盖。