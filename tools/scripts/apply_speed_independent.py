# -*- coding: utf-8 -*-
"""v1.10.6：把"速度倍率"显示从"基线对比"里移出（独立判断，buff 在=显示）。
原逻辑：速度显示被包在 `if (_fireBaseline.TryGetValue(...))` 里 ⇒ 非发射型角色（无基线）不显示。"""
import sys

P = r'C:\Users\txgcs\WorkBuddy\zjb\mod\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs'
s = open(P, encoding='utf-8').read()

# 1) 剪切"速度：timeScale 相对基线"整块（到下一条注释为止）
a_mark = '\t\t\t\t\t// 速度：timeScale 相对基线'
b_mark = '\t\t\t\t\t// 攻速：基线间隔 / 当前间隔'
if s.count(a_mark) != 1 or s.count(b_mark) != 1:
    print('FAIL 速度块标记 命中数 a=%d b=%d' % (s.count(a_mark), s.count(b_mark)))
    sys.exit(1)
a = s.index(a_mark)
b = s.index(b_mark, a)
s = s[:a] + s[b:]

# 2) 新速度块（绝对倍率，不依赖基线）插到 "double[] bv2;" 之前
anchor = '\t\t\t\tdouble[] bv2;'
if s.count(anchor) != 1:
    print('FAIL bv2 锚点 命中数=%d' % s.count(anchor))
    sys.exit(1)
new_speed = (
    '\t\t\t\t// (1) 速度倍率：**绝对倍率**（来自加速 buff 的 timeScaleValue）——不依赖基线，\n'
    '\t\t\t\t//     这样"非发射型但有加速 buff 的角色"也能显示（v1.10.6）。buff 在=加成中。\n'
    '\t\t\t\tif (spd > 1.0001)\n'
    '\t\t\t\t{\n'
    '\t\t\t\t\tlines.Add(("加速 +" + ((spd - 1.0) * 100.0).ToString("0") + "%", ReadyColor));\n'
    '\t\t\t\t}\n'
    '\t\t\t\telse if (spd < 0.9999)\n'
    '\t\t\t\t{\n'
    '\t\t\t\t\tlines.Add(("加速 -" + ((1.0 - spd) * 100.0).ToString("0") + "%", DisabledColor));\n'
    '\t\t\t\t}\n'
    '\t\t\t\t// (2) 弹数/攻速：以种下基线对比（天生差异不算）\n'
)
i = s.index(anchor)
s = s[:i] + new_speed + s[i:]

open(P, 'w', encoding='utf-8').write(s)
print('OK 速度显示已独立于基线，长度=%d' % len(s))
