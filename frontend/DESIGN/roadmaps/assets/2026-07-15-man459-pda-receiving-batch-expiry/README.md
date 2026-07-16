# MAN-459 #813 PDA 收货批号/效期 + 质检门禁 — 移动视口走查存证

日期：2026-07-15 · 分支 `man-459-pda-receiving-batch-expiry`

## 方法

本地 `business-pda` vite dev（127.0.0.1:5126，移动视口 375×812）+ 真实 Chromium（Playwright）。
无 WMS 收货 seed（后端缺口 #926/#927），且无独立收货质检门禁读端点造数路径，故采用
MAN-458 无后端注入配方驱动真实 UI：

- `pda-auth.$patch` 注入 org-001/env-dev 主体（isAuthenticated=true）；
- pinia-colada 缓存按 `_id` 定位真实查询条目后 `setQueryData` 注入门禁/订单信封；
- 其余交互（GS1 解析、三色、日期滚轮、门禁可见性、抽屉 teleport）全部真实运行时执行。

**列表截图（1）背景的「单据加载失败」红条是注入产物**：真 fetch 无后端而失败、
`setQueryData` 后 pinia-colada 又调度了一次 refetch 竞态重置 error。真后端查询成功时
`error=null`，该横幅不出现。质检状态标与行为不受影响。

## 截图

| # | 场景 | 验证点 |
|---|---|---|
| `1-list-quality-badges.png` | 收货单列表 | IB-0001「待检」(琥珀)、IB-0002「合格」(绿)、IB-0003 未收货无标 |
| `2-pending-gs1-expiry.png` | 待检单明细抽屉 | 行级批号 LOT-A24 + 待检标 + 扫 GS1 `1726080110LOT-A24` → **2026-08-01·临近过期(红)** + ⚠临期黄条 + ℹ「该单待质检，合格后方可上架」门禁；无「去上架」 |
| `3-passed-go-putaway.png` | 合格单明细抽屉 | 批号 LOT-B77 + 合格标(绿)；出现「去上架」引导；无待质检门禁提示 |

## 运行时结论（DOM 断言 + 视觉）

- ✅ 列表质检状态标：单据级汇总（待检/合格/无）
- ✅ NvBottomSheet teleport + 行级明细（SKU/数量/批号/门禁标）
- ✅ GS1 扫码解析 → 按批号匹配收货行 → 效期三色（临近过期红）+ 临期黄色提示
- ✅ NvMobileDatePicker 年月日滚轮 → 确定 → 回写 capturedExpiry → 三色标
- ✅ 上架门禁：待检/不合格隐藏「去上架」+ 显示待质检提示；合格显示「去上架」
- ✅ 双 NvScanBar 靠 active 互斥，未见 document 捕获双写冲突

结果页文案「入库完成，待质检」由单测覆盖（`inbound.test.ts`），此处未走完提交（注入
订单 id 非真实，complete 会打空后端）。
