# PDA MES 一线作业（工序执行 / 报工 / 领料 / 完工入库）Implementation Plan（Plan 3）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** 在 PDA 地基上交付 MES 一线四件套——**工序执行**、**报工**、**领料**、**完工入库**——点亮首页对应应用墙入口，跑通"先选工单/工序 → 扫码/操作 → 确认 → 结果"的一线闭环。MES facade 已全部就绪，无后端阻塞（不同于 WMS 待 #374）。

**Architecture:** 消费 BusinessGateway 既有 MES facade（经 generated `@nerv-iip/api-client`）；数据封装进新建 PDA `useBusinessMes` composable（镜像 business-console `useBusinessMes.ts`，org/env 取登录主体）；多步流程用 `defineStepFlow`；所有 create/action 带幂等键防重；文案中文（PDA 无 i18n）。

**Tech Stack:** Vue 3 / `@pinia/colada` / generated api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright。

---

## 范围
四个 MES app-wall 入口全部交付（无后端阻塞）：
- **工序执行** `/mes/operation`（`mes.operation`）：`listBusinessConsoleMesOperationTasks` + start/pause/resume/complete。
- **报工** `/mes/report`（`mes.report`）：选工单→选工序→录入良/次品→`recordBusinessConsoleMesProductionReport`。
- **领料** `/mes/issue`（`mes.issue`）：`listBusinessConsoleMesMaterialIssueRequests` + create（按工单）+ 线边接收 `confirmBusinessConsoleMesLineSideMaterialReceipt`。
- **完工入库** `/mes/receipt`（`mes.receipt`）：`listBusinessConsoleMesFinishedGoodsReceiptRequests` + create。
- 工单作为上下文入口：`listBusinessConsoleMesWorkOrders` + detail。

## 约定速查（执行者先读）
- **api-client 导出**（`@nerv-iip/api-client`，均 `*QueryOptions`/`*MutationOptions`）：
  `listBusinessConsoleMesWorkOrders`、`getBusinessConsoleMesWorkOrderDetail`、`listBusinessConsoleMesOperationTasks`、`start|pause|resume|completeBusinessConsoleMesOperationTask`、`listBusinessConsoleMesProductionReports`、`recordBusinessConsoleMesProductionReport`、`listBusinessConsoleMesMaterialIssueRequests`、`createBusinessConsoleMesMaterialIssueRequest`、`confirmBusinessConsoleMesLineSideMaterialReceipt`、`listBusinessConsoleMesFinishedGoodsReceiptRequests`、`createBusinessConsoleMesFinishedGoodsReceiptRequest`。
- **关键请求体**（path/query 均含 `organizationId`/`environmentId`）：
  - 工序动作 start/pause/resume/complete：`{ reasonCode?, idempotencyKey }`，path `{operationTaskId}`。
  - 报工 `production-reports`：body `{ organizationId, environmentId, workOrderId, operationTaskId, goodQuantity, scrapQuantity, completesOperation, reportedAtUtc, idempotencyKey?, consumedMaterialLots? }`。
  - 领料 create：path `{workOrderId}`，body `{ operationTaskId?, materialId, quantity?, materialIds?, idempotencyKey }`；线边接收：path `{requestId}`，body `{ materialLotId?, receivedQuantity?, evidenceFileIds?, idempotencyKey }`。
  - 完工入库 create：body `{ organizationId, environmentId, workOrderId, skuId, quantity, uomCode, requestedAtUtc, idempotencyKey }`。
  - list 查询：`{ organizationId, environmentId, skip, take, status?, keyword?, workOrderId?, workCenterId?, deviceAssetId? }`（按端点取子集）。
- **org/env**：取 `useAuthStore().principal`（`organizationId`/`environmentId`）；空则不发查询、给空态。
- **幂等键**：新建 `src/composables/makeIdempotencyKey.ts`（`crypto.randomUUID()` fallback `idem-{Date.now()}-{perf}`）；每次 create/action 生成一次。
- **上下文获取**：PDA 任务范式——报工/领料先选工单（list 进入），工序执行直接用 operation-tasks list 行的 `operationTaskId`/`workOrderId`；完工入库先选工单。
- **ui-mobile**：ScanBar(`@scan`,`active`)、ListRow(`title`,`subtitle`,`interactive`,`@select`,slots)、BottomSheet(`open`,`title`,`@update:open`；打开时给页面 ScanBar `active=false`)、Result(`status`,`title`,`description`,actions slot)、AppShellMobile(header/footer/default)。
- **页面**：`src/pages/mes/*.vue`（自动路由，`requiresAuth:true`，不被 vite exclude）。门禁同 PDA 既有：typecheck/test/build + e2e；UI 无工程语言/无假数据；危险动作二次确认；写操作有 Result 反馈。

## 文件结构
```
docs/architecture/mobile-pda-module-product-design.md / frontend-navigation-map.md   # 改：MES PDA 状态
frontend/packages/business-core/src/sop/mesFlows.ts + mesFlows.test.ts               # 新：报工/完工入库 StepFlow
frontend/packages/business-core/src/tasks/pdaTaskKinds.{ts,test.ts}                   # 改：mes.* routeReady=true
frontend/apps/business-pda/src/composables/
  makeIdempotencyKey.ts                                                               # 新
  useBusinessMes.ts + useBusinessMes.test.ts                                          # 新：MES 数据封装
frontend/apps/business-pda/src/pages/mes/
  operation.vue + operation.test.ts    # 工序执行
  report.vue    + report.test.ts       # 报工
  issue.vue     + issue.test.ts        # 领料
  receipt.vue   + receipt.test.ts      # 完工入库
frontend/apps/business-pda/src/pages/index.vue                                        # 改：点亮 4 个 mes 入口
frontend/apps/business-pda/e2e/mes.spec.ts                                            # 新：核心流程 e2e
```

---

## Task 1: 文档 + 点亮 MES 字典 + MES StepFlow（business-core）
**Files:** docs ×2；`business-core/src/tasks/pdaTaskKinds.{ts,test.ts}`；新建 `business-core/src/sop/mesFlows.ts`+test；`business-core/src/index.ts`

- [ ] **Step 1 文档**：模块文档分期标注"MES 工序执行/报工/领料/完工入库 已建 (Plan 3)"；导航图 MES PDA 状态同步（更新校验日期）。
- [ ] **Step 2 点亮字典（TDD）**：`pdaTaskKinds.test.ts` 加断言 `mes.report`/`mes.issue`/`mes.receipt`/`mes.operation` 的 `routeReady===true`；跑红→改 `pdaTaskKinds.ts` 仅这 4 个 `routeReady:true`→跑绿。
- [ ] **Step 3 MES StepFlow（TDD）**：`mesFlows.ts`：
```typescript
import { defineStepFlow } from './defineStepFlow'

export interface ReportCtx { workOrderId?: string; operationTaskId?: string; quantityEntered?: boolean; recorded?: boolean }
export interface ReceiptCtx { workOrderId?: string; skuId?: string; quantityEntered?: boolean; created?: boolean }

export const productionReportFlow = defineStepFlow<ReportCtx>({
  id: 'mes.report',
  steps: [
    { id: 'selectWorkOrder', done: (c) => Boolean(c.workOrderId) },
    { id: 'selectOperation', done: (c) => Boolean(c.operationTaskId) },
    { id: 'enterQuantity', done: (c) => Boolean(c.quantityEntered) },
    { id: 'record', done: (c) => Boolean(c.recorded) },
  ],
})

export const finishedGoodsReceiptFlow = defineStepFlow<ReceiptCtx>({
  id: 'mes.receipt',
  steps: [
    { id: 'selectWorkOrder', done: (c) => Boolean(c.workOrderId) },
    { id: 'enterSkuQuantity', done: (c) => Boolean(c.skuId && c.quantityEntered) },
    { id: 'create', done: (c) => Boolean(c.created) },
  ],
})
```
  写对应 test（currentStep/isComplete/progress 各 1-2 例，仿 wmsFlows 风格），从 `business-core/src/index.ts` 导出。跑红→绿。
- [ ] **Step 4 门禁+commit**：`pnpm -C frontend --filter @nerv-iip/business-core typecheck && ... test` 绿；commit `feat(business-core): mes step flows + light up PDA mes wall`。

## Task 2: PDA MES 数据封装（composable + 幂等键）
**Files:** 新建 `business-pda/src/composables/makeIdempotencyKey.ts`、`useBusinessMes.ts`+`useBusinessMes.test.ts`

- [ ] **Step 1 幂等键**：同 Plan 2 的 `makeIdempotencyKey()`（crypto.randomUUID fallback）。
- [ ] **Step 2 composable 测试（先红，mock api-client + colada，仿 business-console useBusinessEquipment.test 风格）**：断言 (a) principal 无 org/env 时 list query `enabled:false`；(b) `recordReport(body)` 调 record mutation 且 body 含 `idempotencyKey`（未显式传时自动补）；(c) 工序 `startTask(id)` 调 start mutation body 含 `idempotencyKey`；(d) `createIssue/confirmLineSideReceipt/createReceipt` 同理带幂等键。
- [ ] **Step 3 实现 composable**（镜像 business-console `useBusinessMes.ts`；org/env 取 `useAuthStore().principal`）。暴露：
  - `useMesWorkOrders()` → `{ filters, workOrders, total, pending, error, refresh }`
  - `useMesOperationTasks()` → `{ filters, operationTasks, total, pending, error, refresh, startTask(id), pauseTask(id), resumeTask(id), completeTask(id), actionPending }`（动作 body 默认 `{ idempotencyKey }`，可选 `reasonCode`）
  - `useMesProductionReports()` → `{ filters, productionReports, total, refresh, recordReport(body) }`（body 自动补 `idempotencyKey`、`reportedAtUtc=当前ISO`，org/env 注入）
  - `useMesMaterialIssue()` → `{ filters, requests, total, refresh, createIssue(workOrderId, body), confirmLineSideReceipt(requestId, body) }`
  - `useMesReceipts()` → `{ filters, receipts, total, refresh, createReceipt(body) }`
  > 不假分页/假数据；list query `enabled` 绑 `Boolean(org && env)`；`reportedAtUtc`/`requestedAtUtc` 用 `new Date().toISOString()`（注意：脚本/测试环境如禁 `new Date()` 改由调用方传时间——此处是运行时页面代码，允许）。
- [ ] **Step 4 跑绿+commit**：`feat(business-pda): MES data composable + idempotency key`。

## Task 3: 工序执行页 `/mes/operation`
**Files:** `src/pages/mes/operation.vue`+`operation.test.ts`
- [ ] **Step 1 测试（先红）**：mock `useBusinessMes`（返回 2 条工序任务）+ vue-router。断言：渲染 AppShellMobile+ScanBar（扫工单/工序过滤）+ 工序 ListRow（title=工序/工单号，subtitle=状态/工作中心）；点行打开 BottomSheet（动作面板：开始/暂停/恢复/完成，按当前状态显示可用动作）；点"完成"二次确认后调 `completeTask(id)`；成功 Result。
- [ ] **Step 2 实现**：`definePage({meta:{requiresAuth:true,title:'工序执行'}})`。状态机非必需（动作型）；危险动作（完成）走 AlertDialog 二次确认；幂等键自动。BottomSheet 打开时 ScanBar `active=false`。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): MES operation execution page`。

## Task 4: 报工页 `/mes/report`
**Files:** `src/pages/mes/report.vue`+`report.test.ts`
- [ ] **Step 1 测试（先红）**：流程 `productionReportFlow` 驱动：扫/选工单→选工序→录入良品/次品数→提交。断言：缺工单时停在选工单步；提交调 `recordReport`，body 含 `workOrderId/operationTaskId/goodQuantity/scrapQuantity/completesOperation`；成功 Result（"继续报工"/"返回"）。
- [ ] **Step 2 实现**：`title:'报工'`。先 `useMesWorkOrders` 选工单（ScanBar 扫工单号过滤 + ListRow 选），再 `useMesOperationTasks`（按 workOrderId 过滤）选工序，再 BottomSheet 内 Stepper/数字输入良品/次品 + `completesOperation` 开关 → 提交。数量校验（非负、good+scrap>0）。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): MES production reporting page (work order → operation → qty)`。

## Task 5: 领料页 `/mes/issue`
**Files:** `src/pages/mes/issue.vue`+`issue.test.ts`
- [ ] **Step 1 测试（先红）**：渲染领料申请 ListRow（按工单/状态过滤）；新建领料（选工单→物料→数量→`createIssue(workOrderId, body)`）；行内"线边接收"→`confirmLineSideReceipt(requestId, {receivedQuantity, idempotencyKey})`；成功 Result。
- [ ] **Step 2 实现**：`title:'领料'`。新建走 BottomSheet 表单；接收走行动作 + 确认。幂等键自动。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): MES material issue + line-side receipt page`。

## Task 6: 完工入库页 `/mes/receipt`
**Files:** `src/pages/mes/receipt.vue`+`receipt.test.ts`
- [ ] **Step 1 测试（先红）**：`finishedGoodsReceiptFlow` 驱动：选工单→录 SKU/数量/单位→提交 `createReceipt(body)`（body 含 `workOrderId/skuId/quantity/uomCode/requestedAtUtc/idempotencyKey`）；成功 Result。
- [ ] **Step 2 实现**：`title:'完工入库'`。列表 + 新建 BottomSheet 表单（选工单、SKU、数量、单位）。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): MES finished-goods receipt page`。

## Task 7: 首页点亮 4 个 MES 入口
**Files:** `src/pages/index.vue`+`index.test.ts`
- [ ] **Step 1 测试（先红）**：断言 `报工`/`领料`/`完工入库`/`工序执行` 应用墙按钮不再 disabled，点击分别 `router.push('/mes/report'|'/mes/issue'|'/mes/receipt'|'/mes/operation')`；WMS 等其余仍 disabled。
- [ ] **Step 2 实现**：现有 `openTask(route, routeReady)` 已按 `routeReady` 控制——字典点亮后自动可跳；补/确认测试。
- [ ] **Step 3 跑绿+commit**：`test(business-pda): home wall lights up MES entries`。

## Task 8: e2e（核心流程，网关 Mock）
**Files:** `e2e/mes.spec.ts`
- [ ] **Step 1**：扩展 `e2e/fixtures.ts` mock MES list/action/create 端点（envelope）。spec 覆盖：工序执行（列表→完成→Result）、报工（选工单→选工序→录数→Result）、从首页点"工序执行"→URL `/mes/operation`。seedStoredSession（principal 含 org/env）。
- [ ] **Step 2**：真机 Chromium 跑 e2e + commit `test(business-pda): e2e for MES operation + report flows`。

## Task 9: 验收 + PR
- [ ] **Step 1 门禁全绿**：`business-core` typecheck/test；`business-pda` typecheck/test/build；`... exec playwright test --list` + 真机 e2e；`pnpm -C frontend typecheck`（工作区无回归）。
- [ ] **Step 2 push + 开 PR**（base main，标题 `feat(pda): MES 一线作业（工序执行/报工/领料/完工入库）`；body 列范围、门禁、组件复用、无后端阻塞）。

---

## Self-Review
- **无后端阻塞**：MES facade（工单/工序/报工/领料/完工入库）list+action+create 全部已存在并经审计确认；与 WMS（待 #374）不同，MES 可完整交付。
- **同源**：报工/完工入库 StepFlow 落 business-core；幂等键防重；org/env 取登录主体、空 scope 不发请求；composable 镜像 business-console `useBusinessMes`。
- **组件真实复用**：ScanBar/ListRow/BottomSheet/Result/AppShellMobile + defineStepFlow 全用上。
- **占位符**：无 TODO；关键代码（StepFlow、composable 暴露面、各页结构、e2e mock）给出或明确镜像来源 + 真实 facade body 字段。
- **门禁**：每包/页 typecheck/test/build + e2e + 工作区回归防护；UI 无工程语言/无假数据；危险动作二次确认。
- **范围适中**：4 页 + 共享 composable + StepFlow，9 个任务；任务边界清晰、各自可测可提交。
```

