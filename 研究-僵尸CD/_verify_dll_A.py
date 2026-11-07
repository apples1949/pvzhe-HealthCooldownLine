# -*- coding: utf-8 -*-
"""DLL 特征串核对（**字节级**）。

⚠️ 教训（2026-11-07，zombie-dev-a 实测）：
   `Encoding.Unicode.GetString(整个文件字节)` / python 的 `b.decode('utf-16-le')`
   **会假阴性**。原因：.NET 元数据 `#US` 堆里每个用户字符串是「压缩长度 + UTF-16LE 字节」，
   条目起始偏移可以是**奇数**；整块按 utf-16-le 解码时这些串就错位成乱码。
   正确做法：直接 `bytes.count(literal.encode('utf-16-le'))`（字节对齐搜索）。
"""
import io
import hashlib

DLL = r'mods\HealthCooldownLine\runtime_src\bin\Release\JTYHealthCooldownLine.dll'
b = open(DLL, 'rb').read()

# #Strings 堆（类型/方法/字段名）= UTF-8；#US 堆（字符串字面量）= UTF-16LE
CHINESE = ['爆破', '自燃', '自爆', '磁力', '复活', '无敌', '召唤', '偷取', '治疗', '变大',
           '就绪', '已开启', '装填', '咀嚼']
ASCII_IDENT = ['CollectZombieCdLinesA', 'CollectZombieCdLinesB', 'CollectZombieCdABatchLines',
               'CollectZombieCdDemoLines', 'AClassIs', 'ACountUp', 'ACountUpFixed',
               'ZombieField', 'ZombieNum', 'ZombieFlag', 'ZombieNameIs', 'ZombieCdDisplayable',
               'TryCollectZombieCdLines', 'showZombieCd']
# 反射用到的字段名（这些字符串会同时出现在 #US 里，因为 ZombieField(t,"xxx") 是字面量）
FIELD_LITERALS = ['openTimer', 'openTime', 'spawnNext', 'waitGrab', 'spawnTimer', 'spawnTime',
                  'shootingTimer', 'shootingTime', 'invincibleTimer', 'explode', 'timer', 'time']
# 类键字面量（AClassIs 的实参）
CLASS_KEYS = ['ZombieNormalJalatorch', 'ZombieZamboniIceShroom', 'ZombieJackbox',
              'ZombieSeaDemon', 'ZombieHypnotist', 'ZombieFarmer', 'ZombieCaptain',
              'ZombieNecromancerG', 'ZombieNecromancerB']

fail = []

def chk(title, items, enc):
    print('=== %s（%s 字节搜索）===' % (title, enc))
    for s in items:
        n = b.count(s.encode(enc))
        ok = n > 0
        print('  %-30s %s  count=%d' % (s, 'OK ' if ok else 'MISS', n))
        if not ok:
            fail.append((title, s))

chk('中文标签', CHINESE, 'utf-16-le')
chk('方法/类型名', ASCII_IDENT, 'utf-8')
chk('反射字段名字面量', FIELD_LITERALS, 'utf-16-le')
chk('僵尸类键字面量', CLASS_KEYS, 'utf-16-le')

print()
print('DLL =', DLL)
print('size =', len(b))
print('md5  =', hashlib.md5(b).hexdigest())
print('FAILED =', fail if fail else 'none  -> all feature strings hit')
