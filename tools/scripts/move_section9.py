# -*- coding: utf-8 -*-
"""把误插进 FindFieldAlong 的 ⑨ 段（速度/攻速倍率）剪切出来，移到 TryCollectInfo 内（⑦⑧ 段附近）。"""
import sys

P = r'C:\Users\txgcs\WorkBuddy\zjb\mod\HealthCooldownLine\runtime_src\HealthCooldownLineEntry.cs'
s = open(P, encoding='utf-8').read()

startmark = '\t\t// ⑨ 速度倍率 + 攻速倍率（需求 5'
if s.count(startmark) != 1:
    print('FAIL ⑨ 段起点命中数=%d' % s.count(startmark)); sys.exit(1)
start = s.index(startmark)

# ⑨ 段的收尾 = 从 start 起第一个 2-tab 的 catch（段内嵌套 catch 都是 3-tab）
endmark = '\n\t\tcatch { }'
idx = s.find(endmark, start)
if idx < 0:
    print('FAIL 找不到 ⑨ 段收尾'); sys.exit(1)

block = s[start:idx]                    # ⑨ 段正文（结尾是 try 的 '\t\t}'）
rest = s[idx + len(endmark):]           # 其后内容（以 '\n\t}' 方法收尾开头）
s = s[:start] + rest                    # 剪切（return null; 后直接恢复方法收尾）
block_full = block + '\n\t\tcatch { }'  # 补回 catch

# 粘到 ⑦⑧ 段之间（⑧ 段注释行之前）
anchor = '\t\t// ⑧ 疯狂海草（InsaniKelp）'
if s.count(anchor) != 1:
    print('FAIL ⑧ 段锚点命中数=%d' % s.count(anchor)); sys.exit(1)
s = s.replace(anchor, block_full + '\n\n' + anchor, 1)

open(P, 'w', encoding='utf-8').write(s)
print('OK 已剪切并移动 ⑨ 段，文件长度=%d' % len(s))
