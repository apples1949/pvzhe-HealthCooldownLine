# -*- coding: utf-8 -*-
"""把 HealthCooldownLine 的源码/产物/文档同步到本地 git 仓库（Desktop\\git\\pvzhe-HealthCooldownLine）。

规则（与 mods/_sync_repos.py 口径一致）：
  · 只复制"源码 + 打包产物 + 文档 + 构建脚本 + 工具源码"；
  · 排除 *.log / bin / obj / __pycache__ / .cache（构建中间产物可再生成）；
  · `Runtime/ModAssembly.dll` 与 `dist/*.pmod` 是交付物，必须带；
  · 只覆盖、不删除仓库里已有的其他文件；内容相同则跳过（幂等，重跑 = 写 0）。
"""
import hashlib
import os
import shutil
import sys

GIT = os.path.join(os.environ["USERPROFILE"], "Desktop", "git")
# __file__ = <工作区>\mods\HealthCooldownLine\tools\sync_to_repo.py ⇒ 上溯 4 级到工作区
WORKSPACE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
SRC = os.path.join(WORKSPACE, "mods", "HealthCooldownLine")
DST = os.path.join(GIT, "pvzhe-HealthCooldownLine")

SKIP_DIRS = {"bin", "obj", ".cache", "__pycache__", ".vs", ".git"}
SKIP_EXT = {".log", ".out", ".err", ".exit", ".pdb", ".cache"}


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    if not os.path.isdir(SRC):
        sys.exit("源目录不存在: " + SRC)
    os.makedirs(DST, exist_ok=True)
    copied, same, skipped = [], 0, []
    for root, dirs, files in os.walk(SRC):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
        rel = os.path.relpath(root, SRC)
        out_root = DST if rel == "." else os.path.join(DST, rel)
        os.makedirs(out_root, exist_ok=True)
        for name in sorted(files):
            if os.path.splitext(name)[1].lower() in SKIP_EXT:
                skipped.append(os.path.join(rel, name) if rel != "." else name)
                continue
            s = os.path.join(root, name)
            d = os.path.join(out_root, name)
            relfile = name if rel == "." else os.path.join(rel, name)
            if os.path.exists(d):
                try:
                    if os.path.getsize(d) == os.path.getsize(s) and md5(d) == md5(s):
                        same += 1
                        continue
                except OSError:
                    pass
            shutil.copyfile(s, d)
            copied.append(relfile)

    print("同步到:", DST)
    print("  新写/覆盖: %d 个" % len(copied))
    for f in copied:
        print("    + " + f)
    print("  内容相同跳过: %d 个" % same)
    print("  按规则排除(*.log 等): %d 个" % len(skipped))
    return 0


if __name__ == "__main__":
    sys.exit(main())
