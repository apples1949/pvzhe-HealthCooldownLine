# A 批 16 个僵尸 · 可显示 CD 研究（zombie-dev-a）

> 事实源：`unpack\`（0.32.0.0 解包）。
> 通道说明见 `00-框架与契约.md`：①僵尸类字段 / ②`CharacterTimerComponent` 命名计时器 / ③组件。
> 判定口径：**能同时读到「剩余」与「总时长」⇒ 可显示**；只有静态配置常量或瞬发能力 ⇒ 不可显示；
> 已被通用通道显示的 ⇒ 不重复写分支，只登记。
>
> ⚠️ **行号会漂移**：本文引用 `HealthCooldownLineEntry.cs` 的行号（如 `:1359` / `:1593-1643` / `:1877-1915`）
> 是**我实读那一刻**的版本。cdmod-owner 在本轮后续又多次改过该文件（`TimerLabel` 已从 5568 移到 5608 等），
> 所以**请按函数名/代码内容搜，不要把行号当契约**。`unpack\` 下的行号（`TowerDefenseZombie*.cs` 等）不会变。

---

## 0. 图鉴文案是怎么拿到的（方法可复现）

`unpack\Asset\Translate\Translate.csv` 只有 **NAME** 键（821 行，实测 `csv` 里 key 后缀 588 个 `NAME`，
**没有**任何 `_EXPRESTION` / `_HANDBOOK_EXPRESTION`），介绍文案在二进制 `Translate.zh.translation` 里。

该文件是 Godot `OptimizedTranslation`（`RSRC` 魔数，`strings` 是 **NUL 分隔的连续 UTF-8 串块**，
**键名不以文本保存**，只存 `hash_table`/`bucket_table` + 值）。可复现步骤：

1. `b=open('unpack\\Asset\\Translate\\Translate.zh.translation','rb').read()` → `b.split(b'\x00')` → 得 45388 条可解码串（其中 3807 条含中文）；
2. 串的**顺序 = 源 Translation 的键顺序**，同一僵尸的 `NAME / describe / handbookDescribe / handbookStory`
   在块内**相邻**（`NAME` 之后 +1~+8 就是介绍）；
3. 定位方式：按中文名精确匹配（16 个全部命中，索引落在 48475~49802 这一段连续僵尸区内）。

**旁证（键→值的绑定链）**：每个僵尸的卡池资源 `...\Packet\<saveKey>.tres` 同时写着
`name` / `describe` / `handbookDescribe` 三个翻译键，且 16 个僵尸的 `name` 键全部能在
`Translate.csv` 里查到中文名 —— 于是「卡池 tres 的键」与「二进制里的中文块」互相对上：

| 中文名 | 翻译键（NAME，`Translate.csv` 实测存在） | 介绍键（Packet tres） |
|---|---|---|
| 毁灭菇僵尸 | `TOWERDEFENSE_ZOMBIE_DOOMSHROOM_NAME` | `..._DOOMSHROOM_EXPRESTION` / `..._HANDBOOK_EXPRESTION` |
| 火爆辣椒僵尸 | `TOWERDEFENSE_ZOMBIE_JALAPENO_NAME` | `..._JALAPENO_*` |
| 火炬辣椒僵尸 | `TOWERDEFENSE_ZOMBIE_JALATORCH_NAME` | `..._JALATORCH_*` |
| 三叶草气球僵尸 | `TOWERDEFENSE_ZOMBIE_BALLOONBLOVER_NAME` | `..._BALLOONBLOVER_*` |
| 三叶草多彩气球僵尸 | `TOWERDEFENSE_ZOMBIE_BALLOONCOLOURBLOVER_NAME` | `..._BALLOONCOLOURBLOVER_*` |
| 寒冰菇冰车僵尸 | `TOWERDEFENSE_ZOMBIE_ZOMBONI_ICESHROOM_NAME` | `..._ZOMBONI_ICESHROOM_*` |
| 玩偶匣僵尸 | `TOWERDEFENSE_ZOMBIE_JACKBOX_NAME` | `..._JACKBOX_*` |
| 海妖僵尸 | `TOWERDEFENSE_ZOMBIE_SEADEMON_NAME` | `..._SEADEMON_*` |
| 磁场僵尸 | `TOWERDEFENSE_ZOMBIE_MAGNETIC_NAME` | `..._MAGNETIC_*` |
| 鲨鱼僵尸 | `TOWERDEFENSE_ZOMBIE_SHARK_NAME` | `..._SHARK_*` |
| 天使僵尸 | `TOWERDEFENSE_ZOMBIE_ANGEL_NAME` | `..._ANGEL_*` |
| 催眠师僵尸 | `TOWERDEFENSE_ZOMBIE_HYPNOTIST_NAME` | `..._HYPNOTIST_*` |
| 农夫僵尸 | `TOWERDEFENSE_ZOMBIE_FARMER_NAME` | `..._FARMER_*` |
| 海盗船长僵尸 | `TOWERDEFENSE_ZOMBIE_CAPTAIN_NAME` | `..._CAPTAIN_*` |
| 重生法师僵尸 | `TOWERDEFENSE_ZOMBIE_NECROMANCERG_NAME` | `..._NECROMANCERG_*` |
| 巨化法师僵尸 | `TOWERDEFENSE_ZOMBIE_NECROMANCERB_NAME` | `..._NECROMANCERB_*` |

**②类名定位链（每个僵尸都走同一套，全部实读）**：
`Translate.csv` 中文名 → `TOWERDEFENSE_ZOMBIE_<X>_NAME`
→ `CharacterResource.json` 里的类名（如 `ZombieNormalDoomShroom`）
→ `Asset\Anime\Character\Zombie\<章>\<组>\Scene\<X>\TowerDefenseZombie<X>.tscn`
→ 同类目 `TowerDefenseZombie<X>.cs`（编进 `PlantsVsZombies.dll`）+ `<X>ComponentSet.tres`（组件清单）。

**③同名盔甲变体 = 同一个类**（重要：判据必须按**类**、不能按 `charName` 精确匹配）：
`ZombieNormalDoomShroomBlackHelmet`、`ZombieJackbox{BlackHelmet,Bucket,Cone,Helmet,SpecialHelmet}`、
`ZombieSeaDemon{BlackHelmet,Helmet,SpecialHelmet}`、`ZombieFarmerCone`、`ZombieCaptainBucket`
这些卡池 key 共用**同一个** `TowerDefenseZombie*.tscn/.cs`（每个目录只有一份场景文件，如
`Chapter2\Jackbox\Scene\` 只有 `TowerDefenseZombieJackbox.tscn`），差别只在护具/`packetCooldown`。
⇒ 代码里判据 = 「类名 == `TowerDefense`+类键」**或**「`charName` 以类键为前缀」。

---

## 1. 结论总表（16 / 16 全部有结论）

| # | 中文名 | 类名（`CharacterResource` 键） | 通道 | 计时字段（实读） | 显示文案 | 结论 |
|---|---|---|---|---|---|---|
| 1 | 毁灭菇僵尸 | `ZombieNormalDoomShroom` | ① | `timer` / `time`(25~30) / `explode` | `爆破 12.3s` | ✅ 可显示 |
| 2 | 火爆辣椒僵尸 | `ZombieNormalJalapeno` | ① | `timer` / `time`(25~30) / `explode` | `自燃 8.7s` | ✅ 可显示 |
| 3 | 火炬辣椒僵尸 | `ZombieNormalJalatorch` | ① | `timer` / `time`(**7~10**) / `explode` | `自燃 5.4s` | ✅ 可显示（**与图鉴 25~30 不符，以代码为准**） |
| 4 | 三叶草气球僵尸 | `ZombieBalloonBlover` | ② | `CharacterTimerComponent` 键 `"Clover"`（`Run("Clover", 8.0)`） | `吹飞 8.0s`（`TimerLabel` 已加 `Clover→吹飞`） | ✅ 可显示（**已由通用通道 ② 显示，不重复写**） |
| 5 | 三叶草多彩气球僵尸 | `ZombieBalloonColourBlover` | ② | 同上 `"Clover"` 8.0s | 同上 | ✅ 可显示（已由通用通道显示） |
| 6 | 寒冰菇冰车僵尸 | `ZombieZamboniIceShroom` | ① | `timer` / `time`(25~30) / `explode` / `over` | `自爆 18.2s` | ✅ 可显示（**与图鉴 15 秒不符，以代码为准**） |
| 7 | 玩偶匣僵尸 | `ZombieJackbox` | ① | `timer` / `time`(**9~20**) / `explode` / `hasJackBox` | `自爆 6.3s` | ✅ 可显示 |
| 8 | 海妖僵尸 | `ZombieSeaDemon` | ① | `timer`（对常量 `15.0`）/ `spawnNext` | `召唤 9.6s` / `召唤 就绪` | ✅ 可显示 |
| 9 | 磁场僵尸 | `ZombieMagnetic` | ① | `openTimer` / `openTime`(6~12) / `open` | `磁力 3.2s` / `磁力 已开启` | ✅ 可显示（**「磁力时间」不存在**，见 §2.9） |
| 10 | 鲨鱼僵尸 | `ZombieShark` | ③ | `ChomperComponent`：`chewTimer` / `chewTime`=**15.0** / `isChew` | `咀嚼 12.4s`（通用通道现有文案） | ✅ 可显示（**已由通用通道 ②.5 显示，不重复写**） |
| 11 | 天使僵尸 | `ZombieAngel` | ① | `spawnTimer`(递减,初始 15) / `invincible` / `invincibleTimer`(8) | `复活 9.4s` + `无敌 5.2s` | ✅ 可显示（双段） |
| 12 | 催眠师僵尸 | `ZombieHypnotist` | ① | `waitGrab` / `waitTimer`（对常量 `10.0`） | `偷取 4.8s` | ✅ 可显示 |
| 13 | 农夫僵尸 | `ZombieFarmer` | ① | `spawnTimer` / `spawnTime`=**10.0** | `召唤 7.1s` | ✅ 可显示 |
| 14 | 海盗船长僵尸 | `ZombieCaptain` | ① | `spawnTimer` / `spawnTime`=**15.0** | `召唤 11.9s` | ✅ 可显示 |
| 15 | 重生法师僵尸 | `ZombieNecromancerG` | ① | `shootingTimer` / `shootingTime`=**10.0** | `治疗 6.5s` | ✅ 可显示（「30 秒后复活」不算，见 §2.15） |
| 16 | 巨化法师僵尸 | `ZombieNecromancerB` | ① | `shootingTimer` / `shootingTime`=**10.0** | `变大 8.3s` | ✅ 可显示 |

**统计：可显示 16 / 16，不可显示 0。** 其中 2 个（三叶草气球、三叶草多彩气球）与 1 个（鲨鱼）
的计时**已经被通用通道显示**，按契约 §6「不重复写」处理（只登记，不新增分支）。

---

## 2. 逐僵尸证据（文件:行号）

> 下文 `<Z>` = `unpack\Asset\Anime\Character\Zombie\`。

### 2.1 毁灭菇僵尸 — `ZombieNormalDoomShroom` → `TowerDefenseZombieNormalDoomShroom`
- 类/场景：`<Z>Chapter1\Normal\Scene\DoomShroom\TowerDefenseZombieNormalDoomShroom.cs`（215 行）；
  场景 `TowerDefenseZombieNormalDoomShroom.tscn`（`script = ExtResource("3")` 指向该 cs）；
  `ComponentSet` 只挂 `TowerDefenseZombieNormalDoomShroomExplodeDefinition.tres`（无计时组件）。
- 图鉴：`TOWERDEFENSE_ZOMBIE_DOOMSHROOM_HANDBOOK_EXPRESTION`
  → 「血量 500 / 伤害 100/s（啃食）+ 即死（毁灭菇）」**特点：出场 25~30 秒后爆炸，释放一次即死性质的毁灭菇效果**。
- 字段：`:18 public double timer;`（已累计）、`:20 public double time;`（总时长）、`:22 public bool explode;`
- 推进：`:78-92 BatchUpdate` → `if (timer < time) { timer += delta * timeScale; return; }`；
  满则 `:88 explode = true;` 播 `Explode` 动画（`:171-179 AnimeCompleted("Explode")` → `explodeComponent.Explode(); Destroy();`）。
- 总时长来源：`:63 time = GD.RandRange(25.0, 30.0);`（存档恢复路径 `:38-51`）。`explodeComponent.reload = false`（`:59`）。
- 语义：出场 → 爆炸的**自毁倒计时**。剩余 = `time - timer`，总 = `time`。
- 显示：`爆破 12.3s`（青蓝，`total = time`）；`explode == true` 后不再显示。
- 实现：`CollectZombieCdDemoLines`（cdmod-owner 示范段，已复核保留）。
- 结论：**✅ 可显示**。

### 2.2 火爆辣椒僵尸 — `ZombieNormalJalapeno` → `TowerDefenseZombieNormalJalapeno`
- 文件：`<Z>Chapter1\Normal\Scene\Jalapeno\TowerDefenseZombieNormalJalapeno.cs`（203 行）。
- 图鉴：`..._JALAPENO_HANDBOOK_EXPRESTION` → 「500 血 / 100/s + 即死（火爆辣椒）」**特点：出场 25~30 秒后自燃**。
- 字段：`:16 timer` / `:18 time` / `:20 explode`（与 §2.1 同构）。
- 推进：`:59 time = GD.RandRange(25.0, 30.0);`；`:71-73 timer += delta * timeScale`。
- 显示：`自燃 8.7s`（青蓝，`total = time`）。
- 实现：示范段（保留）。
- 结论：**✅ 可显示**。

### 2.3 火炬辣椒僵尸 — `ZombieNormalJalatorch` → `TowerDefenseZombieNormalJalatorch`
- 文件：`<Z>Chapter1\Normal\Scene\Jalatorch\TowerDefenseZombieNormalJalatorch.cs`（246 行）。
- 图鉴：`..._JALATORCH_HANDBOOK_EXPRESTION` → 「特点②出场 25~30 秒后自爆，在自身所在的一竖列留下持续 15 秒的烈焰」。
- 字段：`:8 public const double ExplodeDelayMin = 7.0;`、`:10 public const double ExplodeDelayMax = 10.0;`、
  `:14 timer`、`:16 time`、`:18 explode`、`:20 public const double ExplodeChiliDamage = 1800.0;`
- 推进：`:59 time = GD.RandRange(7.0, 10.0);`；`:66-80 BatchUpdate` → `:71-75 timer += delta * timeScale`，满则 `:76 explode = true`。
- ⚠️ **图鉴写 25~30 秒，代码是 7~10 秒**（`:8/:10/:46/:59` 三处一致，`RandRange(7.0, 10.0)`）
  ⇒ **以代码为准**，显示的是真实倒计时，不是图鉴值。
- 显示：`自燃 5.4s`（青蓝，`total = time`＝7~10）。标签沿用「自燃」（同为辣椒自焚语义，不新造词）。
- 结论：**✅ 可显示**。

### 2.4 三叶草气球僵尸 — `ZombieBalloonBlover` → `TowerDefenseZombieBalloonBlover`
- 文件：`<Z>Chapter1\Balloon\Scene\Blover\TowerDefenseZombieBalloonBlover.cs`（542 行）。
- 组件：`TowerDefenseZombieBalloonBloverComponentSet.tres` → `ComponentTypeId = ['BloverComponent','CharacterTimerComponent']`，
  `InstanceId = ['character.blover','character.timer']` ⇒ **挂了命名计时器**。
- 图鉴：`..._BALLOONBLOVER_HANDBOOK_EXPRESTION` → 「特点③出场 8 秒后，转动叶片吹飞自己，同时吹退敌方阵营的僵尸、
  吹退敌方阵营的飞行植物一格」。
- 计时：`:60 _timer = componentManager.GetRuntime<CharacterTimerComponent>("character.timer");`
  `:113-124 StartCloverTimerIfNeeded()` → **`:120 _timer.Run("Clover", 8.0);`**（总时长 8.0，写死在调用点）；
  `:406 OnCloverTimeout("Clover")` → `:410 _cloverDeparturePending = true`。
- 另有一个**私有** `double _cloverBlowRemaining`（`:22`）：`:436-439` 从 `blover.blowTime`（或被吹时 2.0）起算，
  只是"吹飞位移"的特效时长（2s 级、私有字段、不是可感知冷却）⇒ **不显示**。
- 结论：**✅ 可显示，但已由通用通道 ② 显示**（`TryCollectInfo` ② 段遍历 `timerRunning/timerWaitTime/timerCurrent`，
  键 `Clover` 不在任何排除名单里 ⇒ 会渲染出一行倒计时，分类 = `CatOn(4)`「角色计时器其他CD」）。
  按契约 §6「不要重复行」⇒ **本批不新增分支**。
- ✅ **文案已修好**：原先通用通道会露出原始键名 `Clover 8.0s`。我向 cdmod-owner 提出后，
  `HealthCooldownLineEntry.cs` 的 `TimerLabel()` 已补 `case "Clover": return "吹飞";`
  ⇒ 现在显示 **`吹飞 8.0s`**，且仍然只有一行（不产生第二行）。

### 2.5 三叶草多彩气球僵尸 — `ZombieBalloonColourBlover` → `TowerDefenseZombieBalloonColourBlover`
- 文件：`<Z>Chapter1\BalloonColour\Scene\Blover\TowerDefenseZombieBalloonColourBlover.cs`（586 行）。
- 组件：`TowerDefenseZombieBalloonColourBloverComponentSet.tres` → 同为 `BloverComponent` + `CharacterTimerComponent`（`character.timer`）。
- 图鉴：`..._BALLOONCOLOURBLOVER_HANDBOOK_EXPRESTION` → 「血量 40×6（特殊）+270」「特点③出场 8 秒后…吹飞自己」。
- 计时：`:77 _timer = ...("character.timer")`；`:130-138` → **`:137 _timer.Run("Clover", 8.0);`**；`:394 OnCloverTimeout`。
- 私有 `_cloverBlowRemaining`（`:23`）同 §2.4，不显示。
- 结论：**✅ 可显示，已由通用通道 ② 显示**（同上，`TimerLabel` 加了 `Clover→吹飞` 后文案为 `吹飞 8.0s`）⇒ **不新增分支**。

### 2.6 寒冰菇冰车僵尸 — `ZombieZamboniIceShroom` → `TowerDefenseZombieZamboniIceShroom`
- 文件：`<Z>Chapter2\Zamboni\Scene\IceShroom\TowerDefenseZombieZamboniIceShroom.cs`（272 行）。
- 组件：`TowerDefenseZombieZamboniIceShroomComponentSet.tres` → 只有 `AttackComponent`（`character.attack.0`），无计时组件。
- 图鉴：`..._ZOMBONI_ICESHROOM_HANDBOOK_EXPRESTION` → 「特点②出场 15 秒后自爆，对全场植物释放寒冰菇效果」。
- 字段：`:19 public double timer;`、`:21 public double time;`、`:23 public bool over;`、`:25 public bool explode;`、
  `:15 public double speed = 30.0;`
- 推进：`:78-90 _Ready` → `:87 time = GD.RandRange(25.0, 30.0);`；`:92-106 BatchUpdate` →
  `:97-101 if (timer < time) { timer += delta * timeScale; return; }`；满则 `:102 explode = true; :103 CreateColdEffect(...); :104 Destroy();`
- ⚠️ **图鉴写 15 秒，代码是 25~30 秒**（`:70` 与 `:87` 两处 `RandRange(25.0, 30.0)`）⇒ **以代码为准**。
- 显示：`自爆 18.2s`（青蓝，`total = time`）；`explode == true` 后不再显示。
- 结论：**✅ 可显示**。

### 2.7 玩偶匣僵尸 — `ZombieJackbox` → `TowerDefenseZombieJackbox`
- 文件：`<Z>Chapter2\Jackbox\Scene\TowerDefenseZombieJackbox.cs`（240 行）。目录里只有 **1 份** `TowerDefenseZombieJackbox.tscn/.cs`，
  6 个卡池 key（`ZombieJackbox` / `+BlackHelmet/Cone/Bucket/Helmet/SpecialHelmet`）共用它，`ComponentSet` 无（只继承父 Prefab）。
- 图鉴：`..._JACKBOX_HANDBOOK_EXPRESTION` → 「血量 500 / 100/s / 佩戴：玩偶匣（物品，铁器）」**特点：出场一段时间后打开玩偶匣，释放自爆**；
  弱点：磁力效果。
- 字段：`:24 public bool hasJackBox = true;`、`:26 public bool over;`、`:28 public double timer;`、
  `:30 public double time;`、`:32 public bool explode;`
- 推进：`:85 time = GD.RandRange(9.0, 20.0);`；`:134-157 BatchUpdate` → `:146-150 timer += delta * timeScale`；
  满则 `:151 explode = true;` `:152 instance.unUseBuffFlags += 128;` `:155 SendStateEvent("ToBomb")`（仅 `hasJackBox`）。
  `:159-189` Bomb 动画播完 → `CreateEffect(); Destroy();`。
- 显示：`自爆 6.3s`（青蓝，`total = time`＝9~20）；`explode == true` 后不再显示。
- 结论：**✅ 可显示**。

### 2.8 海妖僵尸 — `ZombieSeaDemon` → `TowerDefenseZombieSeaDemon`
- 文件：`<Z>Chapter3\SeaDemon\Scene\Base\TowerDefenseZombieSeaDemon.cs`（369 行）；4 个卡池 key
  （`ZombieSeaDemon` / `+BlackHelmet/Helmet/SpecialHelmet`）共用。
- 组件：`TowerDefenseZombieSeaDemonComponentSet.tres` → 只有 `AttackComponent`（`character.attack.0`）。
- 图鉴：`..._SEADEMON_HANDBOOK_EXPRESTION` → 「血量 4000 / 300/s（戳刺）」「特性：免控」
  **特点：②每 15 秒在本行和上下两行召唤 3 只缠绕潜水僵尸（每行一只）**。
- 字段：`:13 public double timer;`、`:15 public bool spawnNext;`（另有 `:17 jumpMove`、`:19 isPointing`）。
- 推进：`:109-122 BatchUpdate` →
  `if (!spawnNext && …) { if (timer < 15.0) { timer += delta * timeScale; return; } spawnNext = true; timer = 0.0; }`
  → 状态机 `:200-202 if (!die && !nearDie && spawnNext && !isPointing && SendStateEvent("ToPoint")) spawnNext = false;`
  → `:224-226 command == "spawn"` → `:235 SpawnSnorkle()`（三行各一只，`:252/:293/:332`）。
- 总时长 = **代码字面量 `15.0`**（`:114`），没有对应字段 ⇒ 传常量 15.0（稳定 ⇒ 参与「只显示 ≥5 秒」过滤是安全的）。
- 语义：**每 15 秒一次召唤的周期计时**。剩余 = `15.0 - timer`。
- 显示：`召唤 9.6s`（青蓝，`total=15.0`）；`spawnNext == true`（即将点名，等状态机）⇒ 状态行 `召唤 就绪`（绿）。
- 结论：**✅ 可显示**。

### 2.9 磁场僵尸 — `ZombieMagnetic` → `TowerDefenseZombieMagnetic`
- 文件：`<Z>Chapter4\ZombieMagnetic\Scene\TowerDefenseZombieMagnetic.cs`（100 行）。
- 组件：`TowerDefenseZombieMagneticComponentSet.tres` →
  `ChangeProjectileStateComponent`（`InstanceId = "zombie_magnetic.change_projectile_state"`，
  `checkShape` 是 5×5 格矩形、初始 `Enabled = false`、`fireMethodFlags = 32`(TRACK)、`damageFlags = 2`）。
- 图鉴：`..._MAGNETIC_HANDBOOK_EXPRESTION` → 「血量 1100 + 270 / 佩戴：磁场头盔（一类，铁器）」
  「特点①磁场套装能够吸引 5×5 范围内的植物方子弹优先攻击自己 ②失去头盔后，吸引范围削弱至 3×3」。
- 字段：`:10 public double openTimer;`（已累计）、`:12 public double openTime;`（总时长）、`:14 public bool open;`
- 推进：`:26 openTime = GD.RandRange(6.0, 12.0);`；`:35-54 BatchUpdate` →
  `:42-46 openTimer += delta * timeScale`；满则 `:47 open = true;` `:50 checkShape.Enabled = true;`。
- **关于任务里说的「磁力 cd + 磁力时间」**：
  - 「磁力 cd」= `openTime − openTimer` ✅ 可显示（6~12 秒随机）。
  - 「磁力时间」（开启后能维持多久）**在 0.32 源码里不存在**：`:47 open = true` 之后
    `BatchUpdate` 在 `:38 if (… || open) return;` 直接返回，全类**没有任何地方**把 `open`/`checkShape.Enabled` 复位；
    `ChangeProjectileStateComponent.cs` 全文（224 行）也**没有**时长/计时字段（它的作用只有
    `OnProjectileIntersect`/`OnBulletIntersect` 把子弹 `damageFlags`/`fireMethodFlags` 改成追踪该僵尸）。
    唯一的"削弱"是**掉护具事件**（`:71-78 ArmorHitpointsEmpty("HelmetMagnet")` → 矩形 5×5 → 3×3），
    是**瞬时状态变化**、无持续时间 ⇒ 没有倒计时可显示。
  - ⇒ 因此磁场僵尸**只显示 1 段**（`磁力 x.xs` / `磁力 已开启`），不虚构「磁力时间」。
- 显示：未开 ⇒ `磁力 3.2s`（青蓝，`total = openTime`）；已开 ⇒ `磁力 已开启`（绿，状态行）。
- 实现：示范段（保留）。
- ⚠️ **别把这条规则串到 B 批的「电磁拖车僵尸」**（`ZombieMagnetron`，`Chapter9\Magnetron\`，翻译键
  `TOWERDEFENSE_ZOMBIE_MAGNETRON_NAME`）—— 它**是循环的**，与磁场僵尸不是一套字段（已实读核对）：
  - 磁场僵尸：`openTimer`/`openTime`/`open`，`open=true` 后**永久**有效（`open` 无复位点）；
  - 电磁拖车：`attackTimer`（`TowerDefenseZombieMagnetron.cs:68`）累加到 `>= 10.0`（`:349-352`）
    就 `BeginShooting(selectedGrid)`，而 `BeginShooting` 第一件事就是 **`:559 attackTimer = 0.0;`** ⇒
    倒计时会**一轮一轮重来**（另有 `:398`/`:958` 也多处置零）。
  ⇒ 验收时"磁力不回倒计时"只对 **A 批磁场僵尸**成立；拿电磁拖车去验会得出相反结论。
- 结论：**✅ 可显示（单段）**；「磁力持续时间」**不可显示**（无此字段/逻辑）。

### 2.10 鲨鱼僵尸 — `ZombieShark` → `TowerDefenseZombieShark`
- 文件：`<Z>Chapter4\ZombieShark\Scene\TowerDefenseZombieShark.cs`（304 行）。
  该类**自己没有**计时字段（只有 `:13 audioPlay`、`:15 originalWaterHeight` 等）。
- 组件：`TowerDefenseZombieSharkComponentSet.tres` 里第一个 ext_resource 是
  `Script\Component\TowerDefense\Character\ChomperComponent\Definitions\ZombieSharkDefinition.tres`，
  该定义 `chewTime = 15.0`、`InstanceId = "character.chomper"`、`biteEventName = "bite"`、`swallowAnimeClips = "ChewOver"`。
- 图鉴：`..._SHARK_HANDBOOK_EXPRESTION` → 「血量 1400 / 即死（吞噬，在水中）」「特点：在水中会潜水行动，
  接近一个植物后浮出水面将其直接吞噬，**咀嚼 15 秒**后继续潜水」——与 `chewTime = 15.0` 完全吻合。
- 计时字段（`ChomperComponent`，实读 `unpack\Script\Component\TowerDefense\Character\ChomperComponent\ChomperComponent.cs`）：
  `:112 public float chewTimer;`（已嚼）、`:46 public float chewTime = 30f;`（总时长，被定义覆盖成 15）、
  `:116 public bool isChew;`、`:124 public float currentChewTime;`（快照，**不能当进度**）；
  `:764-789 ChewProcessing` → `:781-786 if (chewTimer < currentChewTime) chewTimer += …; else OnChewOver?.Invoke();`
- 结论：**✅ 可显示，已由通用通道 ②.5 显示**（`TryCollectInfo` Entry.cs:1877-1915，`ChomperComponent` + `character.chomper`，
  文案 `咀嚼 X.Xs`，剩余 = `chewTime − chewTimer`）⇒ **本批不新增分支**（重复行会更糟）。
- 另：`:93-99 BlowBack` 在水中改成击退而不是位移，无计时。

### 2.11 天使僵尸 — `ZombieAngel` → `TowerDefenseZombieAngel`
- 文件：`<Z>Chapter4\Angel\Scene\TowerDefenseZombieAngel.cs`（502 行）。
- 图鉴：`..._ANGEL_HANDBOOK_EXPRESTION` → 「血量 800 / 类型：空中，小型僵尸，魔法僵尸」
  「特点①始终飞行，不会被击落 **②每隔 15 秒，原地复活随机一只在 15 秒内死去的僵尸**（排除自身，且不会复活靠近房子 3 列以内的僵尸）」
  「亡语：可在原地复活一次（被吹飞则在本行最右侧复活），**并在 8 秒内处于无敌状态**，无敌状态免疫吹飞效果」
  「备注：当无敌状态的天使进入场上最左侧 3 列时，会立即解除无敌」。
- 字段：`:27 speed = 10.0`、`:29 isRevive`、`:31 isReviveOver`、`:33 isBlow`、
  **`:35 public double spawnTimer = 15.0;`**（**递减**）、`:37 public bool invincible;`、**`:39 public double invincibleTimer = 8.0;`**
- 推进（复活间隔，`:275-290 WalkProcessing`）：
  ```csharp
  if (spawnTimer > 0.0) { spawnTimer -= delta * timeScale; }
  else if (TowerDefenseManager.HasDeathRecord(base.camp, requireAngelEligible: true)) { spawnTimer = 15.0; SendStateEvent("ToShooting"); }
  else { spawnTimer = 3.0; }
  ```
  ⇒ `spawnTimer` **本身就是"离下一次复活还有多少秒"**，不需要再用总时长减；触发后重置 15（无可复活目标时重置 3）。
- 推进（无敌，`:113-126`）：`if (invincible && …) { if (invincibleTimer > 0 && X > 第5列) invincibleTimer -= delta * timeScale; else { instance.invincible = false; invincible = false; } }`
  `:144-152 ReviveAttackEntered()` → `:149-151 instance.invincible = true; invincible = true; invincibleTimer = 8.0;`
- 显示：`复活 9.4s`（青蓝，**`total = 0`** —— 总时长会在 15/3 之间跳，取不到稳定值，按契约 §5.4 传 0 不参与过滤）；
  `invincible == true 且 invincibleTimer > 0` ⇒ `无敌 5.2s`（绿，`total = 8.0`）。
- 实现：示范段（保留）。
- 结论：**✅ 可显示（双段：复活间隔 + 重生后无敌）**。

### 2.12 催眠师僵尸 — `ZombieHypnotist` → `TowerDefenseZombieHypnotist`
- 文件：`<Z>Chapter5\Hypnotist\Scene\TowerDefenseZombieHypnotist.cs`（530 行）。
- 组件：`TowerDefenseZombieHypnotistComponentSet.tres` → 只有 `AttackComponent`（`character.attack.0`）。
- 图鉴：`..._HYPNOTIST_HANDBOOK_EXPRESTION` → 「血量 1000 / 即死（抓取）」「特点①具有蹦极特点，但被弹飞时会对保护伞造成 100 伤害
  **②成功落地时，使本格所有植物陷入 25 秒沉睡** **③停留 10 秒后，偷取本格所有植物**
  ④成功偷取植物时，令 3×3 范围内所有植物沉睡 15 秒」。
- 字段：**`:15 public bool waitGrab;`**（落地完成 = 开始计时）、**`:21 public double waitTimer;`**（已停秒数）、
  `:17 hasPlant`、`:23 canBlock`、`:27 grabOver`。
- 推进：`:174-194 IdleProcessing` →
  `if (!waitGrab || !CanRunAbility()) return;` → `if (waitTimer < 10.0) { waitTimer += delta * timeScale; return; }`
  → 清 buff → `:193 SendStateEvent("ToGrab")`。`waitGrab` 在 `:271` 落地后置 true。
- 总时长 = **代码字面量 `10.0`**（`:181`），无对应字段 ⇒ 传常量。
- 语义：**落地后"停留偷取"的倒计时**（游戏内就是"还要站多久才偷植物"）。剩余 = `10.0 - waitTimer`。
- 显示：`偷取 4.8s`（青蓝，`total = 10.0`）；仅在 `waitGrab == true` 时显示（未落地时这个计时根本不走，显示满值会误导）。
- 不显示的项：25 秒 / 15 秒沉睡是**加在植物身上的 buff**（植物侧倒计时，不属于本僵尸的 CD；
  植物那边的通用通道会处理），⇒ 僵尸身上没有这两个数。
- 结论：**✅ 可显示（偷取倒计时）**；「催眠/沉睡时长」不在僵尸侧 ⇒ 不在本批显示。

### 2.13 农夫僵尸 — `ZombieFarmer` → `TowerDefenseZombieFarmer`
- 文件：`<Z>Chapter7\Farmer\Scene\TowerDefenseZombieFarmer.cs`（218 行）；2 个卡池 key（`ZombieFarmer` / `+Cone`）共用；无 `ComponentSet`。
- 图鉴：`..._FARMER_HANDBOOK_EXPRESTION` → 「血量 200 + 500 / 佩戴：头巾（一类）」
  **特点：出场后每 10 秒召唤随机一只基础植物僵尸（豌豆/坚果/向日葵/寒冰射手僵尸）**。
- 字段：**`:18 public double spawnTime = 10.0;`**（总间隔）、**`:20 public double spawnTimer;`**（已过）、
  `:15 public Array<string> spawnList`（4 个随机目标）。
- 推进：`:73-91 BatchUpdate` → `if (spawnTimer < spawnTime) { spawnTimer += delta * timeScale; return; }`
  → `:88 SendStateEvent("ToSpawn"); :89 spawnTimer = 0.0;`（`:123 spawnTimer = spawnTime;` 是"施法完成置满"路径）；
  `:153-160 SpawnPlantZombie()` 从 `spawnList` 随机取一个。
- 显示：`召唤 7.1s`（青蓝，`total = spawnTime`，**从实例读**，不写死 10）。
- 结论：**✅ 可显示**。

### 2.14 海盗船长僵尸 — `ZombieCaptain` → `TowerDefenseZombieCaptain`
- 文件：`<Z>Chapter7\Captain\Scene\TowerDefenseZombieCaptain.cs`（344 行）；2 个卡池 key（`ZombieCaptain` / `+Bucket`）共用。
- 图鉴：`..._CAPTAIN_HANDBOOK_EXPRESTION` → 「血量 1000 / 300/s（挥砍）」
  **特点：出场后每 15 秒进行一次召唤，在周围 4 格召唤四只海盗船员僵尸（出现方式为蹦极空投）**
  「亡语：将距离最近的一只海盗船员升格为新的船长，并重置血量和召唤冷却」。
- 字段：**`:11 public double spawnTime = 15.0;`**、**`:13 public double spawnTimer;`**、`:15 over`。
- 推进：`:76-94 BatchUpdate` → `:83-87 spawnTimer += delta * timeScale`，满 → `:91 SendStateEvent("ToSpawn"); :92 spawnTimer = 0.0;`；
  `:123 spawnTimer = spawnTime;`（施法完成置满）；`:188-213 SpawnCrew()` 蹦极空投 4 只。
- 亡语（`:218 SpawnDeathReplacement`）是**瞬发**（升格一只船员，重置对方血量/冷却）⇒ 本僵尸身上没有倒计时。
- 显示：`召唤 11.9s`（青蓝，`total = spawnTime`）。
- 结论：**✅ 可显示**。

### 2.15 重生法师僵尸 — `ZombieNecromancerG` → `TowerDefenseZombieNecromancerG`
- 文件：`<Z>Chapter8\NecromancerG\Scene\TowerDefenseZombieNecromancerG.cs`（220 行）。
- 图鉴：`..._NECROMANCERG_HANDBOOK_EXPRESTION` → 「血量 3000 / 100/s」「特点①免疫所有控制和负面效果
  **②每 10 秒为 3×3 范围所有僵尸（包括自己）恢复 20% 生命值**」
  「亡语：在地面上首次死亡时，会原地留下一个重生坑洞，**并在 30 秒后借助坑洞复活**」。
- 字段：**`:11 public double shootingTime = 10.0;`**、**`:15 public double shootingTimer;`**、`:17 public bool over;`
  （场景 `TowerDefenseZombieNecromancerG.tscn:19` 也写着 `shootingTime = 10.0`，与图鉴 10 秒吻合）
- 推进：`:114-127 BatchUpdate` → `:119-123 shootingTimer += delta * timeScale`，满 → `:124 SendStateEvent("ToShooting"); :125 shootingTimer = 0.0;`；
  `:160-186` 的 `magic` 事件里 `:173 towerDefenseCharacter2.Health(0.2 * hitpointsSave)` 真治疗 20%。
- 显示：`治疗 6.5s`（青蓝，`total = shootingTime`，从实例读）。
- **「30 秒后复活」为什么不算本僵尸的 CD**：亡语在 `:211-219 DestroySet()` → `CraterCreate(nolimit: true, "CraterNG")`，
  30 秒是**重生坑洞（`CraterNG` 这个独立角色）**的计时，不在僵尸实例上；
  重生法师自身没有任何"复活倒计时"字段（只有 `over` 布尔，`:215`）。
- ✅ **这个 30 秒并没有丢** —— 它被**通用坑洞通道**完整覆盖（已核实，非推测）：
  - `HealthCooldownLineEntry.cs:1359-1360` → `TryCollectCraterInfo(character, lines)`；
  - 该函数 `:1587-1588` 注释与实现写明：运行时读角色级字段 `dieDownTimer`（已过，`:1609`/`:1662`），
    总时长读配置 `TowerDefenseCraterConfig.dieDownTime`（`:1682`）；
  - 配置实测：`unpack\Asset\Anime\Character\Crater\CraterNG\Config\TowerDefenseCraterNG.tres` → **`dieDownTime = 30.0`**；
  - 输出 `消失 X.Xs`（`:1643`，`BusyColor`，`total = dieDownTime`），归「障碍物消失」开关 `CatOn(1)`（默认开）。
  ⇒ 玩家在**坑洞上**能看到 `消失 30.0s → 0`，与图鉴「30 秒后借助坑洞复活」一致。
  结论：不需要我写分支（僵尸实例上本来就没有这个数），也**不算"不可显示"**——属于"已由通用通道显示"。
- 结论：**✅ 可显示（治疗 CD）**；「30 秒复活」由 `CraterNG` 坑洞走通用坑洞通道。

### 2.16 巨化法师僵尸 — `ZombieNecromancerB` → `TowerDefenseZombieNecromancerB`
- 文件：`<Z>Chapter8\NecromancerB\Scene\TowerDefenseZombieNecromancerB.cs`（202 行）。
- 图鉴：`..._NECROMANCERB_HANDBOOK_EXPRESTION` → 「血量 3000 / 100/s」「特点①免疫所有控制和负面效果
  **②每 12 秒将自己前方的随机一只僵尸变大**，使目标僵尸体型×1.2 倍，血量×1.5 倍（每只僵尸可变大最多 2 次）」。
- 字段：**`:15 [Export] public double shootingTime = 10.0;`**、**`:17 public double shootingTimer;`**
  （场景 `TowerDefenseZombieNecromancerB.tscn:17` 也是 `shootingTime = 10.0`）。
- ⚠️ **图鉴写 12 秒，代码/场景都是 10.0** ⇒ **以代码为准**（我读实例字段，所以永远跟真实值一致，不会写死）。
- 推进：`:106-119 BatchUpdate` → `:111-115 shootingTimer += delta * timeScale`，满 → `:116 SendStateEvent("ToShooting"); :117 shootingTimer = 0.0;`；
  `:33 SIZE_UP` 特效。
- 显示：`变大 8.3s`（青蓝，`total = shootingTime`）。
- 结论：**✅ 可显示**。

---

## 3. 判据小结（为什么 A 批没有「不可显示」）

A 批 16 个僵尸**全部**都能在运行时读到"剩余 + 总时长"，原因是这批僵尸的机制几乎都是
「隔 N 秒做一件事」（自毁 / 召唤 / 治疗 / 偷取 / 变大 / 复活），而 PVZHE 0.32 的实现方式固定为两种：

| 实现方式 | 特征 | A 批命中 |
|---|---|---|
| ① 类字段累加 | `BatchUpdate` 里 `timer += delta * timeScale;` 然后 `if (timer < time) return;` | 12 个（含 8 个自带 `time`/`spawnTime`/`shootingTime` 字段） |
| ① 递减字段 | `spawnTimer` 从 15 往下减（天使） | 1 个 |
| ② `CharacterTimerComponent` 命名键 | `_timer.Run("Clover", 8.0)` | 2 个 |
| ③ 组件字段 | `ChomperComponent.chewTimer / chewTime` | 1 个 |

**唯一需要特别说明的两条**（都已写进代码注释）：
- 磁场僵尸的「磁力时间」在源码里**不存在**（`open` 一旦 true 永久有效，`ChangeProjectileStateComponent` 无时长字段）；
- 重生法师的「30 秒复活」是**重生坑洞实体**（`CraterNG`）的计时，不在僵尸身上 ——
  且该 30 秒**已被通用坑洞通道覆盖**（`Entry.cs:1359/1593-1643` 读 `dieDownTimer` + `TowerDefenseCraterNG.tres:dieDownTime=30.0`
  → 显示 `消失 30.0s`，见 §2.15），故不需要也不应该在本批重复实现。

---

## 4. 代码落地对照（`runtime_src\ZombieCdA.cs`）

| 僵尸 | 实现位置 | 备注 |
|---|---|---|
| 毁灭菇 / 火爆辣椒 / 磁场 / 天使 | `CollectZombieCdDemoLines`（示范段，cdmod-owner 写，我复核后**保留**） | 证据见 §2.1 / §2.2 / §2.9 / §2.11 |
| 火炬辣椒 / 寒冰菇冰车 / 玩偶匣 | `ACountUp(...)`（通用"已过→总时长"读法） | 三者的字段布局完全同构（`timer`/`time`/`explode`） |
| 农夫 / 海盗船长 / 重生法师 / 巨化法师 | `ACountUp(...)`（`spawnTimer` 或 `shootingTimer` + 对应 `*Time`） | `total` 从实例字段读，不写死 |
| 海妖 | `ACountUpFixed(..., 15.0)` + `spawnNext` 状态行 | 总时长是代码字面量 |
| 催眠师 | `ACountUpFixed(..., 10.0)` + `waitGrab` 门控 | 总时长是代码字面量 |
| 三叶草气球 / 三叶草多彩气球 | **无分支** | 已由通用通道 ② 显示（`Clover`） |
| 鲨鱼 | **无分支** | 已由通用通道 ②.5 显示（`ChomperComponent`） |

**新增标签词**（均 ≤4 汉字，遵守契约 §5.1；**5 个全部已获 cdmod-owner 批准并写进契约 §5.2 词表**）：
`自爆`（寒冰菇冰车 / 玩偶匣）、`召唤`（海妖 / 农夫 / 海盗船长）、`偷取`（催眠师）、`治疗`（重生法师）、`变大`（巨化法师）。
`自燃`（火爆辣椒 / 火炬辣椒）、`爆破`（毁灭菇）、`磁力`（磁场）、`复活`/`无敌`（天使）沿用契约原有词表。

---

## 5. 编译与产物核对（实测记录）

### 5.1 编译

```
python mods\HealthCooldownLine\_build_serial.py
```
→ `[compile] RC=0` / `[compile] OK` / **0 个错误**（4 条警告全部是既有警告：
`SponsorQrInjector.cs:221/232` CS0162、`HealthCooldownLineEntry.cs:669/5804` CS0169，与本批无关）。

产物：`mods\HealthCooldownLine\runtime_src\bin\Release\JTYHealthCooldownLine.dll`
（`AssemblyName = JTYHealthCooldownLine`，见 `HealthCooldownLine.csproj:20`；打包时才改名成 `Runtime\ModAssembly.dll`）。

**最终组装产物（cdmod-owner 打包后，我独立复核过）**：

| 产物 | 大小 | md5 |
|---|---|---|
| `runtime_src\bin\Release\JTYHealthCooldownLine.dll` | 103,936 B | `9c499253507d04f5ab60e134e1fa390a` |
| `Runtime\ModAssembly.dll`（包内） | 103,936 B | `9c499253507d04f5ab60e134e1fa390a`（与 bin **字节完全一致**） |
| `dist\HealthCooldownLine.pmod` | 149,540 B | `198e421061830d152a6fe0bb84749b55` |

独立复核脚本：`研究-僵尸CD\_verify_pmod_A.py` —— 直接 `zipfile` 打开最终 `.pmod`，
取出 `Runtime/ModAssembly.dll` 算 md5 并与 bin 产物**逐字节比对**，再在**包内**那份 DLL 上重查 A 批全部特征串
⇒ 实测 `字节完全一致 = True` / `MISSING = none`。
（包内只有 3 个条目：`mod.json`、`Runtime/ModAssembly.dll`、`Resources/SponsorQR.png`。）

> md5 会随 b 改标签而变（我这版改完是 `07dcc9d5…`，b 改标签前是 `3ad6421b…`，最终 `9c499253…`）——
> **别把 md5 当版本指纹**，要复核就重跑上面两个脚本看 `FAILED/MISSING = none`。

### 5.2 特征串核对（**必须字节级搜，不能用 `Encoding.Unicode.GetString`**）

脚本：`研究-僵尸CD\_verify_dll_A.py`（`python mods\HealthCooldownLine\研究-僵尸CD\_verify_dll_A.py`）
→ 末行 `FAILED = none  -> all feature strings hit`。

**踩过的坑（写给后来人，也提醒了 cdmod-owner）**：
.NET 元数据里「类型/方法/字段名」在 `#Strings` 堆是 **UTF-8**，而「字符串字面量」在 `#US` 堆是
**长度前缀 + UTF-16LE**，且条目起始偏移**可以是奇数**。所以
`[Text.Encoding]::Unicode.GetString(整个文件字节)` 或 `bytes.decode('utf-16-le')` 这类
**整块解码会假阴性**（错位的串变乱码）。本批一开始就被它骗了一轮：`自爆/召唤/偷取/变大/无敌/已开启`
全部报 False，改成 `bytes.count(字面量.encode('utf-16-le'))` 后 count 分别是 1/2/1/1/1/1 ——**代码其实早就编进去了**。
⇒ 核对一律用**字节子串计数**：
```python
b = open(dll, 'rb').read()
b.count('自爆'.encode('utf-16-le'))        # #US 堆：字符串字面量
b.count(b'CollectZombieCdABatchLines')    # #Strings 堆：标识符（UTF-8）
```

> 补充（与 cdmod-owner 对齐后）：`cdmod-owner` 的 `.cache\check_dll_strings.py` 用的也是字节搜索
> （`t.encode("utf-16-le") in data`），**没有**这个假阴性问题；踩坑的是更早那条
> `Encoding.Unicode.GetString(bytes).Contains(...)` 的一行式 PowerShell。
> `爆破/无敌/已开启` 当时报 True 是对的。最终组装会**两个脚本都跑**（本文件覆盖更全）。

核对结果（全部命中）：

| 类别 | 命中项 |
|---|---|
| 我新增的中文标签 | `自爆` 1 / `召唤` 2 / `偷取` 1 / `治疗` 2 / `变大` 1 / `就绪` 4（另 `爆破`3 `自燃`2 `磁力`4 `复活`3 `无敌`1 `已开启`1 为示范段+通用通道） |
| 我新增的方法 | `CollectZombieCdABatchLines` 1 / `AClassIs` 1 / `ACountUp` 2 / `ACountUpFixed` 1 |
| 反射字段名字面量 | `openTimer` `openTime` `spawnNext` `waitGrab` `spawnTimer` `spawnTime` `shootingTimer` `shootingTime` `invincibleTimer` `explode` `timer` `time` 全部 ≥1 |
| 类键字面量 | `ZombieNormalJalatorch` `ZombieZamboniIceShroom` `ZombieJackbox` `ZombieSeaDemon` `ZombieHypnotist` `ZombieFarmer` `ZombieCaptain` `ZombieNecromancerG` `ZombieNecromancerB` 各 1 |

### 5.3 尚未做的（如实说明）

- **未做游戏内运行验证**（本轮只到「编译通过 + 产物特征串命中 + 包内 DLL 与 bin 字节一致」）。
  真机核对所需的**预期值算法 / 判对判错 / 开关前置条件**已单独整理成
  **`研究-僵尸CD\A批-真机验收清单.md`**（含 5 条"看着像 bug 其实是对的"、逐僵尸预期值表、
  6 个代表僵尸的判对判错、记录模板）。
- 显示值依赖运行时字段，静态无法证明每个僵尸都会出现：
  例如 `waitGrab`（催眠师）要落地后才为 true、`explode`（自毁系）置 true 后故意隐藏，
  这些门控逻辑都是按 §2 里实读的源码分支写的，但**真机表现仍需按验收清单抽验**。

---

## 6. 图鉴静态 CD（v1.27.0 新增）

### 6.1 为什么必须做"静态表"（两个面都没有跑起来的计时，已实读）

| 面 | 节点 | 有没有真角色实例 | 计时会不会推进 |
|---|---|---|---|
| 图鉴**格子** | `AlmanacZombieWidow`（`unpack\Prefab\GUI\DialogBox\Almanac\AlmanacZombie\AlmanacZombieWidow.cs`） | **没有**（`Init(config)` 只 `CreateSprite()` + `FreezePreviewTree(sprite)`，`:16` 只有 `public TowerDefensePacketConfig config`） | 不适用 |
| 图鉴**详情面板** | `InformationPanel`（`unpack\Prefab\GUI\InformationPanel\InformationPanel.cs:150-165`） | **有**（`InitPacket` 实例化真角色） | **不会**：`inGame=false` + `SetMainStateMachineDispatchEnabled(false)` ⇒ `BatchUpdate`/状态机不跑，`timer` 恒为初值 |

⇒ 两处都只能显示**配置里的"满 CD 总时长"**，实现为 `partial void CollectAlmanacStaticCdA(string charName, string saveKey, lines)`（契约 v1.2；入参只有身份字符串，**没有 cm / character**），一律 `AddLine`（`Total=0`，不参与「只显示 ≥5 秒」），**不判开关**（外层 `_showAlmanacCd` 已判），**不带 `满CD：` 前缀**（`Entry.cs` 的 `AlmanacCdPrefix` 统一加）。

### 6.2 三列总表（Lead 要求的口径：静态值 / 战斗内口径 / 图鉴能否复用）

`[配置]` = 值确实写在资源里；`[代码]` = 值只在 DLL 的常量/字段默认值/字面量里（配置里查不到，改版会漂）。

| # | 僵尸（后 3 个为本轮 Lead 点名核查） | 图鉴静态 CD | 静态值来源（证据） | 战斗内口径 | 图鉴复用 |
|---|---|---|---|---|---|
|1|毁灭菇僵尸|`爆破 25~30s`|[代码] `TowerDefenseZombieNormalDoomShroom.cs:63` `time = GD.RandRange(25.0, 30.0)`|`爆破 time−timer`（total=time）|✅ 同值（战斗内也是这 2 个字段）|
|2|火爆辣椒僵尸|`自燃 25~30s`|[代码] `TowerDefenseZombieNormalJalapeno.cs:59` 同款|`自燃 time−timer`|✅|
|3|火炬辣椒僵尸|`自燃 7~10s`|[代码] `TowerDefenseZombieNormalJalatorch.cs:8/:10` `ExplodeDelayMin=7.0`/`Max=10.0`，`:59 RandRange(7,10)`|`自燃 time−timer`|✅（**图鉴文案写 25~30，以代码为准**）|
|4|三叶草气球僵尸|`吹飞 8.0s`|[配置] `TowerDefenseZombieBalloonBloverComponentSet.tres` 的 sub_resource `TimerDefinition` → `timerDictionary = {"Clover": 8.0}`|通用② `Clover`（`Blover.cs:120 Run("Clover", 8.0)`）|✅ 定义值=有效值|
|5|三叶草多彩气球僵尸|`吹飞 8.0s`|[配置] `...BalloonColourBloverComponentSet.tres` 同款 `{"Clover": 8.0}`|通用② `Clover`（`:137`）|✅|
|6|寒冰菇冰车僵尸|`自爆 25~30s`|[代码] `TowerDefenseZombieZamboniIceShroom.cs:87 RandRange(25,30)`|`自爆 time−timer`|✅（**图鉴写 15 秒，以代码为准**）|
|7|玩偶匣僵尸|`自爆 9~20s`|[代码] `TowerDefenseZombieJackbox.cs:85 RandRange(9,20)`|`自爆 time−timer`|✅|
|8|海妖僵尸|`召唤 15.0s`|[代码] `TowerDefenseZombieSeaDemon.cs:114 if (timer < 15.0)`|`召唤 15−timer` / `spawnNext` 时状态行|✅|
|9|磁场僵尸|`磁力 6~12s`|[代码] `TowerDefenseZombieMagnetic.cs:26 openTime = RandRange(6.0,12.0)`|`磁力 openTime−openTimer` → `磁力 已开启`|✅|
|10|鲨鱼僵尸|`咀嚼 15.0s`|[配置] `ChomperComponent\Definitions\ZombieSharkDefinition.tres` → `chewTime = 15.0`|通用②.5 `咀嚼 chewTime−chewTimer`|✅|
|11|天使僵尸|`复活 15.0s` + `无敌 8.0s`（**2 行**）|[代码] `TowerDefenseZombieAngel.cs:35 spawnTimer = 15.0`、`:39 invincibleTimer = 8.0`（场景 tscn **无覆盖**）|`复活 spawnTimer` / `无敌 invincibleTimer`|✅|
|12|催眠师僵尸|`偷取 10.0s`|[代码] `TowerDefenseZombieHypnotist.cs:181 if (waitTimer < 10.0)`|`偷取 10−waitTimer`（`waitGrab` 门控）|✅|
|13|农夫僵尸|`召唤 10.0s`|[配置] 场景 `TowerDefenseZombieFarmer.tscn` → `spawnTime = 10.0`（字段 `:18` 同值）|`召唤 spawnTime−spawnTimer`|⚠️ 值一致，但**图鉴拿不到**（见 §6.4）|
|14|海盗船长僵尸|`召唤 15.0s`|[配置] 场景 `TowerDefenseZombieCaptain.tscn` → `spawnTime = 15.0`（字段 `:11` 同值）|`召唤 spawnTime−spawnTimer`|⚠️ 同上|
|15|重生法师僵尸|`治疗 10.0s`|[配置] 场景 `TowerDefenseZombieNecromancerG.tscn:19` → `shootingTime = 10.0`（字段 `:11` 同值）|`治疗 shootingTime−shootingTimer`|⚠️ 同上|
|16|巨化法师僵尸|`变大 10.0s`|[配置] 场景 `TowerDefenseZombieNecromancerB.tscn:17` → `shootingTime = 10.0`（字段 `:15` 同值）|`变大 shootingTime−shootingTimer`|⚠️ 同上（**图鉴写 12 秒，以代码为准**）|
|17|吸金磁僵尸（点名核查）|`吸金 3.0s`|[配置] `TowerDefenseZombieNormalGoldMagnetComponentSet.tres` 的 `MagnetCoinComponentDefinition` → `magnetTime = 3.0`（默认 5f，被组件集覆盖）|`吸金 magnetTime−timer`（**本轮新加战斗内分支**）|✅|
|18|僵尸坚果伽刚特尔（点名核查）|`生成 10.0s`|[配置] `TowerDefenseZombieGargantuarWallnutZTimerComponentDefinition.tres` → `{"Spawn": 10.0}`；被同目录 `...ComponentSet.tres:5` 引用 ⇒ 真挂上；源码 `TowerDefenseZombieGargantuarWallnutZ.cs:45/:175/:179` `Run("Spawn", 10.0)` ⇒ 有效值=10.0|通用② `Spawn`（cat 3 生成计时，TimerLabel→`生成`）|✅ 战斗内已由通用通道显示，**不再加分支**|
|19|僵尸豌豆小鬼（点名核查）|`生成 10.0s`|[配置] `TowerDefenseZombieImpPeashooterZTimerComponentDefinition.tres` → `{"Spawn": 10.0}`（`...ComponentSet.tres:4` 引用）；源码 `:96/:161 Run("Spawn", 10.0)`|通用② `Spawn`（cat 3）|✅ 同上|

### 6.3 数据源统计（回答"图鉴能显示静态值的有哪些 / 战斗内能显示的有哪些 / 两者都拿不到的"）

- **图鉴能显示静态值：19 / 19**（A 批 16 + 点名 3）。
- **战斗内能显示：19 / 19**。其中 18 个走我或通用通道；**1 个是我本轮新加的**（吸金磁，`MagnetCoinComponent`）。
  · 战斗内**已有**分支/通道：16（A 批）+ 坚果伽刚特尔 / 豌豆小鬼（通用② `Spawn`）＝ 18
  · 战斗内**新增**：吸金磁（`character.magnet_coin`）
- **两者都拿不到（配置里确实没有计时）：0 个**。
  · 曾被怀疑的 **僵尸坚果伽刚特尔** 有 CD：`生成 10.0s`（派生定义 `{"Spawn": 10.0}`）——用户感觉"格子裁掉看不到"属**布局/裁切**问题，不是没有数据；
  · **吸金磁僵尸** 有 CD：`吸金 3.0s`（`magnetTime`）——不是"只有出场一次"，而是**周期 CD**（`timer` 累加到满 → 吸一次 → `:262 timer = 0f` 归零）。

### 6.4 唯一的复用风险（必须记下来）

**农夫 / 海盗船长 / 重生法师 / 巨化法师** 这 4 个的权威值在**场景节点**上（`[Export] spawnTime` / `shootingTime`），
而图鉴手里只有 `TowerDefensePacketConfig.characterConfig`（`TowerDefenseZombieConfig`）——
该配置**没有任何计时字段**（实测 dump：`attack/weight/name/hitpointsNearDeath/hitpoints/damagePointData/armorData/customData/ashScene/homeWorld/cost/packetCooldown/plantGridType/maskFlags`）。
⇒ 这 4 个的静态值**只能在 `ZombieCdA.cs` 里硬编码**（写 10.0 / 15.0 / 10.0 / 10.0），
游戏改版若调了场景导出值，静态表**不会自动跟随**（其余 12 个要么是 `[代码]` 常量、要么是组件定义值，同样需要跟改版同步，性质相同）。
这条已在 `ZombieCdA.cs` 的静态段注释里逐条标注 `[配置]` / `[代码]`。

### 6.5 与「详情面板自带冷却行」的区别（避免验收时混淆）

`InformationPanel` 自带那行「冷却时间」读的是 `packetConfig.characterConfig.packetCooldown`，
而僵尸 config 基本不覆盖（例：`TowerDefenseZombieNormalGoldMagnet.tres:22 packetCooldown = 7.5` 是个例外），
多数落到基类默认 **5.0**（`Resource\TowerDefense\Character\Config\TowerDefenseCharacterConfig.cs:83`）
⇒ 图鉴里僵尸常显示"冷却时间 5.0"，那是**卡片冷却**，与本轮的**技能 CD 静态行**是两件事。

### 6.6 一条口径说明：`吸金 3.0s` 与「只显示 ≥5 秒」（Lead 2026-11-08 拍板）

吸金磁僵尸的战斗内 CD 是 **3.0 秒**（`magnetTime`，组件集覆盖值），小于过滤阈值 `4.9`
（`Entry.cs` 的 `ShortTimerThresholdSeconds = 4.9`）。**实现上照实传真实的 `total = 3.0`，不传 0。**

理由（既有语义，不为单个僵尸开后门）：
- 「只显示 ≥5 秒」**默认是关的**（`_onlyLongTimers` 默认 `false`）⇒ 用户平常就能看到 `吸金 2.4s`；
- 只有当用户**主动**打开该开关时，3.0s 这类短 CD 才会被整行隐藏 —— 这正是这个开关的设计意图
  （"总时长 < 4.9 秒的计时不显示"），与加农炮/其他短 CD 一视同仁；
- 若为它传 `total = 0`（= 永不参与过滤），会出现"这个僵尸的 CD 过滤不掉、别人的能过滤掉"的双标，反而更难解释。

同理：**鲨鱼 `咀嚼 15.0s`、天使 `无敌 8.0s`、火炬辣椒 `自燃 7~10s`** 等之所以照实传总时长，
也是为了让「只显示 ≥5 秒」对它们与对植物一致生效。

---

## 7. 本轮协作复核记录（两个"误显示"的性质定性，供报告引用）

> 这两项**代码归 `cdmod-owner`**（`Entry.cs`），本节只记录我复核给出的**判据 + 证据 + 最终拦截点**，
> 已由 owner 采纳并落地（编译 RC=0）。我的作用域文件 `ZombieCdA.cs` 与此无关。

### 7.1 「巨型雪球 / 巨型冰球 / 烈焰毒物 不该显示血量」

| 用户说的 | 真身（类 / 目录） | 图鉴定性 | 最终拦截点（`Entry.cs`） |
|---|---|---|---|
| 巨型雪球 | `TowerDefenseItemSnowBall`（`Item\SnowBall\`） | 物品（召唤物），韧性 `---` | `:205` 类名 `SnowBall`（v1.23.0 已有） |
| 巨型冰球 | `TowerDefenseItemIceball`（`Item\ItemIceball\`） | 同上 | `:228` **`tn.StartsWith("TowerDefenseItem")` 族前缀** |
| 烈焰毒物 = 毒雾 | `TowerDefenseItemPoisonMist`（`Item\ItemPoisonMist\`） | 同上（config `hitpoints = 1.0`） | `:228` 族前缀 |
| 烈焰毒物 = 烈焰 | `TowerDefenseItemMegaFire`（`Item\MegaFire\`） | 同上 | `:350` **专人拦截 `tn0.Contains("MegaFire")`** |
| （顺带）巨型火球 | `TowerDefenseItemFireball` | 同上 | `:228` 族前缀 |

**为什么"烈焰"必须专人拦（本次复核的核心发现）**：
`TryCollectObstacleHp` 的入口闸门是 `if (!IsObstacleCharacter(ch) && !IsObstacleLike(ch)) return;`
—— `&&` 意味着**任一判据为真就放行**；而 `IsObstacleCharacter`（`:159`）含 `|| n.Contains("MegaFire")`
⇒ `ItemMegaFire` **在进入 `IsObstacleLike` 之前**就通过了，那一整套排除表对它**完全无效**。
⇒ 只能像 `Crater` 那样在 `TryCollectObstacleHp` 入口单独 `return`。

**四条必须记住的"不能这样做"**：
1. ❌ 不能按"血量值"判：`Resource\TowerDefense\Character\Config\TowerDefenseCharacterConfig.cs:16` → **`hitpoints = 300.0`（基类默认）**，道具与植物同值；毒雾显式 `1.0` 也照样会显示 `血量 1/1`。
2. ❌ 不能按 `MoveComponent` 判（我复核掉的一条死代码）：冰球/火球挂的是 **`GroundMoveComponent`**
   （`Script\Component\TowerDefense\Character\GroundMoveComponent\GroundMoveComponent.cs:4 : CharacterComponentRuntime`），
   与 `MoveComponent`（`Script\Component\MoveComponent\MoveComponent.cs:5 : ComponentBase`）**无继承关系** ⇒ 判据恒 `null`。
3. ❌ 不能把判据改成 `GroundMoveComponent` 了事：`Prefab\TowerDefense\Character\ComponentSets\TowerDefenseZombieComponentSet.tres:4`
   也给**所有僵尸**挂它，只是被 `tn.Contains("Zombie")` 前置拦住 ⇒ 隐式顺序耦合，动顺序就崩。
4. ❌ 不能笼统删掉 `IsObstacleCharacter` 的 `MegaFire` 分支：`TimeBomb`(3000) / `TimeNuke`(6000) 的 config
   有**真实 hitpoints**，必须继续显示血量；且烈焰的 **15s `AutoDestroy`「消失」计时必须保留**
   （`Item\MegaFire\Scene\TowerDefenseItemMegaFireTimerComponentDefinition.tres:8`，走通用通道②，与血量无关）。

**同类核实（受 owner 之托）**：全库类名/文件名含 `MegaFire` 的只有 `TowerDefenseItemMegaFire` 一个角色
（另一个 `MegaFirePeaSprite` 是 Sprite 类，不是 `TowerDefenseCharacter`）；`CharacterResource.json` 里也只有 `ItemMegaFire` 一个键 ⇒ 该拦截**零误伤**。

### 7.2 「僵尸豌豆小鬼 显示 加速 100%」

**根因**：⑨「等级与加速」通道（`Entry.cs` 的 ⑨ 段）原本对**所有角色**生效，而它读的三类数据都是**植物侧语义**：
`buff.timeScaleValue`（`:3082-3102` 一带，"任何带该字段的 buff"）/ `FireComponent.timeScale` / `AttackComponent` 与 `FireComponent` 的间隔比
⇒ 僵尸的 `FireComponent.timeScale`、`fireIntervalBase/fireInterval` 是**配置里的设计值**（僵尸天生射得快/慢），
拿它当"被加成"呈现就是误导；而且 `timeScaleValue` 还同时被**减速**语义的 buff 使用
（`TowerDefenseCharacterBuffAttackSpeedDown.timeScaleValue = 0.5`、`BuffComponent.cs:69` 的 `RuneStormSlow` 同族）。

**复核结论（owner 的修法完整、无遗漏、无过杀）**：
- 守卫 `isZombieBoost = IsZombieCdHost(character, charName)`（`:1818`）；
- 真正的门只有两个：`:3337`（伤害 ±%）与 `:3359`（加速/攻速/猫窝/加成 buff **整块**）；
- 我逐一核过**所有** boost 输出行 —— `加速 +`(`:3372`)、`加速 -`(`:3376`)、`攻速 +`(`:3386`)、`猫窝加速 ×2`(`:3406`)、
  `伤害 ±`(`:3342`/`:3346`)、加成 buff 循环(`:3416` 起) —— **全部落在两个守卫块内**；
- `isZombieBoost` 全文件只出现 4 次（注释 + 定义 + 2 个守卫）⇒ **不会误伤植物**。
- 附带影响（已确认可接受）：`IsZombieCdHost` 含 `TowerDefenseSkeleton` ⇒ 骷髅伽刚特尔也一并停止 ⑨。

### 7.3 最终产物指纹（owner 本轮最后一次构建）

| 项 | 值 |
|---|---|
| DLL | **115,200 B / md5 `58a7a1bb2f4dad3039697fd2b891da50`** |
| 编译 | `python mods\HealthCooldownLine\_build_serial.py` → `[compile] RC=0`，0 错误（只编译，未 `--install`；装机由 Lead 统一做） |
| 我的 19 条静态文案 + `吸金 ` + 4 个符号 | 全部命中（字节级搜索；`吸金 3.0s`=1、`吸金 `=2、`CollectAlmanacStaticCdA`/`AStaticIs`/`TryCollectMagnetCoinLine`/`CollectZombieCdABatchLines` 各 1） |
| `ZombieCdA.cs` | mtime 2026-11-08 **10:16:36**，自该时刻起**未再改动**（本轮 3 次 md5 变化均非我代码：`b93eb68…`→`58a7a1b…` 是 owner 改的烈焰拦截/`减速`/删重复方法） |
