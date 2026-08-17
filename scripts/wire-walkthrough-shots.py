#!/usr/bin/env python3
"""把演示手册里的「待补截图」占位替换成真实图片引用。

手册 `docs/demo/order-to-shipment-walkthrough.md` 里每处占位形如：

    > 📷 **待补截图**：销售订单列表与统计卡

取证脚本按同一批标签出图，文件名是 `<两位序号>-<标签>.png`。本脚本把两边对起来。

三条刻意的设计：

1. **只替换真拍到的**。目录里没有对应 PNG 的占位**原样保留**——宁可留着「待补截图」
   提醒人去补，也不要造出打不开的图链，那比没有图更坏（翻手册的人以为有图，点开是叉）。
2. **序号只用于消歧**，匹配以标签为准。同名标签按出现顺序配对，避免手册里
   重复标签（如多章都有「工单列表」）互相串图。
3. **幂等**。已经替换成图片引用的位置不会被再动，可以反复跑；补拍几张就再跑一次。

截图不入库（仓库根 .gitignore 第 9 行 `/artifacts/`），手册引的是相对路径，
在走查机器上用任意 Markdown 阅读器打开即可看图。
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

PLACEHOLDER = re.compile(r"^(?P<indent>\s*)> 📷 \*\*待补截图\*\*：(?P<label>.+?)\s*$")
ALREADY_WIRED = re.compile(r"^\s*> !\[")


def build_index(shot_dir: Path) -> dict[str, list[Path]]:
    """标签 → 该标签的所有截图（按文件名排序，支持同名多张）。"""
    index: dict[str, list[Path]] = {}
    for png in sorted(shot_dir.glob("*.png")):
        # `07-销售订单列表与统计卡.png` → 标签 `销售订单列表与统计卡`
        label = re.sub(r"^\d+[-_]", "", png.stem)
        index.setdefault(label, []).append(png)
    return index


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--manual", default="docs/demo/order-to-shipment-walkthrough.md")
    ap.add_argument("--shots", required=True, help="截图目录（仓库相对路径）")
    ap.add_argument("--apply", action="store_true", help="不加此参数只做演练，不写文件")
    args = ap.parse_args()

    repo = Path(__file__).resolve().parent.parent
    manual = repo / args.manual
    shot_dir = repo / args.shots
    if not shot_dir.is_dir():
        print(f"截图目录不存在：{shot_dir}", file=sys.stderr)
        return 1

    index = build_index(shot_dir)
    used: dict[str, int] = {}
    wired = missing = already = 0
    out_lines: list[str] = []

    for line in manual.read_text(encoding="utf-8").splitlines():
        if ALREADY_WIRED.match(line):
            already += 1
            out_lines.append(line)
            continue
        m = PLACEHOLDER.match(line)
        if not m:
            out_lines.append(line)
            continue
        label = m.group("label").strip()
        shots = index.get(label, [])
        nth = used.get(label, 0)
        if nth >= len(shots):
            missing += 1
            out_lines.append(line)  # 没拍到 → 原样留着提醒补拍
            continue
        used[label] = nth + 1
        rel = Path("../..") / shots[nth].relative_to(repo)
        out_lines.append(f'{m.group("indent")}> ![{label}]({rel.as_posix()})')
        wired += 1

    print(f"接上 {wired} 处，已是图片 {already} 处，仍缺 {missing} 处。")
    if missing:
        print("仍缺的标签（保留「待补截图」原样）：")
        seen: set[str] = set()
        for line in manual.read_text(encoding="utf-8").splitlines():
            m = PLACEHOLDER.match(line)
            if not m:
                continue
            label = m.group("label").strip()
            if label in index and used.get(label, 0) >= len(index[label]):
                continue
            if label not in index and label not in seen:
                seen.add(label)
                print(f"  · {label}")

    if args.apply:
        manual.write_text("\n".join(out_lines) + "\n", encoding="utf-8")
        print("已写入手册。")
    else:
        print("（演练模式，未写文件；加 --apply 生效）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
