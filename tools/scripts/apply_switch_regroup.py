# -*- coding: utf-8 -*-
"""v1.12.0：按用户方案重映射分组开关。
新分组：0装填与核能 1命名计时器 2坑洞消失 3选卡栏种植CD 4成长与充能 5产出倒计时 6战斗辅助 7等级与加速
硬编码关闭：疯狂海草点击冷却（EnableKelpClick）、周期区域事件（EnablePeriodEvent）。"""
import sys

P = r'C:\Users\txgcs\WorkBuddy\zjb\mod\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs'
s = open(P, encoding='utf-8').read()


def rep(old, new, n=1):
    global s
    c = s.count(old)
    if c != n:
        print('FAIL %r count=%d expect=%d' % (old[:70], c, n))
        sys.exit(1)
    s = s.replace(old, new)


# ---- A) 条件替换（成长/产出/战斗辅助重编号）----
rep('if (CatOn(2) && growing', 'if (CatOn(4) && growing')
rep('if (CatOn(2) && interval > 0 && remC', 'if (CatOn(4) && interval > 0 && remC')
rep('if (CatOn(2) && interval2 > 0 && rem2', 'if (CatOn(4) && interval2 > 0 && rem2')
rep('if (CatOn(4) && st > 0 && remP', 'if (EnablePeriodEvent && st > 0 && remP')      # 周期事件 → 硬关
rep('if (CatOn(3) && interval > 0 && remS', 'if (CatOn(5) && interval > 0 && remS')    # 产出
rep('if (CatOn(4) && ct > 0', 'if (CatOn(6) && ct > 0')                                # 咀嚼
rep('if (CatOn(4) && bt > 0 && remM', 'if (CatOn(6) && bt > 0 && remM')                # 消化
rep('if (CatOn(4) && rem != null', 'if (CatOn(6) && rem != null')                      # 土豆雷
rep('if (CatOn(4) && bowl != null', 'if (CatOn(6) && bowl != null')                    # 篮球
rep('if (CatOn(4) && cat != null', 'if (CatOn(6) && cat != null')                      # 投石车
rep('if (CatOn(4) && gbRun', 'if (CatOn(6) && gbRun')                                  # 啃碑

# ---- B) 等级与加速：全部 CatOn(5) → CatOn(7) ----
n5 = s.count('CatOn(5)')
if n5 < 1:
    print('FAIL CatOn(5) 命中 0')
    sys.exit(1)
s = s.replace('CatOn(5)', 'CatOn(7)')

# ---- C) 选卡栏种植 CD：CatOn(1) → CatOn(3) ----
rep('!_enabled || !CatOn(1)', '!_enabled || !CatOn(3)')

# ---- D) 坑洞消失：位置法（用"消失"锚定其上方的 CatOn(1)）----
i = s.find('lines.Add(("消失 ')
if i < 0:
    print('FAIL 坑洞锚')
    sys.exit(1)
j = s.rfind('if (CatOn(1) && lines.Count < MaxTotalLines)', 0, i)
if j < 0:
    print('FAIL 坑洞 CatOn(1)')
    sys.exit(1)
s = s[:j] + 'if (CatOn(2) && lines.Count < MaxTotalLines)' + s[j + len('if (CatOn(1) && lines.Count < MaxTotalLines)'):]

# ---- E) 疯狂海草点击冷却：位置法（用"可点击"锚定）→ 硬编码关闭 ----
i = s.find('lines.Add(("可点击"')
if i < 0:
    print('FAIL 海草锚')
    sys.exit(1)
j = s.rfind('if (CatOn(1) && lines.Count < MaxTotalLines)', 0, i)
if j < 0:
    print('FAIL 海草 CatOn(1)')
    sys.exit(1)
s = s[:j] + 'if (EnableKelpClick && lines.Count < MaxTotalLines)' + s[j + len('if (CatOn(1) && lines.Count < MaxTotalLines)'):]

open(P, 'w', encoding='utf-8').write(s)
print('OK 分支重映射完成；剩余 CatOn(1)（命名计时器）=%d' % s.count('CatOn(1)'))
