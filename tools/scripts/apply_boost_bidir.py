# -*- coding: utf-8 -*-
"""v1.10.3：倍率检测改双向（加速/减速都算变化）+ 基线对比诊断。每条断言恰好命中一次。"""
import sys

P = r'C:\Users\txgcs\WorkBuddy\zjb\mod\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs'
s = open(P, encoding='utf-8').read()

subs = [
    # spd/atk 默认 1.0（表示"无变化"），便于双向判断
    ('\t\t\tdouble spd = 0.0;', '\t\t\tdouble spd = 1.0;', 1),
    ('\t\t\tdouble atk = 0.0;', '\t\t\tdouble atk = 1.0;', 1),
    # buff 通道：任一非 1 值都采纳（原来只取"更大的"）
    ('if (v > spd)', 'if (System.Math.Abs(v - 1.0) > 0.0001)', 1),
    # 攻击组件通道：只要有基准与当前就求比值（不论快慢）
    ('if (b0 > 0 && c0 > 0 && c0 < b0 - 0.0001)', 'if (b0 > 0 && c0 > 0)', 1),
    # 发射通道：timeScale 任一非 1 值都采纳；比值双向
    ('if (ts > spd)', 'if (System.Math.Abs(ts - 1.0) > 0.0001)', 1),
    ('if (b1 > 0 && c1 > 0 && c1 < b1 - 0.0001 && (b1 / c1) > atk)',
     'if (b1 > 0 && c1 > 0)', 1),
    # "倍率["诊断：任一偏离 1 就打
    ('if ((spd > 1.0001 || atk > 1.0001) && _buffReported.Add(charName ?? "?"))',
     'if ((System.Math.Abs(spd - 1.0) > 0.001 || System.Math.Abs(atk - 1.0) > 0.001)'
     ' && _buffReported.Add(charName ?? "?"))', 1),
]

# 显示块：双向（+ 绿 / - 灰）
old_show = '''				if (spd > 1.0001)
				{
					lines.Add(("加速 +" + ((spd - 1.0) * 100.0).ToString("0") + "%", ReadyColor));
				}
				if (atk > 1.0001 && lines.Count < MaxTotalLines)
				{
					lines.Add(("攻速 +" + ((atk - 1.0) * 100.0).ToString("0") + "%", ReadyColor));
				}'''
new_show = '''				if (spd > 1.0001)
				{
					lines.Add(("加速 +" + ((spd - 1.0) * 100.0).ToString("0") + "%", ReadyColor));
				}
				else if (spd < 0.9999)
				{
					lines.Add(("加速 -" + ((1.0 - spd) * 100.0).ToString("0") + "%", DisabledColor));
				}
				if (atk > 1.0001 && lines.Count < MaxTotalLines)
				{
					lines.Add(("攻速 +" + ((atk - 1.0) * 100.0).ToString("0") + "%", ReadyColor));
				}
				else if (atk < 0.9999 && lines.Count < MaxTotalLines)
				{
					lines.Add(("攻速 -" + ((1.0 - atk) * 100.0).ToString("0") + "%", DisabledColor));
				}'''

for a, b, n in subs:
    c = s.count(a)
    if c != n:
        print('FAIL 命中数不符: %r 实际=%d 期望=%d' % (a[:60], c, n))
        sys.exit(1)
    s = s.replace(a, b)

if s.count(old_show) != 1:
    print('FAIL 显示块命中数=%d' % s.count(old_show))
    sys.exit(1)
s = s.replace(old_show, new_show)

# 基线/变化诊断：插在"发射加成"诊断块之后
anchor = '''					if (boosted && _fireBoostedReported.Add(charName ?? "?"))
					{'''
if s.count(anchor) != 1:
    print('FAIL 发射加成锚点命中数=%d' % s.count(anchor))
    sys.exit(1)
probe = '''					// 变化诊断（v1.10.3）：记录每角色首次基线与当前值，偏离即打一条
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
'''
s = s.replace(anchor, probe + anchor, 1)

open(P, 'w', encoding='utf-8').write(s)
print('OK 双向倍率 + 变化诊断 已写入，长度=%d' % len(s))
