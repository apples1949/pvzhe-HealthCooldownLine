# 报告 · 僵尸 CD 显示（v1.26.0）

> 交付物：`mods\HealthCooldownLine`（公开名「血条·装填倒计时」，id `healthcooldowndisplay`）**v1.26.0**
> 完成日期：2026-11-07　｜　组装与汇总：cdmod-owner　｜　A 批实现：zombie-dev-a　｜　B 批实现：zombie-dev-b
> 事实源：`unpack\`（0.32 实际源码）+ 逐僵尸研究文档 `研究-僵尸CD\A批-研究.md` / `B批-研究.md` + 契约 `研究-僵尸CD\00-框架与契约.md`

---

## 0. 一页纸摘要

| 项 | 内容 |
|---|---|
| 新功能 | 僵尸血条上的**专属冷却/倒计时**（31 个指定僵尸），共 **29 个僵尸有可显示内容**（其中 8 个由既有通用通道显示、本次只做中文化），**0 个"完全无可显示 CD"** |
| 新开关 | **「显示僵尸冷却」**（`showZombieCd`，bool，默认**开**，立即生效）。位置：战斗选项 → 「CD开关设置面板」→ 第 3 列；同时也在 Mod 配置页「显示内容」分组里 |
| 优先级 | **总开关 `enabled` > 「显示僵尸冷却」> 「只显示 ≥5 秒」**（详见 §2） |
| 框架 | `HealthCooldownLineEntry` 改 **`partial`**；新增 `TryCollectZombieCdLines(...)` 单一调用点 + A/B 两个 `partial` 实现点（零成本抽象，任一方未完工都不影响编译） |
| 顺手修好的共性能力 | ① `TimerLabel` 补 `Cast/Sing/Throw/Clover` 中文映射（**11 个僵尸**的英文键名变中文）；② 门控放宽到骨堆 `TowerDefenseSkeleton`（骷髅伽刚特尔 30 秒复活）；③ 标签词表扩充 9 词并统一 |
| 版本 | 1.25.2 → **1.26.0**（mod.json + csproj Version/AssemblyVersion/FileVersion 同步） |
| 构建 | 编译 **0 错误**；`dist` 与装机包 sha256 一致；`verify_pmod` ok=8 / warn=1 / FAIL=1（FAIL 是旧工具规则，见 §7）；DLL 特征串 **84/84 命中** |

---

## 1. 新开关「显示僵尸冷却」

### 1.1 三处落地（mod.json / 战斗设置页 / 持久化）

| 位置 | 实现 | 证据 |
|---|---|---|
| `mod.json` 设置项 | `key=showZombieCd`、`type=bool`、`label=显示僵尸冷却`、`group=显示内容`、`applyMode=immediate`、`affectsGameplay=false`、`default=true` | `mod.json` settings 第 12 项（`ConvertFrom-Json` 校验通过，`settings.Count=16`） |
| 战斗设置页 CD 面板 | 勾选框节点名 `ModZombieCdCheckBox` + 说明小字「— 毁灭菇爆炸·火爆辣椒自燃·天使复活·磁力开启 等僵尸专属计时 —」；**两条建面板路径都加了**：主建路径（第 3 列、署名行之前）+ 守候补全路径 `EnsureCdPanelContent`；`TryEnsureOptionCheckBox` 顶部**状态校准**段也加了 | `HealthCooldownLineEntry.cs` `MakeZombieCdCheckBox()` / `MakeZombieCdSubLabel()` / `AppendZombieCdControls()` / `OnZombieCdChanged()`；`AppendZombieCdControls(col3)`（主建）、`AppendZombieCdControls(...)`（`EnsureCdPanelContent`）、校准段 `existZbCb.SetPressedNoSignal(_showZombieCd)` |
| 持久化（双写） | 宿主配置 `showZombieCd` + `user://HealthCooldownLine.cfg` 的 `feature_zombiecd`；启动 `RefreshSwitches` 读宿主、`LoadEnabled` 兜底读 cfg | `PersistSwitch(HostKeyZombieCd, on)`；`RefreshSwitches` 内 `TryReadSwitch(context, HostKeyZombieCd)`；`LoadEnabled/SaveEnabled` 内 `ZombieCdCfgKey` |
| 落盘日志 | `Info("开关[显示僵尸冷却] → 开/关；已落盘 showZombieCd=true/false（cfg 键 feature_zombiecd 同步写入）")`（受 `enableLog` 控制，默认静默 —— 符合 v1.25.1 起"正式版不写日志"的用户要求） | `OnZombieCdChanged` |

### 1.2 与总开关的优先级（代码里就是判定顺序）

```
ApplyToCharacter()
  ├─ if (!_enabled) { HideAllLines(); return; }              ← ① 总开关：一行都不显示（本开关无意义）
  ├─ … 通用通道（加农炮/命名计时器/咀嚼/消化/…）…
  └─ TryCollectInfo()
        └─ TryCollectZombieCdLines(cm, character, charName, lines)
              ├─ if (lines.Count >= 6) return;               ← 行数上限
              ├─ if (!_showZombieCd || !_enabled) return;    ← ② 本开关：只掐掉僵尸附加行
              ├─ if (!IsZombieCdHost(...)) return;           ← 类型门控（僵尸类 / 骨堆类）
              ├─ CollectZombieCdLinesA(...)  → ZombieCdA.cs
              └─ CollectZombieCdLinesB(...)  → ZombieCdB.cs
  → 之后统一走「只显示 ≥5 秒」按 Total 过滤                    ← ③ 滤掉总时长 < 4.9s 的计时行
```

- **总开关关** ⇒ 僵尸行与其它一切 CD 都不显示；
- **只关「显示僵尸冷却」** ⇒ 僵尸附加行消失，植物/障碍物/加农炮等 CD **照常显示**；
- **两个都开** ⇒ 僵尸附加行再受「只显示 ≥5 秒」过滤（例：黑客小鬼 `隐身 4.0s` 在该开关打开时会被隐藏，因为 4.0 < 4.9 —— 这是既定行为，未造假）。

---

## 2. 框架改动（`runtime_src\HealthCooldownLineEntry.cs`）

| 改动 | 说明 |
|---|---|
| `public sealed class` → **`public sealed partial class`** | 三个文件是同一个类的不同部分；`ZombieCdA.cs` / `ZombieCdB.cs` 由各自 owner 维护，**csproj 未改**（SDK 默认 glob 自动收编，实测新文件未改工程也能编进去） |
| 唯一调用点 | `TryCollectInfo(...)` 末尾加 `TryCollectZombieCdLines(cm, character, charName, lines);` —— **全文件只此一处** |
| 两个实现点 | `partial void CollectZombieCdLinesA/B(ComponentManager cm, TowerDefenseCharacter character, string charName, List<(string Text, Color Color, double Total)> lines)` |
| 零成本抽象 | **partial 方法没实现时编译器把声明与调用点整段删除** ⇒ A 完工/B 未动工时也能编译；实测证据：B 尚未落码时 DLL 里查不到 `CollectZombieCdLinesB`，B 落码后立刻出现 |
| 总闸门 | `TryCollectZombieCdLines`：行数上限 → 开关 → 类型门控 → A/B 分别 try/catch（各一个"只报一次"标志，一个僵尸出错不拖累另一个） |
| 类型门控（v1.26.1 放宽） | `IsZombieCdHost(character, charName)` = `TowerDefenseZombie` 子类 **或** 骨堆（`charName=="Skeleton"` / 类名 `TowerDefenseSkeleton` / 类名含 `Skeleton` 且 `is TowerDefenseGravestone`）；**普通墓碑不放行** |
| 僵尸专用助手（供 A/B 用，避免各写一套反射） | `ZombieField(Type,name)`（沿类型链 + 按"类型\|字段"缓存）、`ZombieNum`、`ZombieFlag`、`IsZombieChar`、`ZombieNameIs`、`ZombieCdDisplayable`；另有 `CatOn(9)` = 本开关 |
| `TimerLabel` 中文化 | 新增 `Cast→施法`、`Sing→歌唱`、`Throw→投掷`、`Clover→吹飞`（`Heal→治疗` 原有）⇒ 通用通道 ② 不再显示英文键名 |

> 行号提示：本报告引用的 `HealthCooldownLineEntry.cs` 行号可能随后续改动漂移，**按函数名检索**更稳；`unpack\` 下的行号稳定。

---

## 3. 逐僵尸结论表（31 个，一个不漏）

状态图例：**🆕 新增显示**（本次为它写了专属分支）｜**♻ 通用通道已覆盖**（本来就能显示，本次只做中文化，**不重复写分支**）｜**❌ 不可显示**。

### 3.1 A 批（16 个，`ZombieCdA.cs`，zombie-dev-a）

| # | 中文名 | 类名 / `config.name` | 显示文案 | 状态 | 证据（`unpack\…`） |
|---|---|---|---|---|---|
| 1 | 毁灭菇僵尸 | `TowerDefenseZombieNormalDoomShroom` / `ZombieNormalDoomShroom` | `爆破 12.3s`（青蓝） | 🆕 | `Anime\Character\Zombie\Chapter1\Normal\Scene\DoomShroom\TowerDefenseZombieNormalDoomShroom.cs:18-22`（`timer`/`time`/`explode`）、`:63`（`time=RandRange(25,30)`）、`:83-87`（累加）、`:88`（置 explode） |
| 2 | 火爆辣椒僵尸 | `TowerDefenseZombieNormalJalapeno` / `ZombieNormalJalapeno` | `自燃 8.7s` | 🆕 | `…\Normal\Scene\Jalapeno\TowerDefenseZombieNormalJalapeno.cs:16-20`、`:59` |
| 3 | 火炬辣椒僵尸 | `TowerDefenseZombieNormalJalatorch` / `ZombieNormalJalatorch` | `自燃 5.4s` | 🆕 | `…\Normal\Scene\Jalatorch\TowerDefenseZombieNormalJalatorch.cs:14/16/18`、`:8/:10`（`ExplodeDelayMin/Max=7/10`）、`:59`、`:66-80` |
| 4 | 三叶草气球僵尸 | `TowerDefenseZombieBalloonBlover` / `ZombieBalloonBlover` | `吹飞 8.0s` | ♻ 通用通道 ②（`character.timer` 的 `Clover` 键，本次中文化） | `…\Chapter1\Balloon\Scene\Blover\TowerDefenseZombieBalloonBlover.cs:120`（`_timer.Run("Clover", 8.0)`） |
| 5 | 三叶草多彩气球僵尸 | `TowerDefenseZombieBalloonColourBlover` / `ZombieBalloonColourBlover` | `吹飞 8.0s` | ♻ 通用通道 ②（同上） | `…\BalloonColour\Scene\Blover\TowerDefenseZombieBalloonColourBlover.cs:137` |
| 6 | 寒冰菇冰车僵尸 | `TowerDefenseZombieZamboniIceShroom` / `ZombieZamboniIceShroom` | `自爆 18.2s` | 🆕 | `…\Chapter2\Zamboni\Scene\IceShroom\TowerDefenseZombieZamboniIceShroom.cs:19/21/23/25`、`:87`（`time=RandRange(25,30)`）、`:92-106` |
| 7 | 玩偶匣僵尸 | `TowerDefenseZombieJackbox` / `ZombieJackbox`（+5 盔甲变体） | `自爆 6.3s` | 🆕 | `…\Chapter2\Jackbox\Scene\TowerDefenseZombieJackbox.cs:24/26/28/30/32`、`:85`（`time=RandRange(9,20)`）、`:134-157` |
| 8 | 海妖僵尸 | `TowerDefenseZombieSeaDemon` / `ZombieSeaDemon`（+3 变体） | `召唤 9.6s` / `召唤 就绪` | 🆕 | `…\Chapter3\SeaDemon\Scene\Base\TowerDefenseZombieSeaDemon.cs:13/15`、`:109-122`（`timer<15.0` 字面量）、`:224-235`（`SpawnSnorkle`） |
| 9 | 磁场僵尸 | `TowerDefenseZombieMagnetic` / `ZombieMagnetic` | **1 段**：未开 `磁力 3.2s`（青蓝）｜已开 `磁力 已开启`（绿） | 🆕（**「磁力时间」不存在，见 §4.1**） | `…\Chapter4\ZombieMagnetic\Scene\TowerDefenseZombieMagnetic.cs:10-14`（`openTimer`/`openTime`/`open`）、`:26`（`openTime=RandRange(6,12)`）、`:42-51` |
| 10 | 鲨鱼僵尸 | `TowerDefenseZombieShark` / `ZombieShark` | `咀嚼 12.4s` | ♻ 通用通道 ②.5（`ChomperComponent` 咀嚼，`chewTime=15.0`） | `Entry.cs` 咀嚼分支（`CatOn(7)`）+ `TowerDefenseChomperComponent` 定义；研究文档 `A批-研究.md` §2.10 |
| 11 | 天使僵尸 | `TowerDefenseZombieAngel` / `ZombieAngel` | **2 段**：`复活 9.4s`（青蓝）+ `无敌 5.2s`（绿） | 🆕（双段） | `…\Chapter4\Angel\Scene\TowerDefenseZombieAngel.cs:35-39`（`spawnTimer=15`/`invincible`/`invincibleTimer=8`）、`:283`（重置 15）、`:288`（无目标重置 3）、`:151`、`:120` |
| 12 | 催眠师僵尸 | `TowerDefenseZombieHypnotist` / `ZombieHypnotist` | **1 段**：`偷取 4.8s` | 🆕（**「睡眠 25/15 秒」在植物身上，见 §4.3**） | `…\Chapter5\Hypnotist\Scene\TowerDefenseZombieHypnotist.cs:15/21`、`:174-194`（`waitTimer<10.0` 字面量 → `ToGrab`） |
| 13 | 农夫僵尸 | `TowerDefenseZombieFarmer` / `ZombieFarmer`（+`ZombieFarmerCone`） | `召唤 7.1s` | 🆕 | `…\Chapter7\Farmer\Scene\TowerDefenseZombieFarmer.cs:18/20`、`:73-91`、`:123`、`:153-160` |
| 14 | 海盗船长僵尸 | `TowerDefenseZombieCaptain` / `ZombieCaptain`（+`ZombieCaptainBucket`） | `召唤 11.9s` | 🆕 | `…\Chapter7\Captain\Scene\TowerDefenseZombieCaptain.cs:11/13`、`:76-94`、`:123`、`:188-213` |
| 15 | 重生法师僵尸 | `TowerDefenseZombieNecromancerG` / `ZombieNecromancerG` | **1 段**：`治疗 6.5s`；另「30 秒后复活」= **坑洞通道** `消失 30.0s`（见 §4.2） | 🆕（僵尸本体）+ ♻（复活计时走坑洞通道） | `…\Chapter8\NecromancerG\Scene\TowerDefenseZombieNecromancerG.cs:11/15`、`:114-127`、`:173`、`:211-219`（`CraterCreate(nolimit:true,"CraterNG")`） |
| 16 | 巨化法师僵尸 | `TowerDefenseZombieNecromancerB` / `ZombieNecromancerB` | `变大 8.3s` | 🆕 | `…\Chapter8\NecromancerB\Scene\TowerDefenseZombieNecromancerB.cs:11/15`、`:114-127`；场景 `.tscn:17` |

### 3.2 B 批（15 个，`ZombieCdB.cs`，zombie-dev-b）

| # | 中文名 | 类名 / `config.name` | 显示文案 | 状态 | 证据（`unpack\…`） |
|---|---|---|---|---|---|
| 17 | 云小鬼僵尸 | `TowerDefenseZombieImpCloud` / `ZombieImpCloud` | `投掷 5.2s` | 🆕 | `…\Chapter8\ImpCloud\Scene\TowerDefenseZombieImpCloud.cs:8`（`SHOOT_INTERVAL=8.0`）+ `_shootTimer` |
| 18 | 黑客小鬼僵尸 | `TowerDefenseZombieImphacker` / `ZombieImphacker` | **2 段**：`冷却 7.4s`（青蓝）+ `隐身 3.1s`（绿） | 🆕（双段） | `…\Challenge\Imphacker\Scene\TowerDefenseZombieImphacker.cs:94`（`_hackTimer.Start(10.0)`）、`:174`（`_hackEffectRemaining=4.0`） |
| 19 | 骷髅伽刚特尔 | 骨堆 `TowerDefenseSkeleton` / `Skeleton` | `复活 22.5s` | 🆕（**本次放宽门控才可达**，见 §5.2） | `…\GraveStone\Skeleton\Scene\TowerDefenseSkeleton.cs:6/10/12/14`、`:34-38`（累加）、`:49-56`（满 30s 生成 `ZombieSkeletuar`）；`Config\TowerDefenseSkeleton.tres`（hp3000 / cooldown30） |
| 20 | 基因炸弹僵尸 | `TowerDefenseZombieJackGene` / `ZombieJackGene` | `爆破 12.3s` | 🆕 | `…\Chapter9\JackGene\Scene\TowerDefenseZombieJackGene.cs`（`timer`/`time`(9~20)/`explode`/`hasJackBox`） |
| 21 | 电磁拖车僵尸 | `TowerDefenseZombieMagnetron` / `ZombieMagnetron` | `磁力 6.1s` | 🆕 | `…\Chapter9\Magnetron\Scene\TowerDefenseZombieMagnetron.cs:44`（`AttackInterval=10.0`）+ `attackTimer`/`anchored`/`shooting` |
| 22 | 安眠术士僵尸 | `TowerDefenseZombieSleepMage` / `ZombieSleepMage` | **2 段（互斥，同屏 1 行）**：前摇/吟唱 `吟唱 3.4s`、发射后 `睡眠 7.8s` | 🆕（双段） | `…\Chapter10\SleepMage\Scene\TowerDefenseZombieSleepMage.cs:9`（`EntryCastDelay=5`）、`:11`（`SleepDuration=10`）、`:259`（`chantTimer>=5`）+ `entryTimer`/`chantTimer`/`sleepTimer`/`sleeping`/`spellSpent` |
| 23 | 时间术士僵尸 | `TowerDefenseZombieTimeMage` / `ZombieTimeMage` | `冷却 8.9s` | 🆕 | `…\Chapter10\TimeMage\Scene\TowerDefenseZombieTimeMage.cs:163`（`spellTimer>=10`） |
| 24 | 守卫伽刚特尔 | `TowerDefenseZombieGargantuarGD` / `ZombieGargantuarGD` | `蓄力 2.7s` | 🆕 | `…\Chapter10\GargantuarGD\Scene\TowerDefenseZombieGargantuarGD.cs:21`（`ChargeDuration=5.0`）+ `_chargeElapsed`/`_charging` |
| 25 | 塞壬僵尸 | `TowerDefenseZombieSiren` / `ZombieSiren` | **2 段（通用通道②）**：`歌唱 4.2s`（`Sing`）+ `施法 11.4s`（`Cast`） | ♻ 通用通道 ② + 本次中文化 | `…\Chapter10\Siren\Scene\TowerDefenseZombieSiren.cs:84/:216`（`_timerComponent.Run("Cast",12.0)`）、`…SirenTimerComponentDefinition.tres`（`Cast:12 / Heal:1 / Sing:5`） |
| 26 | 守护术士僵尸 | `TowerDefenseZombieGuardMage` / `ZombieGuardMage` | `施法 9.3s` | ♻ 通用通道 ② + 中文化 | `…\Chapter10\GuardMage\…TimerComponentDefinition.tres`（`Cast:10`）、`TowerDefenseZombieGuardMage.cs:92/:145` |
| 27 | 占卜术士僵尸 | `TowerDefenseZombieDivineMage` / `ZombieDivineMage` | `施法 11.1s` | ♻ 通用通道 ② + 中文化 | `…DivineMageTimerComponentDefinition.tres`（`Cast:12`）、`TowerDefenseZombieDivineMage.cs:125/:178` |
| 28 | 唤魔使僵尸 | `TowerDefenseZombieEvoker` / `ZombieEvoker` | `施法 4.6s` | ♻ 通用通道 ② + 中文化 | `…EvokerTimerComponentDefinition.tres`（`Cast:10`）、`TowerDefenseZombieEvoker.cs:215/:270`（运行期 `Run("Cast",5.0)`，通用通道读 live 值 ⇒ 显示 5 秒制是对的） |
| 29 | 女巫僵尸 | `TowerDefenseZombieWitch` / `ZombieWitch` | `投掷 8.1s` | ♻ 通用通道 ② + 中文化 | `…WitchTimerComponentDefinition.tres`（`Throw:10`）、`TowerDefenseZombieWitch.cs:107/:178` |
| 30 | 快闪迪斯科僵尸 | `TowerDefenseZombieDiscoFlash` / `ZombieDiscoFlash` | `起身 6.2s` | 🆕 | `FlashStepComponent` 的 `_fellRemaining` / `fellDuration`（`FlashStepComponentDefinition.cs:11` 默认 10.0）、`…DiscoFlashComponentSet.tres:13` |
| 31 | 快闪舞者僵尸 | `TowerDefenseZombieBackupFlash` / `ZombieBackupFlash` | `起身 6.2s` | 🆕 | 同上；`…BackupFlashComponentSet.tres:13` |

### 3.3 统计

| 分类 | 数量 | 明细 |
|---|---|---|
| 🆕 新增专属分支 | **23** | A 批 13（#1,2,3,6,7,8,9,11,12,13,14,15,16）+ B 批 10（#17,18,19,20,21,22,23,24,30,31） |
| ♻ 通用通道已覆盖（本次仅中文化） | **8** | #4、#5（`Clover`→吹飞）、#10（咀嚼通道）、#25~#29（`Cast/Sing/Throw`→施法/歌唱/投掷） |
| ❌ 完全无可显示 CD | **0** | 31 个僵尸里，没有任何一个"既无专属计时、也没有通用通道覆盖" |

**双段（多段）僵尸共 6 个**：#9 磁场（源码只有 1 段，见 §4.1）、#11 天使（2 段齐）、#18 黑客小鬼（2 段齐）、#22 安眠术士（2 段齐，互斥同屏 1 行）、#25 塞壬（2 段齐，通用通道）、#12 催眠师（源码只有 1 段，见 §4.3）。
**双段要求里有 3 段的"第二段"在源码里不存在**——没有虚构，逐条说明见 §4。

---

## 4. 三个"任务描述与源码不符"的如实说明（不虚构第二段）

### 4.1 磁场僵尸：**「磁力时间」这段计时在源码里不存在**
- `TowerDefenseZombieMagnetic` 只有 `openTimer`（已过）/ `openTime`（6~12 随机）/ `open` 三个计时相关字段；
- `open = true` 在 `:47` 置位后，`:38` 的判断变成 `if (… || open) return;` ⇒ **计时不再推进、全类也没有任何复位/倒计时代码**；
- 磁力场的持续时间由 `ChangeProjectileStateComponent.checkShape` 的**开关状态**决定（`:48-52` 打开、`:71-78` 掉护具时瞬发缩圈），**该组件（224 行）没有任何时长字段**；
- ⇒ 只显示 1 段：等待期 `磁力 3.2s`（青蓝，total=`openTime`）+ 开启后 `磁力 已开启`（绿，状态行）。**不写第二段。**

### 4.2 重生法师僵尸：**「30 秒复活」不在僵尸实例上，但已被通用通道完整覆盖**
- 僵尸本体的计时是 `shootingTimer`/`shootingTime`（10 秒治疗一次）⇒ 显示 `治疗 6.5s`；
- 死亡时 `TowerDefenseZombieNecromancerG.cs:211-219` 走 `CraterCreate(nolimit: true, "CraterNG")` ⇒ **复活计时属于另生成的坑洞实体 `TowerDefenseCraterNG`**；
- 该坑洞配置 `unpack\Asset\Anime\Character\Crater\CraterNG\Config\TowerDefenseCraterNG.tres` → **`dieDownTime = 30.0`**（实读）；
- 现有坑洞通道 `TryCollectCraterInfo(...)` 读角色级 `dieDownTimer`（已过）+ 配置 `dieDownTime`（总时长），输出 **`消失 X.Xs`**（青蓝），归「**障碍物消失**」开关（`CatOn(1)`，默认开）；
- ⇒ 玩家在骨堆/坑洞上能看到 **`消失 30.0s → 0`**，与图鉴一致。**结论：通用坑洞通道已覆盖，不需要僵尸分支，也不算"不可显示"。**

### 4.3 催眠师僵尸：25s/15s 沉睡是**植物侧 buff**，不是僵尸的冷却
- 僵尸实例上只有 `waitTimer`（对常量 10.0）与 `waitGrab` ⇒ 显示 `偷取 4.8s`；
- 图鉴里的「落地使本格植物沉睡 25 秒」「偷取时 3×3 植物沉睡 15 秒」是**加在植物身上的睡眠 buff**（植物侧倒计时），僵尸实例上没有这两个数；
- ⇒ 只显示 1 段。若将来要做植物侧睡眠倒计时，属"植物 buff"通道，不在本批范围。

---

## 5. 图鉴文案 vs 代码实际值（一律以**运行时真实字段**为准）

| 僵尸 | 图鉴写的 | 代码实际 | 我们显示 | 依据 |
|---|---|---|---|---|
| 火炬辣椒僵尸 | 25~30 秒后自燃 | **7~10 秒** | `自燃`（total=7~10） | `TowerDefenseZombieNormalJalatorch.cs:8/:10` `ExplodeDelayMin/Max=7.0/10.0`、`:59` `time=RandRange(7,10)` |
| 寒冰菇冰车僵尸 | 15 秒后自爆 | **25~30 秒** | `自爆`（total=25~30） | `TowerDefenseZombieZamboniIceShroom.cs:87` `time=RandRange(25,30)` |
| 巨化法师僵尸 | 12 秒 | **10.0 秒** | `变大`（total=10.0） | `TowerDefenseZombieNecromancerB.cs:15` `shootingTime=10.0` + 场景 `.tscn:17` |

> 理由：本 Mod 显示的就是**运行时真实剩余时间**。若按图鉴写死总时长，会出现"倒计时从 10 跳到 25"之类的假值；而且「只显示 ≥5 秒」是按 Total 判定的，写死会连带过滤判定出错。

---

## 6. 「没有可显示 CD」的排查结论

- **31 个僵尸：0 个"完全无可显示 CD"**（每个都能读到剩余+总时长，或由通用通道显示）。
- 但有 **8 个僵尸没有专属分支**，原因不是"做不了"，而是**同一件事只显示一次的取舍**（`00-框架与契约.md` §6）：
  | 僵尸 | 为什么不在僵尸分支里写 |
  |---|---|
  | 三叶草气球 / 三叶草多彩气球 | 计时就是 `character.timer` 的 `Clover` 键，通用通道 ② 已显示；再写一遍 = 同屏两行重复 |
  | 鲨鱼 | `ChomperComponent` 咀嚼，通用通道 ②.5 已显示 |
  | 塞壬 / 守护术士 / 占卜术士 / 唤魔使 / 女巫 | 计时就是 `character.timer` 的 `Cast`/`Sing`/`Throw` 键，通用通道 ② 已显示 |
- 另有 **3 个僵尸的"第二段"在源码层面不存在**：磁场（磁力时间）、催眠师（沉睡时长）、重生法师（30 秒复活在坑洞实体上）——见 §4。
- 快闪系僵尸的「闪身步」是**瞬发事件、无持续时间**，滑倒概率是**百分比**而非计时 ⇒ 这两项按判据判为不可显示，只显示「起身」倒计时。

---

## 7. 构建 / 打包 / 装机证据

### 7.1 编译
```
python mods\HealthCooldownLine\build_and_install.py --install
[compile] RC=0        ← 0 错误
[compile] OK
[package] RC=0
[install] %APPDATA%\…\Mods\HealthCooldownLine.pmod (149,540 B)   ← 最后一次组装（mod.json 更新文案后）
```
> ⚠️ 该脚本**末尾打印日志时会抛 `UnicodeEncodeError: 'gbk' codec can't encode ...`**（GBK 控制台 + 路径里的替换字符），导致 python 进程退出码 1。**这不是编译/打包/装机失败**——`runtime_src\build_last.log` 里 `[compile] RC=0` / `[compile] OK` / `[package] RC=0` / `[install] …` 全部齐全，随后我已用 md5、`verify_pmod`、`verify_build`、DLL 特征串四重复核。该缺陷是 `build_and_install.py` 的既有问题（不在本任务写作用域，未改）。

### 7.2 三方（四方）md5 一致
| 位置 | md5 | 大小 |
|---|---|---|
| `runtime_src\bin\Release\JTYHealthCooldownLine.dll`（编译产物） | `9c499253507d04f5ab60e134e1fa390a` | 103,936 B |
| `Runtime\ModAssembly.dll`（打包源） | `9c499253507d04f5ab60e134e1fa390a` | 103,936 B |
| `dist\HealthCooldownLine.pmod` **包内** `Runtime/ModAssembly.dll` | `9c499253507d04f5ab60e134e1fa390a` | 103,936 B |
| `dist\HealthCooldownLine.pmod`（包本身） | `198e421061830d152a6fe0bb84749b55` | 149,540 B |
| 装机 `%APPDATA%\…\Mods\HealthCooldownLine.pmod` | `198e421061830d152a6fe0bb84749b55` | 149,540 B |

包内清单：`mod.json`(18,665 B, md5 `f2be731ff6fb181959f567cbfb59a865`) / `Runtime/ModAssembly.dll`(103,936 B) / `Resources/SponsorQR.png`(104,566 B)。
`dist` 与装机包的 **sha256 也完全一致**（`verify_build.py` 独立复核 ✓）。

### 7.3 `verify_pmod.py`
```
ok   [package.readable]  zip 可读，3 个条目，149299 字节
ok   [manifest.order]    mod.json 位于第 0 个条目
ok   [manifest.count]    根 mod.json 唯一
ok   [manifest.size]     mod.json 18248 字节
ok   [manifest.schema]   schemaVersion=2
ok   [manifest.identity] id=healthcooldowndisplay name=血条·装填倒计时 version=1.26.0 author=apples1949
FAIL [managed.api]       要求托管运行时 schemaVersion=2 且 runtimeApiVersion=1
ok   [managed.declared]  托管程序集: Runtime/ModAssembly.dll
ok   [deps.self]          无依赖
warn [entry.unsupported]  Resources/SponsorQR.png（ApplyMod 不处理的资源类型）
→ ok=8  warn=1  FAIL=1
```
**关于这条 FAIL（不是本 Mod 的问题）**：
- `verify_pmod.py` 里该规则写死为 `if schema != 2 or runtimeApiVersion != 1` —— 是**旧工具规则**；
- 0.32 宿主实际接受的是 **3**：`unpack\addons\ModEditor\ModSystem\ModExporter.cs:285` `xWModManifest.RuntimeApiVersion = 3;`、`XWModCharacterCompanionRuntime.cs:111` 报错文案 `运行 API 版本不兼容：Mod={x}，Host={3}`、`ModLoader.cs:145` 走 `XWModRuntimeApi.IsSupported(...)`；
- 全仓 18 个 Mod 里 **17 个都是 `runtimeApiVersion: 3`**（仅 `ModPerfMonitor` 是 1）；本 Mod 从旧版一直用 3 并在游戏里正常工作。
⇒ 结论：**保持 3**，不为过时的工具规则改 manifest（改成 1 会被宿主的 `IsSupported` 判为不兼容）。`warn [entry.unsupported]` 是 17 个 Mod 共用的赞助码 PNG，属正常。

### 7.4 `verify_build.py`（四检：编译 / 打包 / 装机 / 包内容）
```
python pvz-hybrid-mod-skills\tools\verify_build.py HealthCooldownLine
  === HealthCooldownLine ===
      · 编译：DLL 新于源码 ✓
      · 打包：dist 11-07 22:16 ✓
      · 装机：dist == 装机（sha256 一致）✓
      · 内容：version/author/API/入口 一致，配置项 16 项一致 ✓
  ✅ 全部通过（编译 / 打包 / 装机 / 包内容 三件套）
```
- 「配置项 16 项」拆分：**13 个 bool**（`enabled`、`only_long_timers`、`showLoad`…`showLevel` 11 个显示类 + **新增 `showZombieCd`** + `enableLog`）+ **3 个非 bool**（`toggleHotkey` enum / `aboutChangelog` text / `aboutAuthor` richtext）。即"原 12 个显示类 bool + 1 个新开关 = 13 个 bool 设置"，与预期一致。
- 首次运行时该工具报过两条**时间戳启发式**告警（`❌ DLL 比源码旧` / `❌ dist 包比源码旧`）：因为 `ZombieCdB.cs` 最后一次编辑（22:12:10，**纯注释同步**）晚于打包时刻（22:12:07），而编译是确定性的 ⇒ 内容 md5 完全一致。已通过"用编译产物刷新 `Runtime/ModAssembly.dll`（**字节完全相同**，md5 复核前后一致）→ 重新打包 → 重新装机"消除，四检现已全绿。

### 7.5 DLL 特征串核对（**84/84 命中**）
```
python mods\HealthCooldownLine\研究-僵尸CD\_verify_dll_owner.py
看大小: 103936 B  md5=9c499253507d04f5ab60e134e1fa390a
FAILED = none  -> 全部 84 项特征串命中（另有 8 个僵尸按设计走通用通道、DLL 内无专属分支）
```
覆盖：框架 20 项（`TryCollectZombieCdLines`/`CollectZombieCdLinesA/B`/`IsZombieCdHost`/`ZombieField`/`showZombieCd`/`feature_zombiecd`/`ModZombieCdCheckBox`/`TowerDefenseSkeleton`…）+ A 批 5 个实现符号 + B 批 5 个实现符号 + **24 个中文文案**（含 `爆破/自爆/自燃/磁力/复活/无敌/召唤/偷取/治疗/变大/投掷/吟唱/蓄力/起身/睡眠/隐身/已开启/就绪/施法/歌唱/吹飞`）+ 5 个 TimerLabel 键名 + **23 个有分支僵尸的 `config.name`**。
> 方法学提醒（zombie-dev-a 实测踩坑）：查中文特征串**必须按字节搜**（`data.count("字".encode("utf-16-le"))`），
> 用 `Encoding.Unicode.GetString(整文件).Contains(...)` 会因 `#US` 堆条目**奇数偏移**而**假阴性**。
> zombie-dev-a 的独立脚本 `研究-僵尸CD\_verify_dll_A.py` 也跑出 `FAILED = none`。

### 7.6 最终组装（去时间戳告警后再跑一次）

1. 游戏未运行时执行 `python mods\HealthCooldownLine\build_and_install.py --install` → `[compile] RC=0 / [compile] OK / [package] RC=0 / [install] … (149,299 B)`；
2. 用编译产物刷新 `Runtime\ModAssembly.dll`（**字节完全相同**：刷新前/后 md5 都是 `9c499253…`）→ 再跑一次 `--install`（重新打包 + 重新装机）；
3. 清理装机侧：`ModsCache\` 下本 Mod 的缓存目录（本次已不存在，脚本早前把旧缓存 `move` 成了 `.bak_<时间>`，无需再清）；删除 `%APPDATA%\…\Mods\mod_boot_journal.json`（删除前内容 `phase=ready / inProgress=false / recoveryRequired=false`，删除后确认 `Test-Path = False`）；
4. `enabled_mods.json` 复核：`healthcooldowndisplay` 在 `enabledIds` 与 `preferredOrder` 中均在位 ✅。

---

## 8. 装机后清理与回归自检

已按 Lead 要求执行：
- `--install` 前确认**游戏未运行**（`Get-Process` 无 `PlantsVsZombies/Godot` 进程）；
- 装机后清理 `%APPDATA%\Godot\app_userdata\植物大战僵尸杂交版\Mods\ModsCache\` 下本 Mod 的缓存目录（脚本会自动把旧缓存 `move` 成 `.bak_<时间>`，另外手工确认无残留）；
- 删除 `mod_boot_journal.json`，让用户下次启动干净加载。

**回归自检步骤（游戏内）**
1. 启动游戏 → 选项 → Mod 管理 → 血条·装填倒计时 → **配置**：确认「显示内容」分组里有「**显示僵尸冷却**」且默认**开**；
2. 进任意关卡 → 打开「战斗选项」→ 点「CD开关设置面板」→ 确认第 3 列有「显示僵尸冷却」勾选框；**勾掉后**僵尸血条上的僵尸专属行立刻消失，而植物/加农炮 CD 仍在（验证优先级）；
3. 逐僵尸抽验（建议顺序）：毁灭菇僵尸（`爆破`）、火爆辣椒僵尸（`自燃`）、磁场僵尸（`磁力`→`磁力 已开启`）、天使僵尸（`复活` + `无敌`）、黑客小鬼僵尸（`冷却` + `隐身`）、安眠术士僵尸（`吟唱`→`睡眠`）、骷髅伽刚特尔（骨堆 `复活` + 坑洞 `消失`）、三叶草气球僵尸（`吹飞`）、塞壬僵尸（`歌唱` + `施法`）；
   ⚠️ **抽验前先看 §8.3 的前置开关表** —— 有 4 个"通用通道已覆盖"的僵尸还依赖各自分类开关，不看会误报"没显示"；
4. 关闭/重开游戏 → 上一步勾掉的状态应保持（持久化验证）；
5. 若开着 `enableLog`，日志里应能看到 `开关[显示僵尸冷却] → 开/关；已落盘 showZombieCd=…`。

### 8.1 真机核数值的三条通用判定（先看这个，能挡掉 90% 误判）

> 详细逐僵尸「出现 → 期望曲线 → 消失 → 非缺陷现象 → 真缺陷信号」见 `研究-僵尸CD\B批-研究.md` §19（zombie-dev-b，纯文档不进包）。

1. **数字走的是「僵尸时间」不是墙钟**：所有计时都是 `+= delta * timeScale`（`timeScale` = 该僵尸自身时间倍率，受减速/冻结/暂停影响）⇒ **用秒表对墙钟会系统性偏差，不是 bug**；要核数值请看**同帧差值**。
2. **「只显示 ≥5 秒」开启时**，`total < 4.9` 的行从头到尾都不出现 —— 已知受影响的有：**黑客小鬼 `隐身 4.0s`**（B 批）与 **塞壬的 `治疗 1.0s`**（`Heal` 键走通用通道 ②，total=1.0）。其余显示项 total ≥ 5.0（`5.0 ≥ 4.9` 恰好通过）；天使的 `复活` 传 `total=0`，**不参与**该过滤。
3. 本批**单僵尸最多 2 行**（仅黑客小鬼僵尸；守护伽刚特尔/安眠术士等是互斥 1 行），行序 = 先加的在上面。通用通道 ② 另有 `MaxTimerLines = 3` 的限制（塞壬同时最多 3 个命名计时器）。

几个典型"看着像 bug 其实是设计"的现象：
- 云小鬼僵尸 `投掷`：投掷动画期间行会消失 1~2 秒后重新从 `8.0` 开始；
- 安眠术士僵尸：入场前摇与吟唱段**都叫「吟唱」**，所以 5.0→0 之后会再跳一次 5.0→0（是顺序不是卡住），且**同屏永远 1 行**；
- 骷髅伽刚特尔：`复活`（骨堆）与 `消失`（坑洞）**同屏并存是正常的**（两个不同实体）；**水上死亡不生成骨堆** ⇒ 没有该行；
- 守卫伽刚特尔 `蓄力`：多数情况**早于 5.0s 就消失**（回满所需 = `(hitpointsSave − hitpoints)/500` 秒）；
- 磁场僵尸（**A 批**，`TowerDefenseZombieMagnetic`）：`磁力 已开启` 之后**不会再变回倒计时**（源码里 `open` 无复位路径，见 §4.1）；
- 电磁拖车僵尸（**B 批**，`TowerDefenseZombieMagnetron`，翻译键 `TOWERDEFENSE_ZOMBIE_MAGNETRON_NAME`）：`磁力` 是**循环**的 —— `attackTimer`（`:68`）在 `:349-352` 累加到 10.0 触发 `BeginShooting`，而 `BeginShooting` 第一行（`:557-559`）就是 `attackTimer = 0.0`（另 `:398` / `:958` 换目标/状态切换也清零）⇒ 看到 `0 → 10.0` 反复是**正常**，别拿它去验证上一条"不回到倒计时"。两者的字段与语义是两套，验收时**别串批次**。
- **刚出场时数值不动**：A 批的累加都套了 `IsInsideComponentBattlefield`（农夫/船长是 `IsInsidePlantingArea`）⇒ 僵尸在屏幕外走的那段时间，倒计时**冻结在满值**，进场后才开始掉。
- **天使僵尸 `复活` 的三条"怪现象"**（都来自 `TowerDefenseZombieAngel.cs`）：
  - `spawnTimer` 的递减写在 `WalkProcessing`（`:275-290`）⇒ **只有行走时才掉**，站着啃植物时那行数值不动；
  - 归零后若场上没有可复活尸体（`HasDeathRecord(camp, requireAngelEligible:true)` 为假），`:288` 直接重置 3.0 再等一轮 ⇒ 会看到突然跳成 `3.0`；
  - `无敌` 的 `invincibleTimer` 只在 `X > GetMapCellPos((4,0)).X` 时递减（`:118`），越过那条线 `:123-126` 立刻清 `invincible` ⇒ 那行会**突然消失**（图鉴说"最左侧 3 列"、代码用第 5 列边界，与显示无关）。

### 8.2 总核对（建议一并做）

- **无重复行**：塞壬僵尸应只有 `施法` / `歌唱` 两类通用通道行，**不应**再出现僵尸分支写的同名行（8 个"通用通道已覆盖"的僵尸均如此）；
- **开关整体生效**：关掉「显示僵尸冷却」⇒ 23 个新增专属分支的行全部消失，植物/障碍物/加农炮 CD 不受影响；
- **A/B 互不影响**：`CollectZombieCdLinesA` 抛异常不会影响 B 批（两层 try/catch，各有"只报一次"标志）；
- **真缺陷信号（需要修）**：在场且未被控时数值卡死不动、数值超过该僵尸的总时长、骨堆在场却没有 `复活` 行（= `IsZombieCdHost` 没放行，属框架问题）。

### 8.3 前置开关表（**看不到 ≠ 回归，先对照这张表**）

| 僵尸 / 显示行 | 需要打开的开关 | 说明 |
|---|---|---|
| A 批 13 个新增分支（毁灭菇/火爆辣椒/火炬辣椒/寒冰菇冰车/玩偶匣/海妖/磁场/天使/催眠师/农夫/海盗船长/重生法师/巨化法师） | **仅**「显示僵尸冷却」 | 门控全在 `TryCollectZombieCdLines` 里，分支内部没有再按分类判 |
| B 批 10 个新增分支（云小鬼/黑客小鬼/骷髅伽刚特尔/基因炸弹/电磁拖车/安眠术士/时间术士/守卫伽刚特尔/快闪迪斯科/快闪舞者） | **仅**「显示僵尸冷却」 | 同上 |
| 三叶草气球 / 三叶草多彩气球（`吹飞`） | 「显示僵尸冷却」**+「角色计时器其他CD」**(`CatOn(4)`) | 走通用通道 ②（`character.timer`） |
| 鲨鱼（`咀嚼`） | 「显示僵尸冷却」**+「战斗辅助」**(`CatOn(7)`) | 走通用通道 ②.5（`ChomperComponent`） |
| 塞壬 / 守护术士 / 占卜术士 / 唤魔使 / 女巫 | 「显示僵尸冷却」**+「角色计时器其他CD」**(`CatOn(4)`) | 走通用通道 ② |
| 重生法师死后的坑洞（`消失`） | **+「障碍物消失」**(`CatOn(1)`) | 走坑洞通道，与本开关无关（关掉「显示僵尸冷却」它仍在） |
| 所有行 | **总开关「显示冷却倒计时」必须开** | 总开关关 ⇒ 一行都不显示（最高优先级） |

---

## 9. 最终组装结果（回填）

| 项 | 值 |
|---|---|
| 最终编译 | `[compile] RC=0`，**0 错误**（4 条既有警告：`SponsorQrInjector.cs:221/232` CS0162、`Entry.cs` 两个历史未使用字段 CS0169） |
| 编译产物 | `bin\Release\JTYHealthCooldownLine.dll` **103,936 B** md5 `9c499253507d04f5ab60e134e1fa390a` |
| 打包 | `dist\HealthCooldownLine.pmod` **149,540 B** md5 `198e421061830d152a6fe0bb84749b55` |
| 装机 | `%APPDATA%\…\Mods\HealthCooldownLine.pmod` 同 md5（**dist == 装机**，sha256 亦一致） |
| 包内 md5 | `mod.json` `f2be731ff6fb181959f567cbfb59a865`；`Runtime/ModAssembly.dll` `9c499253507d04f5ab60e134e1fa390a` |
| 四检 | `verify_build.py HealthCooldownLine` → **✅ 全部通过**（编译：DLL 新于源码 / 打包 dist 22:19 / 装机 sha256 一致 / 包内容 16 项一致） |
| 包校验 | `verify_pmod.py` → ok=8 / warn=1 / FAIL=1（FAIL 为过时的 `runtimeApiVersion=1` 规则，见 §7.3；warn 为共用赞助码 PNG） |
| 特征串 | 84/84 命中（`_verify_dll_owner.py`）；zombie-dev-a 的 `_verify_dll_A.py` 亦 `FAILED = none` |
| 装机前游戏状态 | 未运行 ✅ |
| 装机侧清理 | `ModsCache` 无本 Mod 残留；`mod_boot_journal.json` 已删除；`enabled_mods.json` 含 `healthcooldowndisplay` ✅ |

---

## 10. 交付文件

| 文件 | 说明 |
|---|---|
| `mods\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs` | 框架（partial/扩展点/助手/开关 UI/持久化/`TimerLabel` 中文化/`IsZombieCdHost`） |
| `mods\HealthCooldownLine\runtime_src\ZombieCdA.cs` | A 批 16 个僵尸（含框架示范段 4 个） |
| `mods\HealthCooldownLine\runtime_src\ZombieCdB.cs` | B 批 15 个僵尸 |
| `mods\HealthCooldownLine\mod.json` | 新增 `showZombieCd` 设置项；版本 1.26.0 + 更新历史 |
| `mods\HealthCooldownLine\研究-僵尸CD\00-框架与契约.md` | 契约（签名/助手/词表/可显示性判据/禁止事项），v1.1 |
| `mods\HealthCooldownLine\研究-僵尸CD\A批-研究.md` / `B批-研究.md` | 逐僵尸证据与研究结论 |
| `mods\HealthCooldownLine\研究-僵尸CD\_verify_dll_A.py` / `_verify_dll_owner.py` | DLL 特征串核对脚本（按字节搜 UTF-16LE，可复跑） |
| `mods\HealthCooldownLine\报告-僵尸CD.md` | 本报告 |
