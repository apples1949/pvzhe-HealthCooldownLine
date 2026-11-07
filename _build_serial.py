# -*- coding: utf-8 -*-
"""CD Mod 多人并行编译的**串行化**包装（本文件由 Lead 提供，勿改）。

背景：`build_and_install.py` 开头会 `taskkill /F /IM dotnet.exe`，两个 teammate
同时编译同一个工程会互相杀掉对方的 dotnet，并抢 obj/bin 文件锁。

本包装：
  1. 用 `runtime_src/.build.lock` 做跨进程互斥（O_EXCL 创建，超时重试）；
  2. 持锁期间**不再** taskkill，直接调用现有 `build_and_install.py`；
  3. 退出时删锁（异常也删）。

用法（在工程根目录）：
    python mods\\HealthCooldownLine\\_build_serial.py            # 只编译
    python mods\\HealthCooldownLine\\_build_serial.py --install  # 编译+打包+装机
"""
import os
import subprocess
import sys
import time

BASE = os.path.dirname(os.path.abspath(__file__))
LOCK = os.path.join(BASE, "runtime_src", ".build.lock")
TARGET = os.path.join(BASE, "build_and_install.py")
WAIT_MAX = 900          # 秒
STALE = 1800            # 锁超过 30 分钟视为残留


def acquire():
    t0 = time.time()
    while True:
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ("pid=%d time=%s" % (os.getpid(), time.ctime())).encode("utf-8"))
            os.close(fd)
            return True
        except FileExistsError:
            try:
                age = time.time() - os.path.getmtime(LOCK)
            except OSError:
                age = 0
            if age > STALE:
                try:
                    os.remove(LOCK)
                    print("[serial] 清理残留锁（%.0f 秒）" % age)
                except OSError:
                    pass
                continue
            if time.time() - t0 > WAIT_MAX:
                print("[serial] 等锁超时（%.0f 秒），放弃" % (time.time() - t0))
                return False
            print("[serial] 另一个编译在跑，等待 5 秒…")
            time.sleep(5)


def release():
    try:
        os.remove(LOCK)
    except OSError:
        pass


def main():
    if not acquire():
        return 3
    try:
        # 持锁期间保证没有别的编译；只清一次真·残留 dotnet（不杀自己这次）
        p = subprocess.run([sys.executable, TARGET] + sys.argv[1:], cwd=BASE)
        return p.returncode
    finally:
        release()


if __name__ == "__main__":
    sys.exit(main())
