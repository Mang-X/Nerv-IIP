# PDA WMS 一线作业（收货/复核/拣货/上架/盘点）实施计划（Plan 2 扩展 · #374 解锁后）

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development。各步骤使用复选框（`- [ ]`）。
> 取代 `2026-06-10-wms-pda-inbound-outbound.md`（仅覆盖 inbound/outbound）。#374 已交付拣货/上架/盘点列表门面，本计划建设完整的 5 个 WMS 页面。

**目标：**交付 PDA WMS 一线五件套——**收货入库**、**复核发货**、**盘点**（写闭环 + 幂等）+ **拣货**、**上架**（只读任务清单），点亮首页 5 个 WMS 入口。先前 Task 1（business-core wmsFlows[inbound/review] + 点亮 wms.inbound/wms.review）已在分支上，但**页面从未建设**——本计划一并建设，消除当前失效跳转。

**架构：**使用 BusinessGateway WMS 门面（生成的 `@nerv-iip/api-client`，先补充精选导出入口以接出 #374 新增的列表操作）；数据封装到 PDA `useBusinessWms`（org/env 取登录主体；inbound/outbound/count 完成操作使用 `makeIdempotencyKey` 保证幂等；不传 `operatorUserId`——P1 未实现时会返回空结果）；写流程使用 `defineStepFlow`；拣货/上架没有完成端点 → 提供只读任务清单（写闭环经父单完成操作实现）。文案使用中文。

**技术栈：**Vue 3 / `@pinia/colada` / 生成的 api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright。

---

## 范围（#374 解锁后的完整 WMS）
| 页面 | 路由 | 门面 | 性质 |
|---|---|---|---|
| 收货入库 | `/wms/inbound`（`wms.inbound`，已点亮） | inbound-orders 列表 + 完成操作`{idempotencyKey}` | 写闭环 |
| 复核发货 | `/wms/review`（`wms.review`，已点亮） | outbound-orders 列表 + 完成操作`{packReviewNo,passed?,idempotencyKey}` | 写闭环 |
| 拣货 | `/wms/pick`（`wms.pick`，待点亮） | **picking-tasks 列表（只读）** | 只读任务清单 |
| 上架 | `/wms/putaway`（`wms.putaway`，待点亮） | **putaway-tasks 列表（只读）** | 只读任务清单 |
| 盘点 | `/wms/count`（`wms.count`，待点亮） | count-executions 列表 + 完成操作`{countedQuantity?,idempotencyKey}` | 写闭环 |

**不做**：扫码解析（仍缺少，#374 未包含）；真正的个人任务过滤（`operatorUserId` P1 未实现）；拣货/上架的逐任务完成操作（门面没有此端点，写闭环经父单完成操作实现）。

## 约定速查（执行者先读）
- **新列表操作已生成**（无需代码生成）：`listBusinessConsoleWmsPickingTasksQueryOptions`、`listBusinessConsoleWmsPutawayTasksQueryOptions`、`listBusinessConsoleWmsCountExecutionsQueryOptions`；count 写操作：`createBusinessConsoleWmsCountExecutionMutationOptions`、`completeBusinessConsoleWmsCountExecutionMutationOptions`。**精选导出入口 `business-console.ts` 尚未接出，需先补充**（Task 1）。
- **既有导出入口已接出**：inbound/outbound 的列表、创建与完成操作，以及 wcs。
- **行字段**：
  - picking/putaway 任务（`BusinessConsoleWmsWarehouseTaskItem`）：`warehouseTaskId, taskType, taskNo, sourceOrderNo, sourceOrderLineNo, skuCode, uomCode, siteCode, fromLocationCode, toLocationCode, plannedQuantity, executedQuantity, status, createdAtUtc, completedAtUtc?`。
  - count 执行（`BusinessConsoleWmsCountExecutionItem`）：`countExecutionId, countNo, skuCode, uomCode, siteCode, locationCode, expectedQuantity, countedQuantity?, varianceQuantity?, status, createdAtUtc, completedAtUtc?`。
  - inbound/outbound 行：`inboundOrderId/inboundOrderNo/status/createdAtUtc`、`outboundOrderId/outboundOrderNo/status/createdAtUtc`。
- **列表查询参数**：`{organizationId, environmentId, skip, take, status?, keyword?, locationCode?}`（picking/putaway 还包含 `operatorUserId?`——**不要传非空值**，否则会返回空集）。
- **完成操作请求体**（path 为 `{id}`，query 为 org/env）：inbound `{idempotencyKey}`；outbound `{packReviewNo, passed?, idempotencyKey}`；count `{countedQuantity?, idempotencyKey}`。
- **org/env**：`useAuthStore().principal`；scope 为空时不发请求。**幂等键**：新建 `src/composables/makeIdempotencyKey.ts`（crypto.randomUUID 的回退实现）；每次完成或创建操作生成一次，通过后置注入 + Omit 收窄确保调用方不可覆盖。
- **ui-mobile/business-core/约定**与 MES/equipment 分支相同：ScanBar（`@scan`,`active`）、ListRow、BottomSheet、Result、AppShellMobile；`defineStepFlow`；页面位于 `src/pages/wms/*.vue`（`requiresAuth:true`）；UI 不显示工程语言（status 使用中文，GUID 仅作 key，orderNo/taskNo/locationCode 是业务码）；无假数据；写操作防重（完成操作有幂等键 + pending 时禁用）。

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

## Task 1：导出入口接出 + business-core（点亮字典 + count 流程 + WMS 标签）
**文件：**`api-client/src/business-console.ts`；`business-core` `sop/wmsFlows.{ts,test}`、新建 `labels/wmsLabels.{ts,test}`、`tasks/pdaTaskKinds.{ts,test}`、`src/index.ts`；文档 ×2

- [ ] **步骤 1：接出导出入口**（读取 `business-console.ts` 现有 WMS 接出风格并按相同方式补充）：值导出选项增加 `listBusinessConsoleWmsPickingTasksQueryOptions`、`listBusinessConsoleWmsPutawayTasksQueryOptions`、`listBusinessConsoleWmsCountExecutionsQueryOptions`、`createBusinessConsoleWmsCountExecutionMutationOptions`、`completeBusinessConsoleWmsCountExecutionMutationOptions`；类型增加 `BusinessConsoleWmsWarehouseTaskItem`、`BusinessConsoleWmsWarehouseTaskListResponse`、`...WarehouseTaskListRequest`、`BusinessConsoleWmsCountExecutionItem`、`...CountExecutionListResponse`、`...CountExecutionListRequest`、`BusinessConsoleCreateWmsCountExecutionRequest/Response`、`BusinessConsoleCompleteWmsCountExecutionRequest`，以及对应 envelope（沿用现有命名）。运行 `pnpm -C frontend --filter @nerv-iip/api-client typecheck`。
- [ ] **步骤 2：点亮字典（TDD）**：`pdaTaskKinds.test.ts` 断言 `wms.pick`/`wms.putaway`/`wms.count` 的 `routeReady===true`（wms.inbound/review 原本即为 true）；修改 `pdaTaskKinds.ts`，将这三个值改为 true。先失败→再通过。
- [ ] **步骤 3：count 流程（TDD）**：在 `wmsFlows.ts` 中增加 `countExecutionFlow`（selectExecution→enterCount→complete）：
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
  补充测试并从 index 导出。先失败→再通过。
- [ ] **步骤 4：WMS 标签（TDD）**：`labels/wmsLabels.ts`（纯 TS）导出 `warehouseTaskStatusLabel`、`countExecutionStatusLabel`、`inboundOrderStatusLabel`、`outboundOrderStatusLabel`（status code→中文，回退为未知状态）。若 business-console WMS 页面已有映射则沿用；否则将 CMMS/WMS 标准码（open/inProgress/completed/closed/cancelled 等）映射为中文。补充测试覆盖，并从 index 导出。
- [ ] **步骤 5：文档 + 门禁 + 提交**：将模块文档/导航图中的 WMS PDA 状态改为“5 页已建”；确认 `business-core` + `api-client` 的 typecheck/test 通过；提交消息为 `feat(api-client,business-core): wire #374 WMS list facades + count flow/labels + light up WMS wall`。

## Task 2：WMS 数据封装（组合式函数 + 幂等键）
**文件：**新建 `business-pda/src/composables/makeIdempotencyKey.ts`、`useBusinessWms.ts` + 测试
- [ ] **步骤 1：幂等键**：`makeIdempotencyKey()`（crypto.randomUUID 的回退值为 `idem-{Date.now()}-{perf}`）。
- [ ] **步骤 2：测试（先观察失败，模拟 api-client + colada）**：断言 (a) 无 org/env → 列表查询 `enabled:false`；(b) `completeInbound(id)` 请求体包含注入的 `idempotencyKey`，且调用方不可覆盖；(c) `completeOutbound(id,{packReviewNo,passed})` 请求体包含 packReviewNo + idempotencyKey；(d) `completeCount(id,{countedQuantity})` 请求体包含 idempotencyKey；(e) 拣货/上架列表查询已启用并暴露 items/pending/error，**不传非空 operatorUserId**。
- [ ] **步骤 3：实现**（org/env 取自 principal）：
  - `useWmsInbound()` → `{ filters, orders, total, pending, error, refresh, completeInbound(id), completePending }`
  - `useWmsOutbound()` → `{ ..., completeOutbound(id, {packReviewNo, passed}) }`
  - `useWmsPicking()` → `{ filters(含 status/locationCode), tasks, total, pending, error, refresh }`（只读）
  - `useWmsPutaway()` → 同上（只读）
  - `useWmsCount()` → `{ filters, executions, total, pending, error, refresh, completeCount(id, {countedQuantity}) }`
  完成操作内部注入 `idempotencyKey: makeIdempotencyKey()`（后置 + Omit 收窄）；query 不传非空 `operatorUserId`；`enabled` 绑定 scope。
- [ ] **步骤 4：确认测试通过 + 提交**：`feat(business-pda): WMS data composable + idempotency key`。

## Task 3–7：五个作业页（各自配套测试，先失败后通过，沿用 MES/equipment 页面约定）
- [ ] **Task 3 收货入库 `/wms/inbound`**：inbound-orders ListRow + ScanBar（扫描单号→keyword）→ 选单 → BottomSheet 确认 → `completeInbound(id)`（幂等）→ Result；防重（completePending 时禁用）。由 `inboundReceiveFlow` 驱动。提交消息为 `feat(business-pda): WMS inbound receiving page`。
- [ ] **Task 4 复核发货 `/wms/review`**：outbound-orders 列表 → 选单 → 在 BottomSheet 内输入复核单号 packReviewNo + 通过开关 → `completeOutbound(id,{packReviewNo,passed})` → Result；防重。由 `outboundReviewFlow` 驱动。提交消息为 `feat(business-pda): WMS outbound pack-review page`。
- [ ] **Task 5 拣货 `/wms/pick`（只读）**：picking-tasks ListRow（taskNo/源单/SKU/库位 from→to/数量/中文状态）+ ScanBar 扫描库位→`filters.locationCode` + 状态过滤；提供空/加载/错误状态；**无写操作**（页内说明“拣货完成经复核发货过账”）。提交消息为 `feat(business-pda): WMS picking task list (read-only)`。
- [ ] **Task 6 上架 `/wms/putaway`（只读）**：putaway-tasks ListRow 与拣货页面形态相同（说明“上架完成经收货入库过账”）。提交消息为 `feat(business-pda): WMS putaway task list (read-only)`。
- [ ] **Task 7 盘点 `/wms/count`**：count-executions ListRow（盘点号/SKU/库位/预期数/状态）→ 选择执行项 → 在 BottomSheet 中输入实盘数 countedQuantity → `completeCount(id,{countedQuantity})`（幂等）→ Result；由 `countExecutionFlow` 驱动；防重。提交消息为 `feat(business-pda): WMS count execution page`。

## Task 8：首页点亮 5 个入口 + e2e
**文件：**`index.{vue,test.ts}`、`e2e/wms.spec.ts`（并扩展 fixtures）
- [ ] **步骤 1**：index.test 断言收货入库/复核发货/拣货/上架/盘点不再处于 disabled 状态，点击后推送对应路由；MES/equipment（若本分支未点亮）仍处于 disabled 状态。（`openTask` 已按 routeReady 控制，字典点亮后自动可跳转。）
- [ ] **步骤 2：e2e**：扩展 `fixtures.ts`，模拟 WMS 列表/完成操作 + count 完成操作 + picking/putaway 列表；规格覆盖：收货（选单→完成→Result）、盘点（选择执行项→输入数量→完成→Result）、拣货只读列表渲染、首页点击“收货入库”→`/wms/inbound`。在真机 Chromium 上运行。提交消息为 `test(business-pda): home wall lights up WMS entries + e2e`。

## Task 9：验收 + PR
- [ ] 全部门禁通过：api-client/business-core typecheck/test；business-pda typecheck/test/build；`playwright --list` + 真机 e2e；工作区 typecheck 无回归。
- [ ] 推送 + 创建 PR（基于 main，标题为 `feat(pda): WMS 一线作业（收货/复核/拣货/上架/盘点）`；正文列出范围、#374 解锁情况、拣货/上架只读理由、幂等防重，以及与 MES #378/equipment #379 并行时的共享文件冲突提示）。

---

## 自审
- **代码事实驱动**：#374 的三个列表操作已经生成（无需代码生成），导出入口待接出；拣货/上架没有完成端点 → 如实提供只读任务清单（写闭环经父单完成操作实现），不做假写入；`operatorUserId` P1 未实现 → 不传，按库位/状态过滤。
- **消除死跳转**：Task 1 早先点亮的 wms.inbound/review 终于连同页面建出。
- **同源/安全**：count 流程 + WMS 标签落在 business-core；完成操作的幂等键采用后置注入 + Omit，调用方不可覆盖；org/env 取登录主体；UI 不显示工程语言或假数据，写操作防重。
- **并行提示**：与 MES #378、equipment #379 共享 `pdaTaskKinds.ts`/`index.test.ts`/`fixtures.ts`/`business-core index`/`business-console.ts` barrel，合并次序后到者需解一次（基本加性）。
```
