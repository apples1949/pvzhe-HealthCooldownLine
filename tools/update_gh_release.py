# -*- coding: utf-8 -*-
"""更新 pvzhe-HealthCooldownLine 的 GitHub 发行版（tag 固定 `upload`）。

口径与 mods/_github_release.py / 既有《更新GitHub发布.ps1》一致：
  1. GET /releases/tags/upload → 有就复用（PATCH 名字+正文），没有就创建
  2. 有同名附件 → 先 DELETE 再传（否则 GitHub 会自动加 `-1` 后缀）
  3. POST https://uploads.github.com/.../releases/{id}/assets?name=HealthCooldownLine.pmod
  4. 回读 assets[] 的 size 与本地文件比对

token 只经环境变量 GH_TOKEN 传入，不落盘。
用法：
    set GH_TOKEN=ghp_xxx && python mods/HealthCooldownLine/tools/update_gh_release.py
"""
import json
import os
import sys
import urllib.error
import urllib.request

OWNER = "apples1949"
REPO = "pvzhe-HealthCooldownLine"
PMOD_NAME = "HealthCooldownLine.pmod"
# ★★ 本仓库的发行标签是 **`releases`**，不是通用的 `upload`！
#   （mods/_github_release.py 里默认 `upload`，但那是给 WaveInfoBar/SpawnRate 用的；
#     HealthCooldownLine 历史发行一直用 `releases` —— 蓝奏云与发布页文案里引用的下载直链也是它。
#     2026-10-28 曾误用 `upload` 建出重复发行版，已删除并回退到 `releases`。）
TAG = "releases"

# __file__ = <工作区>\mods\HealthCooldownLine\tools\update_gh_release.py
BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
PROJ = os.path.join(BASE, "mods", "HealthCooldownLine")

BODY = """\
血条·装填倒计时 —— 在僵尸血条下方追加一行，显示该角色的冷却/进度数值。

**v1.25.1（2026-10-28）**
- 去掉日志：正式版一行都不再写 `godot.log`（`Initialize` 异常兜底与共享组件 `SponsorQrInjector` 的提示/告警原先绕过开关，现已统一受控）。诊断代码与探针逻辑完整保留。

**v1.25.0（2026-10-28）**
- 修复「磁力花盆不显示消化时间」：它的消化是**角色类字段**（`_digesting` / `_digestTimer`，单次 20s），组件集里只有 `SlotComponent` + `CharacterTimerComponent`、**没有 `MagnetComponent`**，所以原来那条「消化」通道结构上取不到它。现按角色字段显示 `消化 X.Xs`，并屏蔽它内部 0.2s 的 `Scan` 轮询计时器（原先会刷 `Scan 0.1s` 噪音行）。
- 新增「内卷投手」弹数显示 `弹数 N/5`：每被种下一株，给场上每一株同种都 `fireNum + 1`（上限 `maxFireNum = 5`），齐射时按此连发。识别判据用「`_fireNum` + `maxFireNum` 成对存在」——`maxFireNum` 全库只有它一个角色类有，不会误伤其余 100 个带 `_fireNum` 的射手。
- 新增离线反射闸门 `tools/reflect_gate/`：用与 Mod 逐字相同的查找逻辑对着真实 `PlantsVsZombies.dll` 验证字段命中，无需启动游戏即可重跑。

**安装**：解压得到 `HealthCooldownLine.pmod` → 放进游戏 `Mods` 目录 → 墓碑页「选项 → Mod 管理」里启用 → 重启游戏。
"""

TOKEN = os.environ.get("GH_TOKEN", "").strip()
if not TOKEN:
    sys.exit("缺少 GH_TOKEN")

HDR = {
    "Authorization": "Bearer " + TOKEN,
    "Accept": "application/vnd.github+json",
    "User-Agent": "pvzhe-release",
    "X-GitHub-Api-Version": "2022-11-28",
}


def api(method, url, data=None, ctype="application/json"):
    body = None
    if data is not None:
        body = data if isinstance(data, bytes) else json.dumps(data).encode("utf-8")
    req = urllib.request.Request(url, data=body, method=method,
                                 headers=dict(HDR, **{"Content-Type": ctype}))
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            raw = r.read()
            return r.status, (json.loads(raw.decode("utf-8")) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw.decode("utf-8"))
        except Exception:
            return e.code, {"raw": raw.decode("utf-8", "replace")[:300]}


def main():
    man = json.load(open(os.path.join(PROJ, "mod.json"), encoding="utf-8"))
    version = man["version"]
    pmod = os.path.join(PROJ, "dist", PMOD_NAME)
    size = os.path.getsize(pmod)
    print("== %s  version=%s  asset=%s (%d B)" % (REPO, version, PMOD_NAME, size))

    # 1) 取/建 release（tag 固定 upload）
    st, rel = api("GET", "https://api.github.com/repos/%s/%s/releases/tags/%s" % (OWNER, REPO, TAG))
    if st == 200 and isinstance(rel, dict):
        rid = rel["id"]
        print("   已有 release id=%s name=%s" % (rid, rel.get("name")))
        st2, rel2 = api("PATCH", "https://api.github.com/repos/%s/%s/releases/%s" % (OWNER, REPO, rid),
                        {"name": version, "body": BODY, "prerelease": False, "draft": False})
        print("   更新标题/正文 rc=%s name=%s" % (st2, (rel2 or {}).get("name")))
        if st2 != 200:
            print("   !! 更新失败:", str(rel2)[:300])
            return 1
    else:
        st, rel = api("POST", "https://api.github.com/repos/%s/%s/releases" % (OWNER, REPO),
                      {"tag_name": TAG, "target_commitish": "main", "name": version,
                       "body": BODY, "draft": False, "prerelease": False})
        if st not in (200, 201):
            print("   !! 建 release 失败 rc=%s %s" % (st, str(rel)[:300]))
            return 1
        rid = rel["id"]
        print("   已建 release id=%s tag=%s name=%s" % (rid, rel.get("tag_name"), rel.get("name")))

    # 2) 删掉同名旧附件（防止 GitHub 自动加 -1 后缀）
    st, assets = api("GET", "https://api.github.com/repos/%s/%s/releases/%s/assets" % (OWNER, REPO, rid))
    if st == 200 and isinstance(assets, list):
        for a in assets:
            if a.get("name") == PMOD_NAME:
                std, _ = api("DELETE", "https://api.github.com/repos/%s/%s/releases/assets/%s"
                             % (OWNER, REPO, a["id"]))
                print("   删除同名旧附件 %s (id=%s) rc=%s" % (PMOD_NAME, a["id"], std))

    # 3) 上传附件（附件名与仓库同名，沿用既有约定）
    with open(pmod, "rb") as f:
        blob = f.read()
    up_url = ("https://uploads.github.com/repos/%s/%s/releases/%s/assets?name=%s"
              % (OWNER, REPO, rid, PMOD_NAME))
    st, up = api("POST", up_url, blob, ctype="application/octet-stream")
    if st not in (200, 201):
        print("   !! 上传失败 rc=%s %s" % (st, str(up)[:300]))
        return 1
    print("   上传成功 asset id=%s name=%s size=%s" % (up.get("id"), up.get("name"), up.get("size")))

    # 4) 回读校验
    st, assets = api("GET", "https://api.github.com/repos/%s/%s/releases/%s/assets" % (OWNER, REPO, rid))
    ok = False
    if st == 200 and isinstance(assets, list):
        for a in assets:
            if a.get("name") == PMOD_NAME:
                ok = (a.get("size") == size)
                print("   校验 %s: remote=%s local=%s -> %s"
                      % (PMOD_NAME, a.get("size"), size, "OK" if ok else "MISMATCH"))
    print("   release 页面: https://github.com/%s/%s/releases/tag/%s" % (OWNER, REPO, TAG))
    print("   下载直链   : https://github.com/%s/%s/releases/download/%s/%s"
          % (OWNER, REPO, TAG, PMOD_NAME))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
