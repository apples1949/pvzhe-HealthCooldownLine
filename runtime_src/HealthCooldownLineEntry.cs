using System;
using System.Reflection;
using Godot;
using PVZHE.ModEditor.ModSystem;

/// <summary>
/// 「血量显示新增一行：角色计时器倒计时」的托管运行时入口。
///
/// 需求：在原有血条（护盾 / 头盔 / 本体 三行）下方**再加一行**，
///       显示**该角色当前武器的装填剩余时间**（加农炮 <c>CannonComponent.restTime</c>）；
///       **只在有加农炮组件的角色上出现**（全库 10 个），且只在真的在装填时显示。
///
/// ── 为什么必须写 C# ─────────────────────────────────────────────
/// 这一行要**每帧读运行时状态**再写进 Label。数据侧（.tres/.tscn）只能定义静态字段，
/// 没有任何「把 A 组件的运行时数值写进 B 组件 UI」的机制，所以纯资源做不到。
///
/// ── 实读到的引擎事实（都来自 PlantsVsZombies.dll 元数据 + 解包数据，不是猜的）──
///   · 加农炮的「装填时间」是 <c>CannonComponent.restTime</c>（首次是 firstRestTime），
///     配置在共享定义 <c>Script/Component/TowerDefense/Character/CannonComponent/Definitions/
///     CobCannonDefinition.tres</c>（实测 restTime = 35.0）；
///     <b>运行时的剩余量在 private 字段 <c>_restTimerRemaining</c></b>，另有
///     <c>_restTimerRunning</c> / <c>_restTimerPaused</c> / <c>_pausedRestTimerRemaining</c>。
///     组件 InstanceId = "character.cannon"。
///   · 用该组件的角色共 10 个：CobCannon(玉米加农炮)、PeaCannon、IceCobCannon、DoomCobCannon、
///     GarCobCannon、PotatoCobCannon、PumpkinCannon、PowShroom、MagnetShroomDM、
///     ZombieNormalCobCannon。
///
/// ── 铁律（技能文档 §1）─────────────────────────────────────────
/// Initialize / OnAllModsLoaded / Shutdown **一律不许抛**：
/// TryInitializeRuntimeEntry 失败 → 整包无条件回滚（不受 runtimeAssemblyPolicy 保护）；
/// NotifyAllModsLoaded 失败 → State = Failed。所以三个回调全部 try/catch 吞异常。
///
/// ── 反射为什么用在这里 ────────────────────────────────────────
/// 字段类型（timerWaitTime 是 float 还是 double、bodyHitpointLabel 是不是 Label）
/// 没有从元数据里逐个确认过，用强类型访问一旦猜错就是编译期硬失败；
/// 用反射取字段值 + Convert.ToDouble，编译期零假设，运行时最多退化成「不显示」。
/// 技能文档 §6 也提醒：反射接缝要单独加闸门（见 check_gates.cs）。
/// </summary>
public sealed class HealthCooldownLineEntry : IXWModRuntimeEntry
{
	private const string LogPrefix = "[HealthCooldownLine] ";

	/// <summary>CannonComponent（加农炮）在 ComponentSet 里的 InstanceId。</summary>
	private const string CannonInstanceId = "character.cannon";

	/// <summary>自建行 Label 的节点名前缀（v1.7.0：每行一个 Label：ModCooldownOwnLine0..5）。</summary>
	private const string OwnLineNamePrefix = "ModCooldownOwnLine";

	/// <summary>单行高度（px）——各行的垂直排布步进。</summary>
	private const float LineHeight = 20f;

	// ---- 各数据源的 InstanceId（全部从对应 Definition.tres 实读确认）----
	private const string PotatoInstanceId = "character.potato";
	private const string BowlingInstanceId = "character.bowling";
	private const string CatapultInstanceId = "character.catapult";
	private const string TimerInstanceId = "character.timer";
	/// <summary>ChomperComponent（大嘴花咀嚼）的 InstanceId——8 个大嘴花系定义全部为它。</summary>
	private const string ChomperInstanceId = "character.chomper";
	/// <summary>GrowUpComponent（长大）的 InstanceId——9 个角色（阳光菇系/阳光豆/VIP坚果/小鬼僵尸…）。</summary>
	private const string GrowUpInstanceId = "character.grow_up";
	/// <summary>PeriodicAreaEventComponent（周期区域事件）的 InstanceId——冰坚果、伞火把。</summary>
	private const string PeriodicInstanceId = "character.periodic_area_event";
	/// <summary>MagnetComponent（磁力消化）的 InstanceId——9 个磁力系角色（含道具）。</summary>
	private const string MagnetInstanceId = "character.magnet";
	/// <summary>ProduceComponent（阳光/金币/卡包产出）的 InstanceId——29 个产出定义共用。</summary>
	private const string ProduceInstanceId = "character.produce";
	/// <summary>TanglekelpComponent（缠绕海草抓取）的 InstanceId——海草系 6 个角色。</summary>
	private const string TanglekelpInstanceId = "character.tanglekelp";
	/// <summary>GravebusterComponent（墓碑破坏者吞噬墓碑）的 InstanceId。</summary>
	private const string GravebusterInstanceId = "character.gravebuster";

	/// <summary>计时器多行显示的上限（防一个角色刷一屏）。</summary>
	private const int MaxTimerLines = 3;
	/// <summary>该角色面板的总行数上限（6 行 ≈ Label 高度 140px 的容量）。</summary>
	private const int MaxTotalLines = 6;

	/// <summary>扫描节流：每 N 帧扫一次全树。血条本身也是节流刷新的，没必要每帧扫。</summary>
	private const int ScanStride = 10;

	// ---- 设置页开关（运行时注入，不覆盖任何游戏资源）----
	/// <summary>BattleOption 对话框根节点名（Prefab/GUI/DialogBox/BattleOption/BattleOption.tscn）。</summary>
	private const string OptionRootName = "BattleOption";
	/// <summary>注入的 CheckBox 节点名（幂等判断用）。</summary>
	private const string OptionCheckBoxName = "ModCooldownCheckBox";
	/// <summary>「只显示 ≥5 秒」CheckBox 的节点名（v1.19.0）。</summary>
	private const string OnlyLongCheckBoxName = "ModOnlyLongCheckBox";
	/// <summary>两列布局的外层水平容器名（幂等判断用）。</summary>
	private const string OptionColumnsName = "ModOptionColumns";
	/// <summary>本 Mod 所在的新列（右列）名。</summary>
	private const string OptionModColumnName = "ModOptionColumn";
	/// <summary>署名/防冒用声明行的节点名。</summary>
	private const string OptionAuthorLabelName = "ModAuthorLabel";
	/// <summary>开关状态持久化文件（Godot user:// = %APPDATA%\Godot\app_userdata\植物大战僵尸杂交版\）。</summary>
	private const string ConfigPath = "user://HealthCooldownLine.cfg";
	/// <summary>
	/// 缠绕海草"抓取 X/Y"显示硬编码关闭（2026-09-27 用户要求：关闭显示、保留代码）。
	/// 需要恢复时把此值改为 true 即可（代码与诊断探针都完整保留）。
	/// </summary>
	private const bool EnableGrabLine = false;
	/// <summary>
	/// 疯狂海草"点击 X.Xs / 可点击"显示硬编码关闭（2026-09-27 用户要求：关闭显示、保留代码）。
	/// </summary>
	private const bool EnableKelpClick = false;
	/// <summary>
	/// 周期区域事件（"触发 X.Xs"）显示硬编码关闭（2026-09-27 用户要求：关闭显示、保留代码）。
	/// </summary>
	private const bool EnablePeriodEvent = false;
	/// <summary>
	/// **双格植物名单**（v1.11.6）——这类植物占两格、角色原点在左格，行标签默认只盖第一格；
	/// 名单内的植物显示**向右偏移半格**以"居中到两格中间"。
	/// 卡牌特性栏标注"双格"的族群：**全部加农炮变体**（CobCannon/IceCobCannon 寒冰加农炮/
	/// DoomCobCannon 毁灭加农炮/GarCobCannon/PotatoCobCannon）+ **HotDog 热狗射手**。
	/// （"CobCannon"片段精确匹配加农炮系、不误伤 PumpkinCannon 等）
	/// </summary>
	private static readonly string[] DoubleWidthPlants = { "CobCannon", "HotDog" };
	/// <summary>双格植物的水平居中偏移（半格宽，像素；实测可调）。</summary>
	private const float DoubleWidthShift = 50f;

	/// <summary>是否双格植物（名单片段匹配）。</summary>
	private static bool IsDoubleWidth(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return false;
		}
		for (int i = 0; i < DoubleWidthPlants.Length; i++)
		{
			if (name.Contains(DoubleWidthPlants[i]))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 是否"障碍物/物件"类角色（v1.13.0）——坑洞（Crater 系）、墓碑（Gravestone 系，含
	/// TimeBomb/TimeNuke）、大火（ItemMegaFire）。它们的计时归"障碍物消失"开关，
	/// 并额外显示血量（"障碍物血量"开关）。
	/// </summary>
	private static bool IsObstacleCharacter(TowerDefenseCharacter ch)
	{
		try
		{
			string n = ch.GetType().Name;
			return n.Contains("Crater") || n.Contains("GraveStone") || n.Contains("Gravestone")
				|| n.Contains("TimeBomb") || n.Contains("TimeNuke") || n.Contains("MegaFire");
		}
		catch { return false; }
	}

	/// <summary>
	/// **广义障碍物**（v1.13.9）：非植物 / 非僵尸 / 非割草机，且**身上有 HurtComponent（有血量）**
	/// 的角色都算——覆盖垃圾桶（TrashBin）等名单外的障碍物。血量显示用此判定；
	/// "障碍物消失"计时分流仍用 `IsObstacleCharacter`（5 类精确名单）。
	/// </summary>
	private static bool IsObstacleLike(TowerDefenseCharacter ch)
	{
		try
		{
			string tn = ch.GetType().Name;
			if (tn.Contains("Plant") || tn.Contains("Zombie") || tn.Contains("Mower")
				|| tn.Contains("Crater"))
			{
				return false;   // v1.14.0：坑洞也排除（不显示血量）
			}
			// ★★ v1.16.4（用户："障碍物血量会显示普通罐子的血量，不应该显示；
			//   植物罐子和僵尸罐子目前是正常不显示的"）：
			//   罐子系角色类名是 `TowerDefenseVaseNormal` / `TowerDefenseVasePlant` /
			//   `TowerDefenseVaseZombie` / `TowerDefenseVaseSquashBlack`（基类 `TowerDefenseVase`）。
			//   `VasePlant`/`VaseZombie` 因为名字里带 Plant/Zombie 被上面拦下了，
			//   **`VaseNormal`（普通罐子）名字里两样都没有** ⇒ 漏网，把罐子的"血量"也显示了。
			//   ⇒ 统一按 `Vase` 排除（罐子是"容器"，不是有血量的障碍物）。
			if (tn.Contains("Vase"))
			{
				return false;
			}
			ComponentManager cmL = ch.componentManager;
			if (cmL == null)
			{
				return false;
			}
			return FindComponentOfType<HurtComponent>(cmL) != null;
		}
		catch { return false; }
	}

	/// <summary>
	/// ★ v1.15.8：**Godot Timer 型"消失倒计时"**——某些限时物件（如蜘蛛网
	/// `TowerDefenseBungiTargetSP`，源码：`public double lifetime = 15.0;` + 一个
	/// `Timer{WaitTime=lifetime}` 子节点，超时即自己消失）**不走 `CharacterTimerComponent`**，
	/// 所以原来的计时器分支读不到。这里直接找角色节点下的 **Godot `Timer` 子节点**，
	/// 读其 **`TimeLeft`（剩余秒）/`WaitTime`（总时长）**。
	/// 归"障碍物消失"开关（CatOn(1)）；仅对"障碍物类"生效，避免误报。
	/// </summary>
	private void TryCollectGodotTimerLine(TowerDefenseCharacter character,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			// ★ v1.16.0：Timer 归属与标签的精细处理（用户要求）：
			//   ① **僵尸身上的 Timer 不显示**——实测那类是"外来特效计时"（大蒜鸟 GarlicBird 把
			//      5s 特效 Timer `AddChild` 到僵尸身上），不是僵尸自己的机制；全库无僵尸类自建 Timer，
			//      所以"僵尸节点上的 Timer"一律跳过是安全的；
			//   ② **南瓜灯 PumpLantern** 的 Timer 语义是"每 50 秒生成一次护盾"⇒ 标签用 **"护盾"**；
			//      其余角色用中性"计时"（蜘蛛网=存在时长等）。
			string cn16 = SafeCharName(character) ?? "";
			if (cn16.IndexOf("Zombie", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return;   // 僵尸身上的 Timer 不显示
			}
			Godot.Timer tmr = null;
			int n = character.GetChildCount();
			for (int i = 0; i < n; i++)
			{
				Node c = character.GetChild(i, false);
				if (c is Godot.Timer t2 && GodotObject.IsInstanceValid(t2))
				{
					tmr = t2;
					break;
				}
			}
			if (tmr == null)
			{
				return;
			}
			double left = tmr.TimeLeft;
			double wait = tmr.WaitTime;
			if (_obstacleHpReported.Add("TMR|" + cn16 + "|" + wait.ToString("0.###")))
			{
				Info("计时节点[" + cn16 + "]：Timer WaitTime="
					+ wait.ToString("0.###") + " TimeLeft=" + left.ToString("0.###"));
			}
			if (CatOn(1) && left > 0.0 && lines.Count < MaxTotalLines)
			{
				string tag16 = "计时";
				if (cn16.IndexOf("PumpLantern", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					tag16 = "护盾";
				}
				AddTimer(lines, tag16 + " " + left.ToString("0.0") + "s", BusyColor, wait);
			}
		}
		catch { }
	}

	/// <summary>
	/// 障碍物血量（v1.13.0）：读游戏血条组件 `ShowHealthComponent._bodyDisplayText`
	/// （游戏当前显示的本体血量文本，如 "1500/1500"）→ 显示"血量 X"。
	/// </summary>
	private void TryCollectObstacleHp(TowerDefenseCharacter character, string charName,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			// v1.14.0：**坑洞（Crater 系）不显示血量**（用户要求——坑洞没有"血量"概念，
			//   其"消失"计时仍由"障碍物消失"开关正常显示）。
			string tn0 = character.GetType().Name;
			if (tn0.Contains("Crater"))
			{
				return;
			}
			if (!IsObstacleCharacter(character) && !IsObstacleLike(character))
			{
				return;
			}
			// ★ v1.16.1：**护盾类障碍物不再显示血量**——游戏原生血条已经显示它们
			//   （用户要求"护盾障碍物就不用显示血量了，游戏本来就有显示"）。
			//   名单（类名片段）：`Sheild`/`Shield`（`TargetSheild`、`Item/Sheild`）。
			// ★ v1.16.2：**符石类（RuneStone）从本名单移除**——用户明确"符石类要显示血量"，
			//   符石不是护盾，游戏也不会给它画原生血条，必须照常输出 `血量 X/Y`。
			try
			{
				string tn17 = character.GetType().Name;
				if (tn17.IndexOf("Sheild", StringComparison.OrdinalIgnoreCase) >= 0
					|| tn17.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					if (_obstacleHpReported.Add("SHLD|" + (charName ?? "?")))
					{
						Info("血量跳过[" + (charName ?? "?") + "]：护盾类（游戏自带血条，不重复显示）");
					}
					return;
				}
			}
			catch { }
			ComponentManager cmH = character.componentManager;
			if (cmH == null)
			{
				return;
			}
			// ★★ v1.14.3：**能挡子弹 = 挂了 BlockComponent**（由新解包的 C# 源码**定案**）：
			//   `Script/Component/TowerDefense/Character/BlockComponent/BlockComponent.cs`：
			//   `public sealed class BlockComponent : CharacterComponentRuntime,
			//    IProjectileZoneBinding, IProjectileZone, ICatapultProjectileBlockZone`
			//   —— 它才是"子弹是否被挡住"的权威实现（字段：`blockType`/`extendGrid`/`checkShape`/
			//   `checkLadder`/`reboundProjectile`/`WorldRect`/`RowSpan`）。
			//   ⇒ 没有 BlockComponent 的障碍物**不挡子弹** → 不显示血量。
			//   （v1.14.2 用的"命中盒字段"判据已被替换——那是间接信号，这是权威判据。）
			// ★★ v1.15.5："能被攻击"判据升级为**目标系统口径**（源码 `TargetSystem.cs` 693 行——
			//   投射物选目标的完整条件）：
			//     instance.canBeCollection && !instance.invincible && !instance.hologram
			//     && targetRegistrationComponent.canProjectileCheck
			//   叠加原有的"命中盒有效"（`_hitBoxAvailable && _hitBoxDefaultEnabled` 且未抑制）。
			//   实测背景：**普通墓碑**（无命中盒 + 抑制=4）已拦下；**科技地砖 FloorQX**
			//   （hitBoxAvail=True）仍显示 ⇒ 需要 instance 层标志（无敌/幻象/可选中）才能覆盖它。
			//   安全兜底：字段取不到一律**放行**。诊断打印**全部原始值**，便于按实测校准。
			try
			{
				FieldInfo fa16 = FindFieldAlong(character.GetType(), "_hitBoxAvailable");
				FieldInfo fe16 = FindFieldAlong(character.GetType(), "_hitBoxDefaultEnabled");
				FieldInfo fs16 = FindFieldAlong(character.GetType(), "_hitBoxSuppression");
				bool hbOk = true;
				string dbgHb = "";
				if (fa16 != null && fe16 != null)
				{
					bool a16 = (fa16.GetValue(character) is bool b16a) && b16a;
					bool e16 = (fe16.GetValue(character) is bool b16e) && b16e;
					int s16 = (fs16 != null && fs16.GetValue(character) != null)
						? Convert.ToInt32(fs16.GetValue(character)) : 0;
					hbOk = a16 && e16 && s16 <= 0;
					dbgHb = " hitBox=" + a16 + "/" + e16 + "/" + s16;
				}
				// instance 层（血量真身同一路径：HurtComponent._damageInstance）
				bool instOk = true;
				string dbgInst = "";
				try
				{
					HurtComponent hc16 = FindComponentOfType<HurtComponent>(cmH);
					object inst16 = (hc16 != null && _fDamageInstance != null)
						? _fDamageInstance.GetValue(hc16) : null;
					if (inst16 != null)
					{
						const System.Reflection.BindingFlags BF16 = System.Reflection.BindingFlags.Instance
							| System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
						FieldInfo fcbc = inst16.GetType().GetField("canBeCollection", BF16);
						FieldInfo finv = inst16.GetType().GetField("invincible", BF16);
						FieldInfo fhol = inst16.GetType().GetField("hologram", BF16);
						if (fcbc != null && finv != null && fhol != null)
						{
							bool cbc = (fcbc.GetValue(inst16) is bool v1) && v1;
							bool inv = (finv.GetValue(inst16) is bool v2) && v2;
							bool hol = (fhol.GetValue(inst16) is bool v3) && v3;
							// ★ v1.15.7：**去掉 `invincible` 条件**——实测"蜘蛛网类"（BungiTargetSP）
							//   `invincible=True` 但仍需要有血量显示（游戏的"无敌"多指"不可被普通攻击
							//   打死"，并不代表"没有血量"）。改为：可选中 && 非幻象。
							//   （普通墓碑靠 hitBox=False 拦、科技地砖靠 canBeCollection=False 拦。）
							instOk = cbc && !hol;
							dbgInst = " canBeCollection=" + cbc + " invincible=" + inv
								+ "(不参与判定) hologram=" + hol;
						}
					}
				}
				catch { }
				bool attackable = hbOk && instOk;
				if (_obstacleHpReported.Add("HB16|" + (charName ?? "?")))
				{
					Info("可攻击判定[" + (charName ?? "?") + "]：" + dbgHb + dbgInst
						+ " → 可攻击=" + attackable);
				}
				if (!attackable)
				{
					return;   // 不可被攻击 → 不显示血量
				}
			}
			catch { }
			string hpTxt = null;
			string dbgState = "";
			string dbgLabel = "";
			string dbgText = "";
			string dbgHurt = "";
			// ★★ v1.13.8：**血量真身**——`HurtComponent._damageInstance`（TowerDefenseCharacterInstance）
			//   的 `hitpoints`（当前）/ `hitpointsBase`（上限）。**任何时刻可读、与血条显示无关**！
			//   （这是"仔细看炸弹/墓碑"后找到的：HurtComponent 是受伤组件，管线持有角色实例血量的正本。）
			try
			{
				HurtComponent hcH = FindComponentOfType<HurtComponent>(cmH);
				if (hcH != null && _fDamageInstance != null)
				{
					object inst = _fDamageInstance.GetValue(hcH);
					if (inst != null)
					{
						const System.Reflection.BindingFlags BF2 = System.Reflection.BindingFlags.Instance
							| System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
						FieldInfo fHp = inst.GetType().GetField("hitpoints", BF2);
						FieldInfo fHpB = inst.GetType().GetField("hitpointsBase", BF2);
						double hp = (fHp != null && fHp.GetValue(inst) != null)
							? Convert.ToDouble(fHp.GetValue(inst)) : 0.0;
						double hpMax = (fHpB != null && fHpB.GetValue(inst) != null)
							? Convert.ToDouble(fHpB.GetValue(inst)) : 0.0;
						dbgHurt = " hurt=" + hp.ToString("0") + "/" + hpMax.ToString("0");
						if (hpMax > 0.0)
						{
							hpTxt = hp.ToString("0") + "/" + hpMax.ToString("0");
						}
					}
				}
			}
			catch { }
			ShowHealthComponent shc = FindComponentOfType<ShowHealthComponent>(cmH);
			if (shc == null)
			{
				try { shc = character.showHealthComponent; } catch { }
			}
			if (shc != null)
			{
				// ① 状态结构 `_bodyLabelState`（HealthLabelState：Current/Maximum，Initialized 时才可信）
				try
				{
					if (_fBodyState != null)
					{
						object st = _fBodyState.GetValue(shc);
						if (st != null)
						{
							const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Instance
								| System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
							FieldInfo fi0 = st.GetType().GetField("Initialized", BF);
							FieldInfo fc = st.GetType().GetField("Current", BF);
							FieldInfo fm = st.GetType().GetField("Maximum", BF);
							bool init = (fi0 != null) && (fi0.GetValue(st) is bool ib) && ib;
							double cur = (fc != null && fc.GetValue(st) != null)
								? Convert.ToDouble(fc.GetValue(st)) : 0.0;
							double max = (fm != null && fm.GetValue(st) != null)
								? Convert.ToDouble(fm.GetValue(st)) : 0.0;
							dbgState = " state=" + cur.ToString("0") + "/" + max.ToString("0")
								+ " init=" + init;
							if (init && max > 0.0)
							{
								hpTxt = cur.ToString("0") + "/" + max.ToString("0");
							}
						}
					}
				}
				catch { }
				// ② 血条本体 Label 的文本（游戏常显 "HP:x/y"）
				try
				{
					if (_fBodyLabel != null)
					{
						Label lb = _fBodyLabel.GetValue(shc) as Label;
						if (lb != null && GodotObject.IsInstanceValid(lb))
						{
							dbgLabel = " label=\"" + (lb.Text ?? "") + "\"";
							if (string.IsNullOrEmpty(hpTxt) && !string.IsNullOrEmpty(lb.Text))
							{
								hpTxt = lb.Text;
							}
						}
					}
				}
				catch { }
				// ③ 显示缓存文本
				try
				{
					if (_fBodyText != null)
					{
						string tx = _fBodyText.GetValue(shc) as string;
						dbgText = " text=\"" + (tx ?? "") + "\"";
						if (string.IsNullOrEmpty(hpTxt) && !string.IsNullOrEmpty(tx))
						{
							hpTxt = tx;
						}
					}
				}
				catch { }
			}
			// v1.13.7：血量数值平时读不到（血条刷新后才写入）——改为**值出现/变化即记录**，
			//   配合"显示随值出现"：障碍物被攻击/悬停、血条一刷新就能看到"血量 X/Y"。
			if (!string.IsNullOrEmpty(hpTxt))
			{
				if (_obstacleHpReported.Count < 80
					&& _obstacleHpReported.Add((charName ?? "?") + "|" + hpTxt))
				{
					Info("障碍物血量[" + (charName ?? "?") + "]：值=\"" + hpTxt + "\""
						+ dbgHurt + dbgState + dbgLabel);
				}
			}
			else if (_obstacleHpReported.Add("EMPTY|" + (charName ?? "?")))
			{
				Info("障碍物血量[" + (charName ?? "?") + "]：（当前仍空）"
					+ dbgHurt + dbgState + dbgLabel + dbgText);
			}
			if (CatOn(2) && !string.IsNullOrEmpty(hpTxt) && lines.Count < MaxTotalLines)
			{
				AddLine(lines, "血量 " + hpTxt, ReadyColor);
			}
		}
		catch { }
	}
	/// <summary>开关当前状态（默认开，兼容老用户）。</summary>
	private bool _enabled = true;
	// ---- 6 类功能独立开关（v1.9.0，默认全开；键名见 FeatureKeys）----
	/// <summary>装填与核能（加农炮装填、核弹磁力菇核能充能）。</summary>
	private bool _showLoad = true;
	/// <summary>障碍物消失（坑洞/墓碑/定时炸弹/定时核弹/大火的计时显示）。</summary>
	private bool _showObstacle = true;
	/// <summary>障碍物血量（障碍物的血量文本）。</summary>
	private bool _showObstacleHp = true;
	/// <summary>选卡栏种植 CD。</summary>
	private bool _showCardCd = true;
	/// <summary>生成计时（Spawn 键——植物/僵尸版的"生成 X.Xs"）。</summary>
	private bool _showSpawn = true;
	/// <summary>角色计时器其他CD（除障碍物与 Spawn 外的命名计时器）。</summary>
	private bool _showOtherTimer = true;
	/// <summary>成长与充能（长大、蓄能咖啡豆充能）。</summary>
	private bool _showGrowth = true;
	/// <summary>产出倒计时（阳光/金币/卡包）。</summary>
	private bool _showProduce = true;
	/// <summary>战斗辅助（咀嚼、消化、准备、篮球、投石车、啃碑）。</summary>
	private bool _showCombat = true;
	/// <summary>等级与加速（加速/攻速/弹数/伤害/动画/加成 buff）。</summary>
	private bool _showLevel = true;
	/// <summary>
	/// ★ v1.20.0「只显示 ≥5 秒」总过滤 —— **按计时器的总时长判定**（用户 2026-10-06 明确）。
	///
	/// **优先级仅次于总开关**：总开关关闭 ⇒ 一行都不显示；本开关打开 ⇒ 在已通过
	/// 各类开关的行里，再滤掉低于阈值的秒数。UI 上放在第 2 列**总开关的下一行**。
	///
	/// 默认 **false**（不改变老用户现有观感）。配置键见 <see cref="OnlyLongKey"/>。
	/// </summary>
	private bool _onlyLongTimers;

	/// <summary>
	/// 「只显示 ≥5 秒」的阈值（秒）。
	///
	/// 用户原话是"不显示低于 4.9 秒的" —— 因为显示格式是 `ToString("0.0")`，
	/// **4.9 是"能被格式化出来"的最小值**（4.86 会四舍五入成 "4.9"）。
	///
	/// ⚠️ 判定用的是**文本里解析出来的值**，而文本只有 1 位小数
	///   ⇒ `"4.9s"` 既有可能是 4.90、也有可能是 4.85~4.94 ⇒ **无法区分**。
	///   所以这里按用户字面要求处理：**解析值 &lt; 4.9 一律滤掉** ⇒ `"4.9s"` 会被滤掉
	///   （它代表的是"接近 5 秒但不足"的那一档）。
	///   想让"显示 4.9s 也保留"，把这里改成 4.89；
	///   想让"显示 5.0s 及以上才保留"，改成 4.95。
	/// </summary>
	private const double ShortTimerThresholdSeconds = 4.9;

	/// <summary>「只显示 ≥5 秒」的配置键名。</summary>
	private const string OnlyLongKey = "only_long_timers";

	/// <summary>10 类功能开关的配置键名（与 UI 节点名一一对应）。</summary>
	private static readonly string[] FeatureKeys =
	{
		"feature_load", "feature_obstacle", "feature_obstaclehp", "feature_cardcd",
		"feature_spawn", "feature_othertimer", "feature_growth", "feature_produce",
		"feature_combat", "feature_level",
	};
	private bool _configFaultReported;
	private bool _checkBoxFaultReported;
	private bool _checkBoxReadyReported;

	// 新行的配色：装填中用青蓝（跟护盾的青、本体的红区分开），就绪用绿色
	private static readonly Color BusyColor = new Color(0.47f, 0.86f, 1.0f, 1.0f);   // 青蓝：正在装填
	private static readonly Color ReadyColor = new Color(0.35f, 1.0f, 0.42f, 1.0f);  // 绿色：已就绪
	private static readonly Color DisabledColor = new Color(0.75f, 0.75f, 0.75f, 1.0f); // 灰色：用尽/失效

	private XWModRuntimeContext _context;
	private SceneTree _tree;
	private Callable _tickCallable;
	private bool _connected;
	private bool _started;
	private int _frame;
	private bool _faultReported;
	// 诊断：每 100 次扫描打一条统计，防止刷屏
	private int _scanCount;
	private int _dbgChars;
	private int _dbgWithCannon;
	private int _dbgRunning;
	private int _dbgNoLabel;
	private int _dbgNoHealthComp;
	private bool _dbgNoCharReported;
	// 叠种分层：本帧内每个位置槽已占用的高度（用于多角色叠种时向上错开显示）
	private readonly System.Collections.Generic.Dictionary<string, float> _slotUsed
		= new System.Collections.Generic.Dictionary<string, float>();
	private bool _ownLineFaultReported;
	// 计时器诊断：每个「角色|键」首次出现时打一条原始值（已定案三个字段是字典，见 TryBuildInfo ②）
	private readonly System.Collections.Generic.HashSet<string> _timerKeysReported
		= new System.Collections.Generic.HashSet<string>();
	private bool _timerFaultReported;
	// 候选计时器角色「组件取不到」诊断（每个名字只报一次）
	private readonly System.Collections.Generic.HashSet<string> _timerMissingReported
		= new System.Collections.Generic.HashSet<string>();

	// 坑洞消失倒计时：字段查找缓存（按类型，含"找不到"）+ 诊断去重
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _craterFieldCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _craterConfFieldCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo>();
	private readonly System.Collections.Generic.HashSet<string> _craterReported
		= new System.Collections.Generic.HashSet<string>();

	// 咀嚼（ChomperComponent）诊断去重：每个角色名首次「真的在咀嚼」时打一条
	private readonly System.Collections.Generic.HashSet<string> _chewReported
		= new System.Collections.Generic.HashSet<string>();

	// 长大 / 周期事件 / 磁力消化 的诊断去重
	private readonly System.Collections.Generic.HashSet<string> _growReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _perReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _magReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _prodReported
		= new System.Collections.Generic.HashSet<string>();

	/// <summary>
	/// IZM 血量产出：每株植物**历史上最多消耗掉的段数**（键 = 角色名）。
	///
	/// ⚠️ 为什么要记"历史最大值"：`hpNext` 是**阈值游标**，只在 `hitpoints &lt;= hpNext`
	///   时递减。刚种下时 `hpNext = 满血 − 一段`，血量若一直高于它，`hpNext` 就**不动**
	///   ⇒ 直接用它算会得出"剩余次数永远是满的"，而且治疗完全不影响它。
	///   但它对同一株植物是**单调递减**的，所以同一株的 `consumed` 只会变大、不会变小。
	///   取历史最大值（而不是当前值）是为了让总次数不因实例重建而漂移，
	///   剩余次数 = 历史最大值 − 当前已消耗，于是"治疗"不会让数字涨回去。
	/// </summary>
	private readonly System.Collections.Generic.Dictionary<string, int> _prodConsumedMax
		= new System.Collections.Generic.Dictionary<string, int>();

	/// <summary>IZM 血量产出：本条只报一次。</summary>
	private readonly System.Collections.Generic.HashSet<string> _izmProdReported
		= new System.Collections.Generic.HashSet<string>();

	/// <summary>泡椒罐子诊断：每条只报一次（v1.19.3）。</summary>
	private readonly System.Collections.Generic.HashSet<string> _jalaReported
		= new System.Collections.Generic.HashSet<string>();
	// 蓄能咖啡豆（角色级充能）
	private readonly System.Collections.Generic.HashSet<string> _chargeReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo[]> _chargeFieldCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo[]>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo[]> _chargeFieldCache2
		= new System.Collections.Generic.Dictionary<Type, FieldInfo[]>();
	// 土豆雷/篮球/投石车「候选缺组件」诊断探针（每角色名一次）
	private readonly System.Collections.Generic.HashSet<string> _potatoProbed
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _bowlProbed
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _catProbed
		= new System.Collections.Generic.HashSet<string>();
	// 蓄能识别失败诊断探针（每角色名一次）
	private readonly System.Collections.Generic.HashSet<string> _chargeProbeReported
		= new System.Collections.Generic.HashSet<string>();
	// ★ v1.17.0：等级型植物（增压大喷菇 / 豆荚壳）——"等级与加速"类
	//   （与上面的蓄能类区分：那两套是"充能计时"，这两只是"叠种升级"）
	private readonly System.Collections.Generic.HashSet<string> _lvReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.Dictionary<Type, System.Reflection.FieldInfo[]> _lvFieldCache
		= new System.Collections.Generic.Dictionary<Type, System.Reflection.FieldInfo[]>();
	// 海草抓取 / 啃碑 诊断探针（每角色名一次）
	private readonly System.Collections.Generic.HashSet<string> _tkReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _gbReported
		= new System.Collections.Generic.HashSet<string>();
	// 召唤型角色"生成"诊断探针（每角色名一次）
	private readonly System.Collections.Generic.HashSet<string> _spawnReported
		= new System.Collections.Generic.HashSet<string>();
	// 速度/攻速倍率（需求 5）
	private readonly System.Collections.Generic.HashSet<string> _buffReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _buffTsCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo>();
	// 发射组件加成诊断探针（每角色名一次）
	private readonly System.Collections.Generic.HashSet<string> _fireReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.HashSet<string> _fireBoostedReported
		= new System.Collections.Generic.HashSet<string>();
	// 选卡栏卡片种植 CD（v1.10.0）
	private FieldInfo _fCardCd, _fCardCdTimer, _fCardOpen;
	private readonly System.Collections.Generic.HashSet<string> _cardProbeReported
		= new System.Collections.Generic.HashSet<string>();
	// 倍率段执行探测（v1.10.1）
	private readonly System.Collections.Generic.HashSet<string> _boostProbeReported
		= new System.Collections.Generic.HashSet<string>();
	// 发射值基线/变化监控（v1.10.3）
	private readonly System.Collections.Generic.Dictionary<string, double[]> _fireBaseline
		= new System.Collections.Generic.Dictionary<string, double[]>();
	private readonly System.Collections.Generic.HashSet<string> _fireChangedReported
		= new System.Collections.Generic.HashSet<string>();
	// 伤害缩放（damageScale）基线/变化（v1.10.5）
	private readonly System.Collections.Generic.Dictionary<string, double> _dmgBaseline
		= new System.Collections.Generic.Dictionary<string, double>();
	private readonly System.Collections.Generic.HashSet<string> _dmgChangedReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _dmgFieldCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo>();
	// buff 清单诊断（v1.10.7，每角色首次"有 buff"时一次）
	private readonly System.Collections.Generic.HashSet<string> _buffListReported
		= new System.Collections.Generic.HashSet<string>();
	// 加成 buff 数值诊断（v1.10.9，每角色每键一次）
	private readonly System.Collections.Generic.HashSet<string> _buffValReported
		= new System.Collections.Generic.HashSet<string>();
	// 障碍物（v1.13.0）：血量文本字段 + 诊断探针
	private FieldInfo _fBodyText;
	private FieldInfo _fBodyState;
	private FieldInfo _fBodyLabel;
	private FieldInfo _fDamageInstance;
	private readonly System.Collections.Generic.HashSet<string> _obstacleHpReported
		= new System.Collections.Generic.HashSet<string>();
	// 猫窝加成落点探针（v1.11.1）：猫尾草类植物的候选字段快照与变化记录
	private FieldInfo _fSpriteTimeScale;
	private readonly System.Collections.Generic.Dictionary<string, double[]> _catProbe
		= new System.Collections.Generic.Dictionary<string, double[]>();
	private readonly System.Collections.Generic.HashSet<string> _catProbeReported
		= new System.Collections.Generic.HashSet<string>();
	// 猫尾草类"实测发射周期"（v1.11.2）：timer 归零间隔 = 真实攻速
	private readonly System.Collections.Generic.Dictionary<string, double> _catTimerLast
		= new System.Collections.Generic.Dictionary<string, double>();
	private readonly System.Collections.Generic.Dictionary<string, ulong> _catLastZeroMs
		= new System.Collections.Generic.Dictionary<string, ulong>();
	private readonly System.Collections.Generic.Dictionary<string, double> _catPeriod
		= new System.Collections.Generic.Dictionary<string, double>();
	private readonly System.Collections.Generic.Dictionary<string, double> _catPeriodLast
		= new System.Collections.Generic.Dictionary<string, double>();
	private readonly System.Collections.Generic.HashSet<string> _catPeriodReported
		= new System.Collections.Generic.HashSet<string>();
	// 猫尾草类"动画时间缩放"（v1.11.3）★ 猫窝加成的真实落点
	private readonly System.Collections.Generic.Dictionary<string, double> _catAnimTs
		= new System.Collections.Generic.Dictionary<string, double>();
	// **所有角色**的动画时间缩放（v1.11.4，覆盖其他加速源）
	private readonly System.Collections.Generic.Dictionary<string, double> _animTsAll
		= new System.Collections.Generic.Dictionary<string, double>();
	// 变换异常诊断探针（每类型一次）
	private readonly System.Collections.Generic.HashSet<string> _xformOddReported
		= new System.Collections.Generic.HashSet<string>();
	// 核弹磁力菇（核能充能）诊断探针与字段缓存
	private readonly System.Collections.Generic.HashSet<string> _armReported
		= new System.Collections.Generic.HashSet<string>();
	private readonly System.Collections.Generic.Dictionary<Type, FieldInfo[]> _armFieldCache
		= new System.Collections.Generic.Dictionary<Type, FieldInfo[]>();

	/// <summary>
	/// 全部 35 个带 CharacterTimerComponent 的角色（清单：mod/带计时器组件角色清单.md，
	/// 逐条核对过 35/35、组件 InstanceId 均为 character.timer）。
	/// 用于诊断：候选角色上场但 GetRuntime 取不到组件时，报出来定案 InstanceId 问题。
	/// </summary>
	private static readonly System.Collections.Generic.HashSet<string> TimerCandidates
		= new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
	{
		"MedicPlantern", "PlantCaltropMelon", "PlantCaltropZ", "PlantCatTailZ",
		"PlantChomperZ", "PlantGatlingPeaZ", "PlantGoldCat", "PlantHurtTallnut",
		"PlantHypnoShroomShooter", "PlantInsaniKelp", "PlantKelpMine", "PlantMedicPot",
		"PlantMininut", "PlantPeaWell", "PlantPeashooterZ", "PlantPotQX",
		"PlantPotVampire", "PlantPumpkinNut", "PlantRobot", "PlantSnowManPea",
		"PlantSunFlowerZ", "PlantSunTransfer", "PlantThreePeaterZ", "PlantTorchwoodZ",
		"PlantWallnutZ", "ZombieAircraft", "ZombieGargantuarWallnutZ",
		"ZombieImpPeashooterZ", "ZombieImpScaredy", "ZombieSoccer",
		"GraveStoneTargetHealth", "TimeBomb", "TimeNuke", "CraterG", "ItemMegaFire"
	};
	// 诊断用：本次扫描里遇到的角色名（最多记 10 个）
	private readonly System.Collections.Generic.List<string> _dbgNames
		= new System.Collections.Generic.List<string>();
	private readonly System.Collections.Generic.List<string> _dbgDumped
		= new System.Collections.Generic.List<string>();

	// 反射缓存（每帧别反复查字段）
	private FieldInfo _fRunning, _fWait, _fCurrent;
	// 土豆雷 / 篮球 / 投石车 / 命名计时器 / 大嘴花咀嚼 / 长大 / 周期事件 / 磁力消化
	private FieldInfo _fPotatoRun, _fPotatoRemain;
	private FieldInfo _fBowlHit, _fBowlMax;
	private FieldInfo _fCatCur, _fCatMax;
	private FieldInfo _fTimerRun, _fTimerWait, _fTimerCur;
	private FieldInfo _fChompChew, _fChompTime, _fChompCur, _fChompTimer;
	private FieldInfo _fGrowTimer, _fGrowReach, _fGrowTimes;
	private FieldInfo _fPerStaticTime, _fPerTimer;
	private FieldInfo _fMagBreakTotal, _fMagBreakTimer, _fMagArmor;
	private FieldInfo _fProdTimer, _fProdEffInterval, _fProdInterval, _fProdType;
	/// <summary>IZM 血量产出的阈值游标与步长（v1.18.0 新增，见 ProduceComponent 的注释）。</summary>
	private FieldInfo _fProdHpNext, _fProdHpNextInterval;
	private FieldInfo _fTkCur, _fTkMax;
	private FieldInfo _fGraveDur, _fGraveRun, _fGraveStart;
	private FieldInfo _fAtkInterval, _fAtkTimer, _fAtkIntervalBase;
	private FieldInfo _fBuffDict;
	private FieldInfo _fFireTimeScale, _fFireIntervalBase, _fFireInterval, _fFireTimer;
	private FieldInfo _fFireNum, _fFireCurNum;
	private bool _reflectionReady;
	private bool _reflectionFailed;

	/// <summary>
	/// 入口初始化。
	/// ⚠️ 接口签名是 <c>void</c>（不是 bool）—— 编译报 CS0738 就是这个。
	/// 失败靠"抛异常"表达，而抛了会整包回滚，所以这里必须自己吞掉所有异常。
	/// </summary>
	public void Initialize(XWModRuntimeContext context)
	{
		try
		{
			_context = context;
			_frame = 0;
			_faultReported = false;
			string root = (context == null) ? "<null>" : context.PackageRoot;
			Info("初始化完成；PackageRoot=" + root
				+ "。将在血条下方追加一行，显示 character.cannon 的装填剩余时间。");
		}
		catch (Exception ex)
		{
			// 绝不能抛：抛了整包回滚
			try { GD.PrintErr(LogPrefix + "Initialize 异常（已吞）：" + ex.Message); } catch { }
		}
	}

	public void OnAllModsLoaded()
	{
		try
		{
			if (_started)
			{
				return;
			}
			_tree = Engine.GetMainLoop() as SceneTree;
			if (_tree == null)
			{
				Warn("拿不到 SceneTree，倒计时行不会生效（游戏其余部分不受影响）。");
				return;
			}
			_tickCallable = Callable.From(new Action(OnProcessFrame));
			_tree.Connect("process_frame", _tickCallable);
			_connected = true;
			_started = true;
			LoadEnabled();
			Info("已挂载 process_frame（每 " + ScanStride + " 帧扫一次）。倒计时显示当前："
				+ (_enabled ? "开" : "关") + "（设置页开关或 user://HealthCooldownLine.cfg 可改）。");
		}
		catch (Exception ex)
		{
			Warn("OnAllModsLoaded 异常（已吞）：" + ex.Message);
		}
	}

	public void Shutdown()
	{
		try
		{
			if (_connected && _tree != null && GodotObject.IsInstanceValid(_tree))
			{
				_tree.Disconnect("process_frame", _tickCallable);
			}
		}
		catch (Exception ex)
		{
			Warn("Shutdown 断开 process_frame 异常（已吞）：" + ex.Message);
		}
		finally
		{
			_connected = false;
			_started = false;
		}
	}

	// ---------------------------------------------------------------- 每帧

	private void OnProcessFrame()
	{
		try
		{
			_frame++;
			if (_frame % ScanStride != 0)
			{
				return;
			}
			if (_tree == null || !GodotObject.IsInstanceValid(_tree))
			{
				return;
			}
			Node root = _tree.Root;
			if (root == null || !GodotObject.IsInstanceValid(root))
			{
				return;
			}
			_dbgChars = 0; _dbgWithCannon = 0; _dbgRunning = 0; _dbgNoLabel = 0;
			_dbgNoHealthComp = 0;
			_dbgNames.Clear();
			_slotUsed.Clear();   // 叠种分层每帧重建（同一帧内按树顺序确定谁在上/谁在下，跨帧稳定）
			ScanRecursive(root);
			_scanCount++;
			// 每 100 次扫描（约 1000 帧）汇报一次，能看清到底扫到了什么
			if (_scanCount % 100 == 0)
			{
				Info("诊断：角色=" + _dbgChars + " 带加农炮=" + _dbgWithCannon
					+ " 正在装填=" + _dbgRunning + " 取不到血条组件=" + _dbgNoHealthComp
					+ " 拿不到血条标签=" + _dbgNoLabel
					+ " | 本次遇到的角色：" + string.Join(", ", _dbgNames.ToArray()));
			}
		}
		catch (Exception ex)
		{
			if (!_faultReported)
			{
				_faultReported = true;
				Warn("扫描异常（本条只报一次）：" + ex.Message);
			}
		}
	}

	private void ScanRecursive(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}
		// 设置页对话框：BattleOption 一进树就注入开关（幂等，对话框每次打开重新实例化也覆盖）
		try
		{
			// 兼容实例化重名后缀（BattleOption2 之类），用 StartsWith 而非精确等值
			if (node.Name.ToString().StartsWith(OptionRootName, StringComparison.Ordinal))
			{
				TryEnsureOptionCheckBox(node);
			}
		}
		catch (Exception exOpt)
		{
			if (!_checkBoxFaultReported)
			{
				_checkBoxFaultReported = true;
				Warn("设置页开关注入异常（本条只报一次）：" + exOpt.Message);
			}
		}
		// 选卡栏卡片（UI）：种植 CD 数字（v1.10.0）
		try
		{
			if (node.GetType().Name == "TowerDefenseInGamePacketShow")
			{
				ApplyToPacket(node);
			}
		}
		catch { }

		if (node is TowerDefenseCharacter character)
		{
			_dbgChars++;
			try
			{
				TowerDefenseCharacterConfig c = character.config;
				if (c != null && _dbgNames.Count < 10)
				{
					string nm = c.name;   // TowerDefenseCharacterConfig 只有 name 这一个名字字段
					if (!string.IsNullOrEmpty(nm) && !_dbgNames.Contains(nm))
					{
						_dbgNames.Add(nm);
					}
					// 名字里带 Cannon 的角色：把它的组件全列出来（每种只报一次）
					if (nm != null && nm.Contains("Cannon") && !_dbgDumped.Contains(nm))
					{
						_dbgDumped.Add(nm);
						DumpComponents(character, nm);
					}
				}
			}
			catch { }
			ApplyToCharacter(character);
		}
		Godot.Collections.Array<Node> children = node.GetChildren();
		for (int i = 0; i < children.Count; i++)
		{
			ScanRecursive(children[i]);
		}
	}

	// ---------------------------------------------------------------- 核心

	private void ApplyToCharacter(TowerDefenseCharacter character)
	{
		try
		{
			// 总开关关闭：收起全部自建行，不再读组件
			if (!_enabled)
			{
				HideAllLines(character);
				return;
			}

			// ★ 收集式显示（v1.7.0）：一个角色可以有**多行**数值——例：阳光菇=长大+阳光、
			//   核弹磁力菇=装填+消化、僵尸大嘴花=生成+咀嚼。每行独立 Label（颜色可不同），
			//   不再"命中即独占返回"。
			var lines = new System.Collections.Generic.List<(string Text, Color Color, double Total)>();

			// 诊断（v1.8.1）：角色一旦出现"变换异常"（负缩放/旋转/斜切——魅惑僵尸等翻转角色），
			//   首次遇到就打一条完整变换信息 —— 用于定位"文字镜像"的真实实现方式。
			try
			{
				Vector2 gsc2 = character.GlobalScale;
				float gr2 = character.GlobalRotation;
				float gsk2 = character.Skew;
				if (gsc2.X < 0f || gsc2.Y < 0f || System.Math.Abs(gr2) > 0.001f
					|| System.Math.Abs(gsk2) > 0.001f)
				{
					string tn2 = character.GetType().Name + "|odd";
					if (_xformOddReported.Add(tn2))
					{
						Info("变换异常[" + character.GetType().Name + "]：Scale=" + character.Scale
							+ " Rotation=" + character.Rotation.ToString("0.###")
							+ " Skew=" + gsk2.ToString("0.###")
							+ " GlobalScale=" + character.GlobalScale
							+ " GlobalRotation=" + gr2.ToString("0.###"));
					}
				}
			}
			catch { }

			// 猫窝加成落点探针（v1.11.1，诊断用）：猫尾草类植物的候选加速字段变化监控
			ProbeCatBoostFields(character, SafeCharName(character));

			// 坑洞类（角色级字段 dieDownTimer，非组件；坑洞可能没有组件集）
			TryCollectCraterInfo(character, lines);

			// 障碍物血量（v1.13.0：坑洞/墓碑/炸弹核弹/大火的"血量 X"）
			TryCollectObstacleHp(character, SafeCharName(character), lines);

			// ★ v1.15.8：Godot Timer 型"消失倒计时"（蜘蛛网 BungiTargetSP 等限时物件）
			TryCollectGodotTimerLine(character, lines);

			// 组件链（加农炮/计时器/咀嚼/长大/周期/消化/产出/土豆雷/篮球/投石车/蓄能）
			//   （其中 ⑩ 段负责"等级型植物"的等级显示）
			ComponentManager components = character.componentManager;
			if (components != null && lines.Count < MaxTotalLines)
			{
				TryCollectInfo(components, character, SafeCharName(character), lines);
			}

			// ★ v1.20.0：「只显示 ≥5 秒」过滤 —— **按「总时长」判定**（用户 2026-10-06 明确）。
			//
			//   ── 语义（与 v1.19.x 不同）────────────────────────────────────
			//     旧：显示的**剩余秒数** < 4.9 ⇒ 那一行隐藏
			//         （长计时器会在最后 4.8 秒突然消失）
			//     新：该计时器的**总时长** < 4.9 ⇒ **整行从头到尾都不显示**；
			//         长计时器从满值一路显示到 0.0s。
			//   例：加农炮装填 15s（总 15 ≥ 4.9）⇒ 显示 15.0 → 0.0；
			//       某个 3 秒的小冷却（总 3 < 4.9）⇒ 从头到尾都不出现在屏幕上。
			//
			//   ── 怎么拿到总时长 ───────────────────────────────────────────
			//     每行第三个字段 `Total`：由 `AddTimer(lines, 文本, 颜色, 总时长)` 写入；
			//     非计时行走 `AddLine(...)` ⇒ Total = 0 ⇒ **永不参与过滤**。
			//     （不再靠解析显示文本里的秒数 —— 那只能得到"剩余"，拿不到"总时长"。）
			if (_onlyLongTimers)
			{
				for (int fi = lines.Count - 1; fi >= 0; fi--)
				{
					double tot = lines[fi].Total;
					if (tot > 0.0 && tot < ShortTimerThresholdSeconds)
					{
						lines.RemoveAt(fi);
					}
				}
			}

			if (lines.Count == 0)
			{
				HideAllLines(character);
				return;
			}
			ShowLines(character, lines);
		}
		catch (Exception ex)
		{
			if (!_faultReported)
			{
				_faultReported = true;
				Warn("读取角色组件异常（本条只报一次）：" + ex.Message);
			}
		}
	}

	/// <summary>
	/// 把若干行文字显示到该角色上（v1.7.0：多 Label 方案）。
	///   · 每行一个独立 Label ⇒ 各行的颜色可以不同（就绪绿 / 进行中青蓝 可混排）；
	///   · 叠种兼容：同一位置上已有角色在显示时，本角色的整组行**向上错开**
	///     （阳光豆叠阳光菇、多个同格植物等，互不遮挡）；
	///   · 位置基准不变：第一行仍贴血条 HP 行上方（-38），更多行向上生长。
	/// </summary>
	private void ShowLines(TowerDefenseCharacter character,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		Label[] labels = EnsureOwnLines(character);
		if (labels == null)
		{
			_dbgNoLabel++;
			return;
		}

		// 叠种分层：按角色全局位置（量化到 8px 网格）记录"本帧该位置已占用的高度"
		float lift = 0f;
		string slot = SlotKey(character);
		if (slot != null)
		{
			_slotUsed.TryGetValue(slot, out lift);
			_slotUsed[slot] = lift + lines.Count * LineHeight + 2f;
		}

		// 文字镜像/旋转修复（v1.8.1 增强）：角色节点的"翻转"可能两种实现——
		//   (a) Scale.X = -1（负缩放）；(b) Rotation ≈ π（180°旋转，视觉上同样是"文字镜像/倒转"）。
		//   两种都抵消：Scale 取符号反向、Rotation 取负值。
		float gsx = 1f, gsy = 1f, gr = 0f;
		Vector2 cgp = Vector2.Zero;
		try
		{
			Vector2 gsc = character.GlobalScale;
			gsx = gsc.X;
			gsy = gsc.Y;
			gr = character.GlobalRotation;
			cgp = character.GlobalPosition;
		}
		catch { }
		Vector2 fixScale = new Vector2((gsx < 0f) ? -1f : 1f, (gsy < 0f) ? -1f : 1f);
		float fixRot = (System.Math.Abs(gr) > 0.001f) ? -gr : 0f;

		// v1.11.6：双格植物（寒冰/毁灭加农炮、热狗射手等）——角色原点在左格，行标签右移半格，
		//   让文字"居中到两格中间"。
		float dwShift = 0f;
		try
		{
			if (IsDoubleWidth(SafeCharName(character)))
			{
				dwShift = DoubleWidthShift;
			}
		}
		catch { }

		for (int i = 0; i < labels.Length; i++)
		{
			Label lbl = labels[i];
			if (lbl == null || !GodotObject.IsInstanceValid(lbl))
			{
				continue;
			}
			if (i < lines.Count)
			{
				lbl.Visible = true;
				lbl.Text = lines[i].Text;
				lbl.AddThemeColorOverride("font_color", lines[i].Color);
				lbl.Scale = fixScale;   // 抵消角色节点的翻转
				lbl.Rotation = fixRot;  // 抵消角色节点的旋转（180°旋转同样表现为"文字镜像"）
				// ★ v1.8.3：位置改用**全局坐标**——父节点翻转（Scale.X=-1）会把局部 position.x 一起镜像
				//   （Label 左边缘 -80 变 +80 ⇒ 文字被甩到角色右侧很远，截图实证）；全局坐标不受父变换影响。
				//   经验修正：之前"中心在 0 所以位置不受镜像影响"的推断**是错的**——镜像作用于
				//   Label 的左上角坐标，不是中心。
				lbl.GlobalPosition = new Vector2(
					cgp.X - 80f + dwShift,
					cgp.Y - 38f - lift - i * LineHeight);
				lbl.Size = new Vector2(160f, LineHeight);
			}
			else if (lbl.Visible)
			{
				lbl.Visible = false;
			}
		}
	}

	/// <summary>叠种分组用的槽位键（角色全局位置量化到 8px 网格）。</summary>
	private string SlotKey(TowerDefenseCharacter character)
	{
		try
		{
			Vector2 gp = character.GlobalPosition;
			int cx = (int)System.Math.Floor(gp.X / 8f);
			int cy = (int)System.Math.Floor(gp.Y / 8f);
			return cx + "," + cy;
		}
		catch { return null; }
	}

	/// <summary>懒创建该角色的 6 个自建行 Label（ModCooldownOwnLine0..5），返回数组。</summary>
	private Label[] EnsureOwnLines(TowerDefenseCharacter character)
	{
		try
		{
			Label[] arr = new Label[MaxTotalLines];
			for (int i = 0; i < MaxTotalLines; i++)
			{
				string nm = OwnLineNamePrefix + i;
				Label lbl = character.GetNodeOrNull<Label>(nm);
				if (lbl == null || !GodotObject.IsInstanceValid(lbl))
				{
					lbl = new Label();
					lbl.Name = nm;
					lbl.ZIndex = 200;                  // 压在角色贴图之上
					lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
					lbl.HorizontalAlignment = HorizontalAlignment.Center;
					lbl.VerticalAlignment = VerticalAlignment.Bottom;
					lbl.Size = new Vector2(160f, LineHeight);
					Font font = null;
					try { font = ResourceLoader.Load<Font>("res://Asset/Font/fzkt.ttf"); } catch { }
					if (font != null)
					{
						lbl.AddThemeFontOverride("font", font);
					}
					lbl.AddThemeFontSizeOverride("font_size", 14);
					lbl.AddThemeConstantOverride("outline_size", 5);
					lbl.AddThemeColorOverride("font_color", BusyColor);
					lbl.Visible = false;
					character.AddChild(lbl);
				}
				arr[i] = lbl;
			}
			return arr;
		}
		catch (Exception ex)
		{
			if (!_ownLineFaultReported)
			{
				_ownLineFaultReported = true;
				Warn("自建行创建失败（本条只报一次）：" + ex.Message);
			}
			return null;
		}
	}

	/// <summary>该角色无任何可显示数值 / 总开关关闭时：隐藏全部自建行。</summary>
	private void HideAllLines(TowerDefenseCharacter character)
	{
		try
		{
			for (int i = 0; i < MaxTotalLines; i++)
			{
				Label lbl = character.GetNodeOrNull<Label>(OwnLineNamePrefix + i);
				if (lbl != null && GodotObject.IsInstanceValid(lbl) && lbl.Visible)
				{
					lbl.Visible = false;
				}
			}
		}
		catch { }
	}

	// ---------------------------------------------------------------- 坑洞消失倒计时

	/// <summary>
	/// 坑洞类角色（TowerDefenseCrater 及其子类：CraterDayGround/CraterNG/CraterK/CraterImp/…）
	/// 的「消失倒计时」。
	///
	/// 机制（2026-09-26 从 PlantsVsZombies.dll 元数据定案，非推测）：
	///   · 运行时字段 dieDownTimer : Double（角色级，非组件）——递减中；
	///   · 总时长来自配置 TowerDefenseCraterConfig.dieDownTime : Double
	///     （实测：CraterDayGround=200s、CraterNG=30s）；
	///   · 归零后触发 DieDown()（塌陷消失，动画帧过滤 dieDownFliters=[CraterWhole, CraterHalf]）。
	/// 判据是"字段存不存在"（沿类型链找），不是角色名单——以后新增坑洞变体自动覆盖。
	/// </summary>
	private void TryCollectCraterInfo(TowerDefenseCharacter character,
		System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		try
		{
			FieldInfo fi = GetCraterField(character.GetType());
			if (fi == null)
			{
				return;
			}
			object vRaw = fi.GetValue(character);
			if (vRaw == null)
			{
				return;
			}
			double v = Convert.ToDouble(vRaw);
			// ★ 方向定案（2026-09-26 日志实测）：dieDownTimer 是**从 0 递增的"已过时间"**
			//   （首见采样值 0.05~0.3s，而配置总时长 30~200s）——所以剩余 = 配置总时长 - 已过。
			double total = double.NaN;
			try
			{
				object cfg = character.config;
				if (cfg != null)
				{
					FieldInfo cfi = GetCraterConfField(cfg.GetType());
					if (cfi != null && cfi.GetValue(cfg) != null)
					{
						total = Convert.ToDouble(cfi.GetValue(cfg));
					}
				}
			}
			catch { }
			// 诊断：每个坑洞角色名首次打一条原始值
			string nm = SafeCharName(character);
			if (_craterReported.Add(nm ?? "?"))
			{
				Info("坑洞[" + (nm ?? "?") + "]：dieDownTimer(已过)=" + v.ToString("0.###")
					+ " 配置dieDownTime=" + (double.IsNaN(total) ? "?" : total.ToString("0.###")));
			}
			if (double.IsNaN(total) || total <= 0)
			{
				return;   // 总时长读不到就不显示（避免显示错误方向的数据）
			}
			double remain = total - v;
			if (remain <= 0)
			{
				return;   // 未开始 / 已塌陷完
			}
			if (CatOn(1) && lines.Count < MaxTotalLines)
			{
				AddTimer(lines, "消失 " + remain.ToString("0.0") + "s", BusyColor, total);
			}
		}
		catch { }
	}

	/// <summary>沿类型链找坑洞的运行时字段（结果按类型缓存，含"找不到"）。</summary>
	private FieldInfo GetCraterField(Type t)
	{
		FieldInfo fi;
		if (_craterFieldCache.TryGetValue(t, out fi))
		{
			return fi;
		}
		const BindingFlags F = BindingFlags.Instance | BindingFlags.Public
			| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		Type cur = t;
		while (cur != null && fi == null)
		{
			fi = cur.GetField("dieDownTimer", F);
			cur = cur.BaseType;
		}
		_craterFieldCache[t] = fi;
		return fi;
	}

	/// <summary>沿类型链找坑洞配置的 dieDownTime（结果按类型缓存）。</summary>
	private FieldInfo GetCraterConfField(Type t)
	{
		FieldInfo fi;
		if (_craterConfFieldCache.TryGetValue(t, out fi))
		{
			return fi;
		}
		const BindingFlags F = BindingFlags.Instance | BindingFlags.Public
			| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		Type cur = t;
		while (cur != null && fi == null)
		{
			fi = cur.GetField("dieDownTime", F);
			cur = cur.BaseType;
		}
		_craterConfFieldCache[t] = fi;
		return fi;
	}

	// ---------------------------------------------------------------- 处理器链

	/// <summary>
	/// 依次询问各数据源，返回第一个「当前有话可说」的显示内容。
	///
	/// 顺序即优先级（从上到下）：
	///   ① 加农炮装填      character.cannon   → "装填 12.4s" / "装填 就绪"
	///   ② 命名计时器      character.timer    → "倒计时 45.2s"（定时炸弹/核弹/岩浆坑…）
	///   ③ 土豆雷准备出土  character.potato   → "准备 8.3s"
	///   ④ 篮球剩余投抛    character.bowling  → "剩余 3 发"
	///   ⑤ 投石车剩弹      character.catapult → "剩弹 2 发"
	///
	/// 全部来自 PlantsVsZombies.dll 元数据 + 各 Definition.tres 实读（非推测）。
	/// v1.7.0 起改为**收集式**：命中的每个数值都追加一行（多 Label、颜色独立），
	/// 不再"命中即独占"。顺序即显示顺序（从上到下）。
	/// </summary>
	private void TryCollectInfo(ComponentManager cm, TowerDefenseCharacter character,
		string charName, System.Collections.Generic.List<(string Text, Color Color, double Total)> lines)
	{
		if (!EnsureReflection())
		{
			return;
		}

		// ⓪ 核弹磁力菇（TowerDefensePlantMagnetShroomDM）：收集铁器 → 核能充能 → 可发射。
		//   角色类字段（mdprobe 实读）：ironCount（已收集，属性 backing field）/
		//   ironPerFullCharge（充满所需）/ _isArmed（充能完成 = 可发射）。
		//   注意它同时挂加农炮——armed 时"可发射"取代加农炮的"装填 就绪"（语义重复），
		//   但"装填中"（发射后冷却）仍正常显示。
		bool armRole = false;
		try
		{
			FieldInfo fIron, fIronMax, fArmed;
			GetArmFields(character.GetType(), out fIron, out fIronMax, out fArmed);
			if (fIron != null && fIronMax != null)
			{
				armRole = true;
				int armIron = (fIron.GetValue(character) != null)
					? Convert.ToInt32(fIron.GetValue(character)) : 0;
				int armMax = (fIronMax.GetValue(character) != null)
					? Convert.ToInt32(fIronMax.GetValue(character)) : 0;
				bool armArmed = (fArmed != null) && (fArmed.GetValue(character) is bool ab) && ab;
				if (_armReported.Add(charName ?? "?"))
				{
					Info("核能[" + (charName ?? "?") + "]：铁器=" + armIron + "/" + armMax
						+ " 可发射(_isArmed)=" + armArmed);
				}
				if (CatOn(0) && armMax > 0 && lines.Count < MaxTotalLines)
				{
					if (armArmed)
					{
						AddLine(lines, "可发射", ReadyColor);
					}
					else
					{
						AddLine(lines, "充能 " + armIron + "/" + armMax, BusyColor);
					}
				}
			}
		}
		catch { }

		// ① 加农炮装填（v1.7.0：改为"追加行"——核弹磁力菇等同时有加农炮+其他数值，
		//   旧版"命中即独占返回"会吞掉后面的消化/计时器等行）
		try
		{
			CannonComponent cannon = cm.GetRuntime<CannonComponent>(CannonInstanceId);
			if (CatOn(0) && cannon != null && lines.Count < MaxTotalLines)
			{
				double remain;
				if (TryGetRemaining(cannon, out remain))
				{
					_dbgRunning++;
					AddTimer(lines, "装填 " + remain.ToString("0.0") + "s", BusyColor, CannonRestTotal(cannon));
				}
				else if (!armRole)
				{
					// 核弹磁力菇（armRole）的"就绪"由 ⓪ 段的"可发射"表达，这里不重复。
					// ★ v1.15.1：改用官方方法 `CannonComponent.CanFire()`（源码实现：
					//   `Alive && Lifecycle==Active && _runtimeInitialized && canFire && 父节点有效`
					//   且状态机已初始化）——比"读不到剩余时间就当就绪"更准确。
					bool canFire15 = false;
					try { canFire15 = cannon.CanFire(); } catch { }
					if (canFire15)
					{
						AddLine(lines, "装填 就绪", ReadyColor);
					}
				}
			}
		}
		catch { }

		// ② 命名计时器（CharacterTimerComponent）
		// ★★ 2026-09-26 探针定案：计时器三字段是**按计时器名字典化**的集合：
		//    timerRunning / timerWaitTime / timerCurrent : Dictionary<string, bool/double>
		//    当前值从 0 递增（=已过），剩余 = 时长 - 当前值（日志实测核对过）。
		// 遍历正在运行的项，每项一行（按剩余升序、最多 MaxTimerLines 行）。
		try
		{
			CharacterTimerComponent t = cm.GetRuntime<CharacterTimerComponent>(TimerInstanceId);
			if (t != null)
			{
				object runObj = _fTimerRun != null ? _fTimerRun.GetValue(t) : null;
				object waitObj = _fTimerWait != null ? _fTimerWait.GetValue(t) : null;
				object curObj = _fTimerCur != null ? _fTimerCur.GetValue(t) : null;
				System.Collections.IDictionary runDic = runObj as System.Collections.IDictionary;
				System.Collections.IDictionary waitDic = waitObj as System.Collections.IDictionary;
				System.Collections.IDictionary curDic = curObj as System.Collections.IDictionary;
				if (runDic != null && waitDic != null)
				{
					var running = new System.Collections.Generic.List<(string Key, double Rem, double Total)>();
					foreach (System.Collections.DictionaryEntry e in runDic)
					{
						string key = (e.Key != null) ? e.Key.ToString() : null;
						if (string.IsNullOrEmpty(key) || !waitDic.Contains(key))
						{
							continue;
						}
						// 疯狂海草的 "Open"（点击冷却）由 ⑧ 段专门显示为"点击 X.Xs / 可点击"，此处跳过
						if (key == "Open" && charName != null && charName.Contains("InsaniKelp"))
						{
							continue;
						}
						bool on = (e.Value is bool bv) && bv;
						double w = Convert.ToDouble(waitDic[key]);
						double c = (curDic != null && curDic.Contains(key))
							? Convert.ToDouble(curDic[key]) : 0.0;
						double rem = w - c;
						if (rem < 0) { rem = 0; }
						// 诊断：每个「角色|键」首次出现时打一条原始值
						if (_timerKeysReported.Add((charName ?? "?") + "|" + key))
						{
							Info("计时器项[" + (charName ?? "?") + "/" + key + "]：运行中=" + on
								+ " 时长=" + w.ToString("0.###") + " 当前值=" + c.ToString("0.###")
								+ " 剩余(w-c)=" + rem.ToString("0.###"));
						}
						if (on && rem > 0)
						{
							running.Add((key, rem, w));
						}
					}
					int timerAdded = 0;
					running.Sort((a2, b2) => a2.Rem.CompareTo(b2.Rem));
					// v1.13.0：计时器按键分流——障碍物（坑洞/墓碑/炸弹核弹/大火）→"障碍物消失"(1)；
					//   Spawn 键 →"生成计时"(4)；其余 →"角色计时器其他CD"(5)。
					bool isObs13 = IsObstacleCharacter(character);
					for (int i = 0; i < running.Count
						&& timerAdded < MaxTimerLines && lines.Count < MaxTotalLines; i++)
					{
						int catT = isObs13 ? 1
							: ((running[i].Key == "Spawn") ? 4 : 5);
						if (!CatOn(catT))
						{
							continue;
						}
						AddTimer(lines, TimerLabel(running[i].Key) + " "
							+ running[i].Rem.ToString("0.0") + "s", BusyColor, running[i].Total);
						timerAdded++;
					}
				}
			}
			else if (charName != null && TimerCandidates.Contains(charName)
				&& _timerMissingReported.Add(charName))
			{
				// 已知候选角色上场、但按 character.timer 取不到组件 —— 定案 InstanceId 是否逐角色一致
				Info("候选计时器角色[" + charName + "]：GetRuntime(character.timer) 返回 null，"
					+ "组件取不到（请核对该角色的 ComponentSet 里计时器的 InstanceId）。");
			}
		}
		catch (Exception exTimer)
		{
			if (!_timerFaultReported)
			{
				_timerFaultReported = true;
				Warn("计时器分支异常（本条只报一次，已吞）：" + exTimer.Message);
			}
		}

		// ②.5 咀嚼（ChomperComponent，大嘴花系：普通/僵尸/蒜香/花盆/尖刺/爆破/鲨鱼都挂它）
		try
		{
			ChomperComponent ch = cm.GetRuntime<ChomperComponent>(ChomperInstanceId);
			if (ch != null && _fChompChew != null)
			{
				bool chewing = (_fChompChew.GetValue(ch) is bool cb2) && cb2;
				if (chewing)
				{
					double ct = (_fChompTime != null && _fChompTime.GetValue(ch) != null)
						? Convert.ToDouble(_fChompTime.GetValue(ch)) : 0.0;
					double curC = (_fChompCur != null && _fChompCur.GetValue(ch) != null)
						? Convert.ToDouble(_fChompCur.GetValue(ch)) : 0.0;
					double ctm = (_fChompTimer != null && _fChompTimer.GetValue(ch) != null)
						? Convert.ToDouble(_fChompTimer.GetValue(ch)) : 0.0;
					// ★ 方向/字段定案（2026-09-26 日志实测）：
					//   currentChewTime **恒等于 chewTime**（10/30/40 的快照，不是进度！）
					//   ——用它算剩余恒为 0（v1.5.0 显示 0 的根因）；
					//   chewTimer 才是真正的「已嚼时间」（首见采样 0.07~0.23s，从 0 递增）。
					//   剩余 = chewTime - chewTimer。
					double remC = ct - ctm;
					if (remC < 0) { remC = 0; }
					if (ct > 0 && remC > ct) { remC = ct; }
					// 诊断：每个角色名首次「真的在咀嚼」时打一条原始值
					if (_chewReported.Add(charName ?? "?"))
					{
						Info("咀嚼[" + (charName ?? "?") + "]：isChew=True 时长(chewTime)="
							+ ct.ToString("0.###") + " 已嚼(chewTimer)=" + ctm.ToString("0.###")
							+ " currentChewTime(快照)=" + curC.ToString("0.###")
							+ " → 采用显示剩余=" + remC.ToString("0.###"));
					}
					if (CatOn(8) && ct > 0 && lines.Count < MaxTotalLines)
					{
						AddTimer(lines, "咀嚼 " + remC.ToString("0.0") + "s", BusyColor, ct);
					}
				}
			}
		}
		catch { }

		// ②.6 长大（GrowUpComponent）——阳光菇系/阳光豆/双子阳光菇/VIP坚果/小鬼僵尸 等 9 个角色。
		//   growUpTime 是**数组**（各阶段时长，实测共享定义 [60.0]）；growUpReach=已到阶段索引；
		//   timer 从 0 递增（同 dieDownTimer/chewTimer 家族）⇒ 本阶段剩余 = growUpTime[reach] − timer。
		try
		{
			GrowUpComponent gu = cm.GetRuntime<GrowUpComponent>(GrowUpInstanceId);
			if (gu != null && _fGrowTimer != null && _fGrowTimes != null)
			{
				int reach = 0;
				if (_fGrowReach != null && _fGrowReach.GetValue(gu) != null)
				{
					reach = Convert.ToInt32(_fGrowReach.GetValue(gu));
				}
				double total = 0;
				int stages = 0;
				// ★ 读取通道三次定案合订（2026-09-26）：
				//   ① v1.6.0 的 `is System.Array` ——Godot 数组不是 System.Array，恒 false；
				//   ② v1.7.1~4 的 `as Godot.Collections.Array` ——**Array<T> 的基类是 Object、
				//      与非泛型 Array 无继承关系**（GodotSharp 元数据实锤），恒 null；
				//   ③ 本版：统一走 TryReadFloatArray（泛型 as → IList<float> → IEnumerable），
				//      组件字段优先、空则回退 **ComponentDefinition**（基类属性名）上的同名数组。
				object timesObj = _fGrowTimes.GetValue(gu);
				System.Collections.Generic.List<double> times = null;
				string timesSrc = "组件";
				if (!TryReadFloatArray(timesObj, out times))
				{
					var note = new System.Text.StringBuilder();
					try
					{
						PropertyInfo pDef = typeof(GrowUpComponent).GetProperty("ComponentDefinition",
							BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (pDef == null)
						{
							note.Append("ComponentDefinition属性=null;");
							pDef = typeof(GrowUpComponent).GetProperty("Definition",
								BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						}
						object defObj = pDef != null ? pDef.GetValue(gu) : null;
						if (defObj == null)
						{
							note.Append("定义对象=null;");
						}
						else
						{
							object defTimes = null;
							PropertyInfo pTimes = defObj.GetType().GetProperty("growUpTime",
								BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (pTimes != null)
							{
								defTimes = pTimes.GetValue(defObj);
							}
							if (defTimes == null)
							{
								FieldInfo fDefTimes = defObj.GetType().GetField(
									"<growUpTime>k__BackingField",
									BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (fDefTimes != null)
								{
									defTimes = fDefTimes.GetValue(defObj);
								}
							}
							if (TryReadFloatArray(defTimes, out times))
							{
								timesSrc = "定义";
							}
							else
							{
								note.Append("定义数组读取失败=")
									.Append(defTimes == null ? "null" : defTimes.GetType().Name)
									.Append(';');
							}
						}
					}
					catch (Exception exd)
					{
						note.Append("异常:").Append(exd.Message).Append(';');
					}
					if (times == null)
					{
						timesSrc = "组件(回退失败:" + note + ")";
					}
				}
				if (times != null && times.Count > 0)
				{
					stages = times.Count;
					int idx = reach;
					if (idx < 0) { idx = 0; }
					if (idx >= stages) { idx = stages - 1; }
					total = times[idx];
				}
				double t = (_fGrowTimer.GetValue(gu) != null)
					? Convert.ToDouble(_fGrowTimer.GetValue(gu)) : 0.0;
				bool growing = (stages > 0) && (reach < stages) && (total > 0);
				double remG = total - t;
				if (remG < 0) { remG = 0; }
				// 诊断：首次遇到带该组件的角色就打（不再限定 growing——便于暴露字段/类型问题）
				if (_growReported.Add(charName ?? "?"))
				{
					Info("长大[" + (charName ?? "?") + "]：timer=" + t.ToString("0.###")
						+ " 本阶段时长=" + total.ToString("0.###")
						+ " 阶段=" + reach + "/" + stages
						+ " 数据源=" + timesSrc
						+ " 数组类型=" + (timesObj != null ? timesObj.GetType().FullName : "null")
						+ " → growing=" + growing);
				}
				if (CatOn(6) && growing && remG > 0 && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "长大 " + remG.ToString("0.0") + "s", BusyColor, total);
				}
			}
		}
		catch { }

		// ②.7 周期区域事件（PeriodicAreaEventComponent）——冰坚果（staticTime=0.5s 循环）、伞火把。
		//   周期剩余 = staticTime − timer（timer 从 0 递增到 staticTime 触发一轮）。
		try
		{
			PeriodicAreaEventComponent pa
				= cm.GetRuntime<PeriodicAreaEventComponent>(PeriodicInstanceId);
			if (pa != null && _fPerStaticTime != null && _fPerTimer != null)
			{
				double st = (_fPerStaticTime.GetValue(pa) != null)
					? Convert.ToDouble(_fPerStaticTime.GetValue(pa)) : 0.0;
				double t = (_fPerTimer.GetValue(pa) != null)
					? Convert.ToDouble(_fPerTimer.GetValue(pa)) : 0.0;
				double remP = st - t;
				if (remP < 0) { remP = 0; }
				if (st > 0 && _perReported.Add(charName ?? "?"))
				{
					Info("周期[" + (charName ?? "?") + "]：timer(已过)=" + t.ToString("0.###")
						+ " 周期时长(staticTime)=" + st.ToString("0.###")
						+ " → 下次触发剩余=" + remP.ToString("0.###"));
				}
				if (EnablePeriodEvent && st > 0 && remP > 0 && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "触发 " + remP.ToString("0.0") + "s", BusyColor, st);
				}
			}
		}
		catch { }

		// ②.8 磁力消化（MagnetComponent）——磁力菇系 9 个（含磁力坚果/南瓜/炸弹/地雷/道具磁铁）。
		//   只在「吸到了目标护甲正在消化」期间显示（_breakDownArmor 非 null 或计时已走）。
		try
		{
			MagnetComponent mg = cm.GetRuntime<MagnetComponent>(MagnetInstanceId);
			if (mg != null && _fMagBreakTotal != null && _fMagBreakTimer != null)
			{
				double bt = (_fMagBreakTotal.GetValue(mg) != null)
					? Convert.ToDouble(_fMagBreakTotal.GetValue(mg)) : 0.0;
				double t = (_fMagBreakTimer.GetValue(mg) != null)
					? Convert.ToDouble(_fMagBreakTimer.GetValue(mg)) : 0.0;
				bool busy = t > 0;
				if (!busy && _fMagArmor != null)
				{
					try { busy = _fMagArmor.GetValue(mg) != null; } catch { }
				}
				// ★ 方向定案（2026-09-26 日志实测）：breakDownTimer 是**从 breakDownTime 递减的"剩余"**
				//   （首采 14.667 / 总时长 15——接近满值起算；与加农炮 _restTimerRemaining 同族）。
				//   v1.6.0 误按"已过"算 bt−t 导致显示正向递增；本版直接用它。
				double remM = t;
				if (remM < 0) { remM = 0; }
				if (bt > 0 && remM > bt) { remM = bt; }
				if (busy && _magReported.Add(charName ?? "?"))
				{
					Info("消化[" + (charName ?? "?") + "]：breakDownTimer(剩余)=" + t.ToString("0.###")
						+ " 消化总时长(breakDownTime)=" + bt.ToString("0.###"));
				}
				if (CatOn(8) && bt > 0 && remM > 0 && busy && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "消化 " + remM.ToString("0.0") + "s", BusyColor, bt);
				}
			}
		}
		catch { }

		// ②.10 蓄能咖啡豆（TowerDefensePlantSunShroomCharge）——"每 _produceInterval 秒充能等级+1"。
		//   机制在**角色类**上：level（当前等级）/ _produceInterval（充能间隔，30s）/ _produceComponent；
		//   产出组件被角色脚本当计时器用。命中时替代产出分支（否则会重复显示一行"阳光"）。
		bool chargeHandled = false;
		try
		{
			FieldInfo fLevel, fInterval, fProdComp, fMaxLevel;
			GetChargeFields(character.GetType(),
				out fLevel, out fInterval, out fProdComp, out fMaxLevel);
			if (fLevel != null && fInterval != null && fProdComp != null)
			{
				double interval = (fInterval.GetValue(character) != null)
					? Convert.ToDouble(fInterval.GetValue(character)) : 0.0;
				int lv = (fLevel.GetValue(character) != null)
					? Convert.ToInt32(fLevel.GetValue(character)) : 0;
				int lvMax = (fMaxLevel != null && fMaxLevel.GetValue(character) != null)
					? Convert.ToInt32(fMaxLevel.GetValue(character)) : 0;
				ProduceComponent pcC = cm.GetRuntime<ProduceComponent>(ProduceInstanceId);
				double tC = 0.0;
				if (pcC != null && _fProdTimer != null && _fProdTimer.GetValue(pcC) != null)
				{
					tC = Convert.ToDouble(_fProdTimer.GetValue(pcC));
				}
				double remC = interval - tC;
				if (remC < 0) { remC = 0; }
				// ★ 只要三特征命中（认出蓄能类角色）就由本分支**无条件接管**——
				//   即使 interval 暂时读到 0 也绝不退回产出分支：它的 ProduceComponent 是被
				//   角色脚本挪用作"充能计时"的，**不产阳光**——退回会显示错误的"阳光"字样
				//   （用户曾观察到"显示生产"的根因）。
				chargeHandled = true;
				if (_chargeReported.Add(charName ?? "?"))
				{
					Info("蓄能[" + (charName ?? "?") + "]：等级=" + lv + "/" + lvMax
						+ " 充能间隔(_produceInterval)=" + interval.ToString("0.###")
						+ " 组件timer=" + tC.ToString("0.###")
						+ " → 剩余=" + remC.ToString("0.###"));
				}
				if (CatOn(6) && interval > 0 && remC > 0 && lines.Count < MaxTotalLines)
				{
					// v1.8.1：带上充能等级（用户要的"等级显示"）
					AddTimer(lines, "充能" + lv + " " + remC.ToString("0.0") + "s", BusyColor, interval);
				}
			}
			else
			{
				// —— 第二套特征：**蓄能咖啡豆（EnergyBean = TowerDefensePlantEnergyBean）** ——
				//   字段（mdprobe 实读）：_chargeLevel（当前等级）/ ChargeInterval（充能间隔 30）/
				//   _produce（产出口组件）/ _chargeTimer（充能计时）/ MaxChargeLevel（上限5）。
				//   机制：每 ChargeInterval 秒充能等级+1、点击释放加速（ReleaseDuration 15s）。
				//   ★ 用户三次反馈的"蓄能咖啡豆不产阳光、却显示生产"就是它——旧识别套
				//   （level/_produceInterval）匹配的是另一个角色（SunShroomCharge），从未认出它，
				//   于是它一直掉进产出分支显示"阳光"。本套认对它后显示"充能 X.Xs"并接管。
				FieldInfo fCL, fCI, fCP, fCT;
				GetEnergyBeanChargeFields(character.GetType(), out fCL, out fCI, out fCP, out fCT);
				// 识别失败诊断（每角色一次，v1.8.1 降噪：只对该挂产出口组件的角色打）：
				// 把四个字段的找到情况打进日志，静默失败不再发生
				if ((fCL == null || fCI == null || fCP == null)
					&& _chargeProbeReported.Add(charName ?? "?")
					&& ComponentListHasType(cm, "ProduceComponent"))
				{
					Info("蓄能识别失败[" + (charName ?? "?") + "]：类型=" + character.GetType().Name
						+ " _chargeLevel=" + (fCL != null) + " ChargeInterval=" + (fCI != null)
						+ " _produce=" + (fCP != null) + " _chargeTimer=" + (fCT != null));
				}
				if (fCL != null && fCI != null && fCP != null)
				{
					double interval2 = (fCI.GetValue(character) != null)
						? Convert.ToDouble(fCI.GetValue(character)) : 0.0;
					int lv2 = (fCL.GetValue(character) != null)
						? Convert.ToInt32(fCL.GetValue(character)) : 0;
					double t2 = (fCT != null && fCT.GetValue(character) != null)
						? Convert.ToDouble(fCT.GetValue(character)) : 0.0;
					double rem2 = interval2 - t2;
					if (rem2 < 0) { rem2 = 0; }
					chargeHandled = true;   // 无条件接管，绝不退回产出分支
					if (_chargeReported.Add(charName ?? "?"))
					{
						Info("蓄能[" + (charName ?? "?") + "]（EnergyBean系）：等级=" + lv2
							+ " 充能间隔(ChargeInterval)=" + interval2.ToString("0.###")
							+ " 充能计时(_chargeTimer)=" + t2.ToString("0.###")
							+ " → 剩余=" + rem2.ToString("0.###"));
					}
					if (CatOn(6) && interval2 > 0 && rem2 > 0 && lines.Count < MaxTotalLines)
					{
						// v1.8.1：带上充能等级（"充能0 29.7s" = 等级0、距下一级 29.7s）
						AddTimer(lines, "充能" + lv2 + " " + rem2.ToString("0.0") + "s", BusyColor, interval2);
					}
				}
			}
		}
		catch { }

		// ②.9 阳光/金币/卡包产出（ProduceComponent）——29 个产出定义共用 character.produce。
		//   剩余 = 有效间隔 − timer（timer 从 0 递增，产出瞬间归零循环）。
		//   有效间隔优先读 _effectiveProduceInterval（受 buff/等级影响），退化用 _produceInterval。
		//   显示词由 produceType 决定：Coin→金币、Packet→卡包、其余（Sun/JalaSun/…）→阳光。
		//
		// ★★★ v1.19.1（用户要求）：**我是僵尸 / 我是僵尸大冒险 下不显示这条**。
		//   原因（`ProduceComponent.PhysicsProcessValidated` 的分支）：
		//       if (parent is TowerDefensePlant && _IZMMode)
		//           ProcessHealthProduction(physicsFrame);   // ← 掉血产出，**根本不走 timer**
		//       else
		//           ProcessTimedProduction(...);             // ← 只有这条才用 timer
		//   ⇒ 在 IZM 系里 `timer` 一直是 0、"间隔 − timer" 会显示成一个**假的满额倒计时**
		//     （看着像"还要等 25 秒才产阳光"，其实永远不产）。
		//   ⇒ 该模式下这株植物改由 ②.10 的「脑光/阳光 剩 N/M 次」接管，本行必须让位。
		//
		//   ⚠️ **充能等级不受影响**：充能（充能阳光菇 / 蓄能咖啡豆）在上面的 ②.8
		//     分支里已经 `chargeHandled = true`，而本块开头就是 `!chargeHandled`
		//     ⇒ 充能系**结构上就不可能**走到这里，等级行（"充能N X.Xs"）照常显示。
		//   ⚠️ 按用户要求**不加开关**（这是"不生产就不该显示时间"的语义修正，不是偏好）。
		try
		{
			ProduceComponent pc = cm.GetRuntime<ProduceComponent>(ProduceInstanceId);
			if (pc != null && _fProdTimer != null && !chargeHandled && !IsHealthProduceComponent(pc))
			{
				double interval = 0;
				if (_fProdEffInterval != null && _fProdEffInterval.GetValue(pc) != null)
				{
					interval = Convert.ToDouble(_fProdEffInterval.GetValue(pc));
				}
				if (interval <= 0 && _fProdInterval != null && _fProdInterval.GetValue(pc) != null)
				{
					interval = Convert.ToDouble(_fProdInterval.GetValue(pc));
				}
				double t = (_fProdTimer.GetValue(pc) != null)
					? Convert.ToDouble(_fProdTimer.GetValue(pc)) : 0.0;
				double remS = interval - t;
				if (remS < 0) { remS = 0; }
				string prodTypeStr = "";
				string label = "阳光";
				try
				{
					if (_fProdType != null)
					{
						prodTypeStr = (_fProdType.GetValue(pc) as string) ?? "";
					}
					if (prodTypeStr.IndexOf("Coin", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						label = "金币";
					}
					else if (prodTypeStr.IndexOf("Packet", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						label = "卡包";
					}
				}
				catch { }
				if (interval > 0 && _prodReported.Add(charName ?? "?"))
				{
					Info("产出[" + (charName ?? "?") + "]：timer(已过)=" + t.ToString("0.###")
						+ " 有效间隔=" + interval.ToString("0.###")
						+ " produceType=\"" + prodTypeStr + "\""
						+ " → 显示 " + label + " " + remS.ToString("0.###") + "s");
				}
				if (CatOn(7) && interval > 0 && remS > 0 && lines.Count < MaxTotalLines)
				{
					AddTimer(lines, label + " " + remS.ToString("0.0") + "s", BusyColor, interval);
				}
			}
		}
		catch { }

		// ②.10 IZM「血量产出」剩余触发次数（v1.18.0 新增，用户 2026-10-01 需求）
		//
		// ── 机制（读源码得到的硬事实，非推测）─────────────────────────────
		// `ProduceComponent.PhysicsProcessValidated`：
		//     if (parent is TowerDefensePlant && _IZMMode)
		//         ProcessHealthProduction(physicsFrame);   // ← 血量产出走这条
		//     else
		//         ProcessTimedProduction(...);             // ← 平时走"计时产出"
		//
		// `ProduceComponent.ProcessHealthProduction`：
		//     while (parent.instance.hitpoints <= hpNext && hpNextInterval > 0f && num < num2)
		//     {
		//         hpNext -= hpNextInterval;                 // ← 游标单向递减
		//         ProduceAtConfiguredPositions(num, emitEvent: true, physicsFrame);
		//         num++;
		//     }
		//
		// `ProduceComponent.InitializeHealthProductionState`：
		//     hpNextInterval = hitpoints / healthProductionSegments;   // 每段血量
		//     hpNext         = hitpoints - hpNextInterval;             // 初始游标（满血）→ 段数恒为 0
		//
		// ⇒ **触发次数由"掉血跨过阈值"驱动，与时间无关**；
		//   `maxCatchUpProductions`（默认 1）只限制**单帧内**的追赶上限，不限制总次数。
		//   `num` 就是本次产出的数量（向日葵系 = 阳光/脑光数量）。
		//
		//   ⇒ **总可触发次数 = healthProductionSegments**（= 定义里的"生命分段"，
		//      ModEditor 面板：「IZM 血量生产 · 植物每跨过一个生命阈值生产」，
		//      注释亦为「一次伤害跨越多段时受追赶上限限制」）。
		//   ⇒ **剩余次数 = segments − 已消耗段数**。
		//
		// ── 触发条件为什么**不能**只看"当前是不是 IZM 关卡"────────────────
		// `ProduceComponent` 初始化时：
		//     isBaseIZM = instance.IsIZMMode() || instance.IsIZM2Mode();
		//     if (isBaseIZM) _IZMMode = true;
		//     _IZMMode = definition?._IZMMode ?? false;      // ← 定义里的值也会置 true
		// 而**全库只有一个定义**写了 `_IZMMode = true`：
		//     Definitions/DisguiserSunFlowerProduceComponentDefinition.tres  →  _IZMMode = true
		// ⇒ **伪装向日葵在任何模式下都走血量产出**（它出现在第 8 章、杂交乐园、挑战等非 IZM 关卡）。
		//   ⇒ 判据必须是「**本关卡是 IZM 系** 或 **该组件自己 `_IZMMode`**」，
		//     否则伪装向日葵在非 IZM 关卡里会漏显示（这是本版修正的第二个坑）。
		//
		// ── 治疗会不会增加次数？（用户特别要求核查）────────────────────────
		// **不会。** `TowerDefenseCharacterInstance.Health(double num)` 只做：
		//     hitpoints += num;
		//     RefreshDamagePoint();          // 仅刷新"受伤点"贴图
		// 全程**不碰 `hpNext` / `hpNextInterval`**。而 `hpNext` 只在 `ProcessHealthProduction`
		// 里递减、在 `InitializeHealthProductionState`（组件初始化）里按**满血**重置。
		// ⇒ 治疗既不恢复阈值、也不重置游标，**不会多出任何一次触发**。
		//   本 Mod 的"剩余次数"因此是**不会因治疗而回涨**的（见 _prodConsumedMax 的注释）。
		try
		{
			if (_fProdHpNext != null && _fProdHpNextInterval != null)
			{
				ProduceComponent pcIzm = cm.GetRuntime<ProduceComponent>(ProduceInstanceId);
				if (pcIzm != null && IsHealthProduceComponent(pcIzm))
				{
					object hpNextObj = _fProdHpNext.GetValue(pcIzm);
					if (hpNextObj != null)
					{
						float hpNext = ToFloat(hpNextObj);
						float step = 0f;
						if (_fProdHpNextInterval.GetValue(pcIzm) != null)
						{
							step = ToFloat(_fProdHpNextInterval.GetValue(pcIzm));
						}
						// 总次数 = 定义里的 healthProductionSegments（默认 6）
						int segments = 6;
						try
						{
							ProduceComponentDefinition def = null;
							try
							{
								PropertyInfo pd = typeof(ProduceComponent).GetProperty(
									"ComponentDefinition",
									BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
								if (pd != null)
								{
									def = pd.GetValue(pcIzm) as ProduceComponentDefinition;
								}
							}
							catch { }
							if (def != null && def.healthProductionSegments > 0)
							{
								segments = def.healthProductionSegments;
							}
						}
						catch { }
						// hpNextInterval = 满血 / segments ⇒ 满血 = step * segments
						double hpMax = (double)step * segments;

						// 防御：异常配置（步长为 0 或段数非正）⇒ 不显示，但**不能 return**，
						// 否则会跳过本函数后面所有其他信息的收集。
						if (step > 0.0001f && segments > 0)
						{
						// ★ v1.19.2：已触发次数必须**减 1**（用户反馈"显示的不是最大值，而是最大−1"）。
						//   初始 `hpNext = 满血 − 一段`（InitializeHealthProductionState），
						//   此时 `(满血 − hpNext)/段长 = 1`，但"已触发"应为 **0**。
						//   ⇒ 统一走 ConsumedSegments（内部已 −1）。
						int consumed = ConsumedSegments(hpMax, hpNext, step, segments);

						// 历史最大值：让"总次数"与"已消耗"都不因治疗/实例重建而回退
						string key = charName ?? "?";
						int prevMax;
						if (!_prodConsumedMax.TryGetValue(key, out prevMax))
						{
							prevMax = consumed;
						}
						else if (consumed > prevMax)
						{
							prevMax = consumed;
						}
						_prodConsumedMax[key] = prevMax;
						int total = segments;
						if (prevMax > total) { total = prevMax; }   // 防御：段数配置变小过
						int left = total - consumed;
						if (left < 0) { left = 0; }

						if (_izmProdReported.Add(key))
						{
							Info("IZM血量产出[" + key + "]：分段=" + segments
								+ " 每段血=" + step.ToString("0.##")
								+ " 满血=" + hpMax.ToString("0.##")
								+ " hpNext=" + hpNext.ToString("0.##")
								+ " → 已触发 " + consumed + " 次，显示剩余 " + left + " 次"
								+ "（治疗不影响：hpNext 单向递减）");
						}

						if (CatOn(7) && lines.Count < MaxTotalLines)
						{
							string prodLabel = "产出";
							try
							{
								string pt = (_fProdType != null)
									? ((_fProdType.GetValue(pcIzm) as string) ?? "") : "";
								if (pt.IndexOf("Brain", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									prodLabel = "脑光";
								}
								else if (pt.IndexOf("Coin", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									prodLabel = "金币";
								}
								else if (pt.IndexOf("Packet", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									prodLabel = "卡包";
								}
								else if (pt.Length > 0)
								{
									prodLabel = "阳光";
								}
							}
							catch { }
							Color c = (left > 0) ? ReadyColor : DisabledColor;
							AddLine(lines, prodLabel + " 剩 " + left + "/" + total + " 次", c);
						}
						}   // ← 结束 step/segments 防御性判断
					}
				}
			}
		}
		catch { }

		// ②.10b 「伪装」家族：**类自己内联的 hpNext**（v1.18.0，用户问"对伪装向日葵/伪装机枪射手是否有效"后补）
		//
		// ── 为什么必须单独一条（这是我第一版漏掉的）──────────────────────
		// 「伪装」系列走的**不是** `ProduceComponent`，而是每个类**自己**复制了一份同样的
		// 血量分段逻辑，字段直接挂在自己身上（都是 `public double hpNext / hpNextInterval`）：
		//
		//   TowerDefensePlantDisguiserGatling  (伪装机枪射手)：
		//       _Ready():        hpNextInterval = instance.hitpoints / 6.0;
		//                        hpNext = instance.hitpoints - hpNextInterval;
		//       BatchUpdate():   while (instance.hitpoints <= hpNext) { hpNext -= hpNextInterval; Fire(); }
		//       DestroySet():    while (hpNext >= 0.0) { hpNext -= hpNextInterval; num++; } Fire(num);
		//
		//   TowerDefensePlantDisguiserCherry   (伪装樱桃)   同款 → _explodeComponent.Explode()
		//   TowerDefensePlantDisguiserBlover   (伪装三叶草) 同款 → BlowMethod()
		//
		//   ⚠️ 它们的段数是**硬编码 6.0**（内联在 IL 里，读不到常量），
		//      与 `ProduceComponentDefinition.healthProductionSegments` 的默认值 6 相同。
		//      这里用 6 作为总次数；若哪天游戏改了那个 6.0，本行数字会偏大/偏小
		//      —— 已在 README 里标注这个前提。
		//
		//   ⇒ 通用识别规则：**角色实例上直接存在 `hpNext` + `hpNextInterval` 字段**
		//      （不是它某个组件上的）。这条规则自动覆盖整个伪装家族，
		//      以后游戏再加伪装植物也无需改本 Mod。
		//
		// ── 治疗会不会增加次数？同样**不会** ──────────────────────────────
		//   与 ProduceComponent 那条同源：`hpNext` 只在 `BatchUpdate` 里递减、
		//   只在 `_Ready()` 按满血重置；`Health()` 只改 hitpoints，碰不到它。
		try
		{
			float hpNext2 = 0f, step2 = 0f;
			bool hasInline = false;
			try
			{
				Type ct = character.GetType();
				FieldInfo fN = ct.GetField("hpNext",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo fI = ct.GetField("hpNextInterval",
					BindingFlags.Public | BindingFlags.Instance);
				if (fN != null && fI != null)
				{
					object oN = fN.GetValue(character);
					object oI = fI.GetValue(character);
					if (oN != null && oI != null)
					{
						hpNext2 = ToFloat(oN);
						step2 = ToFloat(oI);
						hasInline = step2 > 0.0001f;
					}
				}
			}
			catch { }

			if (hasInline)
			{
				const int InlineSegments = 6;      // ← 与游戏内联的 `/ 6.0` 对应
				double hpMax2 = (double)step2 * InlineSegments;
				// ★ v1.19.2：同样要 −1（见 ConsumedSegments 注释）
				int consumed2 = ConsumedSegments(hpMax2, hpNext2, step2, InlineSegments);
				int left2 = InlineSegments - consumed2;
				if (left2 < 0) { left2 = 0; }

				// 按类名判定"产出型"还是"动作型"
				string tn = character.GetType().Name;
				bool isProducer = tn.IndexOf("DisguiserSunFlower", StringComparison.Ordinal) >= 0;
				string label2 = isProducer ? "脑光" : "次数";

				if (_izmProdReported.Add("inline:" + (charName ?? "?")))
				{
					Info("伪装内联血量产出[" + (charName ?? "?") + "] 类型=" + tn
						+ "：固化分段=" + InlineSegments
						+ " 每段血=" + step2.ToString("0.##")
						+ " hpNext=" + hpNext2.ToString("0.##")
						+ " → 已触发 " + consumed2 + " 次，剩余 " + left2 + " 次"
						+ (isProducer ? "（产出型）" : "（动作型：" + tn + "）"));
				}

				if (CatOn(7) && lines.Count < MaxTotalLines)
				{
					Color c2 = (left2 > 0) ? ReadyColor : DisabledColor;
					AddLine(lines, label2 + " 剩 " + left2 + "/" + InlineSegments + " 次", c2);
				}
			}
		}
		catch { }

		// ②.11 泡椒罐子（TowerDefensePlantJalaVase）—— 装填个数 + 下一发倒计时（v1.19.3）
		//
		// ── 机制（读源码，非推测）──────────────────────────────────────────
		// `Asset/Anime/Character/Plant/Star/JalaVase/Scene/TowerDefensePlantJalaVase.cs`：
		//     private const int MAX_JALA_COUNT = 4;
		//     public Array<TowerDefensePacketConfig> jalaList;   // 已装填的泡椒（≤4）
		//     public Array<double> timerList = {0,0,0,0};         // 每个泡椒各自的计时
		//     public double timeNeed = 50.0;                     // 到点即发射
		//     CanAddJala(): ... && jalaList.Count < 4            // 上限 = MAX_JALA_COUNT
		//     BatchUpdate(): foreach jala:
		//         if (timerList[i] < timeNeed) timerList[i] += delta;
		//         else { timerList[i] = 0; ExecuteJala(jalaList[i]); }   // ← 发射后清零重计
		// ⇒ **"装填次数" = `jalaList.Count`（0~4）**；每个泡椒装填后各自计时 `timeNeed` 秒发射一次。
		//
		// ── 为什么之前没显示 ──────────────────────────────────────────────
		//   本 Mod 原先只认**组件**（`cm.GetRuntime<T>()`）与少数几个角色类字段，
		//   而泡椒罐子的状态**全在角色类自己的 public 字段上**（没有对应组件），
		//   所以一条都没命中 ⇒ 什么都不显示（用户反馈"应该显示装填次数但没显示"）。
		//
		// ── 识别方式 ─────────────────────────────────────────────────────
		//   角色类型上**同时**存在 `jalaList` 与 `timeNeed` 字段 —— 足够独特，不会误伤别的植物。
		//   归类走「装填与核能」开关（CatOn(0)）：用户原话就是"装填次数"。
		//   上限读 `MAX_JALA_COUNT` 常量（`private const`，须用 `GetRawConstantValue()`，
		//   普通 `GetValue` 对 const 会抛 —— 本项目在"等级上限"那里踩过同样的坑）。
		try
		{
			Type jt = character.GetType();
			FieldInfo fJala = jt.GetField("jalaList",
				BindingFlags.Public | BindingFlags.Instance);
			FieldInfo fNeed = jt.GetField("timeNeed",
				BindingFlags.Public | BindingFlags.Instance);
			if (fJala != null && fNeed != null && lines.Count < MaxTotalLines)
			{
				int jn = 0;
				try
				{
					object o = fJala.GetValue(character);
					if (o is Godot.Collections.Array ga)
					{
						jn = ga.Count;
					}
					else if (o is System.Collections.ICollection col)
					{
						jn = col.Count;
					}
				}
				catch { }
				int jmax = 4;
				try
				{
					FieldInfo fMax = jt.GetField("MAX_JALA_COUNT",
						BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
					if (fMax != null && fMax.GetRawConstantValue() is int mv && mv > 0)
					{
						jmax = mv;
					}
				}
				catch { }
				// 下一发倒计时 = min(timeNeed − timerList[i])，只统计"已装填"的那几个
				double need = 0.0;
				try
				{
					object o = fNeed.GetValue(character);
					if (o is double dd) { need = dd; }
					else if (o is float ff) { need = ff; }
				}
				catch { }
				double soonest = double.MaxValue;
				try
				{
					FieldInfo fT = jt.GetField("timerList",
						BindingFlags.Public | BindingFlags.Instance);
					if (fT != null && fT.GetValue(character) is Godot.Collections.Array ta)
					{
						int lim = (jn < ta.Count) ? jn : ta.Count;
						for (int i = 0; i < lim; i++)
						{
							double t = 0.0;
							try { t = ta[i].AsDouble(); } catch { }
							double rem = need - t;
							if (rem < soonest) { soonest = rem; }
						}
					}
				}
				catch { }

				if (_jalaReported.Add(charName ?? "?"))
				{
					Info("泡椒罐子[" + (charName ?? "?") + "]：装填=" + jn + "/" + jmax
						+ " timeNeed=" + need.ToString("0.##")
						+ " 下一发=" + ((soonest == double.MaxValue) ? "<无>" : soonest.ToString("0.###"))
						+ "s");
				}

				if (CatOn(0) && lines.Count < MaxTotalLines)
				{
					AddLine(lines, "装填 " + jn + "/" + jmax, (jn > 0) ? ReadyColor : DisabledColor);
				}
				if (jn > 0 && need > 0.0 && soonest != double.MaxValue
					&& CatOn(0) && lines.Count < MaxTotalLines)
				{
					double r = soonest;
					if (r < 0.0) { r = 0.0; }
					AddTimer(lines, "发射 " + r.ToString("0.0") + "s", BusyColor, need);
				}
			}
		}
		catch { }

		// ③ 土豆雷准备出土
		try
		{
			PotatoComponent potato = cm.GetRuntime<PotatoComponent>(PotatoInstanceId);
			if (potato != null && _fPotatoRun != null)
			{
				object run = _fPotatoRun.GetValue(potato);
				object rem = _fPotatoRemain != null ? _fPotatoRemain.GetValue(potato) : null;
				if (CatOn(8) && rem != null && lines.Count < MaxTotalLines)
				{
					double r = Convert.ToDouble(rem);
					bool running = (run is bool bb) && bb;
					if (running && r > 0)
					{
						AddTimer(lines, "准备 " + r.ToString("0.0") + "s", BusyColor, PotatoReadyTotal(potato));
					}
					else if (!running)
					{
						AddLine(lines, "准备 就绪", ReadyColor);
					}
				}
			}
			else if (potato == null && _potatoProbed.Add(charName ?? "?")
				&& ComponentListHasType(cm, "PotatoComponent"))
			{
				Info("土豆雷候选[" + (charName ?? "?") + "]：组件清单里有 PotatoComponent，"
					+ "但按 character.potato 取不到；该角色 InstanceId 注册表=" + InstanceIdKeysDump(cm));
			}
		}
		catch { }

		// ④ 篮球剩余投抛数——**仅 maxHitNum>0 的真"弹药"角色**：
		//   坚果系（Wallnut/Icenut 等 14 个）用 BowlingComponent 做"被击滚动"效果、
		//   没有 maxHitNum 配置（默认 0）——不筛会产生一排"剩余 0 发"噪音。
		try
		{
			BowlingComponent bowl = cm.GetRuntime<BowlingComponent>(BowlingInstanceId);
			if (CatOn(8) && bowl != null && _fBowlHit != null && _fBowlMax != null && lines.Count < MaxTotalLines)
			{
				object hit = _fBowlHit.GetValue(bowl);
				object max = _fBowlMax.GetValue(bowl);
				if (hit != null && max != null)
				{
					int maxLeft = Convert.ToInt32(max);
					if (maxLeft > 0)
					{
						int left = maxLeft - Convert.ToInt32(hit);
						AddLine(lines, "剩余 " + left + " 发", (left > 0) ? ReadyColor : DisabledColor);
					}
					else if (_bowlProbed.Add(charName ?? "?"))
					{
						Info("篮球[" + (charName ?? "?") + "]：maxHitNum=0（滚动型角色，按设计不显示）");
					}
				}
			}
			else if (bowl == null && _bowlProbed.Add(charName ?? "?")
				&& ComponentListHasType(cm, "BowlingComponent"))
			{
				Info("篮球候选[" + (charName ?? "?") + "]：组件清单里有 BowlingComponent，"
					+ "但按 character.bowling 取不到；该角色 InstanceId 注册表=" + InstanceIdKeysDump(cm));
			}
		}
		catch { }

		// ⑤ 投石车剩弹
		try
		{
			CatapultComponent cat = cm.GetRuntime<CatapultComponent>(CatapultInstanceId);
			if (CatOn(8) && cat != null && _fCatCur != null && _fCatMax != null && lines.Count < MaxTotalLines)
			{
				object cur = _fCatCur.GetValue(cat);
				object max = _fCatMax.GetValue(cat);
				if (cur != null && max != null)
				{
					int left = Convert.ToInt32(cur);
					int total = Convert.ToInt32(max);
					AddLine(lines, (total > 0) ? ("剩弹 " + left + "/" + total) : ("剩弹 " + left), (left > 0) ? ReadyColor : DisabledColor);
				}
			}
			else if (cat == null && _catProbed.Add(charName ?? "?")
				&& ComponentListHasType(cm, "CatapultComponent"))
			{
				Info("投石车候选[" + (charName ?? "?") + "]：组件清单里有 CatapultComponent，"
					+ "但按 character.catapult 取不到；该角色 InstanceId 注册表=" + InstanceIdKeysDump(cm));
			}
		}
		catch { }

		// ⑥ 缠绕海草抓取数（TanglekelpComponent）——海草系 6 个（Tanglekelp/DoomTanglekelp/
		//   TanglekelpH/PlanternTanglekelp/InsaniKelp/僵尸 Snorkle）
		try
		{
			TanglekelpComponent tk = cm.GetRuntime<TanglekelpComponent>(TanglekelpInstanceId);
			if (EnableGrabLine && CatOn(8) && tk != null && _fTkCur != null && _fTkMax != null
				&& lines.Count < MaxTotalLines)
			{
				object tkCur = _fTkCur.GetValue(tk);
				object tkMax = _fTkMax.GetValue(tk);
				if (tkCur != null && tkMax != null)
				{
					int tkC = Convert.ToInt32(tkCur);
					int tkM = Convert.ToInt32(tkMax);
					if (_tkReported.Add(charName ?? "?"))
					{
						Info("抓取[" + (charName ?? "?") + "]：currentGrabNum=" + tkC
							+ " grabNum=" + tkM);
					}
					if (tkM > 0)
					{
						AddLine(lines, "抓取 " + tkC + "/" + tkM, (tkC < tkM) ? ReadyColor : DisabledColor);
					}
				}
			}
		}
		catch { }

		// ⑦ 墓碑破坏者吞噬墓碑（GravebusterComponent）
		//   配置 consumeDuration + 运行时 _consumeRunning/_consumeStartedAtMsec(ms)
		//   ⇒ 剩余 = consumeDuration − (now − startedAtMsec)/1000
		try
		{
			GravebusterComponent gb = cm.GetRuntime<GravebusterComponent>(GravebusterInstanceId);
			if (gb != null && _fGraveDur != null && _fGraveRun != null && _fGraveStart != null)
			{
				bool gbRun = (_fGraveRun.GetValue(gb) is bool gbB) && gbB;
				double dur = (_fGraveDur.GetValue(gb) != null)
					? Convert.ToDouble(_fGraveDur.GetValue(gb)) : 0.0;
				ulong started = (_fGraveStart.GetValue(gb) != null)
					? Convert.ToUInt64(_fGraveStart.GetValue(gb)) : 0UL;
				double remainG = 0.0;
				if (gbRun && dur > 0 && started > 0)
				{
					ulong nowMs = Godot.Time.GetTicksMsec();
					double elapsed = (nowMs >= started) ? (nowMs - started) / 1000.0 : 0.0;
					remainG = dur - elapsed;
					if (remainG < 0) { remainG = 0; }
				}
				if (gbRun && _gbReported.Add(charName ?? "?"))
				{
					Info("啃碑[" + (charName ?? "?") + "]：consumeDuration=" + dur.ToString("0.###")
						+ " → 剩余=" + remainG.ToString("0.###"));
				}
				if (CatOn(8) && gbRun && dur > 0 && remainG > 0 && remainG <= dur
					&& lines.Count < MaxTotalLines)
				{
					AddTimer(lines, "啃碑 " + remainG.ToString("0.0") + "s", BusyColor, dur);
				}
			}
		}
		catch { }

		// ⑩ 等级型植物（v1.17.0，用户需求）：**增压大喷菇（PlantStressShroom）** 与
		//   **豆荚壳（PlantPumpkinPea）** —— 这两只靠「叠种」升级，等级是它们最关键的数值，
		//   此前完全没显示（用户："cd显示mod不会正确显示增压大喷菇的等级，参考充能阳光显示等级"）。
		//
		//   机制（源码定案）：
		//     · `TowerDefensePlantStressShroom`（Chapter7）：`public int level = 1`、
		//       `private const int MaxLevel = 4`，`Cover()` 里
		//       `level = Mathf.Clamp(同种.level + 1, 1, 4)` → 每叠一株升一级；
		//       `SetAttackDamageForLevel`：伤害 = 20 + (lv-1)*20。
		//     · `TowerDefensePlantPumpkinPea`（Chapter7）：同样形态，`Clamp(… + 1, 1, 3)`，上限 3。
		//   显示口径（与"充能阳光菇/蓄能咖啡豆"的 `充能N` 对齐）：**`等级 N/M`**。
		//
		//   ⚠️ 识别判据：**必须带 `public void LevelSet(int)` 才算**——这是"叠种升级"型
		//      独有的公开接口；光看 `level` 字段会误伤 `TowerDefensePlantSunShroomCharge`
		//      （它也有 level，但那是"按时间充能"、已由 ②.10 分支接管并显示 `充能N`）。
		//      ⚠️ 与 ②.10 的先后顺序是**故意的**：充能类先接管，这里只兜住"剩下的"。
		bool levelHandled = false;
		try
		{
			FieldInfo fLv, fLvMax;
			GetLevelFields(character.GetType(), out fLv, out fLvMax);
			if (fLv != null)
			{
				// 排除充能类：它们由 ②.10 显示"充能N"，这里不重复。
				// 判据 = 有 `_chargeLevel`（蓄能咖啡豆）或 `_produceInterval`（充能阳光菇）。
				bool isChargeKind = (FindFieldAlong(character.GetType(), "_chargeLevel") != null)
					|| (FindFieldAlong(character.GetType(), "_produceInterval") != null);
				if (!isChargeKind)
				{
					int lv = ReadConstOrFieldInt(fLv, character, 0);
					int lvMax = ReadConstOrFieldInt(fLvMax, character, 0);
					levelHandled = true;
					if (_lvReported.Add(charName ?? "?"))
					{
						Info("等级[" + (charName ?? "?") + "]：类型=" + character.GetType().Name
							+ " level=" + lv + " MaxLevel=" + lvMax);
					}
					// 等级 1 = 刚种下、没有升级信息量 ⇒ 不占行（血条已经够挤）；
					// 升过级才显示，玩家一眼就知道"这株被叠过"。
					if (lv >= 2 && lines.Count < MaxTotalLines)
					{
						string lvTxt = (lvMax > 0)
							? ("等级 " + lv + "/" + lvMax)
							: ("等级 " + lv);
						AddLine(lines, lvTxt, ReadyColor);
					}
				}
			}
		}
		catch { }
		if (levelHandled)
		{
			return;
		}

		// ⑨ 速度倍率 + 攻速倍率（需求 5，归"等级与加速"类）：
		//   (a) 速度倍率：角色 buff 容器里的加速 buff（BuffCoffee / BuffMagicRootHaste 等）
		//       都带 `timeScaleValue : Double`（时间缩放 = 速度倍率）——取最大值。
		//   (b) 攻速倍率：AttackComponent 的 `attackIntervalBase / attackInterval`
		//       （间隔变小 = 攻速变快；杨桃系"经历火/冰后攻速提升"等效果最终都体现在这里）。
		//   显示：`加速 +N%` / `攻速 +N%`（就绪绿）。
		try
		{
			double spd = 1.0;
			double lastFireCur = 0.0;
			double lastFireBase = 0.0;
			int lastFireNum = 0;
			double lastTs = 1.0;
			var boostBuffs = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>>();
			// 诊断（v1.10.1）：倍率段执行探测——每角色一次，判定卡在哪一步
			try
			{
				if (_boostProbeReported.Add(charName ?? "?"))
				{
					FireComponent fcP = FindComponentOfType<FireComponent>(cm);
					AttackComponent acP = FindComponentOfType<AttackComponent>(cm);
					Info("倍率探测[" + (charName ?? "?") + "]：Fire=" + (fcP != null)
						+ " Attack=" + (acP != null)
						+ " fFireTimeScale=" + (_fFireTimeScale != null)
						+ " fAtkBase=" + (_fAtkIntervalBase != null)
						+ " buff容器=" + (character.buff != null));
				}
			}
			catch { }
			try
			{
				BuffComponent bc = character.buff;
				// 诊断（v1.10.7）：**首次"身上挂着 buff"时**列出 buff 键 —— 判断"到底有没有加成 buff"
				// （配合操作：点蓄能咖啡豆 / 等猫窝节奏触发后再看这条）
				try
				{
					if (bc != null && _fBuffDict != null)
					{
						System.Collections.IDictionary bd0
							= _fBuffDict.GetValue(bc) as System.Collections.IDictionary;
						if (bd0 != null && bd0.Count > 0 && _buffListReported.Add(charName ?? "?"))
						{
							var sb0 = new System.Text.StringBuilder();
							foreach (object k0 in bd0.Keys)
							{
								if (sb0.Length > 0) { sb0.Append(','); }
								sb0.Append(k0);
							}
							Info("buff列表[" + (charName ?? "?") + "]：" + sb0.ToString());
						}
					}
				}
				catch { }
				if (bc != null && _fBuffDict != null)
				{
					System.Collections.IDictionary bd
						= _fBuffDict.GetValue(bc) as System.Collections.IDictionary;
					if (bd != null)
					{
						// v1.10.9：收集"加成类" buff 的**键+配置对象**（配置对象里带数值，如
						// BuffTabooBean 的 _savedTimeScaleInit / time / currentTime）
						try
						{
							foreach (System.Collections.DictionaryEntry e8 in bd)
							{
								string ks8 = (e8.Key != null) ? e8.Key.ToString() : null;
								if (ks8 != null && IsBoostBuff(ks8) && boostBuffs.Count < 3)
								{
									boostBuffs.Add(new System.Collections.Generic.KeyValuePair<string, object>(
										ks8, e8.Value));
								}
							}
						}
						catch { }
						foreach (object bv in bd.Values)
						{
							if (bv == null)
							{
								continue;
							}
							FieldInfo fts = GetBuffTimeScaleField(bv.GetType());
							if (fts == null)
							{
								continue;
							}
							object tv = fts.GetValue(bv);
							if (tv != null)
							{
								double v = Convert.ToDouble(tv);
								if (System.Math.Abs(v - 1.0) > 0.0001)
								{
									spd = v;
								}
							}
						}
					}
				}
			}
			catch { }

			double atk = 1.0;
			try
			{
				AttackComponent ac2 = FindComponentOfType<AttackComponent>(cm);
				if (ac2 != null && _fAtkIntervalBase != null && _fAtkInterval != null)
				{
					double b0 = (_fAtkIntervalBase.GetValue(ac2) != null)
						? Convert.ToDouble(_fAtkIntervalBase.GetValue(ac2)) : 0.0;
					double c0 = (_fAtkInterval.GetValue(ac2) != null)
						? Convert.ToDouble(_fAtkInterval.GetValue(ac2)) : 0.0;
					if (b0 > 0 && c0 > 0)
					{
						atk = b0 / c0;
					}
				}
			}
			catch { }

			// (c) 发射型（FireComponent——绝大多数射手、杨桃系都用它，没有 AttackComponent）：
			//   `timeScale`（时间缩放，>1=加速）、`fireIntervalBase/fireInterval`（间隔变小=攻速快）、
			//   `fireNum/currentFireNum`（多发加成）。**杨桃"经历火/冰后攻速提升"的落点就在这里**。
			try
			{
				FireComponent fc = FindComponentOfType<FireComponent>(cm);
				if (fc != null && _fFireTimeScale != null)
				{
					double ts = (_fFireTimeScale.GetValue(fc) != null)
						? Convert.ToDouble(_fFireTimeScale.GetValue(fc)) : 1.0;
					double b1 = (_fFireIntervalBase != null && _fFireIntervalBase.GetValue(fc) != null)
						? Convert.ToDouble(_fFireIntervalBase.GetValue(fc)) : 0.0;
					double c1 = (_fFireInterval != null && _fFireInterval.GetValue(fc) != null)
						? Convert.ToDouble(_fFireInterval.GetValue(fc)) : 0.0;
					int fn = (_fFireNum != null && _fFireNum.GetValue(fc) != null)
						? Convert.ToInt32(_fFireNum.GetValue(fc)) : 0;
					int fcn = (_fFireCurNum != null && _fFireCurNum.GetValue(fc) != null)
						? Convert.ToInt32(_fFireCurNum.GetValue(fc)) : 0;
					if (System.Math.Abs(ts - 1.0) > 0.0001)
					{
						spd = ts;
					}
					lastFireCur = c1;
					lastFireBase = b1;
					lastFireNum = fn;
					lastTs = ts;
					if (b1 > 0 && c1 > 0)
					{
						atk = b1 / c1;
					}
					// 诊断一：**基线**——首次遇到该发射型角色就打一条（种下时的原始值）
					if (_fireReported.Add(charName ?? "?"))
					{
						Info("发射基线[" + (charName ?? "?") + "]：timeScale=" + ts.ToString("0.###")
							+ " 基础间隔=" + b1.ToString("0.###")
							+ " 当前间隔=" + c1.ToString("0.###")
							+ " 弹数=" + fcn + "/" + fn);
					}
					// 诊断二：**加成**——首次检测到加成时再打一条（与基线对比即知加成改了哪个字段）
					bool boosted = (ts > 1.0001) || (b1 > 0 && c1 > 0 && c1 < b1 - 0.0001)
						|| (fn > 0 && fcn > fn);
					// 变化诊断（v1.10.3）：记录每角色首次基线与当前值，偏离即打一条
					try
					{
						string bk = charName ?? "?";
						if (_fireBaseline.ContainsKey(bk))
						{
							double[] bv = _fireBaseline[bk];
							if ((System.Math.Abs(ts - bv[0]) > 0.001
								|| System.Math.Abs(b1 - bv[1]) > 0.001
								|| System.Math.Abs(c1 - bv[2]) > 0.001
								|| fn != (int)bv[3])
								&& _fireChangedReported.Add(bk))
							{
								Info("发射变化[" + bk + "]：基线(ts=" + bv[0].ToString("0.###")
									+ " base=" + bv[1].ToString("0.###")
									+ " cur=" + bv[2].ToString("0.###")
									+ " fn=" + (int)bv[3] + ") → 当前(ts=" + ts.ToString("0.###")
									+ " base=" + b1.ToString("0.###")
									+ " cur=" + c1.ToString("0.###")
									+ " fn=" + fn + ")");
							}
						}
						else
						{
							_fireBaseline[bk] = new double[] { ts, b1, c1, fn };
						}
					}
					catch { }
					if (boosted && _fireBoostedReported.Add(charName ?? "?"))
					{
						Info("发射加成[" + (charName ?? "?") + "]：timeScale=" + ts.ToString("0.###")
							+ " 基础间隔=" + b1.ToString("0.###")
							+ " 当前间隔=" + c1.ToString("0.###")
							+ " 弹数=" + fcn + "/" + fn);
					}
				}
			}
			catch { }

			if ((System.Math.Abs(spd - 1.0) > 0.001 || System.Math.Abs(atk - 1.0) > 0.001) && _buffReported.Add(charName ?? "?"))
			{
				Info("倍率[" + (charName ?? "?") + "]：速度倍率=" + spd.ToString("0.###")
					+ " 攻速倍率=" + atk.ToString("0.###"));
			}
			// (d) 角色级**伤害缩放**（如狂热星 FeverStar 的 `damageScale`）——基线对比 → `伤害 ±N%`。
			//   v1.10.5：用户的"狂热星也有类似提升"实为伤害加成（该类带 damageScale/projectileScale）。
			try
			{
				FieldInfo fDmg = GetDmgField(character.GetType());
				if (fDmg != null && fDmg.GetValue(character) != null)
				{
					double dm = Convert.ToDouble(fDmg.GetValue(character));
					string bk3 = charName ?? "?";
					double dmBase;
					if (_dmgBaseline.TryGetValue(bk3, out dmBase))
					{
						if (System.Math.Abs(dm - dmBase) > 0.001)
						{
							if (_dmgChangedReported.Add(bk3))
							{
								Info("伤害变化[" + bk3 + "]：基线=" + dmBase.ToString("0.###")
									+ " → 当前=" + dm.ToString("0.###"));
							}
							if (CatOn(9) && lines.Count < MaxTotalLines)
							{
								double rr = (dmBase > 0.0001) ? (dm / dmBase) : 1.0;
								if (rr > 1.0001)
								{
									AddLine(lines, "伤害 +" + ((rr - 1.0) * 100.0).ToString("0") + "%", ReadyColor);
								}
								else if (rr < 0.9999)
								{
									AddLine(lines, "伤害 -" + ((1.0 - rr) * 100.0).ToString("0") + "%", DisabledColor);
								}
							}
						}
					}
					else
					{
						_dmgBaseline[bk3] = dm;
					}
				}
			}
			catch { }

			if (CatOn(9) && lines.Count < MaxTotalLines)
			{
				// ★ v1.10.4：以"种下时的实测值"为基线，只显示**真实变化**——
				//   游戏里不少植物天生 base≠cur（如间隔 1.5 vs 3），拿标称值当基准会产生
				//   常驻噪音（"攻速 -50%"）却漏掉真实变化（寒冰杨桃的"弹数 +1"）。
				// (1) 速度倍率：**绝对倍率**（来自加速 buff 的 timeScaleValue）——不依赖基线，
				//     这样"非发射型但有加速 buff 的角色"也能显示（v1.10.6）。buff 在=加成中。
				// ★ v1.15.2 **回退**：`character.timeScale` **不能用作"植物加速倍率"**——
				//   实测它含**游戏全局快进倍率**（日志：所有植物恒为 3），与 buff 倍率相乘后
				//   产生离谱值（用户报"毁灭咖啡豆+咖啡三叶草 → +1400%"）。
				//   恢复：`spd` 只来自 buff 的 timeScaleValue / FireComponent.timeScale。
				if (spd > 1.0001)
				{
					AddLine(lines, "加速 +" + ((spd - 1.0) * 100.0).ToString("0") + "%", ReadyColor);
				}
				else if (spd < 0.9999)
				{
					AddLine(lines, "加速 -" + ((1.0 - spd) * 100.0).ToString("0") + "%", DisabledColor);
				}
				// (1.2) 攻速：**标称间隔 / 当前间隔**（>1 = 比标称更快，即被加成）——
				//   猫窝"使种在猫窝内的猫尾草类植物攻速翻倍"的落点就在这里
				//   （标称 1.5 → 实际 0.75 = 攻速 +100%）。只显示"变快"（变慢属天生差异，避免噪音）。
				if (lastFireBase > 0.0001 && lastFireCur > 0.0001 && lines.Count < MaxTotalLines)
				{
					double ratioF = lastFireBase / lastFireCur;
					if (ratioF > 1.0001)
					{
						AddLine(lines, "攻速 +" + ((ratioF - 1.0) * 100.0).ToString("0") + "%", ReadyColor);
					}
				}
				// (1.4) ★ **猫窝加速**（v1.15.0 由新解包源码**定案**）——
				//   `FireComponent.cs`：`public bool hasCatPumpkin;`（每 30 物理帧检测：
				//   `physiqueTypeFlags & 0x100` 的猫尾草类且 `parent.cell.HasCharacter("PlantCatPumpkin")`）；
				//   倍率来自 `GetCatPumpkinFireRateScale()` —— **`return 2f`（×2）**，
				//   且同时作用于发射计时器（`timerRunScale *= ...`）与动画
				//   （`sprite.timeScale = ... * GetCatPumpkinFireRateScale()`）。
				//   ⇒ 直接读 hasCatPumpkin 显示"猫窝加速 ×2"（比此前"动画×6"的间接观测准确）。
				try
				{
					ComponentManager cm15 = character.componentManager;
					if (cm15 != null)
					{
						FireComponent fc15 = FindComponentOfType<FireComponent>(cm15);
						bool hp15 = false;
						try { hp15 = (fc15 != null) && fc15.hasCatPumpkin; } catch { }
						if (hp15 && lines.Count < MaxTotalLines)
						{
							AddLine(lines, "猫窝加速 ×2", ReadyColor);
						}
						if (_obstacleHpReported.Add("CAT|" + (charName ?? "?")))
						{
							Info("猫窝加速判定[" + (charName ?? "?") + "]：Fire=" + (fc15 != null)
								+ " hasCatPumpkin=" + hp15);
						}
					}
				}
				catch { }
				// (1.5) 加成 buff 名 + **数值**（v1.10.9，如"毁灭加成 +200%"或"毁灭加成 12.3s"）——
				//       倍率优先（_savedTimeScaleInit 与当前 timeScale 之比），算不出则显示剩余时长。
				for (int bi = 0; bi < boostBuffs.Count && lines.Count < MaxTotalLines; bi++)
				{
					string bkey = boostBuffs[bi].Key;
					object bcfg = boostBuffs[bi].Value;
					string valStr = "";
					double savedTs = 0.0;
					bool hasSaved = false;
					try
					{
						if (bcfg != null)
						{
							FieldInfo fSaved = FindFieldAlong(bcfg.GetType(), "_savedTimeScaleInit");
							FieldInfo fHas = FindFieldAlong(bcfg.GetType(), "_hasSavedTimeScaleInit");
							if (fSaved != null && fSaved.GetValue(bcfg) != null)
							{
								savedTs = Convert.ToDouble(fSaved.GetValue(bcfg));
							}
							if (fHas != null && (fHas.GetValue(bcfg) is bool hb8))
							{
								hasSaved = hb8;
							}
							// 倍率：当前 timeScale / 保存值
							if (hasSaved && savedTs > 0.0001 && lastTs > 0.0001
								&& System.Math.Abs(lastTs - savedTs) > 0.0001)
							{
								double rr8 = lastTs / savedTs;
								valStr = " +" + ((rr8 - 1.0) * 100.0).ToString("0") + "%";
							}
							else
							{
								// 退化：显示剩余时长
								FieldInfo fT8 = FindFieldAlong(bcfg.GetType(), "time");
								FieldInfo fC8 = FindFieldAlong(bcfg.GetType(), "currentTime");
								double bt8 = (fT8 != null && fT8.GetValue(bcfg) != null)
									? Convert.ToDouble(fT8.GetValue(bcfg)) : 0.0;
								double bc8 = (fC8 != null && fC8.GetValue(bcfg) != null)
									? Convert.ToDouble(fC8.GetValue(bcfg)) : 0.0;
								if (bt8 > bc8)
								{
									valStr = " " + (bt8 - bc8).ToString("0.0") + "s";
								}
							}
						}
					}
					catch { }
					if (_buffValReported.Add((charName ?? "?") + "|" + bkey))
					{
						Info("加成值[" + (charName ?? "?") + "/" + bkey + "]：savedTs="
							+ savedTs.ToString("0.###") + " hasSaved=" + hasSaved
							+ " 当前timeScale=" + lastTs.ToString("0.###")
							+ " → 显示\"" + valStr + "\"");
					}
					AddLine(lines, BoostBuffName(bkey) + "加成" + valStr, ReadyColor);
				}
				// (2) 弹数/攻速：以种下基线对比（天生差异不算）
				double[] bv2;
				if (_fireBaseline.TryGetValue(charName ?? "?", out bv2))
				{
					// 弹数变化（多发加成）——寒冰杨桃实测 1→2
					int fnDelta = lastFireNum - (int)bv2[3];
					if (fnDelta > 0)
					{
						AddLine(lines, "弹数 +" + fnDelta, ReadyColor);
					}
					// （攻速已由 (1.2) 的"标称 vs 当前"通道处理——它才是猫窝类加成的落点）
				}
			}
		}
		catch (Exception exBoost)
		{
			if (_boostProbeReported.Add("EX|" + (charName ?? "?")))
			{
				Warn("倍率段异常[" + (charName ?? "?") + "]：" + exBoost.Message);
			}
		}

		// ⑧ 疯狂海草（InsaniKelp）：**点击生成僵尸**，点击冷却 = 它计时器里的 "Open" 键
		//   （定义实读 timerDictionary = { "Open": 2.0 } ——"至少两秒才能成功点击"即此冷却）。
		//   冷却中 → `点击 X.Xs`（青蓝）；就绪 → `可点击`（绿）。
		//   （它的 "Open" 键在 ② 计时器分支被跳过，由本段专门接管，避免"张开"文案重复。）
		try
		{
			if (character.GetType().Name.Contains("InsaniKelp"))
			{
				CharacterTimerComponent tc2
					= cm.GetRuntime<CharacterTimerComponent>(TimerInstanceId);
				if (tc2 != null && _fTimerRun != null && _fTimerWait != null)
				{
					System.Collections.IDictionary runDic2
						= _fTimerRun.GetValue(tc2) as System.Collections.IDictionary;
					System.Collections.IDictionary waitDic2
						= _fTimerWait.GetValue(tc2) as System.Collections.IDictionary;
					System.Collections.IDictionary curDic2 = (_fTimerCur != null)
						? _fTimerCur.GetValue(tc2) as System.Collections.IDictionary : null;
					if (runDic2 != null && runDic2.Contains("Open"))
					{
						bool on2 = (runDic2["Open"] is bool b2) && b2;
						double w2 = (waitDic2 != null && waitDic2.Contains("Open"))
							? Convert.ToDouble(waitDic2["Open"]) : 0.0;
						double c2 = (curDic2 != null && curDic2.Contains("Open"))
							? Convert.ToDouble(curDic2["Open"]) : 0.0;
						double rem2 = w2 - c2;
						if (rem2 < 0) { rem2 = 0; }
						if (_spawnReported.Add(charName ?? "?"))
						{
							Info("点击[" + (charName ?? "?") + "]：Open运行中=" + on2
								+ " 冷却时长=" + w2.ToString("0.###")
								+ " 当前值=" + c2.ToString("0.###")
								+ " → 剩余=" + rem2.ToString("0.###"));
						}
						if (EnableKelpClick && lines.Count < MaxTotalLines)
						{
							if (on2 && rem2 > 0)
							{
								AddTimer(lines, "点击 " + rem2.ToString("0.0") + "s", BusyColor, w2);
							}
							else if (!on2)
							{
								AddLine(lines, "可点击", ReadyColor);
							}
						}
					}
				}
			}
		}
		catch { }
	}

	// ---------------------------------------------------------------- 设置页开关

	/// <summary>
	/// 在 BattleOption 对话框里注入「显示冷却倒计时」开关（幂等）——**两列布局**：
	///   CenterContainer
	///   └── HBox「ModOptionColumns」（新建）
	///       ├── 原 VBoxContainer（血条开关等原列表，整体移入左列——原地不动会重叠）
	///       └── VBox「ModOptionColumn」（新列，内容顶部对齐）
	///           └── CheckBox「显示冷却倒计时」
	///
	/// 背景：BattleOption.cs 在 pck 里是 1 字节占位、只认识自己那几个 CheckBox，
	/// 而 ModLoader 资源管线只认 .tres/.res 配置类别、覆盖不了 Prefab/ 下的 .tscn ——
	/// 所以唯一可行的路是托管代码运行时创建节点 + 插件自己接管 toggled 信号。
	///
	/// reparent 安全性：BattleOption 的 plantHealthCheckBox 等字段是**对象引用**
	/// （元数据实查：F plantHealthCheckBox : CheckBox），赋值后与节点位置无关；
	/// 本注入在对话框 _Ready 之后执行，所以挪动原列表不会破坏游戏脚本。
	/// </summary>
	private void TryEnsureOptionCheckBox(Node optionRoot)
	{
		if (optionRoot == null || !GodotObject.IsInstanceValid(optionRoot))
		{
			return;
		}
		HBoxContainer cols = optionRoot.FindChild(OptionColumnsName, true, false) as HBoxContainer;
		if (cols != null && GodotObject.IsInstanceValid(cols))
		{
			// 已注入：校准总开关 + 6 类功能开关的状态
			CheckBox exist = cols.FindChild(OptionCheckBoxName, true, false) as CheckBox;
			if (exist != null && GodotObject.IsInstanceValid(exist)
				&& exist.ButtonPressed != _enabled)
			{
				exist.SetPressedNoSignal(_enabled);
			}
			for (int i = 0; i < FeatureKeys.Length; i++)
			{
				CheckBox fe = cols.FindChild("ModFeatureCb" + i, true, false) as CheckBox;
				bool want = GetFeatureValue(i);
				if (fe != null && GodotObject.IsInstanceValid(fe) && fe.ButtonPressed != want)
				{
					fe.SetPressedNoSignal(want);
				}
			}
			// ★ v1.19.0：「只显示 ≥5 秒」独立开关（不是 10 类之一，单独校准）
			CheckBox existLongCb = cols.FindChild(OnlyLongCheckBoxName, true, false) as CheckBox;
			if (existLongCb != null && GodotObject.IsInstanceValid(existLongCb)
				&& existLongCb.ButtonPressed != _onlyLongTimers)
			{
				existLongCb.SetPressedNoSignal(_onlyLongTimers);
			}
			return;
		}

		CenterContainer center = optionRoot.GetNodeOrNull<CenterContainer>("CenterContainer");
		VBoxContainer vbox = optionRoot.GetNodeOrNull<VBoxContainer>("CenterContainer/VBoxContainer");
		if (center == null || vbox == null)
		{
			return;   // 对话框还没初始化完，下轮扫描再试
		}

		// 1) 新建水平容器作为 CenterContainer 的唯一子（三列组）
		HBoxContainer hbox = new HBoxContainer();
		hbox.Name = OptionColumnsName;
		hbox.AddThemeConstantOverride("separation", 30);

		// 2) 第 1 列：游戏自带列表 + 总开关 + 署名（v1.13.1 三列布局）
		center.RemoveChild(vbox);
		VBoxContainer col1 = new VBoxContainer();
		col1.Name = "ModCol1";
		col1.AddThemeConstantOverride("separation", 20);
		col1.CustomMinimumSize = new Vector2(300f, 0f);
		col1.Alignment = BoxContainer.AlignmentMode.Begin;
		col1.AddChild(vbox);   // 游戏自带列表（对象引用，reparent 不破坏游戏脚本）

		// 署名/防冒用声明行（v1.15.4：移到**第三列最后一行**——用户要求）；
		// 在第 3 列组装完成后追加（见下方 col3.AddChild(sign)）。

		// 3) 第 2、3 列：**总开关 + 10 类功能开关**（v1.13.2：总开关从第 1 列移到第 2 列顶部，
		//    列间距调紧到 6，确保 5 行都能显示出来——此前第 2/3 列只显示 4 行）
		VBoxContainer col2 = new VBoxContainer();
		col2.Name = OptionModColumnName;
		col2.AddThemeConstantOverride("separation", 6);
		col2.CustomMinimumSize = new Vector2(240f, 0f);
		col2.Alignment = BoxContainer.AlignmentMode.Begin;
		VBoxContainer col3 = new VBoxContainer();
		col3.Name = "ModCol3";
		col3.AddThemeConstantOverride("separation", 6);
		col3.CustomMinimumSize = new Vector2(240f, 0f);
		col3.Alignment = BoxContainer.AlignmentMode.Begin;

		CheckBox cb = new CheckBox();
		cb.Name = OptionCheckBoxName;
		cb.Text = "显示冷却倒计时";
		cb.Alignment = HorizontalAlignment.Center;
		cb.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		cb.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		cb.AddThemeConstantOverride("outline_size", 5);
		cb.ButtonPressed = _enabled;
		cb.Connect("toggled", Callable.From(new Action<bool>(OnToggleChanged)));
		col2.AddChild(cb);     // 总开关（第 2 列顶部，v1.13.2）

		// ★ v1.19.0：「只显示 ≥5 秒」—— **优先级仅次于总开关**，紧挨着它显示。
		//   单独一个 CheckBox（不属于 10 类功能），这样它天然排在总开关下一行、
		//   又在所有分类开关之上，正好符合"优先级仅次于总开关"。
		CheckBox longCb = new CheckBox();
		longCb.Name = OnlyLongCheckBoxName;
		longCb.Text = "只显示 ≥5 秒";
		longCb.Alignment = HorizontalAlignment.Center;
		longCb.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		longCb.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		longCb.AddThemeConstantOverride("outline_size", 5);
		longCb.ButtonPressed = _onlyLongTimers;
		longCb.Connect("toggled", Callable.From(new Action<bool>(OnOnlyLongChanged)));
		col2.AddChild(longCb);
		try
		{
			Label longSub = new Label();
			longSub.Name = OnlyLongCheckBoxName + "Sub";
			longSub.Text = "— 总时长低于 4.9 秒的计时不显示 —";
			longSub.HorizontalAlignment = HorizontalAlignment.Center;
			longSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			longSub.CustomMinimumSize = new Vector2(230f, 0f);
			longSub.AddThemeFontSizeOverride("font_size", 11);
			longSub.AddThemeColorOverride("font_color", new Color(0.78f, 0.86f, 0.95f, 1f));
			col2.AddChild(longSub);
		}
		catch { }

		string[] ftext = { "装填与核能", "障碍物消失", "障碍物血量", "选卡栏种植CD", "生成计时", "角色计时器其他CD", "成长与充能", "产出倒计时", "战斗辅助", "等级与加速" };
		// v1.13.5：**显示顺序表**（用户要求："战斗辅助"与"生成计时"互换位置）——
		//   功能索引不变（文本/配置键/探针仍按原 idx），只调显示位次与"前5/后5"的列分配。
		int[] order = { 0, 1, 2, 3, 8, 9, 6, 7, 4, 5 };   // v1.15.2：角色计时其他CD(5) 与 等级与加速(9) 互换位置
		for (int k = 0; k < order.Length; k++)
		{
			int idx = order[k];   // 捕获副本（for 循环变量共享，必须复制）
			CheckBox fcb = MakeFeatureCheckBox("ModFeatureCb" + idx, ftext[idx],
				GetFeatureValue(idx), idx);
			VBoxContainer target = (k < 5) ? col2 : col3;
			target.AddChild(fcb);
			// v1.15.2：为"说明型"开关加一行小字说明（战斗辅助 / 角色计时器其他CD / 等级与加速），
			//   文字多则自动换行（AutowrapMode + 限定宽度）。
			string subTxt = null;
			if (idx == 8) { subTxt = "— 咀嚼·消化·土豆雷·篮球·投石车·啃碑 —"; }
			else if (idx == 5) { subTxt = "— 开火·蓄力·张开·闭合·倒计时·销毁·布雷·放置·限时 —"; }
			else if (idx == 9) { subTxt = "— 加速·攻速·弹数·伤害·动画·加成buff —"; }
			if (subTxt != null)
			{
				Label sub = new Label();
				sub.Name = "ModFeatureCb" + idx + "Sub";
				sub.Text = subTxt;
				sub.HorizontalAlignment = HorizontalAlignment.Center;
				sub.MouseFilter = Control.MouseFilterEnum.Ignore;
				sub.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
				sub.AddThemeConstantOverride("outline_size", 4);
				sub.AddThemeFontSizeOverride("font_size", 11);
				sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f, 1f));
				sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				sub.CustomMinimumSize = new Vector2(230f, 0f);
				target.AddChild(sub);
			}
		}

		// ★ v1.15.4：署名/防冒用行放在**第三列的最后一行**（用户要求；原在第 1 列游戏列表下方）
		Label sign = new Label();
		sign.Name = OptionAuthorLabelName;
		sign.Text = "apple1949开发中 请勿冒用发布！";
		sign.HorizontalAlignment = HorizontalAlignment.Center;
		sign.MouseFilter = Control.MouseFilterEnum.Ignore;
		sign.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		sign.AddThemeConstantOverride("outline_size", 5);
		sign.AddThemeFontSizeOverride("font_size", 14);
		col3.AddChild(sign);

		hbox.AddChild(col1);
		hbox.AddChild(col2);
		hbox.AddChild(col3);
		center.AddChild(hbox);

		if (!_checkBoxReadyReported)
		{
			_checkBoxReadyReported = true;
			Info("设置页开关已注入（三列布局：第 1 列=游戏列表+总开关，第 2/3 列=功能开关各 5 个，当前："
				+ (_enabled ? "开" : "关") + "）。");
		}
	}

	private void OnToggleChanged(bool on)
	{
		try
		{
			_enabled = on;
			SaveEnabled();
			Info("倒计时显示已切换为：" + (_enabled ? "开" : "关"));
		}
		catch (Exception ex)
		{
			Warn("开关切换处理异常（已吞）：" + ex.Message);
		}
	}

	// ---------------------------------------------------------------- 6 类功能开关

	/// <summary>
	/// "猫窝加成"落点探针（v1.11.1）：对**猫尾草类**植物（`config.physiqueTypeFlags` 含 256——
	/// 豌豆猫/猫尾草/卷心菜尾/僵尸猫尾草等 17 种，全库扫描定案），监控所有候选加速字段：
	/// 发射间隔/基准间隔/发射时间缩放/攻击间隔/**动画 _timeScale（cadence 节奏最可能在这）**。
	/// 任一变化即打日志（含前后值）——用于找出"攻速翻倍"究竟改在哪个字段。
	/// </summary>
	private void ProbeCatBoostFields(TowerDefenseCharacter character, string charName)
	{
		try
		{
			object cfg = character.config;
			if (cfg == null)
			{
				return;
			}
			// v1.11.5 回退：**不再对所有角色记录**（v1.11.4 的"全角色覆盖"引入误报——
			//   动画基准速度天生各异，僵尸等被误判为"加速"）。动画显示只对猫尾草类（探针里记录）。
			FieldInfo fFl = FindFieldAlong(cfg.GetType(), "physiqueTypeFlags");
			if (fFl == null || fFl.GetValue(cfg) == null)
			{
				return;
			}
			if ((Convert.ToInt32(fFl.GetValue(cfg)) & 256) == 0)
			{
				return;   // 非猫尾草类，跳过
			}
			ComponentManager cmP = character.componentManager;
			if (cmP == null)
			{
				return;
			}
			double[] now = new double[5];
			now[0] = -1; now[1] = -1; now[2] = -1; now[3] = -1; now[4] = -1;
			FireComponent fcP = FindComponentOfType<FireComponent>(cmP);
			if (fcP != null)
			{
				try
				{
					if (_fFireInterval != null && _fFireInterval.GetValue(fcP) != null)
					{
						now[0] = Convert.ToDouble(_fFireInterval.GetValue(fcP));
					}
					if (_fFireIntervalBase != null && _fFireIntervalBase.GetValue(fcP) != null)
					{
						now[1] = Convert.ToDouble(_fFireIntervalBase.GetValue(fcP));
					}
					if (_fFireTimeScale != null && _fFireTimeScale.GetValue(fcP) != null)
					{
						now[2] = Convert.ToDouble(_fFireTimeScale.GetValue(fcP));
					}
				}
				catch { }
			}
			AttackComponent acP = FindComponentOfType<AttackComponent>(cmP);
			if (acP != null && _fAtkInterval != null && _fAtkInterval.GetValue(acP) != null)
			{
				now[3] = Convert.ToDouble(_fAtkInterval.GetValue(acP));
			}
			AdobeAnimateSprite spP = FindSpriteOf(character);
			if (spP != null && _fSpriteTimeScale != null && _fSpriteTimeScale.GetValue(spP) != null)
			{
				now[4] = Convert.ToDouble(_fSpriteTimeScale.GetValue(spP));
			}
			string key = charName ?? "?";
			// v1.11.3：把当前动画时间缩放存起来供显示（猫窝加成的真实落点）
			_catAnimTs[key] = now[4];
			double[] old;
			if (_catProbe.TryGetValue(key, out old))
			{
				bool changed = false;
				for (int i = 0; i < 5; i++)
				{
					if (System.Math.Abs(now[i] - old[i]) > 0.001)
					{
						changed = true;
					}
				}
				if (changed && _catProbeReported.Add(key))
				{
					Info("猫窝探针[" + key + "] 变化：间隔 " + old[0].ToString("0.###") + "→"
						+ now[0].ToString("0.###") + "｜基准 " + old[1].ToString("0.###") + "→"
						+ now[1].ToString("0.###") + "｜发射ts " + old[2].ToString("0.###") + "→"
						+ now[2].ToString("0.###") + "｜攻击间隔 " + old[3].ToString("0.###") + "→"
						+ now[3].ToString("0.###") + "｜动画ts " + old[4].ToString("0.###") + "→"
						+ now[4].ToString("0.###"));
				}
			}
			else
			{
				_catProbe[key] = now;
			}

			// ★ v1.11.2：**实测发射周期**——字段全不变 ⇒ 加速很可能是"发射那一刻的瞬时计算"，
			//   唯一可靠办法是**测量真实节奏**：监控 FireComponent.timer 的"归零"事件
			//   （timer 从 0 涨到间隔后发射并归零），归零间隔 = 实际发射周期。
			//   猫窝加速生效时，该周期应从 1.5 掉到 0.75 ⇒ 据此显示"攻速 +N%"。
			try
			{
				if (fcP != null && _fFireTimer != null && _fFireTimer.GetValue(fcP) != null)
				{
					double tNow = Convert.ToDouble(_fFireTimer.GetValue(fcP));
					double tLast;
					if (_catTimerLast.TryGetValue(key, out tLast))
					{
						if (tNow < tLast - 0.01)   // 归零 = 刚发射完一次
						{
							ulong nowMs = Godot.Time.GetTicksMsec();
							ulong lastZero;
							if (_catLastZeroMs.TryGetValue(key, out lastZero) && nowMs > lastZero)
							{
								double period = (nowMs - lastZero) / 1000.0;
								_catPeriod[key] = period;
								double prevPeriod;
								if (_catPeriodLast.TryGetValue(key, out prevPeriod))
								{
								if (System.Math.Abs(period - prevPeriod) > 0.03
									&& _catPeriodReported.Add(key + "|" + period.ToString("0.0")))
								{
									// v1.11.3：周期日志关闭（探针已定案"动画提速"为真实落点），数据仅内部保留
								}
							}
							else
							{
								// 首次：仅记录，不打日志
								_catPeriodReported.Add(key);
							}
								_catPeriodLast[key] = period;
							}
							_catLastZeroMs[key] = nowMs;
						}
					}
					_catTimerLast[key] = tNow;
				}
			}
			catch { }
		}
		catch { }
	}

	/// <summary>
	/// 在节点子树里找第一个 AdobeAnimateSprite（动画精灵）。
	/// ★ v1.11.5：**限制查找深度（3 层）**——否则"猫窝包裹植物"时会把**里面植物**的精灵
	/// 当成猫窝自己的（导致猫窝也显示"动画 ×N"，实测两行重复）。
	/// </summary>
	private AdobeAnimateSprite FindSpriteOf(Node n)
	{
		return FindSpriteOfDepth(n, 0);
	}

	private AdobeAnimateSprite FindSpriteOfDepth(Node n, int depth)
	{
		try
		{
			if (n is AdobeAnimateSprite sp)
			{
				return sp;
			}
			if (depth >= 3)
			{
				return null;
			}
			Godot.Collections.Array<Node> kids = n.GetChildren();
			for (int i = 0; i < kids.Count; i++)
			{
				AdobeAnimateSprite r = FindSpriteOfDepth(kids[i], depth + 1);
				if (r != null)
				{
					return r;
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>造一个功能开关（样式与总开关一致，字号略小）。</summary>
	private CheckBox MakeFeatureCheckBox(string nodeName, string text, bool initial, int idx)
	{
		CheckBox fcb = new CheckBox();
		fcb.Name = nodeName;
		fcb.Text = text;
		fcb.Alignment = HorizontalAlignment.Center;
		fcb.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		fcb.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		fcb.AddThemeConstantOverride("outline_size", 5);
		fcb.AddThemeFontSizeOverride("font_size", 14);
		fcb.ButtonPressed = initial;
		fcb.Connect("toggled", Callable.From(new Action<bool>(on => OnFeatureToggleChanged(idx, on))));
		return fcb;
	}

	/// <summary>「只显示 ≥5 秒」开关切换（v1.19.0）。</summary>
	private void OnOnlyLongChanged(bool on)
	{
		try
		{
			_onlyLongTimers = on;
			SaveEnabled();
			Info("开关[只显示 ≥5 秒] → " + (on ? ("开（总时长低于 " + ShortTimerThresholdSeconds.ToString("0.#") + " 秒的计时器整行不显示）") : "关"));
		}
		catch (Exception ex)
		{
			Warn("「只显示 ≥5 秒」切换异常（已吞）：" + ex.Message);
		}
	}

	private void OnFeatureToggleChanged(int idx, bool on)	{
		try
		{
			SetFeatureValue(idx, on);
			SaveEnabled();
			Info("功能开关[" + FeatureText(idx) + "] → " + (on ? "开" : "关"));
		}
		catch (Exception ex)
		{
			Warn("功能开关切换异常（已吞）：" + ex.Message);
		}
	}

	/// <summary>
	/// 分类开关（v1.9.0）：0=装填与核能 1=计时器与点击 2=成长与充能 3=产出 4=战斗辅助 5=等级与加速。
	/// 各分支的输出统一用它把关（识别/状态逻辑不受影响，只控制"要不要显示这一行"）。
	/// </summary>
	/// <summary>
	/// 选卡栏卡片（TowerDefenseInGamePacketShow）的"种植 CD"数字（v1.10.0）。
	/// 字段：coldDown（冷却总时长）/ coldDownTimer（计时）/ _coldDownOpen（是否启用冷却）。
	/// 剩余 = coldDown − coldDownTimer（方向由诊断核对）；归"计时器与点击"开关（CatOn(1)）。
	/// </summary>
	private void ApplyToPacket(Node card)
	{
		try
		{
			if (_fCardCd == null || _fCardCdTimer == null)
			{
				return;
			}
			if (!_enabled || !CatOn(3))
			{
				HideCardLabel(card);
				return;
			}
			double cd = (_fCardCd.GetValue(card) != null)
				? Convert.ToDouble(_fCardCd.GetValue(card)) : 0.0;
			// ★ 方向定案（10:07 日志）：coldDownTimer **本身就是"剩余"**（从 coldDown 递减，
			//   实测 coldDown=30 / coldDownTimer=29.533 且冷却启用中）——直接用它，不要再相减。
			double ct = (_fCardCdTimer.GetValue(card) != null)
				? Convert.ToDouble(_fCardCdTimer.GetValue(card)) : 0.0;
			bool open = (_fCardOpen == null) || ((_fCardOpen.GetValue(card) is bool cb2) && cb2);
			double remain = ct;
			if (remain < 0) { remain = 0; }
			if (cd > 0 && remain > 0)
			{
				string key = card.GetInstanceId().ToString();
				if (_cardProbeReported.Add(key))
				{
					Info("卡片CD：coldDown=" + cd.ToString("0.###")
						+ " coldDownTimer(剩余)=" + ct.ToString("0.###")
						+ " 启用冷却=" + open);
				}
			}
			Label lbl = EnsureCardLabel(card);
			if (lbl == null)
			{
				return;
			}
			// 显示条件不再依赖 _coldDownOpen（实测该标记为 false 时卡片其实在冷却中）
			if (cd > 0 && remain > 0 && remain <= cd)
			{
				lbl.Visible = true;
				lbl.Text = remain.ToString("0.0");
				Control c = card as Control;
				if (c != null)
				{
					lbl.Position = c.Size / 2f - new Vector2(30f, 12f);
				}
			}
			else if (lbl.Visible)
			{
				lbl.Visible = false;
			}
		}
		catch { }
	}

	/// <summary>卡片上的 CD 数字标签（懒创建、幂等）。</summary>
	private Label EnsureCardLabel(Node card)
	{
		try
		{
			Label lbl = card.GetNodeOrNull<Label>("ModCardCdLabel");
			if (lbl != null && GodotObject.IsInstanceValid(lbl))
			{
				return lbl;
			}
			lbl = new Label();
			lbl.Name = "ModCardCdLabel";
			lbl.ZIndex = 200;
			lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.Size = new Vector2(60f, 24f);
			Font font = null;
			try { font = ResourceLoader.Load<Font>("res://Asset/Font/fzkt.ttf"); } catch { }
			if (font != null)
			{
				lbl.AddThemeFontOverride("font", font);
			}
			lbl.AddThemeFontSizeOverride("font_size", 16);
			lbl.AddThemeConstantOverride("outline_size", 5);
			lbl.AddThemeColorOverride("font_color", BusyColor);
			lbl.Visible = false;
			card.AddChild(lbl);
			return lbl;
		}
		catch { return null; }
	}

	/// <summary>收起卡片上的 CD 标签。</summary>
	private void HideCardLabel(Node card)
	{
		try
		{
			Label lbl = card.GetNodeOrNull<Label>("ModCardCdLabel");
			if (lbl != null && GodotObject.IsInstanceValid(lbl) && lbl.Visible)
			{
				lbl.Visible = false;
			}
		}
		catch { }
	}

	/// <summary>
	/// 追加一行**非计时**信息（计数 / 状态 / 百分比…）。
	/// `Total = 0` ⇒ **永不参与**「只显示 ≥5 秒」过滤。
	/// </summary>
	private static void AddLine(
		System.Collections.Generic.List<(string Text, Color Color, double Total)> ls,
		string text, Color color)
	{
		ls.Add((text, color, 0.0));
	}

	/// <summary>
	/// 追加一行**计时**信息。`total` = 该计时器的**总时长**（秒）。
	///
	/// 「只显示 ≥5 秒」开关打开时，`total &lt; 4.9` 的行**整行不显示**
	/// （不是"剩到 4.9 才隐藏"，而是从头到尾都不出现）；
	/// 总时长 ≥ 4.9 的行则从满值一路显示到 0。
	/// 取不到总时长时传 0 ⇒ 该行不参与过滤（宁可显示，也不误藏）。
	/// </summary>
	private static void AddTimer(
		System.Collections.Generic.List<(string Text, Color Color, double Total)> ls,
		string text, Color color, double total)
	{
		ls.Add((text, color, total));
	}

	/// <summary>
	/// 加农炮装填的**总时长** = `CannonComponent.restTime`（public 字段，装填冷却配置值）。
	/// 取不到返回 0 ⇒ 该行不参与「总时长」过滤。
	/// </summary>
	private static double CannonRestTotal(object cannon)
	{
		try
		{
			if (cannon == null)
			{
				return 0.0;
			}
			FieldInfo f = typeof(CannonComponent).GetField("restTime",
				BindingFlags.Public | BindingFlags.Instance);
			if (f != null)
			{
				object v = f.GetValue(cannon);
				if (v != null)
				{
					return Convert.ToDouble(v);
				}
			}
		}
		catch { }
		return 0.0;
	}

	/// <summary>
	/// 土豆雷「准备」的**总时长** = `PotatoComponent.readyTime`（public float，默认 15s）。
	/// 取不到返回 0 ⇒ 不参与过滤。
	/// </summary>
	private static double PotatoReadyTotal(object potato)
	{
		try
		{
			if (potato == null)
			{
				return 0.0;
			}
			FieldInfo f = typeof(PotatoComponent).GetField("readyTime",
				BindingFlags.Public | BindingFlags.Instance);
			if (f != null)
			{
				object v = f.GetValue(potato);
				if (v != null)
				{
					return Convert.ToDouble(v);
				}
			}
		}
		catch { }
		return 0.0;
	}

	private bool CatOn(int cat)
	{
		switch (cat)
		{
			case 0: return _showLoad;        // 装填与核能
			case 1: return _showObstacle;    // 障碍物消失
			case 2: return _showObstacleHp;  // 障碍物血量
			case 3: return _showCardCd;      // 选卡栏种植 CD
			case 4: return _showSpawn;       // 生成计时
			case 5: return _showOtherTimer;  // 角色计时器其他CD
			case 6: return _showGrowth;      // 成长与充能
			case 7: return _showProduce;     // 产出倒计时
			case 8: return _showCombat;      // 战斗辅助
			default: return _showLevel;      // 等级与加速
		}
	}

	private bool GetFeatureValue(int idx)
	{
		switch (idx)
		{
			case 0: return _showLoad;
			case 1: return _showObstacle;
			case 2: return _showObstacleHp;
			case 3: return _showCardCd;
			case 4: return _showSpawn;
			case 5: return _showOtherTimer;
			case 6: return _showGrowth;
			case 7: return _showProduce;
			case 8: return _showCombat;
			default: return _showLevel;
		}
	}

	private void SetFeatureValue(int idx, bool v)
	{
		switch (idx)
		{
			case 0: _showLoad = v; break;
			case 1: _showObstacle = v; break;
			case 2: _showObstacleHp = v; break;
			case 3: _showCardCd = v; break;
			case 4: _showSpawn = v; break;
			case 5: _showOtherTimer = v; break;
			case 6: _showGrowth = v; break;
			case 7: _showProduce = v; break;
			case 8: _showCombat = v; break;
			default: _showLevel = v; break;
		}
	}

	private static string FeatureText(int idx)
	{
		switch (idx)
		{
			case 0: return "装填与核能";
			case 1: return "障碍物消失";
			case 2: return "障碍物血量";
			case 3: return "选卡栏种植CD";
			case 4: return "生成计时";
			case 5: return "角色计时器其他CD";
			case 6: return "成长与充能";
			case 7: return "产出倒计时";
			case 8: return "战斗辅助";
			default: return "等级与加速";
		}
	}

	private void LoadEnabled()
	{
		try
		{
			ConfigFile cf = new ConfigFile();
			if (cf.Load(ConfigPath) == Error.Ok)
			{
				object v = cf.GetValue("mod", "cooldown_line_enabled", true);
				_enabled = (v is bool b) ? b : true;
				// ★ v1.19.0「只显示 ≥5 秒」（缺键默认 false = 不影响老用户观感）
				object lv = cf.GetValue("mod", OnlyLongKey, false);
				_onlyLongTimers = (lv is bool lb) ? lb : false;
				// 6 类功能开关（v1.9.0；缺键时默认开，兼容老配置）
				for (int i = 0; i < FeatureKeys.Length; i++)
				{
					object fv = cf.GetValue("mod", FeatureKeys[i], true);
					SetFeatureValue(i, (fv is bool fb) ? fb : true);
				}
			}
		}
		catch (Exception ex)
		{
			if (!_configFaultReported)
			{
				_configFaultReported = true;
				Warn("读取开关配置失败（用默认值 开）：" + ex.Message);
			}
		}
	}

	private void SaveEnabled()
	{
		try
		{
			ConfigFile cf = new ConfigFile();
			cf.SetValue("mod", "cooldown_line_enabled", _enabled);
			// ★ v1.19.0「只显示 ≥5 秒」
			cf.SetValue("mod", OnlyLongKey, _onlyLongTimers);
			// 6 类功能开关（v1.9.0）
			for (int i = 0; i < FeatureKeys.Length; i++)
			{
				cf.SetValue("mod", FeatureKeys[i], GetFeatureValue(i));
			}
			cf.Save(ConfigPath);
		}
		catch (Exception ex)
		{
			if (!_configFaultReported)
			{
				_configFaultReported = true;
				Warn("保存开关配置失败（本次游戏内仍生效，重启后回到上次保存值）：" + ex.Message);
			}
		}
	}

	/// <summary>角色名的安全读取（诊断日志用）。</summary>
	private string SafeCharName(TowerDefenseCharacter character)
	{
		try
		{
			TowerDefenseCharacterConfig c = character.config;
			return (c != null && !string.IsNullOrEmpty(c.name)) ? c.name : character.Name.ToString();
		}
		catch { return character != null ? character.Name.ToString() : "<null>"; }
	}

	/// <summary>
	/// 蓄能咖啡豆的角色级特征字段（沿类型链找，按类型缓存）：
	/// level（当前等级）/ _produceInterval（充能间隔）/ _produceComponent / MaxLevel。
	/// 三件套齐全才认定是"蓄能类"角色，避免误伤其他带 level 字段的类。
	/// </summary>
	private void GetChargeFields(Type t,
		out FieldInfo fLevel, out FieldInfo fInterval, out FieldInfo fProdComp, out FieldInfo fMaxLevel)
	{
		FieldInfo[] arr;
		if (_chargeFieldCache.TryGetValue(t, out arr))
		{
			fLevel = arr[0]; fInterval = arr[1]; fProdComp = arr[2]; fMaxLevel = arr[3];
			return;
		}
		fLevel = FindFieldAlong(t, "level");
		fInterval = FindFieldAlong(t, "_produceInterval");
		fProdComp = FindFieldAlong(t, "_produceComponent");
		fMaxLevel = FindFieldAlong(t, "MaxLevel");
		_chargeFieldCache[t] = new FieldInfo[] { fLevel, fInterval, fProdComp, fMaxLevel };
	}

	/// <summary>
	/// 核弹磁力菇（MagnetShroomDM）的角色级字段（沿类型链找，按类型缓存）：
	/// ironCount（已收集铁器，属性 backing field）/ ironPerFullCharge（充满所需）/ _isArmed（可发射）。
	/// </summary>
	private void GetArmFields(Type t,
		out FieldInfo fIron, out FieldInfo fIronMax, out FieldInfo fArmed)
	{
		FieldInfo[] arr;
		if (_armFieldCache.TryGetValue(t, out arr))
		{
			fIron = arr[0]; fIronMax = arr[1]; fArmed = arr[2];
			return;
		}
		fIron = FindFieldAlong(t, "<ironCount>k__BackingField");
		if (fIron == null)
		{
			fIron = FindFieldAlong(t, "ironCount");
		}
		fIronMax = FindFieldAlong(t, "ironPerFullCharge");
		fArmed = FindFieldAlong(t, "_isArmed");
		_armFieldCache[t] = new FieldInfo[] { fIron, fIronMax, fArmed };
	}

	/// <summary>
	/// 读 Godot 的 float 数组（兼容 Godot.Collections.Array&lt;float&gt; / IList&lt;float&gt; / 非泛型枚举）。
	/// ★ 定案（17:32 日志 + GodotSharp 元数据实锤，2026-09-26）：
	///   **`Godot.Collections.Array&lt;T&gt;` 的基类是 Object ——与 `Godot.Collections.Array`
	///   没有继承关系**！所以 `as Godot.Collections.Array` 恒 null
	///   （v1.7.1~v1.7.4 一路失败的原因）。正确通道：**泛型 as（类型精确匹配）→
	///   IList&lt;float&gt; → 非泛型 IEnumerable（逐元素转换）**。
	/// </summary>
	private static bool TryReadFloatArray(object obj, out System.Collections.Generic.List<double> values)
	{
		values = null;
		try
		{
			if (obj == null)
			{
				return false;
			}
			Godot.Collections.Array<float> gF = obj as Godot.Collections.Array<float>;
			if (gF != null && gF.Count > 0)
			{
				values = new System.Collections.Generic.List<double>(gF.Count);
				for (int i = 0; i < gF.Count; i++)
				{
					values.Add((float)gF[i]);
				}
				return true;
			}
			System.Collections.Generic.IList<float> lF
				= obj as System.Collections.Generic.IList<float>;
			if (lF != null && lF.Count > 0)
			{
				values = new System.Collections.Generic.List<double>(lF.Count);
				for (int i = 0; i < lF.Count; i++)
				{
					values.Add(lF[i]);
				}
				return true;
			}
			System.Collections.IEnumerable seq = obj as System.Collections.IEnumerable;
			if (seq != null)
			{
				values = new System.Collections.Generic.List<double>();
				foreach (object o in seq)
				{
					if (o is float f)
					{
						values.Add(f);
					}
					else if (o is double d)
					{
						values.Add(d);
					}
					else if (o is Godot.Variant v)
					{
						values.Add((float)v);
					}
					else if (o != null)
					{
						values.Add(Convert.ToDouble(o));
					}
				}
				if (values.Count > 0)
				{
					return true;
				}
			}
		}
		catch { }
		values = null;
		return false;
	}

	/// <summary>
	/// 蓄能咖啡豆（EnergyBean）的角色级特征字段（沿类型链找，按类型缓存）：
	/// _chargeLevel / ChargeInterval / _produce / _chargeTimer。
	/// 三件套（等级+间隔+产出组件）齐全才认定，避免误伤其他类。
	/// </summary>
	private void GetEnergyBeanChargeFields(Type t,
		out FieldInfo fLevel, out FieldInfo fInterval, out FieldInfo fProduce, out FieldInfo fTimer)
	{
		FieldInfo[] arr;
		if (_chargeFieldCache2.TryGetValue(t, out arr))
		{
			fLevel = arr[0]; fInterval = arr[1]; fProduce = arr[2]; fTimer = arr[3];
			return;
		}
		fLevel = FindFieldAlong(t, "_chargeLevel");
		fInterval = FindFieldAlong(t, "ChargeInterval");
		fProduce = FindFieldAlong(t, "_produce");
		fTimer = FindFieldAlong(t, "_chargeTimer");
		_chargeFieldCache2[t] = new FieldInfo[] { fLevel, fInterval, fProduce, fTimer };
	}

	/// <summary>
	/// 沿类型链（含自身）按 DeclaredOnly 逐级找字段。
	/// ★★ 2026-09-26 第六类坑定案：必须带 <c>BindingFlags.Static</c>！
	///   EnergyBean 的 `ChargeInterval`/`MaxChargeLevel` 是**静态字段**（类级常量）——
	///   只带 Instance 时 GetField 返回 null（**不报错**），导致蓄能识别静默失败。
	///   （离线反射实测：_chargeLevel/_chargeTimer 找到、ChargeInterval/MaxChargeLevel 找不到，
	///   差异就在 static。static 字段的 GetValue 会忽略实例参数，直接传实例即可。）
	/// </summary>
	private static FieldInfo FindFieldAlong(Type t, string name)
	{
		const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
			| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		Type cur = t;
		while (cur != null)
		{
			FieldInfo fi = cur.GetField(name, F);
			if (fi != null)
			{
				return fi;
			}
			cur = cur.BaseType;
		}
		return null;

	}

	/// <summary>
	/// ★ v1.17.0：**等级型植物**（叠种升级）的字段（沿类型链找，按类型缓存）：
	/// `level`（当前等级，`public int level = 1`）/ `MaxLevel`（等级上限，`private const int`）。
	///
	/// 识别判据 = **该类型有 `public void LevelSet(int)` 方法**：
	///   · `TowerDefensePlantStressShroom`（增压大喷菇）→ `LevelSet(int)` ✅ 上限 4
	///   · `TowerDefensePlantPumpkinPea`（豆荚壳）    → `LevelSet(int)` ✅ 上限 3
	///   · `TowerDefensePlantSunShroomCharge`（充能阳光菇）→ **没有** `LevelSet` ❌
	///     它的 level 来自"按时间充能"，由 ②.10 分支显示 `充能N`，不能在这里重复。
	///
	/// ⚠️ 为什么不靠"有没有 level 字段"判定：蓄能咖啡豆/充能阳光菇都有 level，
	///   那样会误伤并把它们的显示吃掉（用户的"充能阳光显示等级"就是 ②.10 的成果）。
	/// ⚠️ `MaxLevel` 是 **const**（编译期字面量）⇒ `GetValue` 会抛
	///   `InvalidOperationException`，必须先用 `GetRawConstantValue()` 取；
	///   两者都包在 try 里，取不到就按 0 处理（显示成不带 `/上限` 的 `等级 N`）。
	/// </summary>
	private void GetLevelFields(Type t,
		out FieldInfo fLevel, out FieldInfo fMaxLevel)
	{
		FieldInfo[] arr;
		if (_lvFieldCache.TryGetValue(t, out arr))
		{
			fLevel = arr[0]; fMaxLevel = arr[1];
			return;
		}
		fLevel = null;
		fMaxLevel = null;
		try
		{
			// 只在"确属叠种升级型"时采信 level 字段
			bool hasLevelSet = false;
			const BindingFlags MF = BindingFlags.Instance | BindingFlags.Public
				| BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			for (Type cur = t; cur != null && !hasLevelSet; cur = cur.BaseType)
			{
				MethodInfo mi = cur.GetMethod("LevelSet", MF);
				if (mi != null && mi.GetParameters().Length == 1)
				{
					hasLevelSet = true;
				}
			}
			if (hasLevelSet)
			{
				fLevel = FindFieldAlong(t, "level");
				fMaxLevel = FindFieldAlong(t, "MaxLevel");
			}
		}
		catch
		{
			fLevel = null;
			fMaxLevel = null;
		}
		_lvFieldCache[t] = new FieldInfo[] { fLevel, fMaxLevel };
	}

	/// <summary>
	/// 读等级字段的整数值。`MaxLevel` 是 const（字面量）⇒ `FieldInfo.GetValue` 对
	/// 字面量字段会抛 `InvalidOperationException`，必须先走 `GetRawConstantValue()`。
	/// 取不到返回 fallback。
	/// </summary>
	private static int ReadConstOrFieldInt(FieldInfo fi, object instance, int fallback)
	{
		if (fi == null)
		{
			return fallback;
		}
		try
		{
			if (fi.IsLiteral)
			{
				object raw = fi.GetRawConstantValue();
				if (raw != null)
				{
					return Convert.ToInt32(raw);
				}
			}
		}
		catch { }
		try
		{
			object v = fi.GetValue((fi.IsStatic || fi.IsLiteral) ? null : instance);
			if (v != null)
			{
				return Convert.ToInt32(v);
			}
		}
		catch { }
		return fallback;
	}

	/// <summary>
	/// "加成类" buff 键白名单（v1.10.8）——这些 buff 的效果不一定落在我们监控的
	/// 发射/攻击字段上（如 TabooBean 的 timeScale 修改在更底层），此时直接把
	/// "身上挂着该加成"显示出来，保证用户能看到加成存在。
	/// （Coffee/MagicRootHaste 不列入——它们走"加速 +N%"通道。）
	/// </summary>
	private static readonly string[] BoostBuffKeys =
	{
		"TabooBean", "RedHeat", "Fluorescence", "Radiance",
		"ButterGene", "CornpultGene", "IceShroomGene",
	};

	private static bool IsBoostBuff(string key)
	{
		for (int i = 0; i < BoostBuffKeys.Length; i++)
		{
			if (BoostBuffKeys[i] == key)
			{
				return true;
			}
		}
		return false;
	}

	private static string BoostBuffName(string key)
	{
		switch (key)
		{
			case "TabooBean": return "毁灭";
			case "RedHeat": return "红热";
			case "Fluorescence": return "荧光";
			case "Radiance": return "光辉";
			default: return key;
		}
	}

	/// <summary>取角色类上的 damageScale 字段（按类型缓存；狂热星等"伤害加成"型植物有它）。</summary>
	private FieldInfo GetDmgField(Type t)
	{
		FieldInfo fi;
		if (_dmgFieldCache.TryGetValue(t, out fi))
		{
			return fi;
		}
		fi = FindFieldAlong(t, "damageScale");
		_dmgFieldCache[t] = fi;
		return fi;
	}

	/// <summary>取 buff 配置类上的 timeScaleValue 字段（按类型缓存；加速类 buff 都有它）。</summary>
	private FieldInfo GetBuffTimeScaleField(Type t)
	{
		FieldInfo fi;
		if (_buffTsCache.TryGetValue(t, out fi))
		{
			return fi;
		}
		fi = FindFieldAlong(t, "timeScaleValue");
		_buffTsCache[t] = fi;
		return fi;
	}

	/// <summary>
	/// 运行时组件注册表 `_runtimeByInstanceId`（Dictionary&lt;string, CharacterComponentRuntime&gt;）。
	/// ★ 定案（10:11 日志）：**运行时组件（FireComponent/AttackComponent 等，派生自
	/// CharacterComponentRuntime）不在 `componentList`（List&lt;ComponentBase&gt;）里**——
	/// 那是另一个体系。找运行时组件必须走本注册表（按值类型匹配，避免 InstanceId 后缀问题）。
	/// </summary>
	private static System.Collections.IDictionary GetRuntimeDict(ComponentManager cm)
	{
		try
		{
			FieldInfo fi = typeof(ComponentManager).GetField("_runtimeByInstanceId",
				BindingFlags.Instance | BindingFlags.NonPublic);
			return fi != null ? fi.GetValue(cm) as System.Collections.IDictionary : null;
		}
		catch { return null; }
	}

	/// <summary>
	/// 从运行时注册表里按类型找一个组件实例（不依赖 InstanceId——有些带后缀如 character.attack.0）。
	/// </summary>
	/// <summary>
	/// 「这株植物走的是血量产出（而非计时产出）」的统一判据。
	///
	/// **②.9 计时产出 与 ②.10 血量产出 必须严格互斥** —— 都用本方法判定，
	/// 一个取反、一个直用，就不可能同时显示（用户反馈"伪装向日葵又显示阳光生产时间
	/// 又显示次数"就是两者判据不一致造成的）。
	///
	/// 两个来源（任一为真即算）：
	///   ① **关卡**是我是僵尸系（IZM / IZM2）⇒ `ProduceComponent` 初始化时
	///      `isBaseIZM` 为真 ⇒ `_IZMMode = true` ⇒ 走 `ProcessHealthProduction`；
	///   ② **该组件自己的 `_IZMMode`**（定义里写死）⇒ 任何模式下都走血量产出。
	///      全库只有 `DisguiserSunFlowerProduceComponentDefinition.tres` 是这种，
	///      所以**伪装向日葵在普通关卡里也走血量产出**、没有计时器。
	/// </summary>
	private bool IsHealthProduceComponent(ProduceComponent pc)
	{
		if (pc == null)
		{
			return false;
		}
		try
		{
			return IsIzmLikeMode() || ReadProduceIzmFlag(pc);
		}
		catch { return false; }
	}

	/// <summary>
	/// 由血量产出的「已触发次数」。
	///
	/// ⚠️⚠️ 公式必须**减 1**（v1.19.2 修，用户反馈"显示的不是最大值，而是最大−1的值"）。
	///
	/// 推导（`ProduceComponent` / 伪装家族同款）：
	///     初始： hpNextInterval = 满血 / segments;
	///            hpNext         = 满血 − hpNextInterval;      ← 注意是"满血 − 一段"
	///     触发： while (hitpoints &lt;= hpNext) { hpNext -= 段长; Produce(); }
	///   ⇒ 满血时 `(满血 − hpNext) / 段长 = 1`，但**一次都还没触发** ⇒ 应减 1。
	///   ⇒ 第 k 次触发后 `hpNext = 满血 − (k+1)·段长` ⇒ `k = (满血 − hpNext)/段长 − 1`。
	///
	/// 例（segments=6）：满血 raw=1 → consumed=0 → 剩余 6/6；
	///   掉一段 raw=2 → consumed=1 → 剩余 5/6；…最后一次触发在血量归零时。
	/// </summary>
	private static int ConsumedSegments(double hpMax, float hpNext, float step, int segments)
	{
		if (step <= 0.0001f || segments <= 0)
		{
			return 0;
		}
		int raw = (int)Math.Floor((hpMax - hpNext) / step + 0.0001);
		int consumed = raw - 1;
		if (consumed < 0) { consumed = 0; }
		if (consumed > segments) { consumed = segments; }
		return consumed;
	}

	/// <summary>
	/// 读 `ProduceComponent._IZMMode`（走不走进度产出）。
	///
	/// 依据（`ProduceComponent.cs` 初始化）：
	///     isBaseIZM = instance.IsIZMMode() || instance.IsIZM2Mode();
	///     if (isBaseIZM) _IZMMode = true;
	///     _IZMMode = definition?._IZMMode ?? false;
	/// 全库只有 `DisguiserSunFlowerProduceComponentDefinition.tres` 把 `_IZMMode` 写成 `true`
	/// ⇒ **伪装向日葵在任何模式下都走血量产出**，判据必须带上它，
	///   否则它在第 8 章 / 杂交乐园 / 挑战等非 IZM 关卡里会漏显示。
	///
	/// 实例字段与定义字段任意一个为真即算真（实例优先，取不到再读定义）。
	/// </summary>
	private bool ReadProduceIzmFlag(ProduceComponent pc)
	{
		try
		{
			// ① 实例上的 _IZMMode（属性 <_IZMMode>k__BackingField，或同名字段）
			Type t = pc.GetType();
			PropertyInfo p = t.GetProperty("_IZMMode",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (p != null && p.GetValue(pc) is bool pb)
			{
				if (pb)
				{
					return true;
				}
			}
			FieldInfo f = t.GetField("_IZMMode",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (f != null && f.GetValue(pc) is bool fb && fb)
			{
				return true;
			}
			// ② 定义上的 _IZMMode
			PropertyInfo pd = t.GetProperty("ComponentDefinition",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			if (pd != null)
			{
				ProduceComponentDefinition def = pd.GetValue(pc) as ProduceComponentDefinition;
				if (def != null && def._IZMMode)
				{
					return true;
				}
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 当前是否处在「我是僵尸」系模式（IZM / IZM2）。
	///
	/// 依据：`TowerDefenseManager.IsIZMMode()` / `IsIZM2Mode()`（均为 public 实例方法），
	/// 实现上判的是 `currentLevelConfig.finishMethod ∈ {IZM, QUIZ, IZM2}`。
	/// 这正是 `ProduceComponent` 判断"走血量产出还是计时产出"用的同一套判据
	/// （`ProduceComponent.cs` L271：`instance.IsIZMMode() || instance.IsIZM2Mode()`），
	/// 所以**显示条件与游戏内部实际走哪条分支完全一致**，不会出现"显示了但其实是计时产出"。
	///
	/// ⚠️ 这里刻意**直接调用** `TowerDefenseManager` 的方法而不是反射：
	///   本工程 csproj 引用了游戏程序集（同文件的 `CannonComponent` 等也直接用），
	///   直接调用更清晰、也不会因反射签名写错而静默失效。
	/// </summary>
	private static bool IsIzmLikeMode()
	{
		try
		{
			TowerDefenseManager m = TowerDefenseManager.Instance;
			if (m == null)
			{
				return false;
			}
			return m.IsIZMMode() || m.IsIZM2Mode();
		}
		catch { return false; }
	}

	/// <summary>
	/// 把反射取到的"数值型装箱对象"转成 float。
	/// ⚠️ **不要用 `Convert.ToSingle(object)`** —— 游戏附带的 .NET 运行时是**裁剪过**的，
	///   项目已实测 `Convert.ToString(object)` 会抛 `Method not found`（血泪教训）。
	///   这里按类型显式分支，只走确定存在的 IL 指令。
	/// </summary>
	private static float ToFloat(object o)
	{
		if (o is float f) { return f; }
		if (o is double d) { return (float)d; }
		if (o is int i) { return i; }
		if (o is long l) { return l; }
		if (o is short s) { return s; }
		if (o is byte b) { return b; }
		return 0f;
	}

	private static T FindComponentOfType<T>(ComponentManager cm) where T : class	{
		try
		{
			System.Collections.IDictionary dic = GetRuntimeDict(cm);
			if (dic == null)
			{
				return null;
			}
			foreach (object v in dic.Values)
			{
				if (v is T hit)
				{
					return hit;
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// 运行时注册表里是否存在指定类型名的组件（诊断用）。
	/// ★ 与 FindComponentOfType 同一根因修正：必须查 `_runtimeByInstanceId`
	/// （`componentList` 是 ComponentBase 体系，装的是另一批东西）。
	/// </summary>
	private bool ComponentListHasType(ComponentManager cm, string typeName)
	{
		try
		{
			System.Collections.IDictionary dic = GetRuntimeDict(cm);
			if (dic == null)
			{
				return false;
			}
			foreach (object v in dic.Values)
			{
				if (v != null && v.GetType().Name == typeName)
				{
					return true;
				}
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 诊断：把该角色「InstanceId → 组件」注册表的全部键列出来（取不到组件时定位用）。
	/// 注册表 = ComponentManager._runtimeByInstanceId（Dictionary&lt;string, CharacterComponentRuntime&gt;）。
	/// </summary>
	private string InstanceIdKeysDump(ComponentManager cm)
	{
		try
		{
			FieldInfo fi = typeof(ComponentManager).GetField("_runtimeByInstanceId",
				BindingFlags.Instance | BindingFlags.NonPublic);
			if (fi == null)
			{
				return "?（找不到注册表字段）";
			}
			System.Collections.IDictionary dic = fi.GetValue(cm) as System.Collections.IDictionary;
			if (dic == null)
			{
				return "?（注册表为空）";
			}
			var sb = new System.Text.StringBuilder();
			foreach (object k in dic.Keys)
			{
				if (sb.Length > 0) { sb.Append(','); }
				sb.Append(k);
			}
			return sb.Length > 0 ? sb.ToString() : "（空）";
		}
		catch { return "?"; }
	}

	/// <summary>计时器键名 → 中文前缀（键名来自各 *TimerComponentDefinition.tres 的命名）。</summary>
	private static string TimerLabel(string key)
	{
		switch (key)
		{
			case "Countdown": return "倒计时";
			case "Ready": return "准备";
			case "Spawn": return "生成";
			case "Destroy": return "销毁";
			case "AutoDestroy": return "消失";
			case "Armed": return "布雷";
			case "Open": return "张开";
			case "Close": return "闭合";
			case "Fire": return "开火";
			case "Power": return "蓄力";
			case "Put": return "放置";
			case "Regen": return "回复";
			case "Health": return "血量";
			case "Heal": return "治疗";
			default: return key;
		}
	}

	/// <summary>
	/// 把一个角色的**全部组件**列进日志（诊断用）。
	/// 目的：确认加农炮角色身上到底挂了哪些组件、它们的 InstanceId 是什么，
	/// 从而判断 GetRuntime<CannonComponent>("character.cannon") 为什么取不到。
	/// ComponentManager.componentList 是组件数组；拿不到就退化成只报 componentDictionary 的键。
	/// </summary>
	private void DumpComponents(TowerDefenseCharacter character, string nm)
	{
		try
		{
			ComponentManager cm = character.componentManager;
			if (cm == null)
			{
				Info("组件清单[" + nm + "]：componentManager 为 null");
				return;
			}
			var sb = new System.Text.StringBuilder();
			sb.Append("组件清单[").Append(nm).Append("]：");
			// 1) componentList（数组，元素是组件实例）
			try
			{
				System.Collections.IEnumerable list =
					cm.componentList as System.Collections.IEnumerable;
				int n = 0;
				if (list != null)
				{
					foreach (object o in list)
					{
						if (o == null) { continue; }
						sb.Append(" <").Append(o.GetType().Name).Append(">");
						n++;
					}
				}
				sb.Append("  [componentList 共 ").Append(n).Append("]");
			}
			catch (Exception e1) { sb.Append(" componentList 读取失败:").Append(e1.Message); }
			// 2) 直接试一把：能不能按类型拿到
			try
			{
				CannonComponent cc = cm.GetRuntime<CannonComponent>(CannonInstanceId);
				sb.Append("  GetRuntime(cannon)=").Append(cc == null ? "null" : cc.GetType().Name);
			}
			catch (Exception e2) { sb.Append(" GetRuntime(cannon) 抛:").Append(e2.Message); }
			Info(sb.ToString());
		}
		catch (Exception ex)
		{
			Info("组件清单[" + nm + "] 输出失败：" + ex.Message);
		}
	}

	/// <summary>
	/// 装填剩余时间。
	/// <c>_restTimerRemaining</c> 本身就是「还剩几秒」（不是"已过时间"），直接用；
	/// <c>_restTimerRunning</c> 为假表示没在装填 ⇒ 不显示这一行。
	/// </summary>
	private bool TryGetRemaining(CannonComponent cannon, out double remaining)
	{
		remaining = 0;
		if (!EnsureReflection())
		{
			return false;
		}
		object runningObj = _fRunning.GetValue(cannon);
		if (runningObj is bool running && !running)
		{
			return false;
		}
		object remObj = _fWait.GetValue(cannon);
		if (remObj == null)
		{
			return false;
		}
		double rem = Convert.ToDouble(remObj);
		if (rem <= 0)
		{
			return false;   // 装填完毕
		}
		remaining = rem;
		return true;
	}

	/// <summary>
	/// 反射缓存。拿不到就永久放弃（只报一次），而不是每帧重试。
	/// 注意类型名里带命名空间差异的可能：先按全名找，找不到再退回遍历。
	/// </summary>
	private bool EnsureReflection()
	{
		if (_reflectionReady)
		{
			return true;
		}
		if (_reflectionFailed)
		{
			return false;
		}
		try
		{
			Type cannonType = typeof(CannonComponent);
			// 这些在 CannonComponent 里是 private 字段 ⇒ 必须带 NonPublic；
			// 一并带 Static（第六类坑教训：静态配置字段用 Instance-only 查找会恒 null 且不报错）
			const BindingFlags F = BindingFlags.Instance | BindingFlags.Static
				| BindingFlags.Public | BindingFlags.NonPublic;
			_fRunning = cannonType.GetField("_restTimerRunning", F);
			_fWait = cannonType.GetField("_restTimerRemaining", F);
			_fCurrent = cannonType.GetField("restTime", F);
			// 土豆雷 / 篮球 / 投石车 / 命名计时器（private 字段要带 NonPublic）
			Type potatoType = typeof(PotatoComponent);
			_fPotatoRun = potatoType.GetField("_readyTimerRunning", F);
			_fPotatoRemain = potatoType.GetField("_readyTimerRemaining", F);
			Type bowlType = typeof(BowlingComponent);
			_fBowlHit = bowlType.GetField("hitNum", F);
			_fBowlMax = bowlType.GetField("maxHitNum", F);
			Type catType = typeof(CatapultComponent);
			_fCatCur = catType.GetField("currentProjectileNum", F);
			_fCatMax = catType.GetField("projectileNum", F);
			Type timerType = typeof(CharacterTimerComponent);
			_fTimerRun = timerType.GetField("timerRunning", F);
			_fTimerWait = timerType.GetField("timerWaitTime", F);
			_fTimerCur = timerType.GetField("timerCurrent", F);
			// 大嘴花咀嚼（ChomperComponent）：isChew=true 时显示"咀嚼 X.Xs"
			Type chomperType = typeof(ChomperComponent);
			_fChompChew = chomperType.GetField("isChew", F);
			_fChompTime = chomperType.GetField("chewTime", F);
			_fChompCur = chomperType.GetField("currentChewTime", F);
			_fChompTimer = chomperType.GetField("chewTimer", F);
			// 长大（GrowUpComponent）：growUpTime 数组 + reach 索引 + timer 已过
			Type growType = typeof(GrowUpComponent);
			_fGrowTimer = growType.GetField("timer", F);
			_fGrowReach = growType.GetField("growUpReach", F);
			_fGrowTimes = growType.GetField("growUpTime", F);
			// 周期区域事件（PeriodicAreaEventComponent）：staticTime 周期 + timer 已过
			Type perType = typeof(PeriodicAreaEventComponent);
			_fPerStaticTime = perType.GetField("staticTime", F);
			_fPerTimer = perType.GetField("timer", F);
			// 磁力消化（MagnetComponent）：breakDownTime 总时长 + breakDownTimer 已过 + 目标护甲
			Type magType = typeof(MagnetComponent);
			_fMagBreakTotal = magType.GetField("breakDownTime", F);
			_fMagBreakTimer = magType.GetField("breakDownTimer", F);
			_fMagArmor = magType.GetField("_breakDownArmor", F);
			// 阳光产出（ProduceComponent）：timer 已过 + 有效间隔 + 产出类型
			Type prodType = typeof(ProduceComponent);
			_fProdTimer = prodType.GetField("timer", F);
			_fProdEffInterval = prodType.GetField("_effectiveProduceInterval", F);
			_fProdInterval = prodType.GetField("_produceInterval", F);
			_fProdType = prodType.GetField("<produceType>k__BackingField", F);
			// IZM 血量产出：hpNext 是**已消耗到的血量阈值游标**（单向递减），
			//   hpNextInterval 是每段血量（= 初始 hitpoints / healthProductionSegments）。
			//   两者都是 public 字段（ProduceComponent.cs L68/L70）。
			_fProdHpNext = prodType.GetField("hpNext", F);
			_fProdHpNextInterval = prodType.GetField("hpNextInterval", F);
			// 缠绕海草（TanglekelpComponent）：已抓 / 上限
			Type tkType = typeof(TanglekelpComponent);
			_fTkCur = tkType.GetField("currentGrabNum", F);
			_fTkMax = tkType.GetField("grabNum", F);
			// 墓碑破坏者（GravebusterComponent）：吞噬时长 + 运行中 + 开始时刻(ms)
			Type gbType = typeof(GravebusterComponent);
			_fGraveDur = gbType.GetField("consumeDuration", F);
			_fGraveRun = gbType.GetField("_consumeRunning", F);
			_fGraveStart = gbType.GetField("_consumeStartedAtMsec", F);
			// 攻击组件（AttackComponent）：攻击/生成间隔 + 计时（"生成僵尸"倒计时用）
			Type atkType = typeof(AttackComponent);
			_fAtkInterval = atkType.GetField("attackInterval", F);
			_fAtkTimer = atkType.GetField("timer", F);
			_fAtkIntervalBase = atkType.GetField("attackIntervalBase", F);
			// buff 容器（速度倍率用）：TowerDefenseCharacter.buff : BuffComponent
			_fBuffDict = typeof(BuffComponent).GetField("_buffDictionary", F);
			// 发射组件（FireComponent，射手/杨桃系）：加速倍率 + 间隔 + 多发
			Type fireType = typeof(FireComponent);
			_fFireTimeScale = fireType.GetField("timeScale", F);
			_fFireIntervalBase = fireType.GetField("fireIntervalBase", F);
			_fFireInterval = fireType.GetField("fireInterval", F);
			_fFireTimer = fireType.GetField("timer", F);
			_fFireNum = fireType.GetField("fireNum", F);
			_fFireCurNum = fireType.GetField("currentFireNum", F);
			// 选卡栏卡片（种植 CD）
			Type cardType = typeof(TowerDefenseInGamePacketShow);
			_fCardCd = cardType.GetField("coldDown", F);
			_fCardCdTimer = cardType.GetField("coldDownTimer", F);
			_fCardOpen = cardType.GetField("_coldDownOpen", F);
			// 动画精灵时间缩放（猫窝 cadence 探针用）
			_fSpriteTimeScale = typeof(AdobeAnimateSprite).GetField("_timeScale", F);
			// 障碍物血量文本（ShowHealthComponent 的本体血量显示文本）
			_fBodyText = typeof(ShowHealthComponent).GetField("_bodyDisplayText", F);
			// 障碍物血量状态（HealthLabelState 结构，含 Current/Maximum——真实数值来源）
			_fBodyState = typeof(ShowHealthComponent).GetField("_bodyLabelState", F);
			// 障碍物血量 Label（血条本体 Label 的 Text，游戏常显 "HP:x/y"）
			_fBodyLabel = typeof(ShowHealthComponent).GetField("bodyHitpointLabel", F);
			// 血量真身：HurtComponent 持有的角色实例（hitpoints / hitpointsBase）
			_fDamageInstance = typeof(HurtComponent).GetField("_damageInstance", F);
			if (_fRunning == null || _fWait == null)
			{
				_reflectionFailed = true;
				Warn("找不到预期字段（CannonComponent._restTimerRunning/_restTimerRemaining），"
					+ "本 Mod 不生效。缺：" + MissingList());
				return false;
			}
			_reflectionReady = true;
			return true;
		}
		catch (Exception ex)
		{
			_reflectionFailed = true;
			Warn("反射准备失败，本 Mod 不生效：" + ex.Message);
			return false;
		}
	}

	private string MissingList()
	{
		string s = "";
		if (_fRunning == null) { s += "_restTimerRunning "; }
		if (_fWait == null) { s += "_restTimerRemaining "; }
		return s.Length == 0 ? "(无)" : s.Trim();
	}

	// ---------------------------------------------------------------- 日志
	// 每条出错路径各用一个「已报告」标志，不共用（技能文档 §4）。

	private bool _initFaultReported;

	/// <summary>
	/// 诊断日志总开关（v1.14.1 用户要求：**注释 log 生成**）——false 时 `Info` 全部静默。
	/// 诊断代码与探针逻辑完整保留（只是跳过输出），改回 true 即恢复；`Warn`（异常报告）不受影响。
	/// </summary>
	private const bool EnableInfoLog = false;   // v1.16.3：验证完毕，关闭诊断日志

	private void Info(string msg)
	{
		if (!EnableInfoLog)
		{
			return;
		}
		try { if (_context != null) { _context.Log(LogPrefix + msg); } else { GD.Print(LogPrefix + msg); } }
		catch { }
	}

	private void Warn(string msg)
	{
		try { if (_context != null) { _context.Warn(LogPrefix + msg); } else { GD.PrintErr(LogPrefix + msg); } }
		catch { }
	}
}
