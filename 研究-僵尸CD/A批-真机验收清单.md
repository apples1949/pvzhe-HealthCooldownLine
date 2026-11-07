# A 批僵尸 CD · 真机验收清单（预期值算法 + 判对/判错）

> 用途：`报告-僵尸CD.md` §8 的 5 步真机验收时，逐条对照"屏幕上应该是多少、为什么"。
> 事实源：`unpack\`（0.32.0.0）实读 + 最终包 `dist\HealthCooldownLine.pmod`
> （包内 `Runtime/ModAssembly.dll` 103,936 B / md5 `9c499253507d04f5ab60e134e1fa390a`，
> 与 `bin\Release\JTYHealthCooldownLine.dll` **字节完全一致**，我独立复核过）。
> 环境：Windows / 单人（联机不做保证，代码里 `HasGameplayAuthority` 一类门控未验）。

---

## 0. 前置开关（不对就是"看不到"，不是代码坏了）

设置页「显示冷却倒计时」里的 4 个开关，**默认全开**（`_showLoad/_showObstacle/_showSpawn/
_showOtherTimer/_showGrowth/_showProduce/_showCombat/_showLevel/_showZombieCd` 全是 `true`，
见 `HealthCooldownLineEntry.cs:538-585`），「只显示 ≥5 秒」默认 **false**（`:563`）。

| 要看的东西 | 必须 | 说明 |
|---|---|---|
| A 批 9 个新增分支（自燃/自爆/召唤/偷取/治疗/变大/磁力） | 「显示僵尸冷却」**ON** | 门控 `_showZombieCd`，由 `TryCollectZombieCdLines` 统一判（我**没有**在分支里再按分类判） |
| 三叶草气球 / 三叶草多彩气球 的 `吹飞 8.0s` | 「显示僵尸冷却」ON **+** 「角色计时器其他CD」**ON** | 它走**通用通道 ②**（`character.timer` 的 `Clover` 键），归 `CatOn(4)`，**不是**僵尸专用开关 |
| 鲨鱼 的 `咀嚼 X.Xs` | 「战斗辅助」**ON** | 走通用通道 ②.5（`ChomperComponent`），归 `CatOn(7)`（`Entry.cs` 的 `if (CatOn(7) && …)`） |
| 重生法师死后坑洞 `消失 30.0s` | 「障碍物消失」**ON** | 走通用坑洞通道 `TryCollectCraterInfo`，归 `CatOn(1)` |

> ⚠️ **最容易误判的一条**：把「角色计时器其他CD」关掉以减少噪音后，三叶草气球那行**会消失**——
> 这是设计（不重复显示），不是回归。A 批 16 个里**只有这 2 个**依赖 cat4。

---

## 1. 五条"看着像 bug 其实是对的"（先背下来，能省一半误会）

1. **刚出场数值不动**：所有 ① 类字段的推进都套了 `base.IsInsideComponentBattlefield`（农夫/船长是
   `IsInsidePlantingArea`）门控 ⇒ 僵尸还在屏幕外走的时候，倒计时**冻结在满值**，进场后才开始掉。
2. **掉得比真实时间慢/快**：`timer += delta * timeScale` —— `timeScale` 是**僵尸自己的**时间缩放，
   被减速/加速 buff 影响时，"游戏内秒"与墙上时钟不一致。按游戏内秒判，别按秒表判。
3. **天使的 `复活` 会停住**：`spawnTimer -= delta * timeScale` 写在 `WalkProcessing`（`Angel.cs:275-290`）里
   ⇒ **只在行走时**掉。它站着啃植物时那一行是**不动**的。
4. **天使的 `复活` 有时突然变成 3.0**：`spawnTimer` 归零后若场上没有"可复活尸体"
   （`TowerDefenseManager.HasDeathRecord(camp, requireAngelEligible: true)` 为假），代码 `Angel.cs:288` 直接
   `spawnTimer = 3.0` 再等一轮 ⇒ 你会看到 15→0 之后跳成 3.0。正常。
5. **天使的 `无敌` 会突然消失**：`invincibleTimer` 只在"比第 5 列更靠右"时递减（`Angel.cs:118` 的
   `X > GetMapCellPos((4,0)).X`）；一旦走到那条线左侧，`Angel.cs:123-126` **立刻**清 `invincible`
   ⇒ 无敌行瞬间消失（图鉴备注也是这么写的，只是它说"最左侧 3 列"、代码用的是第 5 列边界，两者略有出入，
   与显示无关）。

---

## 2. 逐僵尸预期值算法（A 批 16 个）

记法：`D` = 屏幕上的秒数（`ToString("0.0")`），`Δ` = 每帧增量。
所有"总时长"一栏，**除两个写死字面量外，全部从运行时实例字段读**（不是配置常量）。

| # | 僵尸 | 屏幕上应出现 | 预期值算法 | 总时长来源 |
|---|---|---|---|---|
|1|毁灭菇僵尸|`爆破 D s`|`D = time − timer`；`time ~ U[25,30]`；`Δtimer = delta*timeScale`；`explode=true` 后**行消失**|`:63 time = GD.RandRange(25.0, 30.0)`|
|2|火爆辣椒僵尸|`自燃 D s`|同上|`:59` 同款 25~30|
|3|火炬辣椒僵尸|`自燃 D s`|同上|`:59` **7~10**（图鉴写 25~30，**以代码为准**）|
|4|三叶草气球僵尸|`吹飞 D s`|`D = timerWaitTime − timerCurrent`（键 `Clover`）；`Run("Clover", 8.0)` ⇒ 恒 **8.0 → 0**|调用点字面量 `8.0`|
|5|三叶草多彩气球僵尸|`吹飞 D s`|同上|同上 `8.0`|
|6|寒冰菇冰车僵尸|`自爆 D s`|`D = time − timer`；`time ~ U[25,30]`|`:87` **25~30**（图鉴写 15，以代码为准）|
|7|玩偶匣僵尸|`自爆 D s`|`D = time − timer`；`time ~ U[9,20]`|`:85` **9~20**|
|8|海妖僵尸|`召唤 D s` / `召唤 就绪`|未召唤：`D = 15.0 − timer`；`spawnNext=true` 时换成状态行 `召唤 就绪`（绿）|**代码字面量 15.0**（`:114`）|
|9|磁场僵尸|`磁力 D s` → `磁力 已开启`|未开：`D = openTime − openTimer`，`openTime ~ U[6,12]`；`open=true` 后**永久**变状态行（绿）|`:26` 6~12|
|10|鲨鱼僵尸|`咀嚼 D s`|`D = chewTime − chewTimer`；`chewTime = 15.0`；仅在 `isChew=true` 时出现|`ZombieSharkDefinition.tres chewTime=15.0`|
|11|天使僵尸|`复活 D s` + `无敌 D s`|`D = spawnTimer`（**本身就是倒计时**，15→0）；`无敌`：`invincible && invincibleTimer>0` 时 `D = invincibleTimer`（8→0）|`复活`**传 0**（不受 ≥5 秒过滤）；`无敌` total=8.0|
|12|催眠师僵尸|`偷取 D s`|`waitGrab=true` 才出现；`D = 10.0 − waitTimer`|**代码字面量 10.0**（`:181`）|
|13|农夫僵尸|`召唤 D s`|`D = spawnTime − spawnTimer`；`spawnTime = 10.0`|实例字段 `:18`|
|14|海盗船长僵尸|`召唤 D s`|`D = spawnTime − spawnTimer`；`spawnTime = 15.0`|实例字段 `:11`|
|15|重生法师僵尸|`治疗 D s`（+ 死后坑洞 `消失 D s`）|`D = shootingTime − shootingTimer`；`shootingTime = 10.0`；死后坑洞 `D = dieDownTime − dieDownTimer` = 30→0|实例字段 `:11` / 坑洞 tres `dieDownTime=30.0`|
|16|巨化法师僵尸|`变大 D s`|`D = shootingTime − shootingTimer`；`shootingTime = 10.0`（图鉴写 12，**以代码为准**）|实例字段 `:15`（场景 `tscn:17` 也是 10.0）|

> 表里第 9 行「磁场僵尸 `open=true` 后**永久**变状态行」是 **A 批 `ZombieMagnetic` 独有**的结论：
> 它的 `open` 一旦置 true，全类没有任何复位点。**不要套到 B 批「电磁拖车僵尸」`ZombieMagnetron`** ——
> 那是循环的（`attackTimer` 累加到 10.0 → `BeginShooting` 里 `:559 attackTimer = 0.0` 重置，见 §3-② 的提示）。
> 同理，表里 4/5 行（`吹飞` 恒 8.0）、10 行（`咀嚼` 恒 15.0）的"恒定总时长"也只对各自那个僵尸成立。

### 2.1 「只显示 ≥5 秒」开关对这些行的影响（`ShortTimerThresholdSeconds = 4.9`，`Entry.cs:609`）

判定用的是 **总时长**（不是剩余）：`total < 4.9` ⇒ 该行**从头到尾都不出现**。

- **A 批 16 个全部不会被滤掉**：总时长最小的是 火炬辣椒 `7~10` / 天使`无敌` `8.0`，都 ≥ 5；
  天使`复活` 传 `total = 0` ⇒ 按约定**不参与过滤**（宁可显示也不误藏）。
- ⚠️ 但 B 批的**塞壬**有 `Heal:1`（1 秒）⇒ 开「只显示 ≥5 秒」后 `治疗 1.0s` 会**整行消失**。
  这是设计行为，报报告里别记成 bug。（塞壬属 b 的范围，这里只提醒验收时别误判。）

---

## 3. 六个代表僵尸的"判对/判错"

### ① 毁灭菇僵尸 —— `爆破`
- **对**：出现一行青蓝色 `爆破 X.Xs`，`X ∈ (0, 30]`；单调递减，约 25~30 游戏内秒后归 0
  → 播 `Explode` 动画 → `explode=true` ⇒ **该行立刻消失**（不是变成 0.0 卡住）→ 爆炸清场。
- **错的样子**：① 显示 `爆破 0.0s` 且一直卡着（说明 `explode` 没读到）；② 数值 > 30（说明读到了别的 `time`）；
  ③ 刚出场就掉数值（说明进场门控没生效——但那是游戏侧，不是 Mod）。

### ② 磁场僵尸 —— `磁力` → `磁力 已开启`（示范段，双段）
- **对**：先是青蓝 `磁力 X.Xs`，`X ∈ (0,12]`；归 0 那一刻**永久**变成绿色 `磁力 已开启`，
  之后**再也不会**回到倒计时（源码无复位）。
- **错的样子**：① 开关反复横跳；② 出现第二行"磁力持续/磁力时间"——**不可能有**，源码里没有这个计时
  （`open=true` 后 `TowerDefenseZombieMagnetic.cs:38` 直接 return，`ChangeProjectileStateComponent` 无时长字段）；
  ③ 掉头盔后行消失（不该消失——只缩吸引范围 5×5→3×3，状态行还在）。
- 🚫 **只对 A 批磁场僵尸成立，别拿 B 批「电磁拖车僵尸」`ZombieMagnetron` 来验这条**：
  电磁拖车用的是**循环**的 `attackTimer`（`TowerDefenseZombieMagnetron.cs:68` 累加到 `>=10.0` → `:349-352`
  → `BeginShooting` 里 `:559 attackTimer = 0.0` 重置）⇒ 它的倒计时会一轮一轮重来，与磁场僵尸
  `open` 永久置位是**两套完全不同的字段**。拿错僵尸验会得出"文档写错了"的假结论。

### ③ 天使僵尸 —— `复活` + `无敌`（示范段，双行）
- **对**：`复活 X.Xs`，`X ∈ (0,15]`，**只在行走时递减**；到 0 时若有可复活尸体 → 播施法动画 + 重置 15；
  无尸体 → 重置 **3.0**（会看到一次短周期）。召唤成功后，被复活的那只天使身上出现绿色 `无敌 X.Xs`（8→0）。
- **错的样子**：① `复活` 值 > 15；② 站着啃植物时 `复活` 还在掉（不该）；
  ③ `无敌` 行在天使走到左侧后仍然存在（不该，代码会立刻清）。

### ④ 三叶草气球僵尸 —— `吹飞`（**走通用通道，不是我的分支**）
- **对**：出现一行 `吹飞 X.Xs`，`X ∈ (0,8]`，**只有一行**；归 0 → 转动叶片吹飞自己（同时吹退敌方）。
- **前提**：「角色计时器其他CD」必须 ON（`CatOn(4)`）。
- **错的样子**：① 出现两行（一 `吹飞` 一 `Clover`）——不可能，`TimerLabel` 已把 `Clover` 映射成 `吹飞`；
  ② 关掉 cat4 后看不到 —— **正常**；
  ③ 出现 `吹飞 2.0s` 那种短的一行 —— 不该有（`_cloverBlowRemaining` 那个 2 秒吹飞位移**故意不显示**）。

### ⑤ 重生法师僵尸 —— `治疗` + 死后坑洞 `消失`
- **对**：`治疗 X.Xs`，`X ∈ (0,10]`，周期性 10→0 反复；每次归 0 应看到 3×3 内僵尸（含自己）+20% 最大生命。
  被打死后：原地出现坑洞，坑洞上是一行 `消失 X.Xs`（30→0，属「障碍物消失」开关），归 0 后法师在坑洞处复活。
- **错的样子**：① `治疗` 只出现一次后不再出现（说明 `shootingTimer` 没归零/状态机没回 Walk）；
  ② 坑洞没有 `消失` 行（检查「障碍物消失」开关）；③ 把"30 秒复活"记成"僵尸侧不可显示"（错，是坑洞通道覆盖）。

### ⑥ 塞壬僵尸 —— `歌唱` / `施法`（**b 的批次，这里只给验收提醒**）
- 走通用通道 ②，键 `Sing:5` / `Cast:12` / `Heal:1`（`SirenTimerComponentDefinition.tres`）⇒ `歌唱 5.0s` / `施法 12.0s` / `治疗 1.0s`。
- ⚠️ **`治疗 1.0s` 在「只显示 ≥5 秒」开启时会整行消失**（total 1.0 < 4.9）——别记成 bug。
- ⚠️ 三行同时活着时受 `MaxTimerLines = 3` 限制，可能只看到 3 行里的一部分。

---

## 4. 记录模板（每只僵尸一行，便于回填报告 §8）

| 僵尸 | 出现？ | 文案 | 首见值 | 是否单调/正确跳变 | 归零后行为 | 截图/日志 | 判定 |
|---|---|---|---|---|---|---|---|
| 毁灭菇 | | 爆破 X.Xs | | | 播 Explode + 行消失 | | |
| 磁场 | | 磁力 X.Xs → 已开启 | | | 永久状态行 | | |
| 天使 | | 复活 X.Xs / 无敌 X.Xs | | | 重置 15 或 3 / 走左侧即清 | | |
| 三叶草气球 | | 吹飞 X.Xs | | | 吹飞自己 | | |
| 重生法师 | | 治疗 X.Xs / 消失 X.Xs | | | +20% 治疗 / 30s 后复活 | | |

**日志辅助**：设置里把 `enableLog` 打开（默认 false）后，`TryCollectInfo` 会打：
- `计时器项[角色名/键]：运行中=… 时长=… 当前值=… 剩余(w-c)=…`（通道 ②）
- `咀嚼[角色名]：isChew=True 时长(chewTime)=… 已嚼(chewTimer)=…`（通道 ②.5）
- `坑洞[名字]：dieDownTimer(已过)=… 配置dieDownTime=…`（坑洞通道）
我这 9 个新增分支**不打日志**（契约 §9 禁止在分支里 `GD.Print`），所以核对靠屏幕数值 + 上面这些既有日志。

---

## 5. 我这侧的静态证据（真机前可先自查，不必等游戏）

```powershell
# 1) 编译
python mods\HealthCooldownLine\_build_serial.py        # 期望 [compile] RC=0 / OK

# 2) DLL 特征串（字节级！不要用 Encoding.Unicode.GetString 整块解码，会假阴性）
python mods\HealthCooldownLine\研究-僵尸CD\_verify_dll_A.py

# 3) 最终包内 DLL 与 bin 产物是否字节一致 + A 批特征串
python mods\HealthCooldownLine\研究-僵尸CD\_verify_pmod_A.py
```
期望输出：第 2 个脚本末行 `FAILED = none`；第 3 个脚本 `字节完全一致 = True` 且 `MISSING = none`。
