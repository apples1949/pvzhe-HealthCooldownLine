using System;
using System.Reflection;
using Godot;

/// <summary>
/// **v1.27.0「植物侧」附加行**（shovel 维护；契约：`研究-僵尸CD\00-框架与契约.md` v1.2 §11）。
///
/// 本文件是 `HealthCooldownLineEntry` 的另一个 partial 部分，只实现 cdmod-owner 在
/// `HealthCooldownLineEntry.cs` 里声明的两个植物侧扩展点：
///   · `CollectPlantExtraLines(...)`       —— 战斗内，与僵尸通道并列（入口：`Entry.cs` 里的 `TryCollectInfo` 末尾）
///   · `CollectAlmanacStaticCdPlant(...)`  —— 图鉴静态行（图鉴不创建运行时实体 ⇒ 只能写常量）
///
/// ⚠️ 本文件**不**重复定义 `AddLine` / `AddTimer` / `FindFieldAlong` / `CatOn` / 颜色常量等已有成员；
///    也**不写基类列表**（本类只允许一个基类声明，在 `Entry.cs` 里）。
///
/// ── 本轮落地的植物 ─────────────────────────────────────────────────────
/// ① **毁灭向日葵（PlantDoomSunFlower）**：血条上显示「叠种 N/2（· 产出 75）」。
/// ② **魅惑花盆（PlantHypnoPatch）**：战斗内两行（生成僵尸 X.Xs / 可啃 N 次）**由 cdmod-owner 维护**，
///    本文件只补**图鉴静态行**（`生成 30.0s（需先被啃）` + `可啃 3 次`）。
///
/// ── 证据（`unpack\` = 0.32.0.0，全部实读）────────────────────────────────
/// **毁灭向日葵** `Asset\Anime\Character\Plant\Other\DoomSunFlower\Scene\TowerDefensePlantDoomSunFlower.cs`：
///   · `:12  private int _stacks;`                     —— 叠种标记（0 = 底层/未叠种；1 = 叠上去的那一株）
///   · `:44-60 ApplyStack()`：`:46` 缩放 1 → 1.2；`:58` **`_produce.num = (_stacks == 0) ? 25 : 75`**
///   · `:29-42 Cover(...)`：被同种叠上来时 `:33 skipDestroySet = true`（底株不销毁）、`:35 _stacks = 1`、
///     `:37-40` 若底株本身已 `_stacks >= 1` 则 `Destroy()` ⇒ **最多 2 层**
///   · `:83-90 ExportVariantSave()` 把 `stacks` 写进存档；`:109-112 GetNetworkSpecialStateRevision` 也用它
///   · 配置 `Config\TowerDefensePlantDoomSunFlower.tres`：`plantCoverSelf = true`（**允许自我叠种**）、
///     `hitpoints = 1000`、`cost = 300`、`packetCooldown = 30`
///   · 产出定义 `Scene\TowerDefensePlantDoomSunFlowerComponentSet.tres`：
///     `produceType = "DoomSun"`、`sunOnceMax = 75`（满档上限）、未覆盖 `num`/`produceInterval`
///   · `Script\Component\TowerDefense\Character\ProduceComponent\ProduceComponent.cs`：
///     `:145 public int num { get; set; } = 25;`（默认值 = 未叠种档）、`:110 produceType`、`:66 timer`
/// ⇒ 结论：**"_stacks" 是私有字段（反射读），"25/75" 可由 `ProduceComponent.num` 直接读（公开属性）**，
///    两路互为兜底；层数上限 2 是 `Cover()` 里的硬逻辑（源码常量，不是配置项）。
///
/// **魅惑花盆** `Asset\Anime\Character\Plant\Cover\HypnoPatch\Scene\TowerDefensePlantHypnoPatch.cs`：
///   · 公开字段 `:19 over` / `:21 eatTime` / `:23 spawnPacket` / `:25 maxHitpoint` / `:27 spawnTimer`
///   · `:41-49 IdleProcessing`：`if (!HasGameplayAuthority || !CanRunAbility() || **eatTime &lt;= 0**) return;`
///     之后才 `spawnTimer += delta * timeScale`，`:46` **`spawnTimer >= 30.0`** 才生成（⇒ 生成间隔 30s）
///   · `:90-109 AttackDeal`：被僵尸啃（`type == "Eat"`）时 `:97 eatTime++`，`:106-109` **`eatTime >= 3` ⇒ Destroy**
///     ⇒ 最多被啃 3 次
///   · 配置 `Config\TowerDefensePlantHypnoPatch.tres`：`cost = 150`、`packetCooldown = 30.0`、
///     `plantCover = ["PlantHypnoShroom","PlantMagicRoot"]`（注意：**没有** `plantCoverSelf`）
///   · 该植物**没有**自己的 ComponentSet（走 `TowerDefensePlantComponentSet`）⇒ 30s / 3 次 **不在任何 .tres 里**
/// ⇒ 图鉴静态值只能由本文件的常量表给出（并标注源码出处）。
/// </summary>
public sealed partial class HealthCooldownLineEntry
{
	// ================================================================ 植物身份常量

	/// <summary>毁灭向日葵（`TowerDefensePlantConfig.name`，见其 `Config\TowerDefensePlantDoomSunFlower.tres`）。</summary>
	private const string PlantNameDoomSunFlower = "PlantDoomSunFlower";

	/// <summary>魅惑花盆（`Config\TowerDefensePlantHypnoPatch.tres` 的 `name`；图鉴中文名「魅惑花盆」）。</summary>
	private const string PlantNameHypnoPatch = "PlantHypnoPatch";

	/// <summary>魔法石坚果（图鉴中文名「魔法石坚果」；`Config\TowerDefensePlantMagicStoneNut.tres` 的 `name`）。</summary>
	private const string PlantNameMagicStoneNut = "PlantMagicStoneNut";

	/// <summary>
	/// 魔法石坚果的**真实裂纹数上限 = 2**。
	/// 依据：它的 `damagePointData` 只有 3 个 damage point
	/// （`...\MagicStoneNut\DamagePoint\MagicStoneNutDamagePointData.tres`）：
	///   · `Damage0`（`damagePersontage = 1.0`，贴图 `MagicStoneNut_body.png`）—— **完好**
	///   · `Damage1`（`0.6667`，`MagicStoneNut_cracked1.png`）—— 第 1 道裂纹
	///   · `Damage2`（`0.3333`，`MagicStoneNut_cracked2.png`）—— 第 2 道裂纹
	/// ⇒ 肉眼可见的"破损"只有 2 级（不是 3 级），所以文案用 `破损 N/2`。
	/// </summary>
	private const int MagicStoneNutMaxCracks = 2;

	/// <summary>毁灭向日葵的叠种层数上限（`TowerDefensePlantDoomSunFlower.cs:37-40` 的 `_stacks >= 1 ⇒ Destroy()`）。</summary>
	private const int DoomSunFlowerMaxLayers = 2;

	/// <summary>魅惑花盆的生成间隔秒数（`TowerDefensePlantHypnoPatch.cs:46` 的 `spawnTimer >= 30.0`）。</summary>
	private const double HypnoPatchSpawnInterval = 30.0;

	/// <summary>魅惑花盆最多被啃次数（`TowerDefensePlantHypnoPatch.cs:106` 的 `eatTime >= 3 ⇒ Destroy()`）。</summary>
	private const int HypnoPatchMaxEats = 3;

	// ================================================================ 运行状态（本文件私有）

	/// <summary>毁灭向日葵 `_stacks` 的反射句柄（沿类型链找，一次缓存）。</summary>
	private static FieldInfo _dsfStacksField;
	private static bool _dsfStacksProbed;

	/// <summary>「植物附加行」异常只报一次。</summary>
	private bool _plantExtraFaultLogged;

	/// <summary>每株植物只打一条诊断（受 `enableLog` 门控）。</summary>
	private readonly System.Collections.Generic.HashSet<string> _plantExtraReported =
		new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

	// ================================================================ 扩展点 ①：战斗内附加行

	/// <summary>
	/// 战斗内植物附加行。**外层已判**：总开关 `_enabled`、非僵尸/骨堆、行数未满（`&lt; MaxTotalLines`）。
	/// 本方法内部只做两件事：识别植物、按语义自行判分类开关。
	/// </summary>
	partial void CollectPlantExtraLines(ComponentManager cm, TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			if (lines == null || string.IsNullOrEmpty(charName)) { return; }

			// ① 毁灭向日葵：「叠种 N/2（· 产出 75）」
			if (PlantNameIs(charName, PlantNameDoomSunFlower))
			{
				AppendDoomSunFlowerStack(cm, character, charName, lines);
				return;
			}

			// ② 魅惑花盆（PlantHypnoPatch）：「生成僵尸 X.Xs / 可啃 N 次」由 cdmod-owner 在
			//    `HealthCooldownLineEntry.cs` 的 ②.9c 段维护（本文件**不重复实现**）。
			//    这里只补**未啃态**的那一行「生成 待啃」——见 `AppendHypnoPatchPendingLine` 的注释。
			if (PlantNameIs(charName, PlantNameHypnoPatch))
			{
				AppendHypnoPatchPendingLine(character, charName, lines);
				return;
			}

			// ③ 魔法石坚果（PlantMagicStoneNut）：破损阶段 —— 见 `AppendMagicStoneNutCracks`。
			if (PlantNameIs(charName, PlantNameMagicStoneNut))
			{
				AppendMagicStoneNutCracks(character, charName, lines);
				return;
			}
		}
		catch (Exception ex)
		{
			if (!_plantExtraFaultLogged)
			{
				_plantExtraFaultLogged = true;
				Warn("植物附加行异常（本条只报一次）：" + ex.Message);
			}
		}
	}

	/// <summary>
	/// 毁灭向日葵的叠种档位行。
	///
	/// 数据来源两条（互为兜底）：
	///   · `_stacks`（private int，`TowerDefensePlantDoomSunFlower.cs:12`）—— 0 / 1；
	///   · `ProduceComponent.num`（public，`ProduceComponent.cs:145`，默认 25）—— 未叠种 25 / 叠种 75
	///     （由 `ApplyStack()` `:58` 写入）。
	///
	/// 显示口径（与 ⑩「等级型植物」的 `等级 N/M` 对齐）：
	///   · 已叠种（`_stacks >= 1` 或 `num >= 75`）⇒ `叠种 2/2 · 产出 75`（绿 = 已吃到叠种收益）
	///   · 未叠种 ⇒ `叠种 1/2`（青蓝 = 还可以再叠一株）
	///
	/// 分类开关：**「等级与加速」(8) 或「战斗辅助」(7) 任一开启即显示**。
	///   · 归 (8) 的理由：⑩ 段把「增压大喷菇 / 豆荚壳」这类**叠种升级型**植物放在「等级与加速」；
	///     毁灭向日葵的叠种同属"叠上去 ⇒ 数值变大"的语义，放同一类用户最容易理解。
	///   · 也接受 (7)：Lead / cdmod-owner 建议归「战斗辅助」，两者默认都开，OR 起来对玩家最友好。
	/// </summary>
	private void AppendDoomSunFlowerStack(ComponentManager cm, TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		if (!CatOn(8) && !CatOn(7)) { return; }

		// ① `_stacks`（私有字段，沿类型链找，兼容未来的派生变体）
		int stacks = -1;
		try
		{
			Type t = (character != null) ? character.GetType() : null;
			if (t != null)
			{
				if (!_dsfStacksProbed)
				{
					_dsfStacksProbed = true;
					_dsfStacksField = FindFieldAlong(t, "_stacks");
				}
				if (_dsfStacksField != null)
				{
					object v = _dsfStacksField.GetValue(character);
					if (v != null) { stacks = Convert.ToInt32(v); }
				}
			}
		}
		catch { stacks = -1; }

		// ② `ProduceComponent.num`（公开属性；`InstanceId` 见其 ComponentSet：`character.produce`）
		int produceNum = -1;
		try
		{
			ProduceComponent pc = cm.GetRuntime<ProduceComponent>(ProduceInstanceId);
			if (pc != null) { produceNum = pc.num; }
		}
		catch { produceNum = -1; }

		// 两路都拿不到 ⇒ 不猜、不出行（宁可少一行，也不显示假数值）
		if (stacks < 0 && produceNum <= 0) { return; }

		bool stacked = (stacks >= 1) || (produceNum >= 75);
		string text = stacked
			? ("叠种 " + DoomSunFlowerMaxLayers + "/" + DoomSunFlowerMaxLayers
				+ ((produceNum > 0) ? (" · 产出 " + produceNum) : ""))
			: ("叠种 1/" + DoomSunFlowerMaxLayers);

		if (lines.Count < MaxTotalLines)
		{
			AddLine(lines, text, stacked ? ReadyColor : BusyColor);
		}

		if (_plantExtraReported.Add("DSF|" + (charName ?? "?")))
		{
			Info("叠种[" + (charName ?? "?") + "]：_stacks="
				+ ((stacks < 0) ? "拿不到" : stacks.ToString())
				+ " produce.num=" + ((produceNum > 0) ? produceNum.ToString() : "拿不到")
				+ " → 显示「" + text + "」");
		}
	}

	/// <summary>
	/// 魅惑花盆（`PlantHypnoPatch`）**未啃态**的补充行：`生成 待啃`（灰）。
	///
	/// ── 为什么需要它（Lead 2026-11-08 要求；用户反馈"看不到生成 CD"）────────────
	/// 游戏侧：`TowerDefensePlantHypnoPatch.cs:41`
	///   `if (!HasGameplayAuthority || !CanRunAbility() || **eatTime &lt;= 0**) return;`
	/// ⇒ 被僵尸啃过至少 1 次之前 `spawnTimer` 恒 0、`spawnPacket == null`（`:102` 只在被啃时赋值），
	///   这一刻**语义上就不该有「生成僵尸 X.Xs」倒计时**（游戏根本不会生成）。
	/// 但玩家看不到任何"生成"字样 ⇒ 观感是"这功能没做"。
	/// 所以：`eatTime == 0` 时补一行 `生成 待啃`（灰 = 未生效），被啃后由 Entry.cs ②.9c 的
	/// 「生成僵尸 X.Xs」+「可啃 N 次」接管，本方法不再出任何行（避免两处重复显示）。
	///
	/// ── 与 cdmod-owner 那段的分工（契约 v1.2 §11）────────────────────────
	///   · 倒计时行（`生成僵尸 X.Xs`）：Entry.cs ②.9c，**本文件不实现**；
	///   · 未啃态语义行（`生成 待啃`）：本文件（因为他那轮不改 Entry.cs，见其报告 §8 遗留）；
	///   · 二者用「列表里是否已有以 `生成` 开头的行」去重，谁先到都行。
	/// </summary>
	private void AppendHypnoPatchPendingLine(TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		// 已有「生成…」行（被啃后的倒计时）⇒ 不重复
		for (int i = 0; i < lines.Count; i++)
		{
			string tx = lines[i].Text;
			if (!string.IsNullOrEmpty(tx) && tx.StartsWith("生成", StringComparison.Ordinal)) { return; }
		}

		// `eatTime` 用 `FindFieldAlong`（沿类型链找），比 `DeclaredOnly + GetType()` 稳
		int eat = -1;
		try
		{
			Type t = (character != null) ? character.GetType() : null;
			if (t != null)
			{
				FieldInfo fEat = FindFieldAlong(t, "eatTime");
				if (fEat != null)
				{
					object v = fEat.GetValue(character);
					if (v != null) { eat = Convert.ToInt32(v); }
				}
			}
		}
		catch { eat = -1; }

		// 只处理"确定还没被啃过"（0）这一种情况；拿不到 / 已啃（>0）都不出行（不猜）
		if (eat != 0) { return; }

		if (lines.Count < MaxTotalLines)
		{
			AddLine(lines, "生成 待啃", DisabledColor);
		}

		if (_plantExtraReported.Add("HP|" + (charName ?? "?")))
		{
			Info("魅惑花盆[" + (charName ?? "?") + "]：eatTime=0（还没被啃过）⇒ 增补「生成 待啃」"
				+ "（被啃后由 ②.9c 接管「生成僵尸 X.Xs」）");
		}
	}

	/// <summary>
	/// 魔法石坚果（`PlantMagicStoneNut`）的**破损阶段**行：`破损 N/2`。
	///
	/// ── 取数（首选 + 兜底，都是源码实读，不猜）────────────────────────────
	/// ① **首选 `instance.damagePointIndex`**（`public int`，`TowerDefenseCharacterInstance.cs:117`；
	///    `TowerDefenseCharacter.cs:379 public TowerDefenseCharacterInstance instance;`）。
	///    它在受击时由 `TowerDefenseCharacterInstance.cs:1400-1428` 推进：
	///    `ratio = (hitpoints - hitpointsNearDeath) / (hitpointsSave - hitpointsNearDeath)`，
	///    `while (ratio <= _damagePointPercentages[damagePointIndex]) { …触发该 damage point…; damagePointIndex++; }`
	///    ⇒ 语义 = **已触发的 damage point 个数**（0 = 一次没触发；1 = 只触发 Damage0；2 = 加了 Damage1；3 = 加了 Damage2）。
	/// ② **兜底：按同一公式自己算**（`ComputeMagicStoneNutStage`）——直接读
	///    `config.damagePointData.damagePointList[i].damagePersontage`
	///    （`CharacterDamagePointData.cs:23` + `CharacterDamagePointConfig.cs:14`，均 public），
	///    完全复刻上面那段 while 循环；`instance.damagePointIndex` 读不到时才走这条路。
	///
	/// ── 映射（为什么是 N/2 而不是 N/3）──────────────────────────────────────
	///    `damagePointIndex = 0` → 还没受击            → `破损 0/2`
	///    `damagePointIndex = 1` → 只触发 Damage0（**完好贴图** `MagicStoneNut_body.png`）→ `破损 0/2`
	///    `damagePointIndex = 2` → Damage1（`_cracked1.png`，第 1 道裂纹）        → `破损 1/2`
	///    `damagePointIndex = 3` → Damage2（`_cracked2.png`，第 2 道裂纹）        → `破损 2/2`
	///    3 个 damage point 里 `Damage0` 是"完好"档 ⇒ 真实裂纹只有 2 级。
	///
	/// ── 分类开关 ────────────────────────────────────────────────────────
	/// 归「战斗辅助」`CatOn(7)` **或**「等级与加速」`CatOn(8)`：破损阶段既不是计时也不是等级，
	/// 玩家最可能在「战斗辅助」里找；同时沿用与「叠种」一致的 OR 口径，避免关掉一个就完全看不到。
	///
	/// ── 为什么不加「加速 5.0s」─────────────────────────────────────────────
	/// `CastHaste()`（`:62-97`）给本方 `IsMagicPhysique` 植物加的是
	/// `TowerDefenseCharacterBuffCoffee { key="MagicStoneNutHaste", timeScaleValue=3.0, time=5.0 }`；
	/// 而 CD Mod 既有的 ⑨ 段已经按 buff 的 `timeScaleValue` 显示「加速 +N%」（`Entry.cs:3168-3172 / :3424`）
	/// ⇒ 这 5 秒加速**本来就会显示**，再单加一行是重复，且会碰到已定稿的加速通道（Lead 要求别动）。
	/// </summary>
	private void AppendMagicStoneNutCracks(TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		if (!CatOn(7) && !CatOn(8)) { return; }

		int stage = -1;   // = damagePointIndex（0..3）

		// ① 首选：公开字段 instance.damagePointIndex
		try
		{
			if (character != null && character.instance != null)
			{
				stage = character.instance.damagePointIndex;
			}
		}
		catch { stage = -1; }

		// ② 兜底：按 HP 比例 + damagePointData 阈值复算（与游戏内那段 while 完全同式）
		if (stage < 0)
		{
			try { stage = ComputeMagicStoneNutStage(character); }
			catch { stage = -1; }
		}

		if (stage < 0) { return; }   // 两路都拿不到 ⇒ 不出行（不猜）

		// Damage0（persontage 1.0）是"完好"档 ⇒ 裂纹数 = damagePointIndex - 1
		int cracks = stage - 1;
		if (cracks < 0) { cracks = 0; }
		if (cracks > MagicStoneNutMaxCracks) { cracks = MagicStoneNutMaxCracks; }

		string text = "破损 " + cracks + "/" + MagicStoneNutMaxCracks;
		if (lines.Count < MaxTotalLines)
		{
			AddLine(lines, text, (cracks <= 0) ? ReadyColor : BusyColor);
		}

		if (_plantExtraReported.Add("MSN|" + (charName ?? "?")))
		{
			Info("破损[" + (charName ?? "?") + "]：damagePointIndex=" + stage
				+ " → " + text + "（0/2 完好 · 1/2 cracked1 · 2/2 cracked2）");
		}
	}

	/// <summary>
	/// 兜底：没有 `damagePointIndex` 时，按游戏自己的公式复算"已触发几个 damage point"。
	/// 复刻 `TowerDefenseCharacterInstance.cs:1400-1403`：
	///   `ratio = (hitpoints - hitpointsNearDeath) / (hitpointsSave - hitpointsNearDeath)`
	///   `while (i &lt; percentages.Count &amp;&amp; ratio &lt;= percentages[i]) i++;`
	/// 读不到必要字段返回 -1（调用方据此不出行）。
	/// </summary>
	private static int ComputeMagicStoneNutStage(TowerDefenseCharacter character)
	{
		if (character == null) { return -1; }
		var inst = character.instance;
		var cfg = character.config;
		if (inst == null || cfg == null) { return -1; }
		var data = cfg.damagePointData;
		if (data == null) { return -1; }
		var list = data.damagePointList;
		if (list == null || list.Count <= 0) { return -1; }

		double span = inst.hitpointsSave - inst.hitpointsNearDeath;
		if (span <= 0.0) { return -1; }
		double ratio = (inst.hitpoints - inst.hitpointsNearDeath) / span;

		int i = 0;
		while (i < list.Count && ratio <= list[i].damagePersontage) { i++; }
		return i;
	}

	// ================================================================ 扩展点 ②：图鉴静态行
	/// <summary>
	/// 图鉴静态行（植物侧）。**外层已判**总开关 + 「图鉴显示冷却」（`showAlmanacCd`），
	/// 这里**不要**再判开关；只用 `AddLine`（Total = 0，不参与「只显示 ≥5 秒」），最多 3 行。
	///
	/// 图鉴里没有运行时实体（`InformationPanel` 只会实例化"编辑态"预览，状态机不 dispatch ⇒
	/// 计时器永不推进），所以这里写的是**配置/源码里的"满档值"**，不写"剩余"。
	/// </summary>
	partial void CollectAlmanacStaticCdPlant(string charName, string saveKey,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			if (lines == null) { return; }

			// ① 毁灭向日葵：叠种上限 2 层；产出档位 25 → 75（`ApplyStack()` :58）
			if (PlantStaticIs(charName, saveKey, PlantNameDoomSunFlower))
			{
				PlantStaticAdd(lines, "叠种 " + DoomSunFlowerMaxLayers + " 层 · 产出 25→75");
				return;
			}

			// ② 魅惑花盆：生成间隔 30s（`:46`）、最多被啃 3 次（`:106`）；
			//    注意「被啃 1 次后才开始累计生成计时」是硬条件（`:41` 的 `eatTime <= 0 ⇒ return`）。
			if (PlantStaticIs(charName, saveKey, PlantNameHypnoPatch))
			{
				PlantStaticAdd(lines, "生成 " + HypnoPatchSpawnInterval.ToString("0.0") + "s（需先被啃）");
				PlantStaticAdd(lines, "可啃 " + HypnoPatchMaxEats + " 次");
				return;
			}
		}
		catch { }
	}

	// ================================================================ 本文件私有小助手
	// （刻意不与 ZombieCdA/B 的 `ZbA*` / `ZbB*` 助手重名，互不干扰）

	/// <summary>植物身份匹配：`characterConfig.name` 逐字相等。</summary>
	private static bool PlantNameIs(string charName, string want)
	{
		return !string.IsNullOrEmpty(charName) && string.Equals(charName, want, StringComparison.Ordinal);
	}

	/// <summary>图鉴静态行身份匹配：先比 `characterConfig.name`，再比 packet 的 `saveKey`（两者通常同名）。</summary>
	private static bool PlantStaticIs(string charName, string saveKey, string want)
	{
		if (PlantNameIs(charName, want)) { return true; }
		return PlantNameIs(saveKey, want);
	}

	/// <summary>
	/// 图鉴静态行写入：一律 `ReadyColor` + `AddLine`（`Total = 0` ⇒ 不参与「只显示 ≥5 秒」过滤），
	/// 并提前挡住 3 行上限（`AlmanacMaxRows`，契约 v1.2：图鉴叠加层只接 3 行）。
	/// </summary>
	private static void PlantStaticAdd(
		System.Collections.Generic.List<(string Text, Color Color, double Total)> ls, string text)
	{
		if (ls == null || string.IsNullOrEmpty(text)) { return; }
		if (ls.Count >= AlmanacMaxRows) { return; }
		AddLine(ls, text, ReadyColor);
	}
}
