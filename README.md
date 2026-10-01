# 血条·装填倒计时（HealthCooldownLine）

在原有血条的三行（护盾 / 头盔 / 本体）下面**再加一行**，显示该角色的关键事件数值（v1.2+）。
**没有对应组件的角色不显示这一行**；按优先级取第一个命中的：

| 优先级 | 数据源 | InstanceId | 显示 |
|---|---|---|---|
| ① | 加农炮装填 | character.cannon | `装填 12.4s`（青蓝）/ `装填 就绪`（绿） |
| ② | 命名计时器（定时炸弹/核弹等） | character.timer | `倒计时 45.2s`——**多计时器项同时显示，每项一行**（最多 3 行、按剩余升序） |
| ②.5 | 大嘴花咀嚼 | character.chomper | `咀嚼 3.2s`（青蓝，isChew 期间显示） |
| ②.6 | **长大**（阳光菇系 9 个角色） | character.grow_up | `长大 12.0s`（青蓝，分阶段时按当前阶段时长） |
| ②.7 | **周期区域事件**（冰坚果/伞火把） | character.periodic_area_event | `触发 0.4s`（青蓝，提醒：冰坚果周期仅 0.5s，数字快速循环） |
| ②.8 | **磁力消化**（磁力菇系 9 个） | character.magnet | `消化 20.1s`（青蓝，仅吸到护甲消化期间显示） |
| ②.9 | **阳光/金币/卡包产出**（29 个产出定义） | character.produce | `阳光 8.2s` / `金币 6.0s`（青蓝，按 produceType 映射） |
| ③ | 土豆雷准备出土 | character.potato | `准备 8.3s` / `准备 就绪`（绿） |
| ④ | 篮球剩余投抛数 | character.bowling | `剩余 3 发`（绿/灰） |
| ⑤ | 投石车剩弹 | character.catapult | `剩弹 2/3`（绿/灰） |
| ⑥ | **坑洞消失倒计时**（角色级字段，先于组件链判定） | dieDownTimer | `消失 12.4s`（青蓝） |

**v1.14.1 调整（关闭诊断日志）**：`Info` 加总开关常量 **`EnableInfoLog = false`**——诊断输出全部静默
（46 处调用与探针逻辑完整保留，改回 `true` 重建即恢复；`Warn` 异常报告不受影响）。

**v1.14.0 调整（坑洞不显示血量）**：坑洞（Crater 系）**排除在血量显示之外**（两条判定路径双保险）
——它没有"血量"概念；其"消失 X.Xs"计时仍由"障碍物消失"开关正常显示。

**v1.13.9 改进（广义障碍物判定）**：此前障碍物只认 5 类精确名单（坑洞/墓碑/炸弹/核弹/大火），
垃圾桶等名单外的被漏。新增 **`IsObstacleLike`**：**非植物/非僵尸/非割草机 + 有 `HurtComponent`
（有血量）** ⇒ 血量显示改用广义判定——**一切有血量的障碍物**都常显 `血量 X/Y`
（坑洞无血量、不可攻击，本来就不显示）。"障碍物消失"计时分流仍用 5 类精确名单。

**v1.13.8 ★ 修复（血量真身接入——"障碍物血量"终成正果）**：顺着"仔细看炸弹/墓碑类"深挖到
**`HurtComponent._damageInstance`（TowerDefenseCharacterInstance）的 `hitpoints`（当前）/
`hitpointsBase`（上限）** —— **任何时刻可读、与血条显示无关**（此前 ShowHealthComponent 三路全空
是因为数据推给 UI 即走、不驻留）。现在障碍物**常显** `血量 X/Y` 且掉血实时更新；
原三路降为兜底。诊断含 `hurt=cur/max`。

**v1.13.7 改进（障碍物血量"受伤时显示"）**：血量数值平时读不到（血条刷新后才写入组件），
改为**值出现/变化即记录并显示**——障碍物被攻击/悬停、血条一刷新，头上就会显示 `血量 X/Y`；
诊断也升级为"值出现即打日志"（钥匙 `名字|值`）便于验证。

**v1.13.6 新增（战斗辅助说明行）**：在"战斗辅助"开关下方追加一行小字
`— 咀嚼·消化·土豆雷·篮球·投石车·啃碑 —`（玩家一眼知道它管什么）。

**v1.13.5 调整（开关互换）**：按用户要求"**战斗辅助**"与"**生成计时**"互换显示位置
（引入显示顺序表 `order = {0,1,2,3,8,5,6,7,4,9}`，功能索引不变、仅调显示位次与列分配）。

**v1.13.4 加强（障碍物血量三路数据源）**：v1.13.3 的状态结构在"血条未刷新"时读不到值（Initialized=False、
Current/Maximum 为默认 0）。现三路按序取：① `_bodyLabelState`（**要求 Initialized=true 且 max>0**）；
② **`bodyHitpointLabel`（血条本体 Label 的 Text）**；③ `_bodyDisplayText`（显示缓存）。
兜底组件来源加 `character.showHealthComponent`（角色基类直接字段）。
诊断一条打全：`state=cur/max init=.. label=".." text=".." → 采用=".."`。

**v1.13.3 修正（障碍物血量改读状态结构）**：日志显示 `_bodyDisplayText` 平时为空（只是"显示缓存"）
⇒ 墓碑/炸弹无数字。改读 **`ShowHealthComponent._bodyLabelState`（HealthLabelState 结构）** 的
**`Current`/`Maximum`** → 显示 `血量 1500/1500`（文本字段仅作退化兜底）。

**v1.13.2 调整（总开关移到第 2 列 + 列间距调紧）**：三列布局下第 2/3 列第 5 行被裁
（"只显示 4 行"）——按用户要求把**总开关从第 1 列移到第 2 列顶部**（第 1 列=游戏列表+署名），
并把 **col2/col3 的 separation 10→6** 争取 5 行全显。

**v1.13.1 改进（设置页三列布局）**：**第 1 列** = 游戏自带列表 + 总开关 + 署名行；
**第 2 列** = 装填与核能/障碍物消失/障碍物血量/选卡栏种植CD/生成计时；
**第 3 列** = 角色计时器其他CD/成长与充能/产出倒计时/战斗辅助/等级与加速。

**v1.13.0 重构（总开关 + 10 类）**：装填与核能 / **障碍物消失**（原"坑洞消失"扩展：坑洞/墓碑/
定时炸弹/定时核弹/大火） / **障碍物血量**（新——读 `ShowHealthComponent._bodyDisplayText`
显示 `血量 X`） / 选卡栏种植CD / **生成计时**（新——`Spawn` 键，植物/僵尸版） /
**角色计时器其他CD**（新——其余命名计时器） / 成长与充能 / 产出倒计时 / 战斗辅助 / 等级与加速。
计时器**按键分流**：障碍物→障碍物消失；`Spawn`→生成计时；其余→其他CD。
5 物件类名：`Crater` / `Gravestone`（含 `TimeBomb`/`TimeNuke`）/ `ItemMegaFire`。

**v1.12.0 重构（按用户方案：总开关 + 8 类）**：装填与核能 / **命名计时器** / 坑洞消失 /
选卡栏种植CD / 成长与充能（不含周期事件）/ 产出倒计时 / 战斗辅助（咀嚼/消化/土豆雷/篮球/
投石车/啃碑 6 项）/ 等级与加速。**硬编码关闭**（保留代码，改 true 即恢复）：
**疯狂海草点击冷却**、**周期区域事件**。
"命名计时器"= 各角色自定义计时器（倒计时/准备/生成/销毁/消失/布雷/张开/闭合/开火/蓄力/
放置/回复/血量/治疗 + 其他键原样）。

**v1.11.6 新增（双格植物显示居中）**：双格植物（卡牌特性栏标"双格"）的角色原点在左格，
行标签默认只盖第一格——现对名单内植物**右移半格**（`DoubleWidthShift=50f`，实测可调），
文字**居中到两格中间**。名单：**`CobCannon` 全族**（玉米加农炮/**寒冰加农炮 IceCobCannon**/
**毁灭加农炮 DoomCobCannon**/GarCobCannon/PotatoCobCannon）**+ `HotDog` 热狗射手**；
`"CobCannon"` 片段精确避开非双格的 PumpkinCannon。

**v1.11.5 修正（两行重复 + 僵尸误报）**：① `FindSpriteOf` 递归太深——**猫窝的查找递归进了
"里面猫尾草"**、把里面植物的精灵当成自己的（两行数值相同）⇒ 限制深度 3（自己的精灵恰在第 3 层
可命中，里面植物在第 5 层被挡住）。② v1.11.4 的"全角色动画监控"引入僵尸误报（**动画基准速度
天生各异**）⇒ **回退**：动画显示只对猫尾草类。

**v1.11.4 改进（动画监控覆盖所有角色）**：把"动画 `_timeScale` 记录"从猫尾草类探针里独立出来、
**覆盖所有角色**（`_animTsAll`）——任何"走动画层"的加速源（猫窝已实证 / 毁灭咖啡豆等待验）
都会显示 `动画 ×N`。**当前三条加速通道总览**：
① **buff `timeScaleValue`** → `加速 +N%`（咖啡三叶草/蓄能咖啡豆/魔法根加速）；
② **动画 `_timeScale`** → `动画 ×N`（猫窝 ✓）；
③ **字段比值/基线** → `攻速 +N%`（标称 vs 当前）、`弹数 +1`、`伤害 +N%`（damageScale）。

**v1.11.3 ★ 猫窝加成落点定案（动画时间缩放）**：探针实证
`猫窝探针[PlantCatTailZ] 变化：…｜动画ts 1→6`、`[PlantKabbageTail]：…｜动画ts 1→5`
⇒ **猫窝"攻速翻倍"的真实落点 = `AdobeAnimateSprite._timeScale`**（动画时间缩放 1→5/6）——
攻击由**动画关键帧事件**驱动，动画提速即攻速提升（所有字段不变之谜的谜底）。
现显示 **`动画 ×N`**（如 `动画 ×6`）；v1.11.2 的"实测周期"因测错对象（timer 归零 ≠ 发射间隔，
数值乱跳）已移除、日志关闭。

**v1.11.2 新增（实测发射周期）**：日志证实 5 个候选字段**全都没变**（缓存 DLL 时间戳证明 v1.11.1
确已加载、探针却一行未打）⇒ 猫窝加速**极可能是发射那一刻的瞬时计算**（不写回字段）。
现改为**行为级测量**：监控 `FireComponent.timer` 的**归零间隔**（= 真实发射周期）——
猫窝内应见 1.5s → 0.75s，据此显示 `攻速 +N%`。诊断：`实测周期[名]：1.500s → 0.750s`。

**v1.11.1 新增（猫窝加成落点探针）**：**"猫尾草类"判定字段定案 = `physiqueTypeFlags` 含 256**
（全库 17 种：猫尾草/传送猫尾草/僵尸猫尾草/**豌豆猫**/雪豌豆猫/分裂豌豆猫/招财猫/猫猫星机枪/
小猫向日葵/冬瓜猫/猫盆/卷心菜尾/双卷心菜/玉米旋转器/GWC/HWC/SWC——**豌豆猫确实在内，此前判断有误，已更正**）。
实测它的 `fireInterval` 恒 1.5（未变）⇒ 加速落点不在发射字段。现加探针：对猫尾草类监控
**5 个候选字段**（间隔/基准间隔/发射时间缩放/攻击间隔/**动画 `_timeScale`**），任一变化即打
`猫窝探针[名] 变化：…`——用于定位"攻速翻倍"改的是哪个字段。

**v1.11.0 修正（猫窝加成定案 + 攻速"标称对比"）**：猫窝卡牌原文——
**"使种在猫窝内的猫尾草类植物攻速翻倍"** ⇒ 加成**不在猫窝自己身上**，而在**住在其内的猫尾草**上。
攻速检测因之改为 **"标称间隔 / 当前间隔"**（`fireIntervalBase/fireInterval`，**只显示变快**）：
猫窝内猫尾草 1.5 → 0.75 = **`攻速 +100%`**；变慢（天生差异）不显示。
（此前"种下基线对比"会漏掉这种"出生即加成"的常驻效果。）

**v1.10.9 改进（加成显示数值）**：加成 buff 从"只有名字"升级为 **"名字 + 数值"**——
**倍率优先**（`BuffTabooBean._savedTimeScaleInit` 与当前 timeScale 之比 → `毁灭加成 +N%`），
算不出倍率时**退化显示剩余时长**（`毁灭加成 12.3s`）。
诊断：`加成值[名/键]：savedTs=.. hasSaved=.. 当前timeScale=.. → 显示".."`。

**v1.10.8 新增（加成 buff 名显示）**：日志（v1.10.7 的 buff 清单）证实
`buff列表[PlantPeaCat]：TabooBean`——**植物身上确实挂着毁灭咖啡豆的加成 buff**，
但其效果（`BuffTabooBean._savedTimeScaleInit` 改写 timeScale）**不在我们监控的字段上**。
现改为**直接显示"加成 buff 存在"**：白名单
`TabooBean / RedHeat / Fluorescence / Radiance / ButterGene / CornpultGene / IceShroomGene`
→ 植物头上显示 **`毁灭加成`**（绿）等；诊断 `buff列表[名]：键1,键2`。

**v1.10.7 新增（buff 清单诊断）**：任何角色**首次"身上挂着 buff"时**打一条
`buff列表[名]：键1,键2,...`——一劳永逸回答"到底有没有加成 buff"（配合操作：点蓄能咖啡豆 /
等猫窝节奏触发后再看）。另查明："Cadence（节奏）"仅存在于测试类，**无独立组件**——
猫类节奏最终仍体现在 `fireInterval`/`timeScale`（已监控）。

**v1.10.6 修正（速度显示独立于基线）**：原"速度倍率"显示被包在**基线块**内 ⇒
**非发射型角色（无 FireComponent ⇒ 无基线）即使有加速 buff 也不显示**（毁灭咖啡豆系正属此类）。
现移出基线块、**独立判断**（`spd>1` → `加速 +N%`；`spd<1` → `加速 -N%`；buff 不在则不显示），
基线块只保留"弹数/攻速"（这两类才需要与种下时比较）。
另：**毁灭咖啡豆 = 纯爆炸型辅助**（唯一组件 `ExplodeComponent`，无计时字段）——自身无可显示的
持续状态，其加成作用在**被作用的植物**上（那些植物会显示）。

**v1.10.5 新增（伤害加成通道）**：日志显示**狂热星（FeverStar）的加成是"伤害缩放"
（`damageScale`）**、猫窝有加速（`速度倍率=3`）、毁灭咖啡豆是辅助型（`Fire=False`，自己不发射）。
现增加 (d) 通道：读**角色级 `damageScale`** 并与基线对比 → 显示 `伤害 +N%` / `伤害 -N%`；
诊断 `伤害变化[名]：基线=.. → 当前=..`。

**v1.10.4 修正（"加成"真身 = 弹数 +1；基线变化显示；抓取关闭）**：
- 日志定案：**寒冰杨桃的加成是"弹数 1→2"（多发）**，不是攻速；"攻速 0.5"是天生差异
  （游戏里很多植物标称间隔≠实际间隔）。
- 显示逻辑重构：**以"种下时的实测值"为基线，只显示真实变化**——`弹数 +N` / `加速 ±N%` / `攻速 ±N%`
  （天生差异不再产生噪音，真实变化不再被漏掉）。
- **缠绕海草"抓取 X/Y"按用户要求硬编码关闭**（常量 `EnableGrabLine = false`，代码完整保留，
  改回 true 即恢复）。

**v1.10.3 修正（倍率双向显示 + 变化诊断）**：`spd`/`atk` 默认值改 1.0，任一非 1 值都采纳；
显示 `加速 ±N%`（绿/灰）、`攻速 ±N%`；新增「发射变化」诊断（每角色记录首次基线，偏离即打前后对比）。
（v1.10.2 修正：运行时组件必须从 `_runtimeByInstanceId` 取——`componentList` 是另一个体系。）

**v1.10.1 修正（卡片 CD 方向）**：`coldDownTimer` **本身就是剩余**（递减）——v1.10.0 误算成
"已过"导致正数计时；且不再依赖 `_coldDownOpen`（该标记 False 时卡片其实在冷却中，此前漏显示）。

**v1.10.0 新增（选卡栏种植 CD 数字）**：卡片本体 `TowerDefenseInGamePacketShow` 带
**`coldDown`（冷却总时长）/ `coldDownTimer`（计时）/ `_coldDownOpen`**——游戏自带的只有进度条、
没有数字。现在在**每张卡片中央**叠加剩余秒数（`ModCardCdLabel`，16 号字黑描边、青蓝）。
归「计时器与点击」开关；诊断：`卡片CD：coldDown=.. coldDownTimer=.. 剩余=..`。

**v1.9.3 改进（加速基线诊断）**：发射型植物首次出场打一条 `发射基线[名]：timeScale/基础间隔/当前间隔/弹数`，
检测到加成时再打 `发射加成[名]：…`——两条对比即可判定加成改了哪个字段。
（加速源盘点结论：可量化加成只走两条路——buff 倍率 / 直接改发射·攻击字段，均已监控；
猫窝/猫类、咖啡豆系、基因系、杨桃系全部覆盖。）

**v1.9.2 修正（倍率检测扩展到发射组件）**：杨桃系（SeaStar/FeverStar）**用 FireComponent 发射、
没有 AttackComponent**，v1.9.1 的检测对它们失效。现新增发射组件通道：
`timeScale`（加速倍率）、`fireIntervalBase/fireInterval`（间隔比=攻速）、`fireNum/currentFireNum`（多发）
——**杨桃"经历火/冰后攻速提升"的落点应在这里**。诊断：检测到加成时打
`发射加成[名]：timeScale=.. 基础间隔=.. 当前间隔=.. 弹数=../..`。

**v1.9.1 新增（等级与加速：速度/攻速倍率）**：
- **速度倍率**：读角色 buff 容器（`TowerDefenseCharacter.buff`）里加速类 buff 的
  `timeScaleValue`（BuffCoffee/BuffMagicRootHaste 都带）→ 显示 `加速 +N%`（绿）。
- **攻速倍率**：`attackIntervalBase / attackInterval`（间隔变小=攻速变快）→ 显示 `攻速 +N%`。
  **杨桃系"经历火/冰后攻速提升"最终就体现在这里**（需求 3 的攻速部分由此覆盖）。
- 仅当 >1（确有加成）时显示；诊断：`倍率[名]：速度倍率=.. 攻速倍率=..`。

**v1.9.0 新增（6 类功能独立开关）**：设置页右列总开关下方新增 6 个独立开关——
**装填与核能 / 计时器与点击 / 成长与充能 / 产出倒计时 / 战斗辅助 / 等级与加速**，
各自独立记忆（配置文件 user://HealthCooldownLine.cfg 多键）。
各显示分支按类别把关（`CatOn(cat)`）；识别与状态逻辑不受开关影响，只控制是否显示。

**v1.8.5 修正（疯狂海草：点击生成 + 2 秒点击冷却）**：用户纠正——它是**点击触发**生成，
不是自动定时。冷却就是它计时器里的 **`"Open": 2.0`** 键（"至少两秒才能成功点击"）。
现已显示：冷却中 **`点击 X.Xs`**（青蓝）→ 就绪 **`可点击`**（绿）；
同时让 ② 计时器分支**跳过它的 "Open" 键**（否则会多一行"张开"）。
诊断：`点击[PlantInsaniKelp]：Open运行中=.. 冷却时长=2 当前值=.. → 剩余=..`。

**v1.8.4 新增（疯狂海草"生成僵尸"倒计时）**：疯狂海草（InsaniKelp）的"生成僵尸"就是它的
**攻击行为**（攻击事件绑定召唤缠绕潜水僵尸）——生成周期 = `AttackComponent.attackInterval`、
进度 = `timer` ⇒ 显示 **`生成 X.Xs`**。
- 该角色攻击组件的 InstanceId **带后缀**（`character.attack.0`）⇒ 新增 helper
  `FindComponentOfType<T>`：**遍历组件清单按类型取实例**，不依赖 InstanceId。
- 它的 Timer 只有 `"Open": 2.0`（张开动画）、attackType 是通用的 `Default`——两者都不能用于识别，
  故按角色类型名识别（`InsaniKelp`）。诊断：`生成[名]：attackInterval=.. timer=.. → 剩余=..`。
（注：v1.8.5 已把"生成"改为"点击/可点击"的点击冷却语义——见上。）

**v1.8.3 修复（翻转角色文字位置偏移，截图实证）**：魅惑僵尸截图显示 `咀嚼 3.8s` 被甩到
角色右侧远处——**文字方向已正常**（v1.8.1 生效），但**局部坐标 X 被父级翻转镜像**（Label
左上角 -80 → +80）。修复：位置改用**全局坐标** `lbl.GlobalPosition = 角色全局位置 + 偏移`，
不受父变换影响；配合已有的 Scale/Rotation 抵消，翻转/旋转角色的**方向与位置**全部正常。
（修正 v1.8.0 的错误推断："中心在 0 所以位置不受镜像影响"——镜像作用于左上角坐标。）

**v1.8.2 新增（核弹磁力菇核能充能）**：`TowerDefensePlantMagnetShroomDM` 角色级字段
`ironCount`（已收集铁器）/ `ironPerFullCharge`（充满所需）/ `_isArmed`（充能完成）
→ 未满显示 `充能 X/Y`（青蓝），满后显示 `可发射`（绿）；同时让加农炮的
"装填 就绪"不再重复显示（"装填中"仍显示）。日志：`核能[名]：铁器=../.. 可发射=..`。

**v1.8.1 修复（蓄能带等级 + 镜像增强 + 降噪）**：
- 蓄能显示改为 **`充能{等级} {剩余}s`**（如 `充能0 29.7s`）——日志已证明识别成功，
  用户缺的是等级数字；两套蓄能分支（SunShroomCharge 系 / EnergyBean 系）都带等级。
- **文字镜像修复增强**：除负缩放抵消外，新增 **Rotation 抵消**（`lbl.Rotation = -GlobalRotation`，
  覆盖"180° 旋转式翻转"——视觉上同为文字镜像）；并加**变换异常诊断**（任何角色出现
  负缩放/旋转/斜切时首次打出完整变换值，用于精准定位）。
- 蓄能识别失败诊断降噪（只对挂产出口组件的角色打）。

**v1.8.0 新增（第三批 + 镜像修复）**：
- **魅惑僵尸文字镜像修复**：角色节点水平翻转（Scale.X<0）时，子 Label 会跟着镜像 →
  现检测 `GlobalScale.X < 0` 并给 Label 自身 `Scale=(-1,1)` 抵消（位置无需调整）。
- **缠绕海草抓取数**：`TanglekelpComponent`（InstanceId `character.tanglekelp`，6 个角色）
  → `抓取 X/Y`（currentGrabNum/grabNum；未满绿 / 满灰）。
- **墓碑吞噬倒计时**：`GravebusterComponent`（InstanceId `character.gravebuster`）
  → `啃碑 X.Xs`（consumeDuration − (now − _consumeStartedAtMsec)/1000）。
- 变形组件（ChangeProjectileComponent）**全库无使用者**，暂缓。

**v1.7.8 修复（static 字段静默失败）+ 署名行**：
- 蓄能识别真因：`EnergyBean` 的 `ChargeInterval`/`MaxChargeLevel` 是 **static 字段**，
  而字段查找只带 `Instance` 标记 → `GetField` 恒 null（**不报错**）→ 识别静默失败。
  离线反射探针实测锁定（`tools/reflprobe`：不启动游戏、直接加载 DLL 验证查找逻辑）。
  已给 `FindFieldAlong` 加 `BindingFlags.Static`，并新增"蓄能识别失败"诊断。
- mdprobe 支持 `[static]` 标记；全景扫描确认**无第三个受害者**（其余读取字段均为实例）。
- **设置页右列开关下方新增署名行**："apple1949开发中 请勿冒用发布！"（样式与开关一致）。

**v1.7.7 修复（角色认错纠正：蓄能咖啡豆 = EnergyBean）**：mdprobe 实锤
`TowerDefensePlantEnergyBean` 字段（MaxChargeLevel / ChargeInterval / _chargeLevel / _chargeTimer /
_mousePress）与卡牌描述"每30秒充能+1（上限5级）"逐条吻合——**"蓄能咖啡豆"是 EnergyBean**，
不是 SunShroomCharge！此前识别套（level/_produceInterval）从未命中它，故它一直掉进产出分支
显示"阳光"（用户三次反馈的根源）。新增 EnergyBean 专用识别（_chargeLevel/ChargeInterval/_produce），
显示"充能 X.Xs"并接管；SunShroomCharge 旧识别保留（它也是真的蓄能类）。

**v1.7.6 加固（蓄能接管无条件化）**：三特征命中即 `chargeHandled=true`——即使间隔字段
暂时读到 0 也绝不退回产出分支显示"阳光"。

**v1.7.5 修复（Godot 泛型数组真身）**：17:32 日志显示定义对象拿到了、数组转换仍失败 →
查 GodotSharp 元数据实锤：**`Godot.Collections.Array<T>` 的基类是 `Object`，与非泛型
`Godot.Collections.Array` 没有继承关系**——所以 `as Godot.Collections.Array` **恒 null**
（v1.7.1~1.7.4 四版全在用一个死通道；数据很可能一直躺在数组里没被读出来）。
现统一走 `TryReadFloatArray`：**泛型 `as Array<float>` → `IList<float>` → `IEnumerable` 逐元素**。

**v1.7.4 修复（定义访问器属性名）**：17:27 日志显示回退仍失败（`数据源=组件`）。
mdprobe 复核：**GrowUpComponent 没有自己的 `Definition` 属性**——定义资源访问器在**基类
`CharacterComponentRuntime` 上叫 `ComponentDefinition`**（`<ComponentDefinition>k__BackingField`）。
上一版查 "Definition" 得 null 故失败。现先查 `ComponentDefinition`、退查 `Definition`，
且回退每步失败原因（属性 null / 定义对象 null / 数组空或异型 / 异常）全部拼进"数据源"诊断。

**v1.7.3 修复（长大的数据源）**：17:22 日志实锤——运行时组件 `growUpTime` 是**空数组**
（Count=0；timer 在跑、reach=0，就是没数据），阶段时长实际在 **Definition**
（`GrowUpComponentDefinition.growUpTime`，共享定义 [60.0]）上。
现在读取顺序：组件数组（非空）→ **Definition.growUpTime**（反射）→ IList 兜底；
诊断带"数据源=组件/定义"标记。

**v1.7.2 加固（全库同类坑排查）**：
- 全部读取字段的类型逐个用 mdprobe（带类型版）重核：**无新增同类坑**（唯一容器型=
  计时器三字典 + growUpTime，均已处理）。
- **篮球筛选**：坚果系 14 个角色（Wallnut/Firenut/Magnetnut/Icenut 等）挂 BowlingComponent
  做"被击滚动"、无 maxHitNum 配置（默认 0）——加 `maxHitNum>0` 条件，避免刷"剩余 0 发"噪音。
- **候选缺组件诊断**：土豆雷/篮球/投石车三组分支在"组件清单有该组件但按 InstanceId 取不到"时，
  日志直接打出该角色 **InstanceId 注册表全部键**（`_runtimeByInstanceId`）——一劳永逸定位。

**v1.7.1 修复（生长不显示 + 蓄能咖啡豆）**：
- **长大不显示的根因**：`growUpTime` 实际类型是 **`Godot.Collections.Array<float>`**
  （mdprobe 输出 `Array\`1<Single>`），旧代码 `is System.Array` 恒 false ⇒ stages=0 ⇒ 不显示
  （连诊断行都不打）。现改双通道读取（Godot Array 优先 + .NET 数组/IList 兜底），
  诊断也改为"组件在就打一条"（含数组真实类型名）。
- **蓄能咖啡豆（SunShroomCharge）充能倒计时**：机制在**角色类**
  `TowerDefensePlantSunShroomCharge`（字段 `level` / `_produceInterval` / `_produceComponent` /
  `MaxLevel`），产出组件只是计时器。三特征字段齐全才认定 ⇒ 显示 `充能 X.Xs`
  （每 30 秒充能等级+1 的剩余），并**替代**产出分支（避免重复显示"阳光"行）。
  诊断：`蓄能[名]：等级=lv/max 充能间隔=.. 组件timer=.. → 剩余=..`。

**v1.7.0 兼容性重构（多行 / 叠种 / 独占修复）**：
- **一角色多行并存**（收集式重写）：所有数值改为"追加行"——加农炮不再独占返回
  （**核弹磁力菇 PlantMagnetShroomDM 现在同时显示 `装填 就绪` + `消化 X.Xs`**）；
  阳光菇显示 `长大` + `阳光` 两行；最多 6 行。
- **每行独立 Label（ModCooldownOwnLine0..5）**：各行颜色可不同（就绪绿 / 进行中青蓝混排），
  第 0 行仍在 -38（贴 HP 行上方），往上按 20px 行高排。
- **叠种自动错位**：同格多个角色（阳光豆叠阳光菇等）按角色全局位置（量化 8px 网格）
  记录"已占用高度"，后显示的角色整组行向上错开、互不遮挡（每帧重建、顺序稳定）。
- **磁力消化方向修正**：日志实测 `breakDownTimer` 是**从 breakDownTime 递减的"剩余"**
  （首采 14.667/15——接近满值起算，与加农炮 `_restTimerRemaining` 同族），v1.6.x 误按
  "已过"算 `bt−t` 导致正向计时；现直接用它。
- 血条容器路径代码全部移除（实测四字段恒 null、从未生效），统一自建多 Label。

**v1.6.1 新增（阳光产出）**：
- **阳光/金币/卡包产出倒计时（ProduceComponent）**：InstanceId `character.produce`
  （29 个产出定义共用）；剩余 = `_effectiveProduceInterval − timer`（有效间隔受 buff/等级影响，
  退化用 `_produceInterval`）；显示词按 `produceType` 映射：Coin→金币、Packet→卡包、其余→阳光。
  （注：用户早前说不要产阳光间隔，2026-09-26 16:55 改口要求加上——此条按最新要求实现。）

**v1.6.0 新增（第二批）**：
- **长大（GrowUpComponent）**：`growUpTime` 是**数组**（各阶段时长，共享定义 [60.0]）、
  `growUpReach` 为当前阶段索引、`timer` 从 0 递增 ⇒ 剩余 = `growUpTime[reach] − timer`。
  覆盖 9 个角色：阳光菇系（阳光菇/双子/胆小/墨西哥辣椒/阳光菇O）、阳光豆、阳光投手、
  VIP坚果、小鬼僵尸。**字段语义已确认**（timer 家族方向）。
- **周期区域事件（PeriodicAreaEventComponent）**：`staticTime` 为周期、`timer` 递增
  ⇒ 下次触发剩余 = `staticTime − timer`。实测仅 2 个角色：冰坚果（staticTime=0.5s）、伞火把。
  （旧资料称"15 个"是统计口径错误，以解包实测为准。）
- **磁力消化（MagnetComponent）**：`breakDownTime` 总时长、`breakDownTimer` 递增、
  `_breakDownArmor` 为目标护甲 ⇒ 仅 `_breakDownArmor != null`（吸到护甲）或计时已走时显示
  `消化 X.Xs`。覆盖 9 个：磁力菇系（磁力菇/冰/暗影、磁力坚果/南瓜/炸弹/地雷、道具磁铁/磁力波）。
- Label 面板 rect 扩大到 140px（≈7 行容量），多个数值同时显示时自动向上生长。

**v1.5.0 变更**：
- **坑洞方向修正**：日志实测 `dieDownTimer` 是**从 0 递增的"已过"**（首见采样 0.05~0.3s vs 配置 30~200s），
  v1.4.0 直接显示导致"正向计时"；现改为 `配置 dieDownTime − dieDownTimer`（日志也改成打"已过"原值）。
- **多行显示**：一个角色有多个运行中的计时器项时**同时显示、每项一行**（如僵尸大嘴花
  `生成 24.9s` + `咀嚼 3.2s`）；自建 Label 改用 Bottom 垂直对齐 + 放大 rect，
  单行时位置不变（仍在 HP 行上方 -38），多行时向上生长不压血条。
- **新增咀嚼（ChomperComponent）**：InstanceId `character.chomper`（8 个大嘴花系定义全部用同一 ID——
  普通/僵尸/蒜香/花盆/尖刺/爆破/僵尸鲨鱼/僵尸大嘴花），`isChew=true` 时显示 `咀嚼 X.Xs`。
  判据是组件存在，新增变体自动覆盖。
  **字段定案（v1.5.1）**：`currentChewTime` 恒等于 `chewTime`（是"本轮咀嚼时长"快照，**不是进度**——
  v1.5.0 用它算剩余导致显示恒 0）；真正的进度是 **`chewTimer`**（从 0 递增）⇒
  剩余 = `chewTime − chewTimer`。实测时长：普通大嘴花 40s、僵尸大嘴花 30s、大嘴花僵尸 10s。

**v1.4.0 新增**：
- **坑洞消失倒计时**：机制来自 DLL 元数据定案——`TowerDefenseCrater`（所有坑洞的基类）
  有角色级字段 `dieDownTimer:double` 在递减，总时长在配置 `TowerDefenseCraterConfig.dieDownTime`
  （实测白天地面坑 200s、夜晚坑 30s），归零触发 `DieDown()` 塌陷消失。
  判据是"字段存不存在"（沿类型链反射找），不是角色名单 ⇒ 全部坑洞变体（DayGround/NG/N/K/Imp/Lava/G）自动覆盖。
  因为不经过组件、且坑洞可能没有组件集，该分支放在组件链**之前**。
- **设置页开关改为两列布局**：原列表（血条开关等）整体变为左列，右侧新开一列、
  「显示冷却倒计时」放在右列顶部。实现 = 运行时新建 `HBoxContainer` 包住原有 `VBoxContainer`
  + 新列（`Alignment=Begin` 顶对齐）。reparent 安全性已核实：
  `BattleOption` 的 `plantHealthCheckBox` 等字段是**对象引用**（DLL 元数据实查），
  与节点在树中的位置无关，注入又发生在 `_Ready` 之后 ⇒ 挪动不破坏游戏脚本。

## 一行字长什么样

```
HP:100/100       ← 护盾（青）
HP:100/100       ← 头盔（黄）
HP:100/100       ← 本体（红）
装填 12.4s       ← 本 Mod 新增（青蓝）= 正在装填
```
装填完毕、可以开火时，这一行变成 **绿色** 的：

```
装填 就绪        ← 本 Mod 新增（绿色）= 已装填完毕
```

样式（字体 / 字号 14 / 描边 5 / 居中）**全部照抄本体那一行**，只换字体颜色，
所以版式跟原版三行完全一致。

## 装填时间取自哪里

**属性 = `CannonComponent.restTime`**（首次装填是 `firstRestTime`），
**运行时剩余 = `private` 字段 `_restTimerRemaining`**，是否在装填看 `_restTimerRunning`。
组件 InstanceId = `character.cannon`。

配置实例：`Script/Component/TowerDefense/Character/CannonComponent/Definitions/CobCannonDefinition.tres`
→ 实测 `restTime = 35.0`（玉米加农炮 35 秒装填）。

## 覆盖范围（10 个角色）

全库带 `CannonComponent` 的共 10 个：

| 植物 | 僵尸 |
|---|---|
| CobCannon（玉米加农炮） | ZombieNormalCobCannon |
| PeaCannon、IceCobCannon | |
| DoomCobCannon、GarCobCannon | |
| PotatoCobCannon、PumpkinCannon | |
| PowShroom、MagnetShroomDM | |

判据是**组件在不在**，不是角色名单 —— 游戏以后加新的加农炮角色，本 Mod 自动覆盖。

## 目录

```
mod/HealthCooldownLine/
├── mod.json                     清单
├── build_pmod.py                打包脚本（带 3 道硬护栏）
├── runtime_src/
│   ├── HealthCooldownLine.csproj
│   └── HealthCooldownLineEntry.cs   插件源码（唯一真源）
└── Runtime/ModAssembly.dll      编译产物（打包前必须存在）
```

## 怎么构建

```bash
# 1) 编译（用 .NET 9 SDK；游戏自身是 net9.0）
cd runtime_src
C:/Users/txgcs/WorkBuddy/zjb/tools/dotnet9/dotnet.exe build -c Release
# 产物落到 runtime_src/bin/Release/ModAssembly.dll

# 2) 打包（build_pmod.py 内建「护栏0」：会自动把 bin/Release 的最新产物
#    同步到 Runtime/ModAssembly.dll，并打印包内 DLL 的 md5）
python ../build_pmod.py
# -> mod/dist/HealthCooldownLine.pmod

# 3) 装机 + 校验（装机后务必核对包内 DLL md5 == 编译产物 md5）
cp mod/dist/HealthCooldownLine.pmod "$APPDATA/Godot/app_userdata/植物大战僵尸杂交版/Mods/"
unzip -p "$APPDATA/.../Mods/HealthCooldownLine.pmod" Runtime/ModAssembly.dll | md5sum
md5sum runtime_src/bin/Release/ModAssembly.dll   # 两个值必须一致
```

> ⚠️ **2026-09-26 教训**：曾出现「改了代码、忘记把编译产物拷进 `Runtime/`」，打出的包
> 里还是旧 DLL（只有 mod.json 描述是新的）——装机后功能毫无变化。现已由打包器的
> 护栏0自动防住，但**装机后仍要核对 md5**，这是唯一能证明"新代码真的进包了"的证据。

## 装机

把 `.pmod` 放进游戏用户目录的 `Mods\`：

```
%APPDATA%\Godot\app_userdata\植物大战僵尸杂交版\Mods\
```

（技能文档记的路径；实际以游戏内 F3 编辑器显示的 Mod 目录为准。）

## 设计要点（为什么这么写）

1. **必须托管插件**：这一行要每帧读运行时数值写进 Label，数据侧（.tres/.tscn）
   没有任何"把 A 组件的运行时状态写进 B 组件 UI"的机制。
2. **用反射读字段**：`_restTimerRemaining` / `_restTimerRunning` 是 **private 字段**
   （反射要带 `BindingFlags.NonPublic`），且数值类型没逐个确认过。强类型访问一旦猜错
   就是**编译期硬失败**；反射最多退化成"不显示"，且只在初始化时查一次并缓存。
3. **幂等加行**：血量显示视图是**池化复用**的（组件里有 `TryRentPooledView` /
   `ReleaseView`），同一个 `VBoxContainer` 会被反复用。所以按节点名
   `ModCooldownLabel` 查一次，有就只改文本，**绝不重复 add_child**。
4. **三个回调一律不抛**：`Initialize` / `OnAllModsLoaded` / `Shutdown` 全部
   try/catch 吞异常 —— 抛出去会导致**整包无条件回滚**（不受 `runtimeAssemblyPolicy` 保护）。
5. **`runtimeAssemblyPolicy: "optional"`**：程序集加载失败不连坐整包，新 Mod 的稳妥起手式。


## 实测状态（2026-09-26 17:00 首次成功）

**已跑通**：玉米加农炮显示青蓝 `装填 17.6s`、南瓜加农炮显示绿色 `装填 就绪`。

**关键实现变更（血条方案失败后）**：
`ShowHealthComponent` 的 `bodyHitpointLabel` / `shieldHitpointLabel` /
`helmetHitpointLabel` / `centerContainer` **四个字段实测全为 null**（即使血条显示开着），
⇒ 改由 `MakeOwnLine()` **在角色节点上自建 Label**（`ModCooldownOwnLabel`）：
`Position=(-60,-38)`（`-78` 实测太高，`-50` 仍偏高，用户两次截图标注后定 `-38` 紧贴本体 HP 行上沿）、
`ZIndex=200`、字体 `fzkt.ttf` / 字号 14 / 描边 5。
调用链：血条路径失败 ⇒ 自动退到自建，两条路都保留。

**命名计时器（②）已定案修复（2026-09-26 16:18）**：运行时探针实测——
`CharacterTimerComponent` 的 `timerRunning` / `timerWaitTime` / `timerCurrent`
**是字典而非标量**（`Dictionary<string,bool>` / `Dictionary<string,double>`），
按计时器名分项（TimeBomb 有 "Countdown"+"Ready"、CraterG 有 "Spawn"……）。
旧逻辑按 bool/double 直接读 ⇒ 永远不命中 ⇒ **这就是"计时器不显示"的根因**。
现改为：遍历正在运行的项、取剩余最小的一项显示 `倒计时 {剩余}s`，
键名有中文映射（倒计时/准备/生成/销毁/布雷/张开/开火/蓄力……）。
每个「角色|键」首次出现时会打一条 `计时器项[角色/键]：运行中=.. 时长=.. 当前值=.. 剩余(w-c)=..`
用于核对 `timerCurrent` 语义方向（若显示值异常，以该日志为准调整）。


---

## v1.16.2（2026-09-28）符石类恢复显示血量

**问题**：v1.16.1 把「护盾类」与「符石类」混为一谈，一起加进了 `TryCollectObstacleHp`
的"跳过血量"名单，导致**符石（RuneStones 系列）不再显示血量**。

**用户反馈**：「符石类要显示血量啊 哪来的护盾」—— 符石不是护盾。

**核实（读新解包源码）**：
- 符石 4 种 —— `TowerDefenseRuneStones` / `RuneStonesD` / `RuneStonesDLow` / `RuneStonesLow`，
  全部继承 **`TowerDefenseGravestone`**（墓碑基类），带完整 `DamagePointReach`（Damage0~4）——
  是**标准可攻击障碍物**，本来就该显示血量。
- 真护盾 2 种 —— `TowerDefenseItemSheild`（`Item/Sheild`）、
  `TowerDefenseGraveStoneTargetSheild`（`GraveStone/TargetSheild`）—— 这两个才是游戏自带血条、
  无需重复显示的。

**改动**：`TryCollectObstacleHp` 的跳过名单**只保留 `Sheild` / `Shield`**，
删除 `RuneStone` 条件。日志文案同步改为「护盾类（游戏自带血条，不重复显示）」。

**产物**：DLL 60928 字节 / md5 `fc3828ef99ed31dbfa69da5eaae0f81e`；
包装机到 `%APPDATA%\Godot\app_userdata\植物大战僵尸杂交版\Mods\HealthCooldownLine.pmod`，
ModsCache 已改名失效。

**编译环境备忘（本轮踩坑）**：`dotnet build` 在本沙箱**会卡死在 CoreCompile 之后**（进程不退出），
必须加 **`-p:UseSharedCompilation=false`**（关掉编译服务器）才能跑完；
即便这样收尾进程仍可能挂住，此时**直接用 `obj/Release/JTYHealthCooldownLine.dll` 拷到 `bin/Release/`**
（md5 与 obj 一致）再跑 `build_pmod.py` 即可。
