# AGENTS.md — @nerv-iip/ui-mobile（NvUI 移动层 · 库内规则）

> 先读 `frontend/AGENTS.md` 与
> `docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`；本文件只记移动层
> 差异。冻结命名表 = 附录 A。

- 命名：与原版 / PC 层素名冲突 → `NvMobile*`（`NvMobileBadge`
  `NvMobileDialog`）；移动原生专名 → 直接 `Nv*`（`NvScanBar` `NvCell`
  `NvBottomSheet` `NvNumberKeyboard`）。
- 消费者只有 business-pda（触屏 + 扫码枪场景）：触控目标尺寸、防系统键盘
  弹出（只读触发 + NvNumberKeyboard）是本层组件的默认设计约束。
- 包内门禁：`src/nvui-naming.contract.test.ts`。新增导出走 `src/index.ts`
  稳定导出入口，应用侧禁止深路径导入。
