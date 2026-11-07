# -*- coding: utf-8 -*-
"""独立复核：**最终 pmod 包内**的 DLL 是否就是 bin 产物，且 A 批特征串齐全。

业主（cdmod-owner）已跑 verify_pmod/verify_build，这里做一次**只读的独立验证**：
不信任中间产物，直接从 dist/*.pmod 里取 Runtime/ModAssembly.dll 自己算 md5 + 查特征串。
"""
import hashlib
import io
import os
import zipfile

POMD = r'mods\HealthCooldownLine\dist\HealthCooldownLine.pmod'
BIN = r'mods\HealthCooldownLine\runtime_src\bin\Release\JTYHealthCooldownLine.dll'

CHINESE = ['爆破', '自燃', '自爆', '磁力', '复活', '无敌', '召唤', '偷取', '治疗', '变大',
           '就绪', '已开启', '吹飞']          # 吹飞 = TimerLabel 里 Clover 的映射
IDENT = [b'CollectZombieCdABatchLines', b'AClassIs', b'ACountUp', b'ACountUpFixed',
         b'CollectZombieCdLinesA', b'ZombieField']
CLASS_KEYS = ['ZombieNormalJalatorch', 'ZombieZamboniIceShroom', 'ZombieJackbox',
              'ZombieSeaDemon', 'ZombieHypnotist', 'ZombieFarmer', 'ZombieCaptain',
              'ZombieNecromancerG', 'ZombieNecromancerB']

with zipfile.ZipFile(POMD) as z:
    names = z.namelist()
    print('pmod entries (%d):' % len(names))
    for n in sorted(names):
        print('   ', n)
    cand = [n for n in names if n.lower().endswith('modassembly.dll')]
    assert cand, 'package 内找不到 ModAssembly.dll: %s' % names
    blob = z.read(cand[0])
    print()
    print('package DLL =', cand[0], 'size =', len(blob),
          'md5 =', hashlib.md5(blob).hexdigest())

binblob = open(BIN, 'rb').read()
print('bin     DLL size =', len(binblob), 'md5 =', hashlib.md5(binblob).hexdigest())
print('字节完全一致 =', blob == binblob)

print()
miss = []
for s in CHINESE:
    n = blob.count(s.encode('utf-16-le'))
    print('  %-8s #US  count=%d' % (s, n))
    if n == 0:
        miss.append(s)
for s in IDENT:
    n = blob.count(s)
    print('  %-30s #Strings count=%d' % (s.decode(), n))
    if n == 0:
        miss.append(s.decode())
for s in CLASS_KEYS:
    n = blob.count(s.encode('utf-16-le'))
    print('  %-30s 类键 count=%d' % (s, n))
    if n == 0:
        miss.append(s)

print()
print('MISSING =', miss if miss else 'none -> 包内 DLL 的 A 批特征串齐全')
