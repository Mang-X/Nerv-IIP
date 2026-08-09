---
description: 运行前端质量门禁（check + typecheck + test + build）
---

从仓库根目录运行 Nerv-IIP 前端质量门禁，报告每一步是通过还是失败，并附上所有失败输出：

1. `pnpm -C frontend check`
2. `pnpm -C frontend typecheck`
3. `pnpm -C frontend test`
4. `pnpm -C frontend build`
