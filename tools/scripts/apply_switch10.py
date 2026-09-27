# -*- coding: utf-8 -*-
"""v1.13.0：开关扩到 10 类（新增：障碍物消失[原坑洞扩展]、障碍物血量、生成计时、角色计时器其他CD）。
旧→新编号：装填0→0 / 命名计时1→拆 / 坑洞2→1 / 卡片3→3 / 成长4→6 / 产出5→7 / 战斗6→8 / 等级7→9。
新：1障碍物消失 2障碍物血量 3卡片CD 4生成计时 5其他CD 6成长 7产出 8战斗辅助 9等级加速。"""
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


# 降序替换（先迁走大编号，避免被后续误伤）
print('CatOn 计数:', {i: s.count('CatOn(%d)' % i) for i in range(10)})
s = s.replace('CatOn(7)', 'CatOn(9)')   # 等级与加速 7→9
s = s.replace('CatOn(6)', 'CatOn(8)')   # 战斗辅助 6→8
s = s.replace('CatOn(5)', 'CatOn(7)')   # 产出 5→7
s = s.replace('CatOn(4)', 'CatOn(6)')   # 成长 4→6
# 坑洞 2→1（障碍物消失）——应恰好 1 处
rep('CatOn(2)', 'CatOn(1)')

open(P, 'w', encoding='utf-8').write(s)
print('OK 重映射完成；CatOn 计数:', {i: s.count('CatOn(%d)' % i) for i in range(10)})
