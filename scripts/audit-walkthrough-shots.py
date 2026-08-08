#!/usr/bin/env python3
"""筛出「文件名对、图是错的」截图，供人工复核前先过一道机器。

今晚连着栽了三次，都不是脚本报错，而是**脚本报成功、图却不对**：

  1. 取证脚本没带 `--project`，两个 project 写同一路径，读到的是手机截图冒充桌面；
  2. 某次登录超时后，后续 13 张全是登录页，而 report.json 里它们统统记着 `ok`
     ——因为「截图保存成功」不等于「截到了对的东西」；
  3. 用猜的路由取图，页面其实是「页面暂未开放」兜底页，HTTP 200、不含「暂无」，
     两个启发式判据一个都没报警。

所以这里不看 report 的 ok 字段，只看图本身的字节特征：

* **完全相同的字节** → 多个步骤截了同一屏，至少有一步是空操作（真出过：
  「班次级视图」和「甘特图例分组」两张一模一样）。
* **大小扎堆的一群** → 典型是同一张兜底页（登录页/未开放页）被反复截下来。
  同一张页面在不同时刻截图字节数会完全一致，正常业务页则各不相同。
* **异常小** → 多半是白屏或只渲染了骨架。

输出只是**嫌疑名单**，不下结论——最终还得人眼看图。这个脚本的价值是把
几十张缩到几张，让人眼看得过来。
"""

from __future__ import annotations

import argparse
import hashlib
from collections import Counter, defaultdict
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--shots", required=True)
    ap.add_argument("--min-bytes", type=int, default=30_000, help="小于此值视为可疑白屏")
    args = ap.parse_args()

    shot_dir = Path(args.shots)
    pngs = sorted(shot_dir.glob("*.png"))
    if not pngs:
        print("目录里没有 PNG。")
        return 1

    by_hash: dict[str, list[Path]] = defaultdict(list)
    sizes = Counter()
    for p in pngs:
        by_hash[hashlib.sha1(p.read_bytes()).hexdigest()].append(p)
        sizes[p.stat().st_size] += 1

    print(f"共 {len(pngs)} 张。")

    dupes = {h: v for h, v in by_hash.items() if len(v) > 1}
    if dupes:
        print(f"\n【字节完全相同】{len(dupes)} 组——多个步骤截了同一屏，至少有一步没生效：")
        for group in dupes.values():
            print("  · " + "  ==  ".join(p.name for p in group))

    # 同尺寸扎堆：>=3 张一模一样大，几乎必然是同一张兜底页
    crowd = [(s, n) for s, n in sizes.items() if n >= 3]
    if crowd:
        print("\n【同尺寸扎堆】疑似反复截到同一张兜底页（登录页 / 页面暂未开放）：")
        for size, n in sorted(crowd, key=lambda x: -x[1]):
            names = [p.name for p in pngs if p.stat().st_size == size]
            print(f"  · {size} 字节 × {n} 张：{'、'.join(names[:6])}{' …' if n > 6 else ''}")

    small = [p for p in pngs if p.stat().st_size < args.min_bytes]
    if small:
        print(f"\n【异常小】疑似白屏或只有骨架（< {args.min_bytes} 字节）：")
        for p in small:
            print(f"  · {p.name}  {p.stat().st_size}")

    if not dupes and not crowd and not small:
        print("\n没有可疑项——但这只说明没露出上述三种破绽，**图对不对仍须人眼核**。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
