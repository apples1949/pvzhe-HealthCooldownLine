# -*- coding: utf-8 -*-
"""给 HealthCooldownLineEntry.cs 的各显示分支插入分类开关条件（CatOn(cat) && ...）。
每条替换都断言"恰好命中一次"，未命中立即报错（不静默）。"""
import re
import sys

P = r'C:\Users\txgcs\WorkBuddy\zjb\mod\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs'
s = open(P, encoding='utf-8').read()
orig = s

# (原串, 新串, 期望命中数)
subs = [
    # ⓪ 核弹磁力菇核能充能 → 装填(0)
    ('if (armMax > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(0) && armMax > 0 && lines.Count < MaxTotalLines)', 1),
    # ① 加农炮装填 → 装填(0)
    ('if (cannon != null && lines.Count < MaxTotalLines)',
     'if (CatOn(0) && cannon != null && lines.Count < MaxTotalLines)', 1),
    # ② 命名计时器 → 计时器(1)
    ('for (int i = 0; i < running.Count',
     'for (int i = 0; CatOn(1) && i < running.Count', 1),
    # ②.5 咀嚼 → 战斗辅助(4)
    ('if (ct > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && ct > 0 && lines.Count < MaxTotalLines)', 1),
    # ②.6 长大 → 成长(2)
    ('if (growing && remG > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(2) && growing && remG > 0 && lines.Count < MaxTotalLines)', 1),
    # ②.7 周期区域事件 → 战斗辅助(4)
    ('if (st > 0 && remP > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && st > 0 && remP > 0 && lines.Count < MaxTotalLines)', 1),
    # ②.8 磁力消化 → 战斗辅助(4)
    ('if (bt > 0 && remM > 0 && busy && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && bt > 0 && remM > 0 && busy && lines.Count < MaxTotalLines)', 1),
    # ②.10a 蓄能（SunShroomCharge 系）→ 成长(2)
    ('if (interval > 0 && remC > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(2) && interval > 0 && remC > 0 && lines.Count < MaxTotalLines)', 1),
    # ②.10b 蓄能（EnergyBean 系）→ 成长(2)
    ('if (interval2 > 0 && rem2 > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(2) && interval2 > 0 && rem2 > 0 && lines.Count < MaxTotalLines)', 1),
    # ②.9 产出 → 产出(3)
    ('if (interval > 0 && remS > 0 && lines.Count < MaxTotalLines)',
     'if (CatOn(3) && interval > 0 && remS > 0 && lines.Count < MaxTotalLines)', 1),
    # ③ 土豆雷 → 战斗辅助(4)
    ('if (rem != null && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && rem != null && lines.Count < MaxTotalLines)', 1),
    # ④ 篮球 → 战斗辅助(4)
    ('if (bowl != null && _fBowlHit != null && _fBowlMax != null && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && bowl != null && _fBowlHit != null && _fBowlMax != null && lines.Count < MaxTotalLines)', 1),
    # ⑤ 投石车 → 战斗辅助(4)
    ('if (cat != null && _fCatCur != null && _fCatMax != null && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && cat != null && _fCatCur != null && _fCatMax != null && lines.Count < MaxTotalLines)', 1),
    # ⑥ 缠绕抓取 → 战斗辅助(4)
    ('if (tk != null && _fTkCur != null && _fTkMax != null && lines.Count < MaxTotalLines)',
     'if (CatOn(4) && tk != null && _fTkCur != null && _fTkMax != null && lines.Count < MaxTotalLines)', 1),
    # ⑦ 啃碑 → 战斗辅助(4)
    ('if (gbRun && dur > 0 && remainG > 0 && remainG <= dur',
     'if (CatOn(4) && gbRun && dur > 0 && remainG > 0 && remainG <= dur', 1),
]

for a, b, n in subs:
    c = s.count(a)
    if c != n:
        print('FAIL 命中数不符: %r 实际=%d 期望=%d' % (a[:60], c, n))
        sys.exit(1)
    s = s.replace(a, b)

# 特殊：两处相同的 if (lines.Count < MaxTotalLines) → 坑洞(计时器1) / 疯狂海草点击(计时器1)
pat = 'if (lines.Count < MaxTotalLines)'
idxs = [m.start() for m in re.finditer(re.escape(pat), s)]
if len(idxs) != 2:
    print('FAIL 两处 lines.Count 条件命中数=%d（期望2）' % len(idxs))
    sys.exit(1)
for pos in reversed(idxs):
    s = s[:pos] + 'if (CatOn(1) && lines.Count < MaxTotalLines)' + s[pos + len(pat):]

open(P, 'w', encoding='utf-8').write(s)
print('OK 全部替换完成，文件长度 %d -> %d' % (len(orig), len(s)))
