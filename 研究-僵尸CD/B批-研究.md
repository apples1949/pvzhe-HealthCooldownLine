# B 批 · 15 个僵尸的 CD 研究（`ZombieCdB.cs` 依据）

> 责任人：`zombie-dev-b`（task-4）。事实源：`unpack\` = 0.32.0.0 实际源码 + 图鉴译文。
> 契约：`mods\HealthCooldownLine\研究-僵尸CD\00-框架与契约.md`（v1，cdmod-owner）。
> 实现：`mods\HealthCooldownLine\runtime_src\ZombieCdB.cs`（`partial void CollectZombieCdLinesB`）。
>
> **路线（每个僵尸都走完，不猜）**：① 图鉴中文介绍（`Translate.zh.translation`）→ ② 类名与组件/计时字段
> （`Asset\Anime\Character\Zombie\**\Scene\*.cs`、`Script\Component\**`、`Config\*.tres`）→ ③ 可显示性判定
> （能同时读到「剩余 + 总时长」才算可显示）→ ④ 写分支 → ⑤ 本文件逐条留证据。

---

## 0. 结论总表

| # | 中文名 | 类名（config.name） | 组件/字段 | 语义 | 显示文案 | 结论 |
|---|---|---|---|---|---|---|
| 1 | 云小鬼僵尸 | `TowerDefenseZombieImpCloud`（`ZombieImpCloud`） | `_shootTimer` + 常量 8.0 | 投掷地刺小鬼 CD | `投掷 5.2s` | ✅ 可显示 |
| 2 | 黑客小鬼僵尸 | `TowerDefenseZombieImphacker`（`ZombieImphacker`） | `_hackTimer`(Godot`Timer`,10s) / `_hackEffectRemaining`(4s) | 篡改卡槽 CD / 隐身剩余 | `冷却 7.4s`<br>`隐身 3.1s` | ✅ 可显示（2 行） |
| 3 | 骷髅伽刚特尔 | 骨堆 = `TowerDefenseSkeleton`（`Skeleton`） | `timer` / `time = 30.0` | 死后骨堆聚拢复活 | `复活 22.5s` | ✅ 可显示（框架 v1.1 已放行骨堆实体，见 §3） |
| 4 | 基因炸弹僵尸 | `TowerDefenseZombieJackGene`（`ZombieJackGene`） | `timer` / `time`(9~20 随机) / `explode` / `hasJackBox` | 自爆倒计时 | `爆破 12.3s` | ✅ 可显示 |
| 5 | 电磁拖车僵尸 | `TowerDefenseZombieMagnetron`（`ZombieMagnetron`） | `attackTimer` + `AttackInterval` 10.0 / `anchored` / `shooting` | 磁力拖拽 CD | `磁力 6.1s` | ✅ 可显示 |
| 6 | 安眠术士僵尸 | `TowerDefenseZombieSleepMage`（`ZombieSleepMage`） | `entryTimer`(5) / `chantTimer`(5) / `sleepTimer`(10) / `sleeping` / `spellSpent` | 入场前摇 / 吟唱 / 发射后睡眠 | `吟唱 3.4s`<br>`睡眠 7.8s` | ✅ 可显示（互斥，同屏 1 行） |
| 7 | 时间术士僵尸 | `TowerDefenseZombieTimeMage`（`ZombieTimeMage`） | `spellTimer` + 10.0 | 定时魔弹 CD | `冷却 8.9s` | ✅ 可显示 |
| 8 | 守卫伽刚特尔 | `TowerDefenseZombieGargantuarGD`（`ZombieGargantuarGD`） | `_chargeElapsed` + `ChargeDuration` 5.0 / `_charging` | 50% 血蓄力（限伤+回血）剩余 | `蓄力 2.7s` | ✅ 可显示 |
| 9 | 塞壬僵尸 | `TowerDefenseZombieSiren`（`ZombieSiren`） | `character.timer` 的 `Cast`12.0 / `Sing`5.0 | 歌唱 CD / 歌唱持续 | （通用通道②）`施法 11.4s`<br>`歌唱 4.2s` | ⚠️ 已由**通用通道②**显示，不重复写 |
| 10 | 守护术士僵尸 | `TowerDefenseZombieGuardMage`（`ZombieGuardMage`） | `character.timer` 的 `Cast`10.0 | 施法加盾 CD | （通用通道②）`施法 9.3s` | ⚠️ 已由通用通道②显示 |
| 11 | 占卜术士僵尸 | `TowerDefenseZombieDivineMage`（`ZombieDivineMage`） | `character.timer` 的 `Cast`12.0（另有 `_prophecyRemaining` 10s） | 占卜 CD | （通用通道②）`施法 11.1s` | ⚠️ 已由通用通道②显示 |
| 12 | 唤魔使僵尸 | `TowerDefenseZombieEvoker`（`ZombieEvoker`） | `character.timer` 的 `Cast`（定义 10.0，运行时 `Run("Cast",5.0)`） | 召唤魔魂/影子 CD | （通用通道②）`施法 4.6s` | ⚠️ 已由通用通道②显示 |
| 13 | 女巫僵尸 | `TowerDefenseZombieWitch`（`ZombieWitch`） | `character.timer` 的 `Throw`10.0 | 投毒药瓶 CD | （通用通道②）`投掷 8.1s` | ⚠️ 已由通用通道②显示 |
| 14 | 快闪迪斯科僵尸 | `TowerDefenseZombieDiscoFlash`（`ZombieDiscoFlash`） | `FlashStep._fellRemaining` / `fellDuration` 10.0 | 滑倒→起身倒计时 | `起身 6.2s` | ✅ 可显示 |
| 15 | 快闪舞者僵尸 | `TowerDefenseZombieBackupFlash`（`ZombieBackupFlash`） | `FlashStep._fellRemaining` / `fellDuration` 10.0 | 滑倒→起身倒计时 | `起身 6.2s` | ✅ 可显示 |

统计：**写分支 10 个**（云小鬼 / 黑客小鬼 / 骷髅伽刚特尔 / 基因炸弹 / 电磁拖车 / 安眠术士 / 时间术士 / 守卫伽刚特尔 / 快闪迪斯科 / 快闪舞者）
＋ **5 个已由通用通道②覆盖**（塞壬 / 守护术士 / 占卜术士 / 唤魔使 / 女巫；契约 §4 末尾 / §6 末行：不重复写）。
即：**15/15 全部可显示，零"不可显示"**。

> **v1.1 变更（Lead 2026-11-07 拍板，cdmod-owner 落地）**：
> ① 词表新增 `投掷 / 吟唱 / 蓄力 / 起身`（本文件 §1/§6/§8/§14/§15 的文案已同步为正式词）；
> ② `TimerLabel` 补 `Cast→施法 / Sing→歌唱 / Throw→投掷 / Clover→吹飞`（`Heal→治疗` 原本就有）
>    ⇒ §9~§13 的 5 个僵尸由"英文键名"变成中文，且**仍不产生重复行**；
> ③ `TryCollectZombieCdLines` 门控由 `IsZombieChar` 换成 `IsZombieCdHost(character, charName)`
>    （`TowerDefenseSkeleton` 骨堆、以及"类名含 Skeleton 且是 Gravestone"的派生类放行；普通墓碑不放行）
>    ⇒ §3 骷髅伽刚特尔的分支现在**真的会被调用到**；
> ④ 契约不变量 #1 改为"`character` 不一定是 `TowerDefenseZombie`"⇒ 本批代码全程反射、无强硬转（符合）。

### 图鉴文案来源（怎么取的，可复现）
0.32 的 `unpack\Asset\Translate\Translate.zh.translation` 是**明文 RSRC**（magic `RSRC`，非 0.31 的 zstd 帧），
内部字符串以 `\x00` 结尾、按 `[图鉴故事][中文名][简介][属性块]` 相邻排列 ⇒ 直接按 `\x00` 切分即可取到条目。
（旧脚本 `mods\WhiteCardCategories\tools\zh_texts.py` 假设 zstd 帧，在 0.32 上会报"没找到 zstd 帧"，本批改用直读。）

---

## 1. 云小鬼僵尸（`ZombieImpCloud` → `TowerDefenseZombieImpCloud`）

- **图鉴**：「小鬼僵尸的经典cosplay。」特点：①飞行，**每 8 秒**暂停行动、向下扔出一只地刺小鬼僵尸；②遇到空中植物时停下用云朵尖刺攻击。
- **源码**：`unpack\Asset\Anime\Character\Zombie\Chapter8\ImpCloud\Scene\TowerDefenseZombieImpCloud.cs`
  - `:8` `private const float SHOOT_INTERVAL = 8f;`
  - `:20` `private double _shootTimer;`（已累计秒数）
  - `:22` `private bool _isShooting;`（投掷动画期间计时暂停）
  - `:110-128` `BatchUpdate`：`:117-121` `if (_shootTimer < 8.0) { _shootTimer += delta * timeScale; return; }` → `:122` `_shootTimer = 0.0;`
- **可显示性**：剩余 = `8.0 − _shootTimer`，总 = `8.0`（常量）⇒ ✅ **可显示**。
- **实现**：`_shootTimer` 是 private，走 `ZombieField(t, "_shootTimer")` 反射；`_isShooting == true` 时不显示
  （此时源码本来就没在推进计时）。`AddTimer(..., total = 8.0)`。
- **文案**：`投掷 5.2s`（青蓝 `BusyColor`）。标签 `投掷` = 契约 §5.2 **v1.1 新增词**（Lead 2026-11-07 批准）。

## 2. 黑客小鬼僵尸（`ZombieImphacker` → `TowerDefenseZombieImphacker`）

- **图鉴**：①**每隔 10 秒**，把玩家卡槽中随机一张植物卡篡改为小鬼卡（或在传送带加一张），**随后隐身 4 秒**；②小鬼卡放置一次后恢复；③卡槽只剩一张时不生效。
- **源码**：`...\Challenge\Imphacker\Scene\TowerDefenseZombieImphacker.cs`
  - `:12` `private Timer _hackTimer;`（Godot `Timer` 节点，`:69` `GetNode<Timer>("HackTimer")`）
  - `:90-104` `BatchUpdate`：`:92-95` `if (!isHack && _hackTimer.IsStopped()) { _hackTimer.Start(10.0); }` ⇒ **冷却 10s**
  - `:192` 存档里就写的 `dictionary["hackCooldownRemaining"] = _hackTimer.TimeLeft`（官方口径：剩余 = `TimeLeft`）
  - `:14` `private double _hackEffectRemaining;`　`:172-175` `Hack()` 里 `_hackEffectRemaining = 4.0;` ⇒ **隐身 4s**
  - `:82-89` `_hackEffectRemaining = Math.Max(0, _hackEffectRemaining - delta * GetTimerTimeScale(true));`
  - `:96-103` 隐身期间 `instance.canBeCollection = false`（不可拾取）；`:170-171` `collisionFlags = 0`
- **可显示性**：两段都能读到「剩余 + 总」⇒ ✅ **可显示（2 行）**：
  - 篡改 CD：剩余 = `Timer.TimeLeft`（`IsStopped()` 时按 0 处理），总 = `10.0`
  - 隐身：剩余 = `_hackEffectRemaining`，总 = `4.0`
- **实现**：`_hackTimer` 不是 double 字段，`ZombieNum` 不适用 ⇒ 写了专用助手 `ZbBTimerLeft`（反射取字段 → 强转 `Godot.Timer` → `IsStopped()` / `TimeLeft`）。
- **文案**：`冷却 7.4s`（青蓝）、`隐身 3.1s`（绿 `ReadyColor`，表示"隐身生效中"）。
  注：`4.0 < 4.9` ⇒ 若用户打开「只显示 ≥5 秒」开关，这一行会整行隐藏（契约 §5.4 的既定行为，不是 bug）；总时长仍按真实值 4.0 传。

## 3. 骷髅伽刚特尔（`Skeleton` / 骨堆 = `TowerDefenseSkeleton`）★ 重点/易错

- **图鉴**：本体 3000 血；血量低于一半会丢出骷髅小鬼；**亡语：在陆地上首次死亡时散落成骨堆：血量 3000，会在 30 秒后聚拢复活**；复活后失去丢小鬼能力。
- **关键事实：30 秒计时不挂在 Skeletuar 上，挂在"骨堆"实体上**。证据链：
  1. `...\Challenge\Skeletuar\Scene\TowerDefenseZombieSkeletuar.cs:104-162` `AnimeCompleted`：死亡动画播完 →
     `:130` `GetPacketConfig("Skeleton")` → `:141/:145` 生成该 packet → `:158` `Destroy();`
     （`over` 字段只标记"已复活过"，`:8` `public bool over;`，本身没有计时）
  2. `packet "Skeleton"` 的配置是 `unpack\Asset\Anime\Character\GraveStone\Skeleton\Packet\Skeleton.tres`
     （`saveKey = "Skeleton"`）→ `Config\TowerDefenseSkeleton.tres`：`hitpoints = 3000.0`、`packetCooldown = 30.0`
  3. 其类为 `unpack\Asset\Anime\Character\GraveStone\Skeleton\Scene\TowerDefenseSkeleton.cs`：
     - `:6` `public partial class TowerDefenseSkeleton : TowerDefenseGravestone`
     - `:12` `public double timer;`　`:14` `public double time = 30.0;`
     - `:27-38` `BatchUpdate`：`if (timer < time) { timer += delta * timeScale; return; }`
     - `:49-56` 计时满 → `GetPacketConfig("ZombieSkeletuar")` 生成 `TowerDefenseZombieSkeletuar` 并 `zombie.over = true;`（= 聚拢复活）
- **可显示性**：剩余 = `time − timer`，总 = `30.0` ⇒ **数据层面 ✅ 可显示**。
- **⚠️ 门控已放宽（v1.1，cdmod-owner 2026-11-07）**：`TryCollectZombieCdLines` 的判定从 `IsZombieChar`
  换成 `IsZombieCdHost(character, charName)` —— `TowerDefenseZombie` 子类放行；
  `charName == "Skeleton"` 或类名 `TowerDefenseSkeleton` 放行；类名含 `Skeleton` 且 `character is TowerDefenseGravestone` 放行；
  **普通墓碑不放行**（不按 Gravestone 基类一刀切）。⇒ 本分支现在**真的会被调用到**。
- **处理**：代码里已就绪（身份判据 `charName == "Skeleton"` 或类名 `TowerDefenseSkeleton`，读 `timer`/`time`/`over`），
  v1.1 放行后按 §17 重新编译自检，`IsZombieCdHost` 已在产 DLL 元数据中确认存在。
  同时注意契约不变量 #1 已改为"`character` 不一定是 `TowerDefenseZombie`"⇒ 本批代码全程反射、无强硬转。
- **文案**：`复活 22.5s`（青蓝，`total = time = 30.0`）。

## 4. 基因炸弹僵尸（`ZombieJackGene` → `TowerDefenseZombieJackGene`）

- **图鉴**：佩戴"基因炸弹"；出场一段时间后打开盒子自爆，对 3×3 内敌方单位触发基因突变（植物→豌豆射手、僵尸→普通僵尸，不触发亡语）；弱点：磁力。
- **源码**：`...\Chapter9\JackGene\Scene\TowerDefenseZombieJackGene.cs`
  - `:28` `public double timer;`　`:30` `public double time;`
  - `:85` `_Ready`：`time = GD.RandRange(9.0, 20.0);`（**总时长每只随机 9~20 秒**）
  - `:134-157` `BatchUpdate`：`:146-150` `if (timer < time) { timer += delta * timeScale; return; }` → `:151` `explode = true;` → `:155` `SendStateEvent("ToBomb")`
  - `:24` `public bool hasJackBox = true;`（`:192-199` 护具 `JackGenebox` 被打掉时置 false）
- **可显示性**：剩余 = `time − timer`，总 = `time`（实例内稳定）⇒ ✅ **可显示**；`explode == true` 或 `hasJackBox == false` 时不显示。
- **文案**：`爆破 12.3s`（青蓝，`total = time`）。total 传动态真实值，参与「≥5 秒」过滤是安全的（9~20 恒 ≥4.9）。

## 5. 电磁拖车僵尸（`ZombieMagnetron` → `TowerDefenseZombieMagnetron`）

- **图鉴**：①入场后若本行有正常高度植物（排除低矮/飞行），则暂停移动，**每隔 10 秒**对本行最靠近自己的正常高度植物发射磁力射线，把该格除飞行植物外所有植物向前拖一格；②前方有连续地面植物时一次性全部拉动；弱点：爆胎。
- **源码**：`...\Chapter9\Magnetron\Scene\TowerDefenseZombieMagnetron.cs`
  - `:44` `public const double AttackInterval = 10.0;`
  - `:68` `public double attackTimer;`　`:72` `public bool anchored;`　`:78` `public bool shooting;`
  - `:339-355` `ProcessAnchored`：`else if (!shooting && CanRunAbility()) { attackTimer += delta * timeScale; if (attackTimer >= 10.0) BeginShooting(selectedGrid); }`
  - `:557-559` `BeginShooting`：`attackTimer = 0.0;`（发射即重置）
  - `:392-401` `SetStopColumn` 变化时 `attackTimer = 0.0;`
  - `:46` `public const double PullDuration = 2.0;`　`:80` `public double pullRemaining;`　`:618` `pullRemaining = 2.0;`（拖拽动作时长）
- **可显示性**：剩余 = `10.0 − attackTimer`，总 = `10.0` ⇒ ✅ **可显示**（只在 `anchored == true && shooting == false` 时显示——不锚定时计时器不在推进，显示会误导）。
- **不显示**：`pullRemaining`（拖拽动作 2s，`total = 2 < 4.9` 会被「≥5 秒」过滤，且语义上是动作时长不是 CD）。
- **文案**：`磁力 6.1s`（青蓝，`total = 10.0`）。

## 6. 安眠术士僵尸（`ZombieSleepMage` → `TowerDefenseZombieSleepMage`）

- **图鉴**：免疫减速/冻结/普通黄油；①**入场后 5 秒**后原地停下开始吟唱，**5 秒**后向前发射一枚催眠魔弹，然后继续前进；③命中植物造成 60 魔法伤害 + 30 秒睡眠；④**发射魔弹后原地睡着 10 秒**后醒来，+1000 血并加速。提示：咖啡豆可对抗。
- **源码**：`...\Chapter10\SleepMage\Scene\TowerDefenseZombieSleepMage.cs`
  - `:9` `EntryCastDelay = 5.0`；`:37` `public double entryTimer;`；`:164-179` `TryStartCast`：`:173` `entryTimer += delta * timeScale;` `:174` `if (!(entryTimer < 5.0)) SendStateEvent("ToMageCast")`
  - `:35` `public double chantTimer;`；`:248-265` `CastProcessing`：`:258-263` `chantTimer += delta * timeScale; if (chantTimer >= 5.0) { shooting = true; ... }`
  - `:11` `SleepDuration = 10.0`；`:45` `public double sleepTimer;`；`:378-389` `ProcessSleep`：`:383-387` `sleepTimer += delta * timeScale; if (!(sleepTimer < 10.0)) WakeUpFromSleep();`
  - `:43` `public bool sleeping;`（`:336-355` `EnterSleep` 置真；`:400-416` `WakeUpFromSleep` 置假并把 `sleepTimer = 0`）
  - `:29` `public bool spellSpent;`（`:282` 发射后置真 —— **只会发动一次魔法**）
- **可显示性**：
  - 「发射 CD」：**没有循环发射 CD**（图鉴与 `spellSpent` 都表明一次性）⇒ 可显示的是它的**准备阶段**（前摇 5s / 吟唱 5s）。
  - 睡眠 10s：✅ 可显示。
  - 三段互斥（前摇→吟唱→睡眠），同屏最多 1 行，符合"单僵尸 ≤2 行"。
- **实现**：`sleeping` → `睡眠 (10 − sleepTimer)`；否则若 `chantTimer > 0` → `吟唱 (5 − chantTimer)`；否则若 `!spellSpent` → `吟唱 (5 − entryTimer)`。
- **文案**：`吟唱 3.4s`（青蓝，`total = 5.0`）/ `睡眠 7.8s`（青蓝，`total = 10.0`）。
  标签 `吟唱` = 契约 §5.2 **v1.1 新增词**（`施法` 一词留给通用通道的 `Cast`，两者不混用）。

## 7. 时间术士僵尸（`ZombieTimeMage` → `TowerDefenseZombieTimeMage`）

- **图鉴**：免疫减速/冻结/普通黄油；②**入场后每 10 秒**向前发射一枚定时魔弹（150 魔法伤害 + 5 秒定身，并使 3×3 内植物减速 5 秒）；亡语：首次死亡时时间回溯，在本行最右侧生成全新时间术士（新的不带亡语）。
- **源码**：`...\Chapter10\TimeMage\Scene\TowerDefenseZombieTimeMage.cs`
  - `:28` `public double spellTimer;`
  - `:153-168` `SpellProcessing`：`:162-166` `spellTimer += delta * timeScale; if (spellTimer >= 10.0) SendStateEvent("ToMageCast");`
  - `:199-213` `AnimeEvent("fire")`：`:209` `spellTimer = 0.0;` ⇒ 发射即重置，循环 10s
  - 亡语（`:293-380` `DieEntered`/`CompleteRewind`）是**瞬发无持续计时**，不作为 CD 显示
- **可显示性**：剩余 = `10.0 − spellTimer`，总 = `10.0` ⇒ ✅ **可显示**。
- **文案**：`冷却 8.9s`（青蓝，`total = 10.0`）。标签用通用兜底词 `冷却`（定时魔弹，词表无需新增）。

## 8. 守卫伽刚特尔（`ZombieGargantuarGD` → `TowerDefenseZombieGargantuarGD`）

- **图鉴**：血量 2000（一类）+5000；速度比普通巨人更慢；②本体血量一旦低于 50% **立即暂停行动、原地进入蓄力**：限伤 100、每秒回 500 血、**最多持续 5 秒**；回满立即退出；③每次退出立即放一次 3×3 强力锤击；④强力锤击后需血量回到 50% 以上再次跌破才重新触发。
- **源码**：`...\Chapter10\GargantuarGD\Scene\TowerDefenseZombieGargantuarGD.cs`
  - `:21` `private const double ChargeDuration = 5.0;`
  - `:37` `private bool _charging;`　`:41` `private double _chargeElapsed;`
  - `:142-149` `ChargeEntered`：`_charging = true; _chargeElapsed = 0.0;`
  - `:151-171` `ChargeProcessing`：`:156-157` `num = Clamp(delta*timeScale, 0, 5.0 − _chargeElapsed); _chargeElapsed += num;`
    `:165` `if (instance.hitpoints >= hitpointsSave || _chargeElapsed + 1e-9 >= 5.0) { _chargeEnding = true; ... }`
  - `:103-122` `UpdateChargeTrigger`：`:109` 阈值 `hitpointsSave * 0.5`，`:117-120` 跌破即 `SendStateEvent("ToCharge")`
- **可显示性**：剩余 = `5.0 − _chargeElapsed`，总 = `5.0` ⇒ ✅ **可显示**（只在 `_charging == true` 时）。
- **注意**：触发阈值（50% 血）是**状态条件不是计时**，不做成倒计时行（契约 §6：静态/条件值不假装倒计时）。
- **文案**：`蓄力 2.7s`（青蓝，`total = 5.0`）。标签 `蓄力` = 契约 §5.2 **v1.1 新增词**。

## 9. 塞壬僵尸（`ZombieSiren` → `TowerDefenseZombieSiren`）— 已由通用通道②显示

- **图鉴**：免疫大多数负面效果、免疫常规海草拖拽；②**入场后每 12 秒歌唱一次，歌唱持续 5 秒**，期间无法移动；③友方僵尸 1.5 倍加速、友方魔法僵尸额外每秒回 2% 最大生命（最低 40）；④范围内敌方眩晕至歌唱结束。
- **源码**：`...\Chapter10\Siren\Scene\TowerDefenseZombieSiren.cs`
  - `:10` `SingInterval = 12.0`　`:12` `SingDuration = 5.0`
  - `:20` `private CharacterTimerComponent _timerComponent;`　`:49` `componentManager.GetRuntime<CharacterTimerComponent>("character.timer")`
  - `:84`/`:216` `_timerComponent.Run("Cast", 12.0);`（歌唱 CD）　`:340` `_timerComponent.Run("Sing", 5.0);`（歌唱持续）　`:341` `Run("Heal", 1.0)`
  - `:30-41` `SongRemaining => Max(0, 5.0 − timerCurrent["Sing"])`
  - 计时器定义：`...\Siren\Scene\TowerDefenseZombieSirenTimerComponentDefinition.tres`
    `timerDictionary = { "Cast": 12.0, "Heal": 1.0, "Sing": 5.0 }`，`InstanceId = "character.timer"`
- **可显示性**：两段都可得（`timerWaitTime − timerCurrent`）⇒ 可显示。
- **⚠️ 但已由通用通道②显示**：`HealthCooldownLineEntry.cs:1781-1867`（开关 `_showOtherTimer`，默认 `true`，`:546`）
  会遍历 `character.timer` 的**运行中**计时器逐行输出，`Cast`/`Sing` 都会被显示。
  ⇒ 按契约 §4 末尾 / §6 末行：**不重复写**（写了就是同屏两行重复）。
- **✓ 已落地（v1.1）**：cdmod-owner 已给 `TimerLabel` 补 `Cast→施法`、`Sing→歌唱`、`Throw→投掷`、`Clover→吹飞`（`Heal→治疗` 原本就有）
  ⇒ 塞壬现在显示 **`施法 11.4s`（歌唱 CD）+ `歌唱 4.2s`（歌唱时长）**，中文且**不产生重复行**。「歌唱 CD + 歌唱时长」需求由此 100% 满足。

## 10. 守护术士僵尸（`ZombieGuardMage` → `TowerDefenseZombieGuardMage`）— 已由通用通道②显示

- **图鉴**：①**入场后每 10 秒**施法，为 3×3 内除自己外同阵营非 BOSS 僵尸加 300 点护盾；②自身无法获得任何护盾。
- **源码**：`...\Chapter10\GuardMage\Scene\TowerDefenseZombieGuardMage.cs`
  - `:9` `CastInterval = 10.0`；`:36` `GetRuntime<CharacterTimerComponent>("character.timer")`
  - `:92`/`:145` `Run("Cast", 10.0)`
  - 定义：`TowerDefenseZombieGuardMageTimerComponentDefinition.tres` → `timerDictionary = { "Cast": 10.0 }`，`InstanceId = "character.timer"`
- **可显示性**：可显示（剩余 = 10 − current）⇒ 但**已由通用通道②显示**（v1.1 起显示为 `施法 9.3s`）⇒ 不重复写。

## 11. 占卜术士僵尸（`ZombieDivineMage` → `TowerDefenseZombieDivineMage`）— 已由通用通道②显示

- **图鉴**：持有水晶球时，**入场后每 12 秒占卜一次**，使卡槽中随机一张卡进入"预言"状态**持续 10 秒**；水晶球被击破后无法继续占卜，已有预言不会立刻消失。
- **源码**：`...\Chapter10\DivineMage\Scene\TowerDefenseZombieDivineMage.cs`
  - `:10` `CastInterval = 12.0`；`:61` `GetRuntime<CharacterTimerComponent>("character.timer")`
  - `:125`/`:178` `Run("Cast", 12.0)`（占卜 CD）
  - `:39` `private double _prophecyRemaining;`；`:217` `_prophecyRemaining = 10.0;`；`:107` 每帧递减
  - 定义：`TowerDefenseZombieDivineMageTimerComponentDefinition.tres` → `{ "Cast": 12.0 }`，`InstanceId = "character.timer"`
- **可显示性**：占卜 CD 可显示，但**已由通用通道②显示**（v1.1 起显示为 `施法 11.1s`）⇒ 不重复写。
- **`_prophecyRemaining`（10s）不显示**，理由：它是"玩家卡槽里那张卡的预言状态剩余"，属于**卡面效果**，
  不是该僵尸自身的行动 CD；且词表里没有对应标签。若将来要做，应挂在卡槽/卡面 HUD 上而不是僵尸血条行。

## 12. 唤魔使僵尸（`ZombieEvoker` → `TowerDefenseZombieEvoker`）— 已由通用通道②显示

- **图鉴**：免疫控制；②**入场后每 5 秒**展开魔法卷轴，在前方一格召唤一个魔魂（**最多同时 3 个**）；
  ③集齐 3 个后每 5 秒在本格召唤唤魔使影子；亡语：消灭自己召唤的所有魔魂与影子。
- **源码**：`...\Chapter10\Evoker\Scene\TowerDefenseZombieEvoker.cs`
  - `:10` `CastInterval = 5.0`；`:12` `MaxSouls = 3`
  - `:81` `GetRuntime<CharacterTimerComponent>("character.timer")`；`:215`/`:270` `Run("Cast", 5.0)`
  - `:286-306` `AnimeEvent("magic")`：`:297` `if (HasFullSouls()) SummonShadow(); else SpawnSoul();`
  - 定义：`TowerDefenseZombieEvokerTimerComponentDefinition.tres` → `{ "Cast": 10.0 }`，`InstanceId = "character.timer"`
    ⚠️ 注意：定义里写 10.0，但**代码每次 `Run("Cast", 5.0)` 会把 `timerWaitTime` 改成 5.0**（`CharacterTimerComponent.Run`，`:266-285`），
    所以运行期总时长是 **5.0**，通用通道读的是 live 值 ⇒ 显示正确（v1.1 起显示为 `施法 4.6s`）。
- **可显示性**：可显示（CD 5s）⇒ **已由通用通道②显示** ⇒ 不重复写。
- **魔魂数量（`SoulCount` 0~3，`:66-73`）**：是"状态/计数"不是时间 CD；词表里没有对应标签 ⇒ 本批不显示
  （若产品要，建议 owner 在词表加 `魔魂` 并给 A/B 批统一说明；否则不该由我单方面新增词）。

## 13. 女巫僵尸（`ZombieWitch` → `TowerDefenseZombieWitch`）— 已由通用通道②显示

- **图鉴**：飞行/小型/魔法僵尸；①每次撞击飞行植物后退半格；②免疫吹飞但会被吹退；③**入场后每 10 秒投掷一枚毒药瓶**
  （当前格有地面植物→毒雾；否则留毒药瓶障碍：耐久 500，被摧毁生成毒雾，僵尸碰到则回 500 血并 1.25 倍速）。
- **源码**：`...\Chapter10\Witch\Scene\TowerDefenseZombieWitch.cs`
  - `:9` `ThrowInterval = 10.0`；`:46` `GetRuntime<CharacterTimerComponent>("character.timer")`
  - `:107`/`:178` `Run("Throw", 10.0)`
  - 定义：`TowerDefenseZombieWitchTimerComponentDefinition.tres` → `{ "Throw": 10.0 }`，`InstanceId = "character.timer"`
- **可显示性**：可显示（`Throw` 10s）⇒ **已由通用通道②显示**（v1.1 起显示为 `投掷 8.1s`）⇒ 不重复写。
- 另有 `_bottleFlightRemaining`（`:34`，`:221` `= 0.65`）是**投掷物飞行时长**（0.65s，瞬发动画），不作 CD。

## 14. 快闪迪斯科僵尸（`ZombieDiscoFlash` → `TowerDefenseZombieDiscoFlash`）

- **图鉴**：①入场后召唤 **4 只**快闪舞者僵尸；②附近有敌方子弹时发动闪身步躲避；③躲避有概率滑倒：**无法行动，10 秒后起身**；
  ④初始滑倒概率 5%，每次成功闪身 +5%，最高 100%，滑倒后重置 5%；⑤自身未滑倒时，自己召唤的舞者滑倒 → 立刻在该格额外召唤 1 只新舞者。
- **源码**：
  - 主体 `...\Challenge\DiscoFlash\Scene\TowerDefenseZombieDiscoFlash.cs`
    - `:61` `public FlashStepComponent FlashStep { get; private set; }`　`:195` `componentManager.GetRuntime<FlashStepComponent>()`
    - `:545-556` 动画事件 `spawn` → `SpawnDancer()`（**动画事件驱动，不是定时 CD**）
    - `:248-257` `OnFlashStepFell`（自己摔倒 → 全部属下滑倒）；`:259-304` `OnDancerFell`（舞者摔倒 → 补一只）
  - 组件 `unpack\Script\Component\TowerDefense\Character\FlashStepComponent\FlashStepComponent.cs`
    - `:68` `private double _fellRemaining;`
    - `:427-440` `Fall()`：`:436` `_fellRemaining = Definition.fellDuration;` `:437` `SendStateEvent(FellEvent);`
    - `:483-498` `OnPhysicsProcess`：`:490-497` `if (IsFallen) { _fellRemaining = Max(0, _fellRemaining − delta * _parent.timeScale); if (_fellRemaining <= 0 && state == fell_idle) SendStateEvent(StandUpEvent); }`
    - `:96-107` `public bool IsFallen`；`:109-123` `IsFlashing`；`:92` `public int FallChance { get; private set; } = 5;`（`:410-414` 每次成功闪身 +5）
    - `:398-425` `TryFlash()`（**事件触发、瞬时**，无持续计时）
  - 定义 `...\FlashStepComponent\FlashStepComponentDefinition.cs:11` `public double fellDuration { get; set; } = 10.0;`
    ComponentSet 里是内联实例、**未覆盖** `fellDuration` ⇒ 运行期 10.0：
    `...\DiscoFlash\Scene\TowerDefenseZombieDiscoFlashComponentSet.tres:11-13`（`ComponentTypeId="FlashStepComponent"`、`InstanceId="character.flash_step"`）
- **可显示性**：滑倒起身：剩余 = `_fellRemaining`，总 = `Definition.fellDuration` ⇒ ✅ **可显示**（仅滑倒中）。
  - 闪身步本身：事件触发瞬时完成 ⇒ ❌ 不作为 CD（契约 §6）。
  - `FallChance`：概率百分比、不是时间 ⇒ 本批不显示（词表无对应标签）。
- **实现**：组件实例经属性反射取（`ZbBProp(character, "FlashStep")`，不引用敌方脚本类）；
  剩余读 private `_fellRemaining`；总时长读 `ComponentDefinition.fellDuration`（属性反射，取不到回落 10.0）。
- **文案**：`起身 6.2s`（青蓝，`total = 10.0`）。标签 `起身` = 契约 §5.2 **v1.1 新增词**。

## 15. 快闪舞者僵尸（`ZombieBackupFlash` → `TowerDefenseZombieBackupFlash`）

- **图鉴**：可由快闪迪斯科僵尸 4 只一组召唤；②附近有子弹时闪身步躲避；③躲避有概率滑倒：**无法行动，10 秒后起身**（5% 起、每次 +5%、最高 100%、滑倒后重置）；⑤召唤自己的迪斯科滑倒时，若自身未滑倒则立即滑倒。
- **源码**：`...\Challenge\BackupFlash\Scene\TowerDefenseZombieBackupFlash.cs`
  - `:25` `public FlashStepComponent FlashStep { get; private set; }`　`:105` `componentManager.GetRuntime<FlashStepComponent>()`
  - `:304-310` `OnFlashStepFell()` → 通知 leader（`_flashLeader.OnDancerFell(this)`）
  - ComponentSet：`...\BackupFlash\Scene\TowerDefenseZombieBackupFlashComponentSet.tres:11-13`，同样 `character.flash_step` / 未覆盖 `fellDuration`
- **可显示性/实现/文案**：与 §14 完全相同（同一组件、同一字段）⇒ ✅ `起身 6.2s`（`total = 10.0`），代码里与迪斯科共用同一分支。

---

## 16. 三项决策：**全部已落地**（v1.1，Lead 2026-11-07 拍板 / cdmod-owner 实施）

| # | 事项 | 状态 |
|---|---|---|
| 1 | 标签新词 `投掷`（云小鬼）/ `吟唱`（安眠术士）/ `蓄力`（守卫伽刚特尔）/ `起身`（快闪系） | ✅ 已批准并写进契约 §5.2 词表 v1.1；`ZombieCdB.cs` 顶部 `ZbBLbl*` 已换成正式词，编译 + DLL 核对通过（`投掷`/`吟唱`/`蓄力`/`起身` 全 True） |
| 2 | `TimerLabel` 补中文映射（owner 文件） | ✅ 已补 `Cast→施法`、`Sing→歌唱`、`Throw→投掷`、`Clover→吹飞`（`Heal→治疗` 原本就有）⇒ §9~§13 的 5 个僵尸现在显示中文，且不产生重复行 |
| 3 | 骷髅伽刚特尔的框架门控 | ✅ 已放宽：`IsZombieCdHost(character, charName)` 放行「僵尸类 / `charName=="Skeleton"` / 类名 `TowerDefenseSkeleton` / 类名含 `Skeleton` 且 `is TowerDefenseGravestone`」，**普通墓碑不放行**；骨堆分支现在会被调用到，显示 `复活 22.5s` |

配套变更：**契约不变量 #1 改为「`character` 不一定是 `TowerDefenseZombie`」** ⇒
本批代码全程反射取字段、**无任何 `(TowerDefenseZombie)character` 强硬转**（已复核）。

---

## 17. 编译与自检证据（本批 · v1.1 定稿）

```
python mods\HealthCooldownLine\_build_serial.py   →  [compile] RC=0 / [compile] OK（0 错误，4 条历史 warning 与本批无关）
产物 mods\HealthCooldownLine\runtime_src\bin\Release\JTYHealthCooldownLine.dll
长度 103936 B，LastWriteTime 2026-11-07 22:12:30，
md5 = 9c499253507d04f5ab60e134e1fa390a
（该产物含 A 批 ZombieCdA.cs + 本批 + owner 的 TimerLabel/门控改动；owner 组装时会重编，md5 以他的为准）
```

DLL 特征串核对（`Encoding.Unicode` = 字面量在 #US 堆里是 UTF-16；类型/成员名在 #Strings 堆里是 UTF-8）：

- 中文标签 10 个（UTF-16）：`投掷` / `吟唱` / `蓄力` / `起身` / `冷却` / `睡眠` / `爆破` / `磁力` / `复活` / `隐身` ⇒ 全部 **True**
- `TimerLabel` 中文映射（UTF-16）：`施法` / `歌唱` / `投掷` / `吹飞` / `治疗` ⇒ 全部 **True**
- 框架门控（UTF-8 元数据）：`IsZombieCdHost` ⇒ **True**（证明放宽确实进了这份产物）
- 本批符号：`ZbBIsZombie` / `ZbBTimerLeft` / `ZbBProp` / `ZbBDoubleProp` / `CollectZombieCdLinesB` ⇒ True
- 反射字段名（UTF-16）：`_shootTimer` / `_isShooting` / `_hackTimer` / `_hackEffectRemaining` / `hasJackBox` / `explode` /
  `attackTimer` / `anchored` / `shooting` / `spellTimer` / `_chargeElapsed` / `_charging` / `sleeping` / `sleepTimer` /
  `chantTimer` / `entryTimer` / `spellSpent` / `fellDuration` / `_fellRemaining` / `FlashStep` ⇒ 全部 **True**
- 9 个 config 名 + 骨堆配置名（UTF-16）：`ZombieImpCloud` / `ZombieImphacker` / `ZombieJackGene` / `ZombieMagnetron` /
  `ZombieSleepMage` / `ZombieTimeMage` / `ZombieGargantuarGD` / `ZombieDiscoFlash` / `ZombieBackupFlash` / `Skeleton` ⇒ 全部 **True**

**文件冻结**：`ZombieCdB.cs` 定稿于 2026-11-07 22:12:10（此后仅本 md 有改动），已向 cdmod-owner 报「B 批冻结」。

---

## 18. 风险与"没做的事"（如实登记 · v1.1）

| 项 | 说明 |
|---|---|
| `隐身 4.0s` 会被"只显示 ≥5 秒"整行过滤 | 契约 §5.4 既定行为（该开关默认关闭、由用户自选）；total 按真实 4.0 传，**不造假**。owner 保留此口径 |
| 未做运行期实测 | 本批只做了「编译 + DLL 特征串」静态自检；游戏内实际显示由 owner 统一 `--install` + 真机抽验（pvzhe-mod-verify）。我配合给出预期值算法 |
| 骨堆分支依赖 owner 的门控实现 | 已在产 DLL 里核对 `IsZombieCdHost` 存在；若将来 owner 再收紧，骨堆行会静默消失（不影响其他 9 个僵尸） |
| 快闪系只显示滑倒起身 | 闪身步（`TryFlash`）是瞬时事件、滑倒概率是百分比，两者都不符合"时间 CD"判据；若产品要显示概率，需先加词表 |
| 唤魔使魔魂数量（0~3）未显示 | 是"计数/状态"不是时间 CD，且词表无对应标签；未擅自新增词（遵守契约 §5.2） |
| 占卜术士 `_prophecyRemaining`(10s) 未显示 | 属于"玩家卡槽那张卡的预言剩余"，不是僵尸自身 CD；应挂卡面 HUD |

---

## 19. 真机抽验「预期值算法」（给 cdmod-owner 的 §8 五步验收用）

> 7 个代表僵尸：云小鬼 / 黑客小鬼 / 安眠术士 / 电磁拖车 / 守卫伽刚特尔 / 骷髅伽刚特尔 / 快闪系。
> 每条给 **出现条件 → 期望数值曲线 → 消失条件 → 常见"非缺陷"现象 → 真缺陷信号**。

### 19.0 三条通用判定规则（先看这个，能挡掉 90% 的误判）

1. **数字走的是「僵尸时间」不是墙钟**：所有累加都是 `+= delta * timeScale`（`timeScale` = 该僵尸自身时间倍率，
   受减速/冻结/暂停/狂暴影响）⇒ 用秒表对墙钟会**系统性偏差**，这不是 bug。要核数值请用「同一行相邻帧的差值」而不是墙钟。
2. **「只显示 ≥5 秒」开关**（默认关）：`total < 4.9` 的行**整行**从不出现。本批只有 **`隐身 4.0s`** 会受影响（其余 total 都 ≥5.0，`5.0 ≥ 4.9` 恰好通过）。
3. **行数**：本批单僵尸最多 2 行（仅黑客小鬼），`MaxTotalLines=6` 不会被挤爆；行序 = 先加的在上。

### 19.1 云小鬼僵尸（`投掷`）

| 项 | 内容 |
|---|---|
| 出现条件 | 进入战场（`IsInsideComponentBattlefield`）且 `_isShooting == false`、`CanRunAbility() == true` |
| 期望曲线 | `投掷 8.0s` → 单调递减 → 到 `0.0` 附近后**行消失 1~2 秒**（投掷动画期间 `_isShooting=true`）→ 重新 `投掷 8.0s` |
| 消失条件 | 投掷动画播放期间；死亡；离开战场；被冻结/催眠（`CanRunAbility()` 假） |
| 非缺陷现象 | 进战场后**第一行不是 8.0**（`_shootTimer` 从入场就开始累计，显示时为 8−已过）；遇到空中植物改用云朵尖刺攻击时倒计时照走 |
| 真缺陷信号 | 数值**卡死不动**（≠ 暂停）；数值 >8.0；一行都不出现但僵尸已在场且未被控 |

### 19.2 黑客小鬼僵尸（`冷却` + `隐身`，2 行）

| 项 | 内容 |
|---|---|
| 出现条件 | `冷却`：`_hackTimer` 已在跑（进场后 `BatchUpdate` 里 `Start(10.0)`）；`隐身`：`_hackEffectRemaining > 0` |
| 期望曲线 | 篡改动画 `hack` 事件瞬间 → **`隐身 4.0s` 出现** + `冷却 10.0s` 已在跑（两行同时存在约 4 秒）→ `隐身` 先归零消失 → `冷却` 继续到 0 → **`冷却` 归零那一刻应看到卡槽里一张卡被换成小鬼卡** |
| 消失条件 | `隐身`：4 秒后；`冷却`：触发篡改后 Timer 重启（`Runtime` 里 `!isHack && IsStopped()` 才 `Start(10)`，所以实际间隔 = 10s + 动画时长，略大于 10） |
| 非缺陷现象 | 打开「≥5 秒」开关后**只有 `隐身` 行消失**；卡槽只剩 1 张卡时篡改不生效（图鉴③）但 CD 照跑 |
| 真缺陷信号 | `隐身` 出现后超过 5 秒仍在；`冷却` 到 0 后 3 秒内没有下一轮篡改且 `冷却` 不再重启；两行同时显示 >6 秒 |

### 19.3 安眠术士僵尸（`吟唱` → `睡眠`，互斥同屏 1 行）

| 项 | 内容 |
|---|---|
| 出现条件 | 前摇段：`!spellSpent && !sleeping && chantTimer == 0`；吟唱段：`chantTimer > 0`；睡眠段：`sleeping == true` |
| 期望曲线 | `吟唱 5.0s`→`0`（入场前摇，`entryTimer` 5s）→ **跳回 `吟唱 5.0s`** → `0`（吟唱段，`chantTimer` 5s）→ 发射魔弹 → **行消失**（`spellSpent=true`）→ `睡眠 10.0s` → `0` → 醒来 +1000 血 + 加速 → **行消失** |
| 消失条件 | 发射后到入睡之间（约 1 个动画）；醒来后（一次性魔法，不再显示） |
| 非缺陷现象 | ①前摇与吟唱**都叫「吟唱」**，视觉上"5.0→0 再回跳 5.0"是**两段顺序**，不是重复或卡住；②同屏**永远只有 1 行**（设计如此，不是漏行）；③被咖啡豆/打断时可能不进入睡眠段 |
| 真缺陷信号 | 同屏出现 2 行（前摇+睡眠并存）；醒来后仍显示 `睡眠`；发射后仍显示 `吟唱` |

### 19.4 电磁拖车僵尸（`磁力`）

| 项 | 内容 |
|---|---|
| 出现条件 | `anchored == true`（本行有正常高度植物、停车准备拖拽）**且** `shooting == false` |
| 期望曲线 | 锚定瞬间 `磁力 10.0s` → 单调递减 → 归零时发射磁力射线（**该格除飞行植物外全部前移一格**）→ 拖拽动作期间（`shooting=true`，`PullDuration=2.0`）**行隐藏** → 循环 `磁力 10.0s` |
| 消失条件 | 正在射击（约 2s）；本行无可拖植物（`ResumeDriving` 继续前进）；爆胎（被尖刺扎）即死 |
| 非缺陷现象 | ①首次锚定时数值**可能不是 10.0**（`SetStopColumn` 换列才归零）；②本行只有低矮/飞行植物时**整行不出现**（图鉴①的排除条件）；③拖拽后植物位置变化会让它重新对齐/换列，数值可能突然回到 10.0（`attackTimer` 归零） |
| 真缺陷信号 | 行显示但连续 2 个 10s 周期植物都没被拖动；`shooting` 期间行仍在显示；数值长期停在某个非零值（未在锚定却仍在累计） |

### 19.5 守卫伽刚特尔（`蓄力`）

| 项 | 内容 |
|---|---|
| 出现条件 | `_charging == true`（本体血量**首次跌破 50%** 触发；需要先回到 ≥50% 才能再次触发） |
| 期望曲线 | 跌破 50% 那一帧 → `蓄力 5.0s` → 递减；期间**限伤 100/次、每秒回 500 血**；**回满或满 5 秒 → 行消失 + 立刻 3×3 强力锤击** |
| 消失条件 | 回满 / 5 秒到 / 死亡；锤击后需血量回到 ≥50% 再跌破才会再次出现 |
| 非缺陷现象 | ①大多情况下会**在 5.0s 之前就消失**（被磨到 50% 后回满只需 `(hitpointsSave−hitpoints)/500` 秒）——这是设计，不是提前结束；②「每秒回 500」是**离散跳变**（每满 1s 才回一次），不是平滑回血；③`Magic`/`MagicIdle`/`MagicSmash` 动画切换时数值不变 |
| 真缺陷信号 | 血量已跌破 50% 却**无行**（且已 `≥50%` 过一次）；行显示超过 5.0s 仍未消失；限伤失效（单次受伤 >100）—— 那是战斗逻辑，不是显示，但值得记 |

### 19.6 骷髅伽刚特尔（骨堆 `复活` + 坑洞 `消失` 是两个通道）

| 项 | 内容 |
|---|---|
| 出现条件 | `复活`：**骨堆** `TowerDefenseSkeleton` 生成后（`charName=="Skeleton"`，`timer < time`）；`消失`：同格出现坑洞 `CraterK`（通用通道，`dieDownTimer`，非本批代码） |
| 期望曲线 | 骷髅伽刚特尔**陆地**死亡 → 死亡动画播完 → 骨堆出现（血量 3000）→ `复活 30.0s` → 递减 → 归零 → 骨堆消失并生成**新的**骷髅伽刚特尔（`over=true`：**不再能丢骷髅小鬼**，血量按 `hitpointScale` 缩放） |
| 消失条件 | 骨堆被打掉（3000 血）；计时满（转生）；**水上死亡不会生成骨堆**（源码 `if (inWater || over) break;`）⇒ 无 `复活` 行 |
| 非缺陷现象 | ①`复活 X.Xs`（骨堆）与 `消失 X.Xs`（坑洞，通用通道）**可能同屏并存**，是**两个不同实体**，都正常；②骨堆是 **Gravestone 不是 Zombie**（契约不变量 #1 已为此改），这是 v1.1 门控放行后才出现的**新显示**；③骨堆放在墓碑组外（`RemoveFromGroup("Gravestone")`），不会触发普通墓碑逻辑 |
| 真缺陷信号 | 骨堆在场但 `复活` 行**不出现**（说明 `IsZombieCdHost` 门控没放行到 → 框架问题）；数值不是 30 上下；计时到 0 却不转生 |

### 19.7 快闪系（快闪迪斯科 / 快闪舞者：`起身`）

| 项 | 内容 |
|---|---|
| 出现条件 | `FlashStep._fellRemaining > 0`（`flash_step.fell` / `fell_idle` 两态；`IsFallen`） |
| 期望曲线 | 闪身步**判罚滑倒**那一刻 → `起身 10.0s` → 单调递减 → 归零 → 播放起身动画 → **行消失**，恢复行动 |
| 消失条件 | 起身完成；死亡（滑倒状态下死亡用 `FellDeath` 动画）；组件被移除 |
| 非缺陷现象 | ①**迪斯科滑倒时它的舞者也会跟着倒**（`OnFlashStepFell` 遍历 `_flashDancers` 调 `Fall()`）⇒ 同屏多只显示 `起身` 是正常的；②舞者滑倒 → 迪斯科补召 1 只新舞者（**新舞者不显示 `起身`**，因为它没倒）；③闪身成功（未摔）**不显示任何行**（瞬时事件）；④滑倒概率 5%→+5%→…→100% **不显示**（百分比不是时间 CD） |
| 真缺陷信号 | 倒了却无行；行显示但该僵尸已经站起来/恢复移动；`起身` 与 `无敌` 类行混在一起（本批不写无敌） |

### 19.8 一次性总核对（5 步验收里建议插这条）

- 同一僵尸**不要出现重复行**（例如塞壬不应同时出现 `施法/歌唱` **和** 我自己写的 `歌唱` —— 我只在通用通道②未覆盖时才写行，B 批已核对无重复）。
- A/B 批**互不影响**：`CollectZombieCdLinesA` 抛异常不影响 B（契约 §1.5）；若 A 批某僵尸显示异常，先确认不是我这边。
- 关闭「显示僵尸冷却」总开关后，**B 批 10 个分支的行全部消失**（外层 `_showZombieCd` 门控）。

