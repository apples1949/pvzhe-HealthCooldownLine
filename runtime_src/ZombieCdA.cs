// ============================================================================
//  ZombieCdA.cs —— A 批 16 个僵尸的专属 CD 显示（partial class 的一部分）
//
//  所属：mods\HealthCooldownLine（「血条·装填倒计时」v1.26.0 起）
//  Owner：zombie-dev-a（A 批 16 个：毁灭菇僵尸 / 火爆辣椒僵尸 / 火炬辣椒僵尸 /
//          三叶草气球僵尸 / 三叶草多彩气球僵尸 / 寒冰菇冰车僵尸 / 玩偶匣僵尸 / 海妖僵尸 /
//          磁场僵尸 / 鲨鱼僵尸 / 天使僵尸 / 催眠师僵尸 / 农夫僵尸 / 海盗船长僵尸 /
//          重生法师僵尸 / 巨化法师僵尸）
//
//  ⚠️ 契约见 `研究-僵尸CD\00-框架与契约.md`（签名/助手/显示格式/可显示性判据都在那里）。
//  ⚠️ 本文件是本类的**另一个部分**：Entry.cs 里所有 private 成员在这里直接可用，
//     但**不要**重复定义 AddLine / AddTimer / CatOn / ZombieField / FindFieldAlong 等已有成员。
//  ⚠️ 实现点方法**必须**带 partial 且不能加访问修饰符。
// ============================================================================

using System;
using System.Reflection;
using Godot;
using PVZHE.ModEditor.ModSystem;

public sealed partial class HealthCooldownLineEntry
{
	// ========================================================================
	//  A 批实现点（框架调用入口，由 cdmod-owner 在 Entry.cs 里声明）
	//  门控（僵尸类型 / 「显示僵尸冷却」开关 / 总开关 / 行数）已在 TryCollectZombieCdLines 做过。
	// ========================================================================
	partial void CollectZombieCdLinesA(ComponentManager cm, TowerDefenseCharacter character,
		string charName, System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			// ==================================================================
			//  ★★★ 示范段（框架验证用，cdmod-owner 写，v1.26.0）★★★
			//  ──────────────────────────────────────────────────────────────
			//  范围：**只到这 4 个僵尸**（毁灭菇僵尸 / 火爆辣椒僵尸 / 磁场僵尸 / 天使僵尸）。
			//  用途：证明「僵尸类字段 → 血条附加行」这条框架链路跑得通，并给 A 批其余
			//        12 个僵尸一个可直接照抄的模板。
			//
			//  ★ zombie-dev-a 接手后：
			//    · 这 4 个僵尸**本来就在你的 A 批清单里** ⇒ 你可以原样保留，也可以按你的
			//      研究结论重写（显示更细的分段/换标签），但请保持「示范段结束」标记仍可辨认；
			//    · 你的其余 12 个僵尸分支**写在下面「示范段结束」标记之后**，别再改示范段内部；
			//    · 若你想删掉示范段：整段删除（从 `CollectZombieCdDemoLines(...)` 调用到
			//      `CollectZombieCdDemoLines` 方法结束）即可，`CollectZombieCdLinesA` 这个签名必须留。
			// ==================================================================
			CollectZombieCdDemoLines(character, charName, lines);
			// ================== ★★★ 示范段结束 ★★★ ==========================

			// ↓↓↓ zombie-dev-a：A 批其余 12 个僵尸（研究见 研究-僵尸CD\A批-研究.md）↓↓↓
			CollectZombieCdABatchLines(character, charName, lines);
		}
		catch
		{
			// 整批兜底：单个僵尸出错不拖累后面的僵尸（外层还有一层 WarnOnceZombie 兜底）。
		}
	}

	// ========================================================================
	//  示范段实现（4 个僵尸）
	// ========================================================================
	private void CollectZombieCdDemoLines(TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		// ── 示范 1：毁灭菇僵尸 ZombieNormalDoomShroom ──────────────────────────
		//   证据：unpack\Asset\Anime\Character\Zombie\Chapter1\Normal\Scene\DoomShroom\
		//         TowerDefenseZombieNormalDoomShroom.cs:18-22 —— public timer/time/explode；
		//         :63 `time = GD.RandRange(25.0, 30.0)`；:83-87 `timer += delta * timeScale`；
		//         :88 计时满 ⇒ explode = true（播 Explode 动画后自杀式清场）。
		//   显示：`爆破 12.3s`（青蓝，total = time 25~30）
		if (IsZombieOf(charName, character, "ZombieNormalDoomShroom"))
		{
			CollectSelfDestructZombie(character, "爆破", lines);
		}
		// ── 示范 2：火爆辣椒僵尸 ZombieNormalJalapeno ─────────────────────────
		//   证据：...\Chapter1\Normal\Scene\Jalapeno\TowerDefenseZombieNormalJalapeno.cs:16-20
		//         （同款 timer/time/explode；:59 随机 25~30）
		//   显示：`自燃 8.7s`
		else if (IsZombieOf(charName, character, "ZombieNormalJalapeno"))
		{
			CollectSelfDestructZombie(character, "自燃", lines);
		}
		// ── 示范 3：磁场僵尸 ZombieMagnetic ──────────────────────────────────
		//   证据：...\Chapter4\ZombieMagnetic\Scene\TowerDefenseZombieMagnetic.cs:10-14
		//         public openTimer/openTime/open；:26 `openTime = GD.RandRange(6.0, 12.0)`；
		//         :42-51 计时满 ⇒ open = true（打开 checkShape，吸引附近火力）。
		//   显示：未开 `磁力 3.2s`（青蓝）/ 已开 `磁力 已开启`（绿）
		else if (IsZombieOf(charName, character, "ZombieMagnetic"))
		{
			CollectMagneticZombie(character, lines);
		}
		// ── 示范 4：天使僵尸 ZombieAngel ─────────────────────────────────────
		//   证据：...\Chapter4\Angel\Scene\TowerDefenseZombieAngel.cs:35-39
		//         public spawnTimer = 15.0（复活/施法间隔倒计时，:283 重置 15.0、:288 无目标重置 3.0）
		//         public invincible / invincibleTimer = 8.0（重生后无敌剩余，:151 重置、:120 递减）
		//   显示：`复活 9.4s`（青蓝）+ `无敌 5.2s`（绿，仅在 invincible 时）
		else if (IsZombieOf(charName, character, "ZombieAngel"))
		{
			CollectAngelZombie(character, lines);
		}
	}

	/// <summary>
	/// 判「是不是这个僵尸」：`charName`（= config.name）精确匹配为主，
	/// 类名包含为兜底（配置键缺失、或同一逻辑被多个配置键复用时也能命中）。
	/// </summary>
	private static bool IsZombieOf(string charName, TowerDefenseCharacter character, string key)
	{
		try
		{
			if (ZombieNameIs(charName, key)) { return true; }
			string tn = (character != null) ? character.GetType().Name : null;
			if (string.IsNullOrEmpty(tn) || string.IsNullOrEmpty(key)) { return false; }
			// 类名 = "TowerDefenseZombie" + 配置键片段，例如 ZooNormalDoomShroom ↔ TowerDefenseZombieNormalDoomShroom
			// （配置键去掉了 "TowerDefense" 前缀，故用"类名包含配置键"做包含匹配即可）
			return tn.IndexOf(key, StringComparison.Ordinal) >= 0;
		}
		catch { return false; }
	}

	/// <summary>
	/// 自毁型僵尸（毁灭菇僵尸 / 火爆辣椒僵尸 共用同一套字段布局）：
	/// `timer`（已累计）/ `time`（总时长）/ `explode`（已进入爆炸动画）。
	/// 剩余 = time − timer。
	/// </summary>
	private void CollectSelfDestructZombie(TowerDefenseCharacter character, string label,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			Type t = character.GetType();
			FieldInfo fTimer = ZombieField(t, "timer");
			FieldInfo fTime = ZombieField(t, "time");
			FieldInfo fExplode = ZombieField(t, "explode");
			if (fTime == null || fTimer == null) { return; }   // 结构变了 ⇒ 不显示（绝不瞎猜）
			if (ZombieFlag(character, fExplode)) { return; }   // 已经在爆了，倒数没有意义

			double elapsed = ZombieNum(character, fTimer, 0.0);
			double total = ZombieNum(character, fTime, 0.0);
			double remain = total - elapsed;
			if (!ZombieCdDisplayable(remain, total)) { return; }
			if (lines.Count >= MaxTotalLines) { return; }

			AddTimer(lines, label + " " + remain.ToString("0.0") + "s", BusyColor, total);
		}
		catch { }
	}

	/// <summary>
	/// 磁场僵尸：`openTimer`（已累计）/ `openTime`（总时长 6~12）/ `open`（磁场已开启）。
	/// 未开 ⇒ 倒数；已开 ⇒ 状态行「磁力 已开启」。
	/// </summary>
	private void CollectMagneticZombie(TowerDefenseCharacter character,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			Type t = character.GetType();
			FieldInfo fTimer = ZombieField(t, "openTimer");
			FieldInfo fTime = ZombieField(t, "openTime");
			FieldInfo fOpen = ZombieField(t, "open");
			if (fTime == null || fTimer == null) { return; }
			if (lines.Count >= MaxTotalLines) { return; }

			if (ZombieFlag(character, fOpen))
			{
				AddLine(lines, "磁力 已开启", ReadyColor);
				return;
			}
			double elapsed = ZombieNum(character, fTimer, 0.0);
			double total = ZombieNum(character, fTime, 0.0);
			double remain = total - elapsed;
			if (!ZombieCdDisplayable(remain, total)) { return; }
			AddTimer(lines, "磁力 " + remain.ToString("0.0") + "s", BusyColor, total);
		}
		catch { }
	}

	/// <summary>
	/// 天使僵尸：`spawnTimer`（下一次复活/施法间隔，倒计时）+ `invincible`/`invincibleTimer`
	/// （重生后无敌剩余）。
	/// ⚠️ `spawnTimer` 的**总时长不固定**（触发后重置 15.0，场上没有可复活目标时重置 3.0）
	///   ⇒ `total` 传 0（不参与「只显示 ≥5 秒」过滤），否则会出现"有时候显示有时候不显示"的怪现象。
	/// </summary>
	private void CollectAngelZombie(TowerDefenseCharacter character,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			Type t = character.GetType();
			FieldInfo fSpawn = ZombieField(t, "spawnTimer");
			FieldInfo fInv = ZombieField(t, "invincible");
			FieldInfo fInvTimer = ZombieField(t, "invincibleTimer");

			if (fSpawn != null)
			{
				double s = ZombieNum(character, fSpawn, 0.0);
				if (s > 0.0 && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "复活 " + s.ToString("0.0") + "s", BusyColor, 0.0);
				}
			}
			if (fInvTimer != null && ZombieFlag(character, fInv))
			{
				double iv = ZombieNum(character, fInvTimer, 0.0);
				if (iv > 0.0 && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "无敌 " + iv.ToString("0.0") + "s", ReadyColor, 8.0);
				}
			}
		}
		catch { }
	}

	// ========================================================================
	//  以下：A 批其余 12 个僵尸（zombie-dev-a）
	//
	//  研究全文（含每个字段的 文件:行号 证据、图鉴原文、可显示性判据）见
	//  `研究-僵尸CD\A批-研究.md`。本段对应它的 §4「代码落地对照」表。
	//
	//  这 12 个里有 3 个**故意不写分支**（论证见研究 md）：
	//    · 三叶草气球僵尸 / 三叶草多彩气球僵尸 —— 唯一的计时是 CharacterTimerComponent 的
	//      "Clover" 键（Blover.cs:120 / BalloonColourBlover.cs:137 都是 `Run("Clover", 8.0)`），
	//      已被 TryCollectInfo 的 ② 通用命名计时器通道显示 ⇒ 再写一遍就是重复行。
	//    · 鲨鱼僵尸 —— 唯一计时是 ChomperComponent 的 chewTimer/chewTime(=15.0)，
	//      已被 TryCollectInfo 的 ②.5「咀嚼」通道显示（Entry.cs:1877-1915）⇒ 同上。
	// ========================================================================
	private void CollectZombieCdABatchLines(TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		if (lines.Count >= MaxTotalLines) { return; }

		// ── 火炬辣椒僵尸 ZombieNormalJalatorch ──────────────────────────────
		//   证据：...\Chapter1\Normal\Scene\Jalatorch\TowerDefenseZombieNormalJalatorch.cs
		//         :14 `public double timer` / :16 `public double time` / :18 `public bool explode`
		//         :8/:10 `ExplodeDelayMin = 7.0` / `ExplodeDelayMax = 10.0`
		//         :59 `time = GD.RandRange(7.0, 10.0)`；:66-80 BatchUpdate：:71-75 累加，:76 满则 explode = true
		//   ⚠️ 图鉴（..._JALATORCH_HANDBOOK_EXPRESTION）写"25~30 秒"，代码是 7~10 秒 ⇒ 以代码为准。
		//   显示：`自燃 5.4s`（青蓝，total = time = 7~10）
		if (AClassIs(character, charName, "ZombieNormalJalatorch"))
		{
			ACountUp(character, "自燃", "timer", "time", "explode", lines);
			return;
		}

		// ── 寒冰菇冰车僵尸 ZombieZamboniIceShroom ───────────────────────────
		//   证据：...\Chapter2\Zamboni\Scene\IceShroom\TowerDefenseZombieZamboniIceShroom.cs
		//         :19 timer / :21 time / :23 over / :25 explode；:87 `time = GD.RandRange(25.0, 30.0)`
		//         :92-106 BatchUpdate：:97-101 累加，:102-104 满则 explode = true → CreateColdEffect + Destroy
		//   组件清单 TowerDefenseZombieZamboniIceShroomComponentSet.tres 只有 AttackComponent（无计时组件）
		//   ⚠️ 图鉴（..._ZOMBONI_ICESHROOM_HANDBOOK_EXPRESTION）写"15 秒后自爆"，代码是 25~30 秒 ⇒ 以代码为准。
		//   显示：`自爆 18.2s`（青蓝，total = time）
		if (AClassIs(character, charName, "ZombieZamboniIceShroom"))
		{
			ACountUp(character, "自爆", "timer", "time", "explode", lines);
			return;
		}

		// ── 玩偶匣僵尸 ZombieJackbox（含 6 个盔甲变体卡池 key，共用同一个类）──
		//   证据：...\Chapter2\Jackbox\Scene\TowerDefenseZombieJackbox.cs
		//         :24 hasJackBox / :26 over / :28 timer / :30 time / :32 explode
		//         :85 `time = GD.RandRange(9.0, 20.0)`；:134-157 BatchUpdate：:146-150 累加，
		//         :151-155 满则 explode = true →（hasJackBox 时）ToBomb 状态；:174-189 Bomb 播完即自爆销毁
		//   显示：`自爆 6.3s`（青蓝，total = time = 9~20）
		if (AClassIs(character, charName, "ZombieJackbox"))
		{
			ACountUp(character, "自爆", "timer", "time", "explode", lines);
			return;
		}

		// ── 海妖僵尸 ZombieSeaDemon（含 4 个盔甲变体）───────────────────────
		//   证据：...\Chapter3\SeaDemon\Scene\Base\TowerDefenseZombieSeaDemon.cs
		//         :13 `public double timer` / :15 `public bool spawnNext`
		//         :109-122 BatchUpdate：`if (timer < 15.0) { timer += delta*timeScale; return; }`（:114-118）
		//                  → :119 `spawnNext = true` / :120 `timer = 0.0`
		//         :200-202 状态机走完 ToPoint ⇒ spawnNext 复位；:224-235 `command=="spawn"` ⇒ SpawnSnorkle()
		//   总时长是**代码字面量 15.0**（无字段）⇒ 走 ACountUpFixed。
		//   显示：`召唤 9.6s`（青蓝，total = 15.0）/ `spawnNext` 时 `召唤 就绪`（绿）
		if (AClassIs(character, charName, "ZombieSeaDemon"))
		{
			if (ZombieFlag(character, ZombieField(character.GetType(), "spawnNext")))
			{
				AddLine(lines, "召唤 就绪", ReadyColor);
			}
			else
			{
				ACountUpFixed(character, "召唤", "timer", 15.0, lines);
			}
			return;
		}

		// ── 催眠师僵尸 ZombieHypnotist ───────────────────────────────────────
		//   证据：...\Chapter5\Hypnotist\Scene\TowerDefenseZombieHypnotist.cs
		//         :15 `public bool waitGrab`（落地完成，:271 置 true）/ :21 `public double waitTimer`
		//         :174-194 IdleProcessing：`if (!waitGrab || !CanRunAbility()) return;`
		//                  → `if (waitTimer < 10.0) { waitTimer += delta*timeScale; return; }`（:181-185）
		//                  → 清 buff → :193 SendStateEvent("ToGrab")（偷取本格所有植物）
		//   总时长是**代码字面量 10.0**（:181，无字段）⇒ 走 ACountUpFixed；
		//   并且必须 `waitGrab == true` 才显示（没落地时这个计时根本不推进，显示满值会误导）。
		//   显示：`偷取 4.8s`（青蓝，total = 10.0）
		//   注：图鉴里「落地使本格植物沉睡 25 秒」「偷取时 3×3 植物沉睡 15 秒」是**加在植物身上**的
		//       睡眠 buff（植物侧倒计时），僵尸实例上没有这两个数 ⇒ 不在本批显示。
		if (AClassIs(character, charName, "ZombieHypnotist"))
		{
			if (ZombieFlag(character, ZombieField(character.GetType(), "waitGrab")))
			{
				ACountUpFixed(character, "偷取", "waitTimer", 10.0, lines);
			}
			return;
		}

		// ── 农夫僵尸 ZombieFarmer（含 ZombieFarmerCone）──────────────────────
		//   证据：...\Chapter7\Farmer\Scene\TowerDefenseZombieFarmer.cs
		//         :18 `public double spawnTime = 10.0` / :20 `public double spawnTimer`
		//         :73-91 BatchUpdate：:80-84 累加，:85-90 满 ⇒ SendStateEvent("ToSpawn") + spawnTimer = 0
		//         （:123 `spawnTimer = spawnTime` 是施法完成置满的路径）；:153-160 随机召一只植物僵尸
		//   显示：`召唤 7.1s`（青蓝，total = spawnTime，**从实例读**，不写死 10）
		if (AClassIs(character, charName, "ZombieFarmer"))
		{
			ACountUp(character, "召唤", "spawnTimer", "spawnTime", null, lines);
			return;
		}

		// ── 海盗船长僵尸 ZombieCaptain（含 ZombieCaptainBucket）──────────────
		//   证据：...\Chapter7\Captain\Scene\TowerDefenseZombieCaptain.cs
		//         :11 `public double spawnTime = 15.0` / :13 `public double spawnTimer`
		//         :76-94 BatchUpdate：:83-87 累加，:88-93 满 ⇒ ToSpawn + spawnTimer = 0；
		//         :123 施法完成置满；:188-213 SpawnCrew() 蹦极空投 4 只船员
		//   显示：`召唤 11.9s`（青蓝，total = spawnTime）
		//   注：亡语（:218 SpawnDeathReplacement）是瞬发升格一只船员，本僵尸身上没有对应倒计时。
		if (AClassIs(character, charName, "ZombieCaptain"))
		{
			ACountUp(character, "召唤", "spawnTimer", "spawnTime", null, lines);
			return;
		}

		// ── 重生法师僵尸 ZombieNecromancerG ─────────────────────────────────
		//   证据：...\Chapter8\NecromancerG\Scene\TowerDefenseZombieNecromancerG.cs
		//         :11 `public double shootingTime = 10.0` / :15 `public double shootingTimer`
		//         （场景 TowerDefenseZombieNecromancerG.tscn:19 也是 shootingTime = 10.0）
		//         :114-127 BatchUpdate：:119-123 累加，:124-125 满 ⇒ ToShooting + 归零；
		//         :173 `Health(0.2 * hitpointsSave)` 真回 20% 血 ⇒ 这就是图鉴说的"每 10 秒治疗"
		//   显示：`治疗 6.5s`（青蓝，total = shootingTime）
		//   注：图鉴「亡语：30 秒后借助坑洞复活」**不在本僵尸身上** —— 亡语在
		//       :211-219 DestroySet() → `CraterCreate(nolimit: true, "CraterNG")`，
		//       30 秒是重生坑洞（CraterNG 这个独立角色）的计时，由框架的障碍物计时通道负责。
		if (AClassIs(character, charName, "ZombieNecromancerG"))
		{
			ACountUp(character, "治疗", "shootingTimer", "shootingTime", null, lines);
			return;
		}

		// ── 巨化法师僵尸 ZombieNecromancerB ─────────────────────────────────
		//   证据：...\Chapter8\NecromancerB\Scene\TowerDefenseZombieNecromancerB.cs
		//         :15 `[Export] public double shootingTime = 10.0` / :17 `public double shootingTimer`
		//         （场景 TowerDefenseZombieNecromancerB.tscn:17 也是 10.0）；:33 SIZE_UP 变大特效
		//         :106-119 BatchUpdate：:111-115 累加，:116-117 满 ⇒ ToShooting + 归零
		//   ⚠️ 图鉴（..._NECROMANCERB_HANDBOOK_EXPRESTION）写"每 12 秒"，代码/场景都是 10.0 ⇒ 以代码为准
		//      （我们读的是**实例字段**，所以永远跟真实值一致）。
		//   显示：`变大 8.3s`（青蓝，total = shootingTime）
		if (AClassIs(character, charName, "ZombieNecromancerB"))
		{
			ACountUp(character, "变大", "shootingTimer", "shootingTime", null, lines);
			return;
		}

		// ── 不可显示 / 不重复写（登记，详见 研究-僵尸CD\A批-研究.md §2.4/§2.5/§2.10/§3）──
		//  · 三叶草气球僵尸（ZombieBalloonBlover）/ 三叶草多彩气球僵尸（ZombieBalloonColourBlover）
		//    证据：Balloon\Scene\Blover\TowerDefenseZombieBalloonBlover.cs:120 `_timer.Run("Clover", 8.0)`、
		//          BalloonColour\Scene\Blover\TowerDefenseZombieBalloonColourBlover.cs:137 同款；
		//          组件集里 InstanceId = "character.timer" ⇒ 已由 ② 通用命名计时器通道显示。
		//    结论：不写分支（否则同一件事显示两行）。
		//  · 鲨鱼僵尸（ZombieShark）
		//    证据：ChomperComponent chewTime = 15.0 / chewTimer 递增（ZombieSharkDefinition.tres +
		//          ChomperComponent.cs:112/46/116、:764-789）⇒ 已由 ②.5「咀嚼」通道显示。
		//    结论：不写分支。
	}

	/// <summary>
	/// 判「是不是这个僵尸」——**按类名**（最稳）+ `charName` 前缀兜底。
	///
	/// 为什么必须按类而不是按 `charName` 精确匹配：
	///   同名盔甲变体共用**同一个** `TowerDefenseZombie*.cs/.tscn`，但 `config.name` 各不相同。
	///   实测（`CharacterResource.json` + `Packet\*.tres`）：
	///     `ZombieJackbox{BlackHelmet,Cone,Bucket,Helmet,SpecialHelmet}` → 都是 `TowerDefenseZombieJackbox`
	///     `ZombieSeaDemon{BlackHelmet,Helmet,SpecialHelmet}`         → 都是 `TowerDefenseZombieSeaDemon`
	///     `ZombieFarmerCone` → `TowerDefenseZombieFarmer`；`ZombieCaptainBucket` → `TowerDefenseZombieCaptain`
	///     `ZombieNormalDoomShroomBlackHelmet` → `TowerDefenseZombieNormalDoomShroom`
	///   ⇒ 只做精确匹配会漏掉全部带盔甲的变体。
	/// </summary>
	private static bool AClassIs(TowerDefenseCharacter character, string charName, string classKey)
	{
		try
		{
			if (character != null && !string.IsNullOrEmpty(classKey)
				&& string.Equals(character.GetType().Name, "TowerDefense" + classKey, StringComparison.Ordinal))
			{
				return true;
			}
			if (!string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(classKey)
				&& charName.StartsWith(classKey, StringComparison.Ordinal))
			{
				return true;
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 通用读法 A：**「已过」字段从 0 累加到「总时长」字段**（PVZHE 僵尸最常见的写法）。
	///   剩余 = total − elapsed；`total` 同时作为「只显示 ≥5 秒」过滤用的总时长。
	///   `doneField` 非空时：该布尔为 true（已进入爆炸/施法/死亡）⇒ 不再显示倒数。
	/// 字段取不到就**直接不显示**（结构改了宁可没有，也不瞎猜 —— 契约 §6 判据）。
	/// </summary>
	private void ACountUp(TowerDefenseCharacter character, string label,
		string elapsedField, string totalField, string doneField,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			if (lines.Count >= MaxTotalLines) { return; }
			Type t = character.GetType();
			FieldInfo fElapsed = ZombieField(t, elapsedField);
			FieldInfo fTotal = ZombieField(t, totalField);
			if (fElapsed == null || fTotal == null) { return; }
			if (!string.IsNullOrEmpty(doneField)
				&& ZombieFlag(character, ZombieField(t, doneField)))
			{
				return;
			}
			double elapsed = ZombieNum(character, fElapsed, 0.0);
			double total = ZombieNum(character, fTotal, 0.0);
			double remain = total - elapsed;
			if (!ZombieCdDisplayable(remain, total)) { return; }
			AddTimer(lines, label + " " + remain.ToString("0.0") + "s", BusyColor, total);
		}
		catch { }
	}

	/// <summary>
	/// 通用读法 B：同上，但**总时长是代码字面量**（僵尸类里没有对应字段）。
	///   A 批用到两处，都是"字面量常量"而非配置：
	///     · 海妖僵尸 `if (timer &lt; 15.0)`（TowerDefenseZombieSeaDemon.cs:114）
	///     · 催眠师僵尸 `if (waitTimer &lt; 10.0)`（TowerDefenseZombieHypnotist.cs:181）
	///   因为是**稳定常量**，直接当 `total` 传（能正确参与「只显示 ≥5 秒」过滤）；
	///   这与契约 §6 禁止的"用静态配置值假装倒计时"不同 —— 这里计时本身是运行时的，
	///   只有"上限"是常量。
	/// </summary>
	private void ACountUpFixed(TowerDefenseCharacter character, string label,
		string elapsedField, double fixedTotal,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			if (lines.Count >= MaxTotalLines || fixedTotal <= 0.0) { return; }
			FieldInfo fElapsed = ZombieField(character.GetType(), elapsedField);
			if (fElapsed == null) { return; }
			double elapsed = ZombieNum(character, fElapsed, 0.0);
			double remain = fixedTotal - elapsed;
			if (!ZombieCdDisplayable(remain, fixedTotal)) { return; }
			AddTimer(lines, label + " " + remain.ToString("0.0") + "s", BusyColor, fixedTotal);
		}
		catch { }
	}
}
