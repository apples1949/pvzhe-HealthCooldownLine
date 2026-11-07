using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;

/// <summary>
/// 「僵尸 CD 附加行」**B 批实现**（15 个僵尸）——task-4 / zombie-dev-b。
///
/// ── 契约 ─────────────────────────────────────────────────────────────
/// `mods\HealthCooldownLine\研究-僵尸CD\00-框架与契约.md`（v1，cdmod-owner 产出）。
/// 本研究记录（中文名 / 类名 / 字段 / 语义 / 显示文案 / 证据 文件:行号 / 可显示性）：
/// `mods\HealthCooldownLine\研究-僵尸CD\B批-研究.md`。
///
/// ── 写法约定（照抄 A 批，避免 CS0111 整包炸掉）────────────────────────
///  · 本文件与 `HealthCooldownLineEntry.cs` 是**同一个类**的 different part ⇒
///    `AddLine` / `AddTimer` / `BusyColor` / `ReadyColor` / `DisabledColor` /
///    `CatOn` / `MaxTotalLines` / `ZombieField` / `ZombieNum` / `ZombieFlag` /
///    `ZombieNameIs` / `ZombieCdDisplayable` **直接可用，绝不重复定义**。
///  · 具体僵尸类一律走反射（`ZombieField`）；只有 `TowerDefenseZombie` /
///    `TowerDefenseCharacter` / `CharacterTimerComponent` 这类基类/通用组件才强类型。
///  · 每个僵尸一个 try/catch：一个僵尸读崩了不许影响同批后面的僵尸。
///  · 追加行前一律判 `lines.Count < MaxTotalLines`；单僵尸 ≤2 行。
///  · 计时行一律 `AddTimer(text, color, 总时长)`；总时长取不到就传 0（不参与 ≥5s 过滤）。
///
/// ── 本批结论速览（详细证据见 B批-研究.md）─────────────────────────────
///  可显示（10 个，本文件有分支；含"骨堆"实体）：
///    云小鬼僵尸(`投掷`) / 黑客小鬼僵尸（2 段：`冷却`+`隐身`）/ 基因炸弹僵尸(`爆破`) /
///    电磁拖车僵尸(`磁力`) / 安眠术士僵尸(`吟唱` + `睡眠`) / 时间术士僵尸(`冷却`) /
///    守卫伽刚特尔(`蓄力`) / 骷髅伽刚特尔(`复活`，计时挂在骨堆 TowerDefenseSkeleton 上，
///    框架 v1.1 已放行该实体) / 快闪迪斯科僵尸(`起身`) / 快闪舞者僵尸(`起身`)
///  已由通用通道 ② 显示（5 个，本文件**不重复写**，只留说明）：
///    塞壬僵尸 / 守护术士僵尸 / 占卜术士僵尸 / 唤魔使僵尸 / 女巫僵尸
///    —— 它们的计时器全部是 `character.timer` 的命名计时器（Cast/Sing/Throw），
///       `TryCollectInfo` 的 ② 段（HealthCooldownLineEntry.cs:1781-1867）已经会显示；
///       v1.1 起 `TimerLabel` 已把 Cast/Sing/Throw 映射为 施法/歌唱/投掷（中文）。
///  ⚠️ 契约不变量 #1（v1.1 变更）：`character` **不一定**是 TowerDefenseZombie
///     ⇒ 本文件禁止任何 `(TowerDefenseZombie)character` 强硬转，一律走反射。
/// </summary>
public sealed partial class HealthCooldownLineEntry
{
	// ─────────────────────────────────────────────────────────────────
	// 标签常量（契约 §5.2 词表 **v1.1**）。
	// 「投掷 / 吟唱 / 蓄力 / 起身」四个新词已由 Lead 2026-11-07 拍板、cdmod-owner 写进
	// 契约 §5.2 词表（原先本批临时借用「冷却 / 准备」兜底词，现已换成正式词）。
	// 「冷却」只留给「通用某技能冷却」语义：黑客小鬼的篡改 CD、时间术士的定时魔弹 CD。
	// ─────────────────────────────────────────────────────────────────
	private const string ZbBLblCd = "冷却";       // 通用"某技能冷却"兜底（黑客篡改 / 时间术士魔弹）
	private const string ZbBLblThrow = "投掷";    // 云小鬼（投掷地刺小鬼）★ v1.1 新词
	private const string ZbBLblChant = "吟唱";    // 安眠术士（前摇 / 吟唱）★ v1.1 新词
	private const string ZbBLblCharge = "蓄力";   // 守卫伽刚特尔（50% 血蓄力）★ v1.1 新词
	private const string ZbBLblGetUp = "起身";    // 快闪迪斯科 / 快闪舞者（滑倒后起身）★ v1.1 新词
	private const string ZbBLblSleep = "睡眠";    // 安眠术士（发射后睡着）
	private const string ZbBLblBoom = "爆破";     // 基因炸弹僵尸
	private const string ZbBLblMagnet = "磁力";   // 电磁拖车僵尸
	private const string ZbBLblRevive = "复活";   // 骷髅伽刚特尔（骨堆聚拢复活）
	private const string ZbBLblInvis = "隐身";    // 黑客小鬼僵尸

	// 各僵尸的**构建期常量**（数值全部与源码常量逐一对应，行号写在 B批-研究.md）
	private const double ZbBImpCloudInterval = 8.0;      // ImpCloud.cs:8  SHOOT_INTERVAL
	private const double ZbBHackerHackCd = 10.0;         // Imphacker.cs:94 _hackTimer.Start(10.0)
	private const double ZbBHackerInvis = 4.0;           // Imphacker.cs:174 _hackEffectRemaining = 4.0
	private const double ZbBMagnetronInterval = 10.0;    // Magnetron.cs:44 AttackInterval
	private const double ZbBSleepMageEntry = 5.0;        // SleepMage.cs:9  EntryCastDelay
	private const double ZbBSleepMageChant = 5.0;        // SleepMage.cs:259 chantTimer >= 5.0
	private const double ZbBSleepMageSleep = 10.0;       // SleepMage.cs:11 SleepDuration
	private const double ZbBTimeMageInterval = 10.0;     // TimeMage.cs:163 spellTimer >= 10.0
	private const double ZbBGargantuarGDCharge = 5.0;    // GargantuarGD.cs:21 ChargeDuration
	private const double ZbBSkeletonRevive = 30.0;       // TowerDefenseSkeleton.cs:14 time = 30.0
	private const double ZbBFlashStepFellDur = 10.0;     // FlashStepComponentDefinition.cs:11 fellDuration 默认值

	/// <summary>
	/// B 批（15 个僵尸）的附加行实现点。contract §1：必须是 `partial void`、**不加**访问修饰符。
	/// </summary>
	partial void CollectZombieCdLinesB(ComponentManager cm, TowerDefenseCharacter character,
		string charName, System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		if (lines == null || character == null) { return; }
		if (lines.Count >= MaxTotalLines) { return; }

		// ════════════════════════════════════════════════════════════
		// ① 云小鬼僵尸  ZombieImpCloud  →  TowerDefenseZombieImpCloud
		//    图鉴：每 8 秒暂停行动、向下扔一只地刺小鬼。
		//    证据：Chapter8\ImpCloud\Scene\TowerDefenseZombieImpCloud.cs
		//          :8  `private const float SHOOT_INTERVAL = 8f`
		//          :20 `private double _shootTimer`
		//          :117-121 `if (_shootTimer < 8.0) { _shootTimer += delta * timeScale; return; }`
		//          :22 `private bool _isShooting`（进入投掷动画期间计时暂停）
		//    语义：已累计投掷计时 + 总 8s ⇒ 可显示（剩余 = 8 − _shootTimer）
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieImpCloud", "TowerDefenseZombieImpCloud"))
			{
				Type t = character.GetType();
				FieldInfo fTimer = ZombieField(t, "_shootTimer");
				FieldInfo fShooting = ZombieField(t, "_isShooting");
				if (fTimer != null && !ZombieFlag(character, fShooting))
				{
					double elapsed = ZombieNum(character, fTimer, 0.0);
					double rem = ZbBImpCloudInterval - elapsed;
					if (ZombieCdDisplayable(rem, ZbBImpCloudInterval) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblThrow + " " + rem.ToString("0.0") + "s", BusyColor, ZbBImpCloudInterval);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ② 黑客小鬼僵尸  ZombieImphacker  →  TowerDefenseZombieImphacker
		//    图鉴：每隔 10 秒把卡槽里一张植物卡篡改为小鬼卡，随后隐身 4 秒。
		//    证据：Challenge\Imphacker\Scene\TowerDefenseZombieImphacker.cs
		//          :12 `private Timer _hackTimer`（Godot Timer 节点 "HackTimer"，:69）
		//          :94 `_hackTimer.Start(10.0)`（冷却 10s，超时后 :92-95 重新启动）
		//          :192 `dictionary["hackCooldownRemaining"] = _hackTimer.TimeLeft`
		//          :14 `private double _hackEffectRemaining`；:174 `= 4.0`（隐身时长）
		//          :84 `_hackEffectRemaining -= delta * ...`
		//    语义：两段 ⇒ 两行（篡改 CD 10s / 隐身 4s），都可显示
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieImphacker", "TowerDefenseZombieImphacker"))
			{
				Type t = character.GetType();
				FieldInfo fHackTimer = ZombieField(t, "_hackTimer");
				FieldInfo fInvis = ZombieField(t, "_hackEffectRemaining");

				// ②-1 篡改卡槽 CD：Godot Timer.TimeLeft（源码自己就用它做存档恢复）
				double hackRem = ZbBTimerLeft(character, fHackTimer);
				if (ZombieCdDisplayable(hackRem, ZbBHackerHackCd) && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, ZbBLblCd + " " + hackRem.ToString("0.0") + "s", BusyColor, ZbBHackerHackCd);
				}

				// ②-2 隐身（不可选中/不可拾取）剩余
				double invisRem = ZombieNum(character, fInvis, 0.0);
				if (ZombieCdDisplayable(invisRem, ZbBHackerInvis) && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, ZbBLblInvis + " " + invisRem.ToString("0.0") + "s", ReadyColor, ZbBHackerInvis);
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ③ 基因炸弹僵尸  ZombieJackGene  →  TowerDefenseZombieJackGene
		//    图鉴：出场一段时间后打开盒子自爆（3×3 基因突变）。
		//    证据：Chapter9\JackGene\Scene\TowerDefenseZombieJackGene.cs
		//          :28/30 `public double timer; public double time;`
		//          :85 `time = GD.RandRange(9.0, 20.0);`（_Ready，总时长每只不同）
		//          :146-150 `if (timer < time) { timer += delta * timeScale; return; }`
		//          :151 `explode = true`（之后进爆炸动画，不再倒数）
		//          :24 `public bool hasJackBox`（盒子被打掉就不炸了）
		//    语义：可显示（剩余 = time − timer；total 传真实 time，动态但实例内稳定）
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieJackGene", "TowerDefenseZombieJackGene"))
			{
				Type t = character.GetType();
				FieldInfo fTimer = ZombieField(t, "timer");
				FieldInfo fTime = ZombieField(t, "time");
				FieldInfo fExplode = ZombieField(t, "explode");
				FieldInfo fBox = ZombieField(t, "hasJackBox");
				if (fTimer != null && fTime != null
					&& !ZombieFlag(character, fExplode) && ZombieFlag(character, fBox))
				{
					double elapsed = ZombieNum(character, fTimer, 0.0);
					double total = ZombieNum(character, fTime, 0.0);
					double rem = total - elapsed;
					if (ZombieCdDisplayable(rem, total) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblBoom + " " + rem.ToString("0.0") + "s", BusyColor, total);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ④ 电磁拖车僵尸  ZombieMagnetron  →  TowerDefenseZombieMagnetron
		//    图鉴：入场后若本行有正常高度植物，则暂停移动、每 10 秒发射磁力射线拖拽。
		//    证据：Chapter9\Magnetron\Scene\TowerDefenseZombieMagnetron.cs
		//          :44 `public const double AttackInterval = 10.0;`
		//          :68 `public double attackTimer;`
		//          :339-354 ProcessAnchored: `attackTimer += delta * timeScale; if (attackTimer >= 10.0) BeginShooting(...)`
		//          :557-559 BeginShooting: `attackTimer = 0.0;`（发射即重置）
		//          :72 `public bool anchored;`（只有锚定待拖拽时才在计时）
		//          :78 `public bool shooting;`
		//          :46 `PullDuration = 2.0` / :80 `pullRemaining`（拖拽动作时长，2s < 5s 过滤线，不显示）
		//    语义：可显示（剩余 = 10 − attackTimer）——`anchored` 为假时计时器不在推进，不显示
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieMagnetron", "TowerDefenseZombieMagnetron"))
			{
				Type t = character.GetType();
				FieldInfo fTimer = ZombieField(t, "attackTimer");
				FieldInfo fAnchored = ZombieField(t, "anchored");
				FieldInfo fShooting = ZombieField(t, "shooting");
				if (fTimer != null && ZombieFlag(character, fAnchored) && !ZombieFlag(character, fShooting))
				{
					double elapsed = ZombieNum(character, fTimer, 0.0);
					double rem = ZbBMagnetronInterval - elapsed;
					if (rem > ZbBMagnetronInterval) { rem = ZbBMagnetronInterval; }
					if (ZombieCdDisplayable(rem, ZbBMagnetronInterval) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblMagnet + " " + rem.ToString("0.0") + "s", BusyColor, ZbBMagnetronInterval);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ⑤ 安眠术士僵尸  ZombieSleepMage  →  TowerDefenseZombieSleepMage
		//    图鉴：入场 5 秒后停下吟唱，再 5 秒发射催眠魔弹；发射后原地睡着 10 秒
		//          （醒来 +1000 血并加速）。只会发动一次魔法 ⇒ 没有循环"发射 CD"，
		//          可显示的是 **前摇 5s / 吟唱 5s / 睡眠 10s** 三段（前两段互斥，实际同屏 1 行）。
		//    证据：Chapter10\SleepMage\Scene\TowerDefenseZombieSleepMage.cs
		//          :9  `EntryCastDelay = 5.0`；:37 `public double entryTimer`；:173-177 累加判 5.0
		//          :11 `SleepDuration = 10.0`；:45 `public double sleepTimer`；:383-387
		//          :35 `public double chantTimer`；:258-263 `chantTimer += ...; if (chantTimer >= 5.0)`
		//          :43 `public bool sleeping`；:29 `public bool spellSpent`
		//    语义：可显示
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieSleepMage", "TowerDefenseZombieSleepMage"))
			{
				Type t = character.GetType();
				FieldInfo fSleeping = ZombieField(t, "sleeping");
				FieldInfo fSleepTimer = ZombieField(t, "sleepTimer");
				FieldInfo fChantTimer = ZombieField(t, "chantTimer");
				FieldInfo fEntryTimer = ZombieField(t, "entryTimer");
				FieldInfo fSpellSpent = ZombieField(t, "spellSpent");

				if (ZombieFlag(character, fSleeping))
				{
					// ⑤-1 发射后的睡眠（醒来会 +1000 血 + 加速）
					double rem = ZbBSleepMageSleep - ZombieNum(character, fSleepTimer, 0.0);
					if (ZombieCdDisplayable(rem, ZbBSleepMageSleep) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblSleep + " " + rem.ToString("0.0") + "s", BusyColor, ZbBSleepMageSleep);
					}
				}
				else if (!ZombieFlag(character, fSpellSpent))
				{
					// ⑤-2 吟唱中（chantTimer 只在施法状态里推进）⇒ 5 − chantTimer
					double chantElapsed = ZombieNum(character, fChantTimer, 0.0);
					if (chantElapsed > 0.0)
					{
						double remC = ZbBSleepMageChant - chantElapsed;
						if (ZombieCdDisplayable(remC, ZbBSleepMageChant) && lines.Count < MaxTotalLines)
						{
							AddTimer(lines, ZbBLblChant + " " + remC.ToString("0.0") + "s", BusyColor, ZbBSleepMageChant);
						}
					}
					else
					{
						// ⑤-3 入场前摇 ⇒ 5 − entryTimer
						double remE = ZbBSleepMageEntry - ZombieNum(character, fEntryTimer, 0.0);
						if (ZombieCdDisplayable(remE, ZbBSleepMageEntry) && lines.Count < MaxTotalLines)
						{
							AddTimer(lines, ZbBLblChant + " " + remE.ToString("0.0") + "s", BusyColor, ZbBSleepMageEntry);
						}
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ⑥ 时间术士僵尸  ZombieTimeMage  →  TowerDefenseZombieTimeMage
		//    图鉴：入场后每 10 秒发射一枚定时魔弹（150 魔法伤害 + 5s 定身）。
		//    证据：Chapter10\TimeMage\Scene\TowerDefenseZombieTimeMage.cs
		//          :28 `public double spellTimer;`
		//          :153-168 SpellProcessing：`spellTimer += delta * timeScale; if (spellTimer >= 10.0) ToMageCast`
		//          :199-213 AnimeEvent("fire")：`spellTimer = 0.0;`（发射即重置 ⇒ 循环 10s）
		//    语义：可显示（剩余 = 10 − spellTimer）
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieTimeMage", "TowerDefenseZombieTimeMage"))
			{
				FieldInfo fTimer = ZombieField(character.GetType(), "spellTimer");
				if (fTimer != null)
				{
					double rem = ZbBTimeMageInterval - ZombieNum(character, fTimer, 0.0);
					if (ZombieCdDisplayable(rem, ZbBTimeMageInterval) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblCd + " " + rem.ToString("0.0") + "s", BusyColor, ZbBTimeMageInterval);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ⑦ 守卫伽刚特尔  ZombieGargantuarGD  →  TowerDefenseZombieGargantuarGD
		//    图鉴：本体血量一旦低于 50% 立即原地进入蓄力状态（限伤 100、每秒回 500 血、
		//          最多 5 秒）；每秒回满或满 5 秒即退出并放一次 3×3 强力锤击。
		//    证据：Chapter10\GargantuarGD\Scene\TowerDefenseZombieGargantuarGD.cs
		//          :21 `private const double ChargeDuration = 5.0;`
		//          :37 `private bool _charging;`  :41 `private double _chargeElapsed;`
		//          :151-171 ChargeProcessing：`_chargeElapsed += ...; ... if (回满 || _chargeElapsed+1e-9 >= 5.0) 结束`
		//    语义：可显示（剩余 = 5 − _chargeElapsed）——只在 `_charging` 为真时显示
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieGargantuarGD", "TowerDefenseZombieGargantuarGD"))
			{
				Type t = character.GetType();
				FieldInfo fCharging = ZombieField(t, "_charging");
				FieldInfo fElapsed = ZombieField(t, "_chargeElapsed");
				if (ZombieFlag(character, fCharging) && fElapsed != null)
				{
					double rem = ZbBGargantuarGDCharge - ZombieNum(character, fElapsed, 0.0);
					if (ZombieCdDisplayable(rem, ZbBGargantuarGDCharge) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblCharge + " " + rem.ToString("0.0") + "s", BusyColor, ZbBGargantuarGDCharge);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ⑧ 骷髅伽刚特尔  ZombieSkeletuar  →  TowerDefenseZombieSkeletuar
		//    图鉴：在陆地上首次死亡时散落成骨堆（血量 3000），30 秒后聚拢复活
		//          （复活后失去丢小鬼能力）。
		//    ★ 关键事实：**30 秒复活计时不在 Skeletuar 身上，而在"骨堆"实体上**：
		//        Skeletuar 死亡动画播完 → 生成 packet "Skeleton"（TowerDefenseZombieSkeletuar.cs:130-158）
		//        "Skeleton" 的 config 是 `Asset\Anime\Character\GraveStone\Skeleton\Config\TowerDefenseSkeleton.tres`
		//        （hitpoints = 3000.0 / packetCooldown = 30.0），类 = **TowerDefenseSkeleton**
		//        `TowerDefenseSkeleton : TowerDefenseGravestone`（GraveStone\Skeleton\Scene\TowerDefenseSkeleton.cs:6）
		//        且 `TowerDefenseGravestone : TowerDefenseCharacter`（Prefab\TowerDefense\Character\TowerDefenseGravestone.cs:5）
		//        ⇒ 骨堆 **不是 TowerDefenseZombie**（契约不变量 #1 在 v1.1 已改为
		//          "character 不一定是 TowerDefenseZombie"）。
		//    ★ 框架侧已放行（cdmod-owner 2026-11-07，`TryCollectZombieCdLines` 的门控由
		//        `IsZombieChar` 换成 `IsZombieCdHost(character, charName)`）：
		//          · `TowerDefenseZombie` 子类 → 放行
		//          · `charName == "Skeleton"` 或类名 `TowerDefenseSkeleton` → 放行
		//          · 类名含 `Skeleton` 且 `character is TowerDefenseGravestone` → 放行（兜底派生类）
		//          · **普通墓碑不放行**（不按 Gravestone 基类一刀切）
		//        ⇒ 本分支在当前版本**可被调用到**，显示 30 秒复活倒计时。
		//    字段（数据本身齐全、可显示）：
		//        GraveStone\Skeleton\Scene\TowerDefenseSkeleton.cs
		//        :12 `public double timer;`  :14 `public double time = 30.0;`
		//        :34-38 `if (timer < time) { timer += delta * timeScale; return; }`
		//        :49-56 计时满 → 生成 ZombieSkeletuar 并 `zombie.over = true`
		//    结论：**可显示**；身份判据同时覆盖 config 名 "Skeleton" 与类名 TowerDefenseSkeleton。
		//          ⚠️ 这里传进来的可能**不是** TowerDefenseZombie ⇒ 本文件任何地方都不得
		//             写 `(TowerDefenseZombie)character` 之类的强硬转（本批代码全程走反射）。
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZombieNameIs(charName, "Skeleton") || ZbBClassIs(character, "TowerDefenseSkeleton"))
			{
				Type t = character.GetType();
				FieldInfo fTimer = ZombieField(t, "timer");
				FieldInfo fTime = ZombieField(t, "time");
				FieldInfo fOver = ZombieField(t, "over");
				if (fTimer != null && fTime != null && !ZombieFlag(character, fOver))
				{
					double elapsed = ZombieNum(character, fTimer, 0.0);
					double total = ZombieNum(character, fTime, ZbBSkeletonRevive);
					if (total <= 0.0) { total = ZbBSkeletonRevive; }
					double rem = total - elapsed;
					if (ZombieCdDisplayable(rem, total) && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, ZbBLblRevive + " " + rem.ToString("0.0") + "s", BusyColor, total);
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ⑨ 快闪迪斯科僵尸 / ⑩ 快闪舞者僵尸
		//    ZombieDiscoFlash → TowerDefenseZombieDiscoFlash
		//    ZombieBackupFlash → TowerDefenseZombieBackupFlash
		//    图鉴：附近有敌方子弹时发动闪身步躲避；躲避有概率滑倒 —— 无法行动，10 秒后起身。
		//    证据：Script\Component\TowerDefense\Character\FlashStepComponent\FlashStepComponent.cs
		//          :68 `private double _fellRemaining;`
		//          :427-439 Fall()：`_fellRemaining = Definition.fellDuration; SendStateEvent(FellEvent)`
		//          :483-498 OnPhysicsProcess：`_fellRemaining -= delta * _parent.timeScale;`
		//                                    `if (_fellRemaining <= 0 && fell_idle) SendStateEvent(StandUp)`
		//          :96-107 `public bool IsFallen`（fell / fell_idle 两态）
		//          :92   `public int FallChance { get; private set; } = 5;`（5%→每次成功闪身 +5%，上限 100）
		//          FlashStepComponentDefinition.cs:11 `public double fellDuration { get; set; } = 10.0;`
		//          （两个僵尸的 ComponentSet 里该定义是内联实例、**没有**覆盖 fellDuration ⇒ 10.0）
		//          ComponentSet InstanceId = "character.flash_step"
		//          （DiscoFlash\Scene\TowerDefenseZombieDiscoFlashComponentSet.tres:13 /
		//            BackupFlash\Scene\TowerDefenseZombieBackupFlashComponentSet.tres:13）
		//    语义：可显示（滑倒中：剩余 = _fellRemaining，总 = fellDuration）
		//          ※ 闪身步本身是**事件触发、瞬时完成**（TryFlash，:398-425），没有持续计时 ⇒ 不作为 CD 显示；
		//            FallChance 是概率百分比，不是时间 CD ⇒ 按契约 §5.2 不新增词表外的行，不显示。
		// ════════════════════════════════════════════════════════════
		try
		{
			if (ZbBIsZombie(character, charName, "ZombieDiscoFlash", "TowerDefenseZombieDiscoFlash")
				|| ZbBIsZombie(character, charName, "ZombieBackupFlash", "TowerDefenseZombieBackupFlash"))
			{
				// 组件实例通过属性 `FlashStep` 取（DiscoFlash.cs:61 / BackupFlash.cs:25，
				// 都是 `public FlashStepComponent FlashStep { get; private set; }`）——
				// 走属性反射，避免强类型引用敌方脚本类。
				object flashStep = ZbBProp(character, "FlashStep");
				if (flashStep != null)
				{
					Type ct = flashStep.GetType();
					FieldInfo fRemaining = ZombieField(ct, "_fellRemaining");
					if (fRemaining != null)
					{
						double rem = ZombieNum(flashStep, fRemaining, 0.0);
						object def = ZbBProp(flashStep, "ComponentDefinition");
						double total = ZbBDoubleProp(def, "fellDuration", ZbBFlashStepFellDur);
						if (total <= 0.0) { total = ZbBFlashStepFellDur; }
						if (ZombieCdDisplayable(rem, total) && lines.Count < MaxTotalLines)
						{
							AddTimer(lines, ZbBLblGetUp + " " + rem.ToString("0.0") + "s", BusyColor, total);
						}
					}
				}
			}
		}
		catch { }

		// ════════════════════════════════════════════════════════════
		// ── 已由**通用通道 ②**显示（本批 5 个，这里不重复写行）──────────────
		//
		// 判据：这 5 个僵尸的计时器全部是 `CharacterTimerComponent` 的命名计时器，
		//       InstanceId = "character.timer"，而 `TryCollectInfo` 的 ② 段
		//       （HealthCooldownLineEntry.cs:1781-1867，开关 `_showOtherTimer` 默认 true，
		//        Entry.cs:546）已经按键名逐行显示（剩余 = timerWaitTime − timerCurrent）。
		//       ⇒ 契约 §4 末尾 / §6 末行：**不重复写**，只在此备注。
		//       ★ v1.1：cdmod-owner 已给 `TimerLabel` 补 Cast→施法 / Sing→歌唱 / Throw→投掷 映射
		//         ⇒ 这 5 个僵尸现在显示的是中文（施法/歌唱/投掷），**仍然不产生重复行**。
		//
		//  ── 塞壬僵尸（ZombieSiren → TowerDefenseZombieSiren）
		//     Chapter10\Siren\Scene\TowerDefenseZombieSirenTimerComponentDefinition.tres
		//       timerDictionary = { "Cast": 12.0, "Heal": 1.0, "Sing": 5.0 }, InstanceId = "character.timer"
		//     TowerDefenseZombieSiren.cs:84/:216 `_timerComponent.Run("Cast", 12.0)`（歌唱 CD）
		//                                 :340 `_timerComponent.Run("Sing", 5.0)`（歌唱持续）
		//     ⇒ 通用通道显示 `施法 11.x s`（歌唱 CD）与 `歌唱 4.x s`（歌唱时长）。
		//
		//  ── 守护术士僵尸（ZombieGuardMage → TowerDefenseZombieGuardMage）
		//     ...\TowerDefenseZombieGuardMageTimerComponentDefinition.tres: timerDictionary = { "Cast": 10.0 }
		//     TowerDefenseZombieGuardMage.cs:92/:145 `Run("Cast", 10.0)`（施法/加盾 CD）
		//
		//  ── 占卜术士僵尸（ZombieDivineMage → TowerDefenseZombieDivineMage）
		//     ...\TowerDefenseZombieDivineMageTimerComponentDefinition.tres: timerDictionary = { "Cast": 12.0 }
		//     TowerDefenseZombieDivineMage.cs:125/:178 `Run("Cast", 12.0)`（占卜 CD）
		//     （另有 :39 `_prophecyRemaining`，:217 `= 10.0`：那是"卡槽里预言效果"的剩余，
		//       属于玩家的卡不是僵尸自身 CD，且不在词表内 ⇒ 不显示，见 B批-研究.md）
		//
		//  ── 唤魔使僵尸（ZombieEvoker → TowerDefenseZombieEvoker）
		//     ...\TowerDefenseZombieEvokerTimerComponentDefinition.tres: timerDictionary = { "Cast": 10.0 }
		//     TowerDefenseZombieEvoker.cs:215/:270 `Run("Cast", 5.0)`（**运行时把总时长改成 5s**）
		//     ⇒ 通用通道按 live 的 timerWaitTime 显示 `施法 4.x s`（正确值，取自 Run 的入参）
		//
		//  ── 女巫僵尸（ZombieWitch → TowerDefenseZombieWitch）
		//     ...\TowerDefenseZombieWitchTimerComponentDefinition.tres: timerDictionary = { "Throw": 10.0 }
		//     TowerDefenseZombieWitch.cs:107/:178 `Run("Throw", 10.0)`（投毒药瓶 CD）
		// ════════════════════════════════════════════════════════════
	}

	// ─────────────────────────────────────────────────────────────────
	// 本批私有助手（名字全部带 ZbB 前缀，绝不与 Entry.cs / ZombieCdA.cs 撞名）
	// ─────────────────────────────────────────────────────────────────

	/// <summary>双通道身份判据：`charName`（config.name，首选）**或**类名（兜底）命中即算本僵尸。
	/// 两个通道都试（不是"配置名缺失才看类名"）—— 本批 15 个僵尸的 config.name 与类名
	/// 是一一对应的（已逐条核对 Config/*.tres 的 name 行），任一路径命中都不会串味；
	/// 这样将来配置键改名也不会静默丢显示。</summary>
	private static bool ZbBIsZombie(TowerDefenseCharacter character, string charName,
		string configName, string className)
	{
		if (ZombieNameIs(charName, configName))
		{
			return true;
		}
		return ZbBClassIs(character, className);
	}

	/// <summary>类名精确匹配（区分大小写）。</summary>
	private static bool ZbBClassIs(TowerDefenseCharacter character, params string[] classNames)
	{
		try
		{
			if (character == null || classNames == null) { return false; }
			string n = character.GetType().Name;
			for (int i = 0; i < classNames.Length; i++)
			{
				if (string.Equals(n, classNames[i], StringComparison.Ordinal)) { return true; }
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 读 `Godot.Timer` 字段的 `TimeLeft`（剩余秒数）。
	/// 用于黑客小鬼的 `_hackTimer`（Imphacker.cs:12，Godot Timer 节点）——
	/// 它不是 double 字段，不能走 `ZombieNum`。取不到/已释放 ⇒ 返回 0（不显示）。
	/// </summary>
	private static double ZbBTimerLeft(object holder, FieldInfo fTimerField)
	{
		try
		{
			if (holder == null || fTimerField == null) { return 0.0; }
			if (!(fTimerField.GetValue(holder) is Timer timer)) { return 0.0; }
			if (!GodotObject.IsInstanceValid(timer)) { return 0.0; }
			if (timer.IsStopped()) { return 0.0; }
			double left = timer.TimeLeft;
			return (left > 0.0 && !double.IsNaN(left) && !double.IsInfinity(left)) ? left : 0.0;
		}
		catch { return 0.0; }
	}

	/// <summary>按属性名（含非 public）读对象属性值，取不到返回 null。带按「类型全名|属性名」缓存。</summary>
	private static object ZbBProp(object obj, string name)
	{
		try
		{
			if (obj == null || string.IsNullOrEmpty(name)) { return null; }
			Type t = obj.GetType();
			string key = t.FullName + "|" + name;
			PropertyInfo pi;
			if (!_zbBPropCache.TryGetValue(key, out pi))
			{
				pi = t.GetProperty(name,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				_zbBPropCache[key] = pi;
			}
			return (pi != null) ? pi.GetValue(obj) : null;
		}
		catch { return null; }
	}

	private static readonly System.Collections.Generic.Dictionary<string, PropertyInfo> _zbBPropCache =
		new System.Collections.Generic.Dictionary<string, PropertyInfo>();

	/// <summary>按属性名读 double（取不到/类型不符 ⇒ fallback，绝不抛）。</summary>
	private static double ZbBDoubleProp(object obj, string name, double fallback)
	{
		try
		{
			object v = ZbBProp(obj, name);
			if (v != null) { return Convert.ToDouble(v); }
		}
		catch { }
		return fallback;
	}
}
