# 演示数据补口批次——前端移交清单（2026-07-27）

> 来源：owner 真机走查数据缺口批（8 项）。本批纪律：后端 seed/引擎/校验器/读面归数据补口代理修；
> 下列问题经核查**根因在前端**，登记移交前端批处理，后端不动。

## 1. 库存管理页空白（缺口 7）——前端信息架构问题，数据在库

- 事实：L1 二期已写入约 6.2 万条库存流水、5035 行台账（`wms`/`inventory` 侧数据齐全）；页面仍空白。
- 根因：`frontend/apps/business-console/src/composables/useBusinessInventory.ts` 的
  `hasRequiredAvailabilityScope`（L113-121）要求 **org/env/skuCode/uomCode/siteCode 五项全部非空**
  才发可用量查询（`enabled: availabilityEnabled`）。org/env 有默认值，但 skuCode/uomCode/siteCode
  需要用户手填，走查首开页面必然空白，且无引导。
- 建议修法（前端批）：
  - skuCode 换 SKU 选择器（84 个 SKU 可全量下拉/搜索）；
  - uomCode 默认 `pcs`、siteCode 默认 `SITE-001`（世界观唯一站点）；
  - 或提供不需 SKU 维度的聚合默认视图（按站点/库区汇总），进入页面即有数。
- 同源问题：WMS 入库单页的 scope-required 提示同一模式，一并处理。

## 2. NCR 关闭原因不展示（缺口 2 的前端半边）

- 后端已修：`CloseReason` 已补入 Quality 服务读面投影与 BusinessGateway facade DTO
  （`BusinessConsoleQualityItem.closeReason`、`BusinessConsoleQualityNcrDetailResponse.closeReason`），
  OpenAPI/types.gen 已再生；历史 NCR 三种处置路径的关闭原因均为中文。
- 前端缺口：`frontend/apps/business-console/src/pages/quality/ncrs.vue` 的详情抽屉
  （`ncrContextItems`）与列表均**没有任何展示 CloseReason 的位置**——用户看到的"关闭原因为空"
  实为字段从未上屏。建议在已关闭 NCR 的详情条目里加「关闭原因」只读行（`ncr.closeReason`），
  列表可选加列。

## 3. 生产驾驶舱（缺口 8）——后端已修，前端可顺手增强（非阻塞）

- 后端已修：`GET /api/business-console/v1/mes/overview` 的 `blockers` / `pendingWork`
  不再恒空：阻塞含 挂起工单（areaCode `quality-hold`）/ 排程失效（`capacity-schedule`）/
  齐套缺料（`material-kitting`）/ 入库过账失败（`material-receipt`）；待办含
  待派工 `dispatch-operation-tasks`（route `/mes/dispatch`）、待报工 `report-production`
  （`/mes/production-reports`）、待入库过账 `post-finished-goods-receipt`（`/mes/receipts`）。
  areaCode 命名已对齐页面现有的 `material`/`quality`/`capacity` 子串过滤，无需前端改动即生效。
- 可选增强：`pendingWork` 的 `routeHint` 目前页面未用作跳转，可加点击直达。

## 不属于前端也不属于数据的登记

（截至本批完结，经营管理五页的聚合在 ERP 后端全部存在，均按数据补口处理，无「需要产品功能」项。）
