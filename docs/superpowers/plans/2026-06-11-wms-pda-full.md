# PDA WMS 一线作业（收货/复核/拣货/上架/盘点）Implementation Plan（Plan 2 扩展 · #374 解锁后）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).
> 取代 `2026-06-10-wms-pda-inbound-outbound.md`（仅覆盖 inbound/outbound）。#374 已交付拣货/上架/盘点 list facade，本计划做完整 WMS 5 页。

**Goal:** 交付 PDA WMS 一线五件套——**收货入库**、**复核发货**、**盘点**（写闭环 + 幂等）+ **拣货**、**上架**（只读任务清单），点亮首页 5 个 WMS 入口。先前 Task 1（business-core wmsFlows[inbound/review] + 点亮 wms.inbound/wms.review）已在分支上，但**页面从未建**——本计划连同建出，消除当前死跳转。

**Architecture:** 消费 BusinessGateway WMS facade（generated `@nerv-iip/api-client`，先补 curated barrel 接出 #374 新 list）；数据封装 PDA `useBusinessWms`（org/env 取登录主体；inbound/outbound/count complete 带 `makeIdempotencyKey` 幂等；不传 `operatorUserId`——P1 未实装会返回空）；写流程用 `defineStepFlow`；拣货/上架无 complete 端点 → 只读任务清单（写闭环经父单 complete）。文案中文。

**Tech Stack:** Vue 3 / `@pinia/colada` / generated api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright。

---

## 范围（#374 解锁后的完整 WMS）
| 页面 | 路由 | facade | 性质 |
|---|---|---|---|
| 收货入库 | `/wms/inbound`（`wms.inbound`，已点亮） | inbound-orders list + complete`{idempotencyKey}` | 写闭环 |
| 复核发货 | `/wms/review`（`wms.review`，已点亮） | outbound-orders list + complete`{packReviewNo,passed?,idempotencyKey}` | 写闭环 |
| 拣货 | `/wms/pick`（`wms.pick`，待点亮） | **picking-tasks list（只读）** | 只读任务清单 |
| 上架 | `/wms/putaway`（`wms.putaway`，待点亮） | **putaway-tasks list（只读）** | 只读任务清单 |
| 盘点 | `/wms/count`（`wms.count`，待点亮） | count-executions list + complete`{countedQuantity?,idempotencyKey}` | 写闭环 |

**不做**：扫码 resolve（仍缺，#374 未含）；真正个人任务过滤（`operatorUserId` P1 未实装）；拣货/上架的逐任务 complete（facade 无此端点，写闭环经父单 complete）。

## 约定速查（执行者先读）
- **新 list 已在 generated**（无需 codegen）：`listBusinessConsoleWmsPickingTasksQueryOptions`、`listBusinessConsoleWmsPutawayTasksQueryOptions`、`listBusinessConsoleWmsCountExecutionsQueryOptions`；count 写：`createBusinessConsoleWmsCountExecutionMutationOptions`、`completeBusinessConsoleWmsCountExecutionMutationOptions`。**curated barrel `business-console.ts` 未接出，需先补**（Task 1）。
- **既有 barrel 已接出**：inbound/outbound list + create + complete、wcs。
- **行字段**：
  - picking/putaway 任务（`BusinessConsoleWmsWarehouseTaskItem`）：`warehouseTaskId, taskType, taskNo, sourceOrderNo, sourceOrderLineNo, skuCode, uomCode, siteCode, fromLocationCode, toLocationCode, plannedQuantity, executedQuantity, status, createdAtUtc, completedAtUtc?`。
  - count 执行（`BusinessConsoleWmsCountExecutionItem`）：`countExecutionId, countNo, skuCode, uomCode, siteCode, locationCode, expectedQuantity, countedQuantity?, varianceQuantity?, status, createdAtUtc, completedAtUtc?`。
  - inbound/outbound 行：`inboundOrderId/inboundOrderNo/status/createdAtUtc`、`outboundOrderId/outboundOrderNo/status/createdAtUtc`。
- **list 查询参数**：`{organizationId, environmentId, skip, take, status?, keyword?, locationCode?}`（picking/putaway 还有 `operatorUserId?`——**不要传非空**，会返回空集）。
- **complete 请求体**（path `{id}`，query org/env）：inbound `{idempotencyKey}`；outbound `{packReviewNo, passed?, idempotencyKey}`；count `{countedQuantity?, idempotencyKey}`。
- **org/env**：`useAuthStore().principal`；空 scope 不发请求。**幂等键**：新建 `src/composables/makeIdempotencyKey.ts`（crypto.randomUUID fallback）；每次 complete/create 生成一次，注入后置 + Omit 收窄，调用方不可覆盖。
- **ui-mobile/business-core/约定** 同 MES/equipment 分支：ScanBar(`@scan`,`active`)、ListRow、BottomSheet、Result、AppShellMobile；`defineStepFlow`；页面 `src/pages/wms/*.vue`（`requiresAuth:true`）；UI 无工程语言（status 中文、GUID 仅作 key、orderNo/taskNo/locationCode 业务码）；无假数据；写操作防重（complete 有幂等键 + pending 禁用）。

## 文件结构
```
docs/architecture/mobile-pda-module-product-design.md / frontend-navigation-map.md   # 改：WMS PDA 状态（5 页已建）
frontend/packages/api-client/src/business-console.ts                                  # 改：补接 #374 WMS list + count 写 + 类型
frontend/packages/business-core/src/sop/wmsFlows.ts + test                            # 改：加 countExecutionFlow
frontend/packages/business-core/src/labels/wmsLabels.ts + test                        # 新：WMS 任务/盘点/单据 status 中文
frontend/packages/business-core/src/tasks/pdaTaskKinds.{ts,test.ts}                   # 改：点亮 wms.pick/putaway/count
frontend/apps/business-pda/src/composables/
  makeIdempotencyKey.ts                                                               # 新
  useBusinessWms.ts + test                                                            # 新：5 域数据封装
frontend/apps/business-pda/src/pages/wms/
  inbound.vue/+test  review.vue/+test  pick.vue/+test  putaway.vue/+test  count.vue/+test
frontend/apps/business-pda/src/pages/index.vue + index.test.ts                        # 改：点亮 5 个 WMS 入口
frontend/apps/business-pda/e2e/wms.spec.ts                                            # 新：核心流程 e2e
```

---

## Task 1: barrel 接出 + business-core（点亮字典 + count flow + WMS 标签）
**Files:** `api-client/src/business-console.ts`；`business-core` `sop/wmsFlows.{ts,test}`、新 `labels/wmsLabels.{ts,test}`、`tasks/pdaTaskKinds.{ts,test}`、`src/index.ts`；docs ×2

- [ ] **Step 1 barrel 接出**（读 `business-console.ts` 现有 WMS 接出风格，按样补）：value options 加 `listBusinessConsoleWmsPickingTasksQueryOptions`、`listBusinessConsoleWmsPutawayTasksQueryOptions`、`listBusinessConsoleWmsCountExecutionsQueryOptions`、`createBusinessConsoleWmsCountExecutionMutationOptions`、`completeBusinessConsoleWmsCountExecutionMutationOptions`；type 加 `BusinessConsoleWmsWarehouseTaskItem`、`BusinessConsoleWmsWarehouseTaskListResponse`、`...WarehouseTaskListRequest`、`BusinessConsoleWmsCountExecutionItem`、`...CountExecutionListResponse`、`...CountExecutionListRequest`、`BusinessConsoleCreateWmsCountExecutionRequest/Response`、`BusinessConsoleCompleteWmsCountExecutionRequest`，及对应 envelope（按现有命名）。`pnpm -C frontend --filter @nerv-iip/api-client typecheck`。
- [ ] **Step 2 点亮字典（TDD）**：`pdaTaskKinds.test.ts` 断言 `wms.pick`/`wms.putaway`/`wms.count` `routeReady===true`（wms.inbound/review 本就 true）；改 `pdaTaskKinds.ts` 把这三个翻 true。跑红→绿。
- [ ] **Step 3 count flow（TDD）**：`wmsFlows.ts` 加 `countExecutionFlow`（selectExecution→enterCount→complete）：
```typescript
export interface CountExecCtx { countExecutionId?: string; countEntered?: boolean; completed?: boolean }
export const countExecutionFlow = defineStepFlow<CountExecCtx>({
  id: 'wms.count',
  steps: [
    { id: 'selectExecution', done: (c) => Boolean(c.countExecutionId) },
    { id: 'enterCount', done: (c) => Boolean(c.countEntered) },
    { id: 'complete', done: (c) => Boolean(c.completed) },
  ],
})
```
  补 test，从 index 导出。跑红→绿。
- [ ] **Step 4 WMS 标签（TDD）**：`labels/wmsLabels.ts`（纯 TS）导出 `warehouseTaskStatusLabel`、`countExecutionStatusLabel`、`inboundOrderStatusLabel`、`outboundOrderStatusLabel`（status code→中文，fallback 未知状态）。镜像 business-console WMS 页若有映射；否则用 CMMS/WMS 标准码（open/inProgress/completed/closed/cancelled 等）→中文。test 覆盖。从 index 导出。
- [ ] **Step 5 文档 + 门禁 + commit**：模块文档/导航图 WMS PDA 状态改"5 页已建"；`business-core` + `api-client` typecheck/test 绿；commit `feat(api-client,business-core): wire #374 WMS list facades + count flow/labels + light up WMS wall`。

## Task 2: WMS 数据封装（composable + 幂等键）
**Files:** 新 `business-pda/src/composables/makeIdempotencyKey.ts`、`useBusinessWms.ts` + test
- [ ] **Step 1 幂等键**：`makeIdempotencyKey()`（crypto.randomUUID fallback `idem-{Date.now()}-{perf}`）。
- [ ] **Step 2 测试（先红，mock api-client + colada）**：断言 (a) 无 org/env → list `enabled:false`；(b) `completeInbound(id)` body 含注入 `idempotencyKey`、调用方不可覆盖；(c) `completeOutbound(id,{packReviewNo,passed})` body 含 packReviewNo+idempotencyKey；(d) `completeCount(id,{countedQuantity})` body 含 idempotencyKey；(e) 拣货/上架 list query enabled、暴露 items/pending/error，**不传非空 operatorUserId**。
- [ ] **Step 3 实现**（org/env 取 principal）：
  - `useWmsInbound()` → `{ filters, orders, total, pending, error, refresh, completeInbound(id), completePending }`
  - `useWmsOutbound()` → `{ ..., completeOutbound(id, {packReviewNo, passed}) }`
  - `useWmsPicking()` → `{ filters(含 status/locationCode), tasks, total, pending, error, refresh }`（只读）
  - `useWmsPutaway()` → 同上（只读）
  - `useWmsCount()` → `{ filters, executions, total, pending, error, refresh, completeCount(id, {countedQuantity}) }`
  complete 内部注入 `idempotencyKey: makeIdempotencyKey()`（后置 + Omit 收窄）；query 不传非空 `operatorUserId`；`enabled` 绑 scope。
- [ ] **Step 4 跑绿 + commit**：`feat(business-pda): WMS data composable + idempotency key`。

## Task 3–7: 五个作业页（各 + test，先红后绿，镜像 MES/equipment 页约定）
- [ ] **Task 3 收货入库 `/wms/inbound`**：inbound-orders ListRow + ScanBar(扫单号→keyword) → 选单 → BottomSheet 确认 → `completeInbound(id)`（幂等）→ Result；防重（completePending 禁用）。`inboundReceiveFlow` 驱动。commit `feat(business-pda): WMS inbound receiving page`。
- [ ] **Task 4 复核发货 `/wms/review`**：outbound-orders 列表 → 选单 → BottomSheet 内输复核单号 packReviewNo + 通过开关 → `completeOutbound(id,{packReviewNo,passed})` → Result；防重。`outboundReviewFlow` 驱动。commit `feat(business-pda): WMS outbound pack-review page`。
- [ ] **Task 5 拣货 `/wms/pick`（只读）**：picking-tasks ListRow（taskNo/源单/SKU/库位 from→to/数量/状态中文）+ ScanBar 扫库位→`filters.locationCode` + 状态过滤；空/加载/错误态；**无写操作**（页内说明"拣货完成经复核发货过账"）。commit `feat(business-pda): WMS picking task list (read-only)`。
- [ ] **Task 6 上架 `/wms/putaway`（只读）**：putaway-tasks ListRow 同拣货形态（说明"上架完成经收货入库过账"）。commit `feat(business-pda): WMS putaway task list (read-only)`。
- [ ] **Task 7 盘点 `/wms/count`**：count-executions ListRow（盘点号/SKU/库位/预期数/状态）→ 选执行 → BottomSheet 输实盘数 countedQuantity → `completeCount(id,{countedQuantity})`（幂等）→ Result；`countExecutionFlow` 驱动；防重。commit `feat(business-pda): WMS count execution page`。

## Task 8: 首页点亮 5 入口 + e2e
**Files:** `index.{vue,test.ts}`、`e2e/wms.spec.ts`（+扩展 fixtures）
- [ ] **Step 1**：index.test 断言 收货入库/复核发货/拣货/上架/盘点 不再 disabled、点击 push 对应路由；MES/equipment（若本分支未点亮）仍 disabled。（`openTask` 已按 routeReady 控制，字典点亮后自动可跳。）
- [ ] **Step 2 e2e**：扩展 `fixtures.ts` mock WMS list/complete + count complete + picking/putaway list；spec 覆盖：收货（选单→完成→Result）、盘点（选执行→输数→完成→Result）、拣货只读列表渲染、首页点"收货入库"→`/wms/inbound`。真机 Chromium 跑。commit `test(business-pda): home wall lights up WMS entries + e2e`。

## Task 9: 验收 + PR
- [ ] 门禁全绿：api-client/business-core typecheck/test；business-pda typecheck/test/build；`playwright --list` + 真机 e2e；工作区 typecheck 无回归。
- [ ] push + 开 PR（base main，标题 `feat(pda): WMS 一线作业（收货/复核/拣货/上架/盘点）`；body 列范围、#374 解锁、拣货/上架只读理由、幂等防重、与 MES #378/equipment #379 并行+共享文件冲突提示）。

---

## Self-Review
- **代码事实驱动**：#374 三个 list 已在 generated（无需 codegen），barrel 待接出；拣货/上架无 complete 端点 → 诚实做只读任务清单（写闭环经父单 complete），不做假写入；`operatorUserId` P1 未实装 → 不传，按库位/状态过滤。
- **消除死跳转**：Task 1 早先点亮的 wms.inbound/review 终于连同页面建出。
- **同源/安全**：count flow + WMS 标签落 business-core；complete 幂等键注入后置 + Omit 不可覆盖；org/env 取登录主体；UI 无工程语言/无假数据/写操作防重。
- **并行提示**：与 MES #378、equipment #379 共享 `pdaTaskKinds.ts`/`index.test.ts`/`fixtures.ts`/`business-core index`/`business-console.ts` barrel，合并次序后到者需解一次（基本加性）。
```

