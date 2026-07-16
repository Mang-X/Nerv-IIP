# AGENTS.md — @nerv-iip/ui-mobile（NvUI 移动层 · 库内规则）

> 根 `AGENTS.md` 与 `frontend/packages/ui/AGENTS.md` 的库内规则同样适用于
> 本包（原版零改动、复制重建、R1–R5 命名、token var 链）。本文件只记移动层
> 差异。权威 ADR 0020，冻结命名表 = 附录 A。

- 命名：与原版 / PC 层素名冲突 → `NvMobile*`（`NvMobileBadge`
  `NvMobileDialog`）；移动原生专名 → 直接 `Nv*`（`NvScanBar` `NvCell`
  `NvBottomSheet` `NvNumberKeyboard`）。
- 消费者只有 business-pda（触屏 + 扫码枪场景）：触控目标尺寸、防系统键盘
  弹出（只读触发 + NvNumberKeyboard）是本层组件的默认设计约束。
- 包内门禁：`src/nvui-naming.contract.test.ts`。新增导出走 `src/index.ts`
  stable barrel，app 侧禁止深路径导入。
