# PDA WMS 收货入库 + 复核发货 一线作业页 Implementation Plan（Plan 2）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** 在已落地的 PDA 地基（`business-pda` + `@nerv-iip/ui-mobile` + `@nerv-iip/business-core`）上，交付**收货入库**与**复核发货**两条扫码 SOP 作业页，并点亮首页应用墙对应入口——真正跑通 ScanBar→ListRow→BottomSheet→Result + StepFlow 的一线闭环。

**Architecture:** 作业页消费 BusinessGateway 既有 WMS facade（inbound/outbound 的 list + complete，经 generated `@nerv-iip/api-client`）；数据封装进新建 PDA 专用 `useBusinessWms` composable（镜像 business-console 同名 composable 的模式）；org/env 取自登录主体；complete 用幂等键防重；流程用 `defineStepFlow` 状态机驱动。文案直接中文（PDA 无 i18n）。

**Tech Stack:** Vue 3 / `@pinia/colada` / generated api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright(e2e)。

---

## 范围（受 #374 约束）

**可建（facade 已存在）**
- **收货入库** `/wms/inbound`（`wms.inbound`）：`listBusinessConsoleWmsInboundOrders` + `completeBusinessConsoleWmsInboundOrder`（body `{ idempotencyKey }`）。
- **复核发货** `/wms/review`（`wms.review`）：`listBusinessConsoleWmsOutboundOrders` + `completeBusinessConsoleWmsOutboundOrder`（body `{ packReviewNo, passed?, idempotencyKey }`）。

**本计划不做（被 #374 阻塞，保持 `routeReady:false`/disabled，不做半截入口）**
- 拣货 `wms.pick`、上架 `wms.putaway`、盘点 `wms.count`（缺独立 list facade）；扫码 resolve 路由（缺 `/barcode/resolve`）。这些在 #374 落地后另起计划。
- 收货页的库存上下文（inventoryContext）作为只读增强可选展示；不阻塞主流程。

## 约定速查（执行者先读）

- **api-client 导出**（`@nerv-iip/api-client`）：`listBusinessConsoleWmsInboundOrdersQueryOptions`、`completeBusinessConsoleWmsInboundOrderMutationOptions`、`listBusinessConsoleWmsOutboundOrdersQueryOptions`、`completeBusinessConsoleWmsOutboundOrderMutationOptions`。请求形如 `{ path:{inboundOrderId|outboundOrderId}, query:{organizationId, environmentId, skip, take, status?, keyword?, ...}, body:{...} }`。
- **org/env 来源**：登录主体 `useAuthStore().principal`（`ConsolePrincipalResponse` 含 `organizationId`/`environmentId`）。scope 为空时**不发查询**、给空态（AGENTS.md：空 scope 不发失败请求）。
- **幂等键**：`makeIdempotencyKey()` = `crypto.randomUUID()`（不可用则 `idem-{Date.now()}-{rand}`）；每次 complete 生成一次。
- **ui-mobile**：`ScanBar`(`placeholder`,`active`,`@scan`)、`ListRow`(`title`,`subtitle`,`interactive`,`@select`,slots `meta`/`trailing`)、`BottomSheet`(`open`,`title`,`description`,`@update:open`)、`Result`(`status`,`title`,`description`,slot `actions`)、`AppShellMobile`(slots `header`/`footer`/default)。打开 BottomSheet 时给页面 ScanBar 传 `active=false` 以免抢焦点。
- **business-core**：`defineStepFlow({id,steps:[{id,done(ctx)}]})` → `currentStep/isComplete/progress`；`PDA_TASK_KINDS`（点亮 `wms.inbound`/`wms.review` 的 `routeReady`）。
- **作业页路由**：放 `src/pages/wms/inbound.vue`、`src/pages/wms/review.vue`（自动路由，不被 vite exclude；`requiresAuth: true`）。
- **门禁**：`pnpm -C frontend --filter @nerv-iip/business-pda typecheck|test|build`；e2e `... exec playwright test`（真机 Chromium 经 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`）。UI 无工程语言/无假数据；写操作有 Result 反馈 + 危险动作二次确认。

## 文件结构

```
docs/architecture/mobile-pda-module-product-design.md   # 改：§5 回填 #374；分期标注 WMS 收货/复核已建
docs/architecture/frontend-navigation-map.md            # 改：PDA 仓储作业状态
frontend/apps/business-pda/
  src/composables/useBusinessWms.ts        + useBusinessWms.test.ts   # 新：PDA WMS 数据封装
  src/composables/makeIdempotencyKey.ts                                # 新：幂等键
  src/pages/wms/inbound.vue                + inbound.test.ts           # 新：收货入库
  src/pages/wms/review.vue                 + review.test.ts            # 新：复核发货
  src/pages/index.vue                                                   # 改：应用墙点亮项可跳转
  e2e/wms.spec.ts                                                       # 新：两页 e2e
frontend/packages/business-core/
  src/tasks/pdaTaskKinds.ts                + pdaTaskKinds.test.ts       # 改：wms.inbound/wms.review routeReady=true
  src/sop/wmsFlows.ts                      + wmsFlows.test.ts          # 新：收货/复核 StepFlow 定义（同源）
```

---

## Task 1: 文档先行 + 点亮任务字典与 SOP

**Files:**
- Modify: `docs/architecture/mobile-pda-module-product-design.md`, `docs/architecture/frontend-navigation-map.md`
- Modify: `frontend/packages/business-core/src/tasks/pdaTaskKinds.ts` + `pdaTaskKinds.test.ts`
- Create: `frontend/packages/business-core/src/sop/wmsFlows.ts` + `wmsFlows.test.ts`; export from `src/index.ts`

- [ ] **Step 1: 文档**
  - 模块文档 §5 后端缺口回填："拣货/上架/盘点 list、个人任务、扫码 resolve 见 #374"；分期标注"WMS 收货入库 + 复核发货已建（Plan 2），其余 WMS 待 #374"。
  - 导航图 PDA 仓储作业：收货入库/复核发货标"已落地（PDA）"，拣货/上架/盘点标"待 #374"。

- [ ] **Step 2: 点亮字典（先测后改）**
  在 `pdaTaskKinds.test.ts` 增断言：`getPdaTaskKind('wms.inbound')?.routeReady === true` 且 `getPdaTaskKind('wms.review')?.routeReady === true`；其余 wms.* 仍 `false`。跑红。
  改 `pdaTaskKinds.ts`：`wms.inbound` 与 `wms.review` 的 `routeReady` 改 `true`（其余不变）。跑绿。

- [ ] **Step 3: 同源 SOP 定义（先测后建）**
  `wmsFlows.test.ts`：
```typescript
import { describe, expect, it } from 'vitest'
import { inboundReceiveFlow, outboundReviewFlow } from './wmsFlows'

describe('wms PDA step flows', () => {
  it('inbound: order selected → complete', () => {
    expect(inboundReceiveFlow.currentStep({}).id).toBe('selectOrder')
    expect(inboundReceiveFlow.isComplete({ orderId: 'IB1', completed: true })).toBe(true)
    expect(inboundReceiveFlow.progress({ orderId: 'IB1' })).toEqual({ completed: 1, total: 2 })
  })
  it('outbound: order → packReviewNo → complete', () => {
    expect(outboundReviewFlow.currentStep({ orderId: 'OB1' }).id).toBe('enterReviewNo')
    expect(outboundReviewFlow.isComplete({ orderId: 'OB1', packReviewNo: 'PR1', completed: true })).toBe(true)
  })
})
```
  `wmsFlows.ts`:
```typescript
import { defineStepFlow } from './defineStepFlow'

export interface InboundReceiveCtx { orderId?: string; completed?: boolean }
export interface OutboundReviewCtx { orderId?: string; packReviewNo?: string; completed?: boolean }

export const inboundReceiveFlow = defineStepFlow<InboundReceiveCtx>({
  id: 'wms.inbound.receive',
  steps: [
    { id: 'selectOrder', done: (c) => Boolean(c.orderId) },
    { id: 'complete', done: (c) => Boolean(c.completed) },
  ],
})

export const outboundReviewFlow = defineStepFlow<OutboundReviewCtx>({
  id: 'wms.outbound.review',
  steps: [
    { id: 'selectOrder', done: (c) => Boolean(c.orderId) },
    { id: 'enterReviewNo', done: (c) => Boolean(c.packReviewNo) },
    { id: 'complete', done: (c) => Boolean(c.completed) },
  ],
})
```
  从 `business-core/src/index.ts` 导出 `inboundReceiveFlow`/`outboundReviewFlow` + 类型。

- [ ] **Step 4: business-core 门禁 + commit**
  `pnpm -C frontend --filter @nerv-iip/business-core typecheck && ... test` → 绿。
  `git add` business-core + docs；commit `feat(business-core): wms inbound/outbound step flows + light up PDA wall`.

## Task 2: PDA WMS 数据封装（composable）

**Files:** Create `frontend/apps/business-pda/src/composables/makeIdempotencyKey.ts`, `useBusinessWms.ts` + `useBusinessWms.test.ts`

- [ ] **Step 1: 幂等键**
```typescript
// makeIdempotencyKey.ts
export function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && 'randomUUID' in c) return c.randomUUID()
  return `idem-${Date.now()}-${Math.trunc(performance.now())}`
}
```
> 注：`Math.random` 在某些环境受限，用 `performance.now()` 作熵；若 lint 限制，改用计数器。执行者按仓库实际可用 API 定。

- [ ] **Step 2: composable 测试（mock api-client + colada，镜像 business-console 测试风格）**
  写 `useBusinessWms.test.ts`：mock `@nerv-iip/api-client` 的 list/complete options 与 `@pinia/colada` 的 `useQuery`/`useMutation`，断言：(a) principal 无 org/env 时查询 `enabled:false`（不发请求）；(b) `completeInbound(id)` 调用 complete mutation 且 body 含 `idempotencyKey`；(c) `completeOutbound(id,{packReviewNo,passed})` body 含三字段。跑红。

- [ ] **Step 3: 实现 composable**（读 `frontend/apps/business-console/src/composables/useBusinessWms.ts` 镜像；org/env 改取自 `useAuthStore().principal`）
  暴露：`useWmsInboundOrders()` → `{ filters, inboundOrders, total, pending, error, refresh, completeInbound(id), completeInboundPending }`；`useWmsOutboundOrders()` → `{ filters, outboundOrders, total, pending, error, refresh, completeOutbound(id,{packReviewNo,passed}), completeOutboundPending }`。查询 `enabled` 绑定 `Boolean(organizationId && environmentId)`。complete 内部 `mutateAsync({ path, query:{organizationId,environmentId}, body })`，inbound body `{ idempotencyKey: makeIdempotencyKey() }`，outbound body `{ packReviewNo, passed, idempotencyKey: makeIdempotencyKey() }`。
  > 不引入假分页/假数据；total 取响应 `total`。

- [ ] **Step 4: 跑绿 + commit**（`feat(business-pda): WMS inbound/outbound data composable + idempotency key`）

## Task 3: 收货入库作业页 `/wms/inbound`

**Files:** Create `src/pages/wms/inbound.vue` + `inbound.test.ts`

- [ ] **Step 1: 测试（先红）**
  mock `useBusinessWms`（返回 2 条待收货单）+ `vue-router`。断言：渲染 `AppShellMobile` + `ScanBar`（placeholder 以"扫描"开头）+ 单据 `ListRow`；扫码输入单号过滤（设置 `filters.keyword`）；点行打开 `BottomSheet`（确认完成）；确认调用 `completeInbound(id)`；成功后显示 `Result`（status success）。

- [ ] **Step 2: 实现页**
  `definePage({ meta:{ requiresAuth:true, title:'收货入库' } })`。结构：`AppShellMobile` → header 标题 + 返回；body：`ScanBar @scan="(v)=>filters.keyword=v"`（BottomSheet 打开时 `:active="!sheetOpen"`）+ 待收货 `ListRow` 列表（title=单号，subtitle=状态/时间，`@select` 打开确认 sheet）+ 空态；`BottomSheet`（确认完成，含 `inboundReceiveFlow` 进度）→ 主操作"确认入库"（AlertDialog 二次确认或直接 sheet 内确认）；完成走 `Result`（成功/失败 + "继续下一单"/"重试"actions）。文案中文，无工程语言。

- [ ] **Step 3: 跑绿 + commit**（`feat(business-pda): WMS inbound receiving page (scan→confirm→complete)`）

## Task 4: 复核发货作业页 `/wms/review`

**Files:** Create `src/pages/wms/review.vue` + `review.test.ts`

- [ ] **Step 1: 测试（先红）** 同 Task 3 形态，但完成需输入 `packReviewNo`（复核单号）：BottomSheet 内有复核单号输入 + 通过/不通过；确认调用 `completeOutbound(id,{packReviewNo,passed})`；`outboundReviewFlow` 驱动步骤（选单→输复核号→完成）。

- [ ] **Step 2: 实现页** `definePage({ meta:{ requiresAuth:true, title:'复核发货' } })`。结构同 inbound，BottomSheet 内增复核单号 Input + 通过开关；`packReviewNo` 空时禁用确认；完成 `Result`。

- [ ] **Step 3: 跑绿 + commit**（`feat(business-pda): WMS outbound pack-review page (scan→review-no→complete)`）

## Task 5: 首页应用墙点亮跳转

**Files:** Modify `src/pages/index.vue` + `index.test.ts`

- [ ] **Step 1: 测试（先红）** 断言：`收货入库` 与 `复核发货` 应用墙按钮**不再 disabled**，点击 `router.push('/wms/inbound')` / `'/wms/review'`；其余（拣货/上架/盘点）仍 disabled 不跳。
- [ ] **Step 2: 实现** 现有 `openTask(route, routeReady)` 已按 `routeReady` 控制——字典点亮后这两项自动可跳转，无需改逻辑；仅确认/补测试。若 index 有本地 disabled 逻辑则同步。
- [ ] **Step 3: 跑绿 + commit**（`test(business-pda): home wall lights up inbound/review entries`）

## Task 6: e2e（两页流程，网关 Mock）

**Files:** Create `e2e/wms.spec.ts`

- [ ] **Step 1: 写 spec** 复用 `e2e/fixtures.ts`：扩展 `routeBusinessConsoleApi` mock `/wms/inbound-orders`(list) + `/wms/inbound-orders/{id}/complete`、`/wms/outbound-orders`(list) + `/complete`，返回 envelope。seedStoredSession（principal 含 org/env）。
  - inbound：goto `/wms/inbound` → 见待收货单 → 点单 → 确认 → 见成功 Result。
  - review：goto `/wms/review` → 点单 → 输复核单号 → 确认 → 成功 Result。
  - 从首页点"收货入库"→ URL `/wms/inbound`。
- [ ] **Step 2: 跑 e2e（真机 Chromium）+ commit**（`test(business-pda): e2e for WMS inbound + outbound review flows`）

## Task 7: 验收 + PR

- [ ] **Step 1: 门禁全绿**
  `pnpm -C frontend --filter @nerv-iip/business-core typecheck|test`；`pnpm -C frontend --filter @nerv-iip/business-pda typecheck|test|build`；`... exec playwright test --list` + 真机 `e2e`；`pnpm -C frontend typecheck`（工作区无回归）。
- [ ] **Step 2: push + 开 PR**（base main，标题如 `feat(pda): WMS 收货入库 + 复核发货 一线作业页`；body 列范围、#374 依赖、门禁结果、组件复用）。

---

## Self-Review
- **范围受代码事实约束**：只建 facade 已存在的 inbound/outbound（list+complete）；pick/putaway/count/scan-resolve 明确归 #374，保持 disabled，不做空跳转。
- **同源**：StepFlow 定义在 `business-core`（PC 可复用）；幂等键防重；org/env 取登录主体、空 scope 不发请求。
- **组件真实复用**：ScanBar/ListRow/BottomSheet/Result/AppShellMobile + defineStepFlow 全部用上。
- **占位符**：无 TODO；关键代码（StepFlow、幂等键、composable 行为、页面结构、e2e mock）均给出或明确镜像来源。
- **门禁**：每包/页 typecheck/test/build + e2e + 工作区回归防护。
```
