# MAN-459 #813 PDA 收货批号/效期 + 质检门禁 — 移动视口走查存证

日期：2026-07-15（初版，advisory）→ 2026-07-16（闭环复验） · 分支 `man-459-pda-receiving-batch-expiry`

## 更正：后端 #935 已交付完整闭环（#926/#927 无缺口，已关闭）

初版把功能做成「效期本地提示、未落库」，并开了 #926/#927 两个后端缺口——**这是对着落后
main 的分支基线分析所致**。实际后端 PR **#935「收货批号与效期采集闭环」**早已在 main：
`ReceivingQualityGate` 投影带 `productionDate`/`expiryDate`，`CompleteWmsInboundOrder`
收 `lines`（`WmsInboundLineCaptureInput`: lineNo/lotNo/productionDate/expiryDate）。合并 main
后 regen `api-client` 即得全字段。前端遂改为真实闭环：效期以后端 `line.expiryDate` 为准
（无需扫码即三色显示），采集值随 `completeInbound` 落库。#926/#927 已关闭。

## 闭环复验（2026-07-16，真实栈）

`nerv.ps1 dev` 起完整栈（BusinessGateway 5119 / PlatformGateway 5100 均 Healthy）+
`business-pda` vite（127.0.0.1:5126，移动视口 375×812）+ 真实 Chromium（Playwright）。
无 WMS 收货 seed，故用 Playwright `route` 拦截两个 **GET 读端点**回放真实契约信封，
**`completeInbound` POST 放行到真网关并抓取 payload**；鉴权用 `pda-auth.$patch` 注入
org-001/env-dev + SPA 内 `router.push` 导航（避免整页重载冲掉注入）。

`completeInbound` 落库铁证（真网关请求体）：

```json
{
  "idempotencyKey": "…",
  "lines": [
    { "lineNo": "1", "lotNo": "LOT-A", "productionDate": "2026-02-01", "expiryDate": "2026-08-01" }
  ]
}
```

| #                                       | 场景           | 验证点                                                                                                                    |
| --------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `closedloop-1-list-badges.png`          | 收货单列表     | IB-9001「待检」(琥珀)、IB-9002「合格」(绿)                                                                                |
| `closedloop-2-sheet-backend-expiry.png` | 待检单明细抽屉 | 行级批号 LOT-A + 待检标 + **后端 `expiryDate` 2026-08-01·临近过期(红)，无需扫码** + ⚠临期黄条 + ℹ待质检门禁；无「去上架」 |
| `closedloop-3-after-complete.png`       | 提交后         | `确认完成` → completeInbound 携 lines 达网关                                                                              |

## 初版方法（advisory，历史留存）

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

| #                           | 场景           | 验证点                                                                                                                                               |
| --------------------------- | -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `1-list-quality-badges.png` | 收货单列表     | IB-0001「待检」(琥珀)、IB-0002「合格」(绿)、IB-0003 未收货无标                                                                                       |
| `2-pending-gs1-expiry.png`  | 待检单明细抽屉 | 行级批号 LOT-A24 + 待检标 + 扫 GS1 `1726080110LOT-A24` → **2026-08-01·临近过期(红)** + ⚠临期黄条 + ℹ「该单待质检，合格后方可上架」门禁；无「去上架」 |
| `3-passed-go-putaway.png`   | 合格单明细抽屉 | 批号 LOT-B77 + 合格标(绿)；出现「去上架」引导；无待质检门禁提示                                                                                      |

## 运行时结论（DOM 断言 + 视觉）

- ✅ 列表质检状态标：单据级汇总（待检/合格/无）
- ✅ NvBottomSheet teleport + 行级明细（SKU/数量/批号/门禁标）
- ✅ GS1 扫码解析 → 按批号匹配收货行 → 效期三色（临近过期红）+ 临期黄色提示
- ✅ NvMobileDatePicker 年月日滚轮 → 确定 → 回写 capturedExpiry → 三色标
- ✅ 上架门禁：待检/不合格隐藏「去上架」+ 显示待质检提示；合格显示「去上架」
- ✅ 双 NvScanBar 靠 active 互斥，未见 document 捕获双写冲突

结果页文案「入库完成，待质检」由单测覆盖（`inbound.test.ts`），此处未走完提交（注入
订单 id 非真实，complete 会打空后端）。
