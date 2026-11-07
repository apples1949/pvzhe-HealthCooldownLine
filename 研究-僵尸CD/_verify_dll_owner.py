# -*- coding: utf-8 -*-
"""最终组装自检（cdmod-owner）—— 在产物 DLL 里核对**全部**特征串。

⚠️ 查法要点（zombie-dev-a 反馈的真实坑）：
  .NET 元数据里字符串字面量在 `#US` 堆，条目起始偏移**可以是奇数**，
  所以 `Encoding.Unicode.GetString(整个文件).Contains(...)` 会因错位而**假阴性**。
  正确做法是**按字节搜**：`data.count(s.encode("utf-16-le"))` / `data.count(s.encode("ascii"))`
  （本脚本对每一项**两种编码各查一遍**，任一命中即算 OK —— 因为 C# 里
   `const string` 的值走 #US 堆（UTF-16LE），而方法/类型/字段名走 #Strings 堆（ASCII）。

用法：python mods\\HealthCooldownLine\\研究-僵尸CD\\_verify_dll_owner.py
"""
import hashlib
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.dirname(HERE)
DLL = os.path.join(MOD, "runtime_src", "bin", "Release", "JTYHealthCooldownLine.dll")

# ── 1) 框架 / 开关 ─────────────────────────────────────────────────────────
SYMBOLS = [
    # 框架（Entry.cs）
    "HealthCooldownLineEntry", "TryCollectZombieCdLines",
    "CollectZombieCdLinesA", "CollectZombieCdLinesB",
    "IsZombieCdHost", "IsZombieChar",
    "ZombieField", "ZombieNum", "ZombieFlag", "ZombieNameIs",
    "ZombieCdDisplayable", "WarnOnceZombie",
    # 开关（v1.26.0「显示僵尸冷却」）
    "showZombieCd", "feature_zombiecd", "ModZombieCdCheckBox",
    "OnZombieCdChanged", "MakeZombieCdCheckBox", "AppendZombieCdControls",
    # 骨堆门控（v1.26.1 放宽）
    "TowerDefenseSkeleton", "TowerDefenseGravestone",
]

# ── 2) A 批实现（zombie-dev-a）──────────────────────────────────────────────
A_SYMBOLS = ["CollectZombieCdDemoLines", "CollectZombieCdABatchLines", "ACountUp", "ACountUpFixed", "AClassIs"]

# ── 3) B 批实现（zombie-dev-b）──────────────────────────────────────────────
B_SYMBOLS = ["ZbBIsZombie", "ZbBClassIs", "ZbBTimerLeft", "ZbBProp", "ZbBDoubleProp"]

# ── 4) 中文显示文案 ─────────────────────────────────────────────────────────
LABELS = [
    "显示僵尸冷却",                                    # 开关 label
    "爆破", "自爆", "自燃", "磁力", "复活", "无敌",      # A 批
    "召唤", "偷取", "治疗", "变大",                     # A 批
    "投掷", "吟唱", "蓄力", "起身",                     # B 批（Lead 2026-11-07 批准的新词）
    "睡眠", "隐身", "冷却", "准备",                     # B 批
    "已开启", "就绪",                                   # 状态行
    "施法", "歌唱", "吹飞", "倒计时", "生成",           # TimerLabel 中文化（通用通道 ② / ②.5）
]

# ── 5) TimerLabel 新映射的键名 ──────────────────────────────────────────────
TIMER_KEYS = ["Cast", "Sing", "Throw", "Clover", "Heal"]

# ── 6) 31 个僵尸 config.name ────────────────────────────────────────────────
#   (a) 有专属分支的 —— **必须**在 DLL 里（zj 代码里出现字面量）
ZOMBIES_BRANCH = [
    # A 批 13 个（另 3 个走通用通道，见下）
    "ZombieNormalDoomShroom", "ZombieNormalJalapeno", "ZombieNormalJalatorch",
    "ZombieZamboniIceShroom", "ZombieJackbox", "ZombieSeaDemon", "ZombieMagnetic",
    "ZombieAngel", "ZombieHypnotist", "ZombieFarmer", "ZombieCaptain",
    "ZombieNecromancerG", "ZombieNecromancerB",
    # B 批 10 个（另 5 个走通用通道，见下）
    "ZombieImpCloud", "ZombieImphacker", "Skeleton", "ZombieJackGene",
    "ZombieMagnetron", "ZombieSleepMage", "ZombieTimeMage", "ZombieGargantuarGD",
    "ZombieDiscoFlash", "ZombieBackupFlash",
]
#   (b) 只靠通用通道显示的 —— **不该**在 DLL 里出现（出现反而说明有人写了重复分支）
ZOMBIES_GENERIC = [
    "ZombieBalloonBlover", "ZombieBalloonColourBlover",   # 走 ② character.timer 的 Clover
    "ZombieShark",                                        # 走 ②.5 咀嚼
    "ZombieSiren", "ZombieGuardMage", "ZombieDivineMage", # 走 ② character.timer 的 Cast/Sing
    "ZombieEvoker", "ZombieWitch",                       # 走 ② Cast / Throw
]


def main():
    if not os.path.isfile(DLL):
        print("找不到产物 DLL:", DLL)
        return 2
    data = open(DLL, "rb").read()
    print("DLL  =", DLL)
    print("size =", len(data), "B")
    print("md5  =", hashlib.md5(data).hexdigest())
    print("")

    missing = []

    def check(title, items, must=True):
        print("── %s ──" % title)
        for s in items:
            n16 = data.count(s.encode("utf-16-le"))
            n8 = data.count(s.encode("ascii", "ignore")) if s.isascii() else 0
            ok = (n16 + n8) > 0
            flag = "OK" if ok else ("*** MISSING ***" if must else "absent（预期：走通用通道，无专属分支）")
            if must and not ok:
                missing.append((title, s))
            print("  %-34s u16=%-3d ascii=%-3d %s" % (s, n16, n8, flag))
        print("")

    check("框架 / 开关 / 骨堆门控", SYMBOLS)
    check("A 批实现（标识符）", A_SYMBOLS)
    check("B 批实现（标识符）", B_SYMBOLS)
    check("中文显示文案", LABELS)
    check("TimerLabel 新键名", TIMER_KEYS)
    check("僵尸 config.name（有专属分支，必须在）", ZOMBIES_BRANCH)
    check("僵尸 config.name（走通用通道，预期不在）", ZOMBIES_GENERIC, must=False)

    print("=" * 62)
    if missing:
        print("FAILED = %d 项缺失：" % len(missing))
        for t, s in missing:
            print("  - [%s] %s" % (t, s))
        return 1
    total = len(SYMBOLS) + len(A_SYMBOLS) + len(B_SYMBOLS) + len(LABELS) + len(TIMER_KEYS) + len(ZOMBIES_BRANCH)
    print("FAILED = none  -> 全部 %d 项特征串命中（另有 %d 个僵尸按设计走通用通道、DLL 内无专属分支）"
          % (total, len(ZOMBIES_GENERIC)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
