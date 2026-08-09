# PDA WMS 收货入库 + 复核发货一线作业页实施计划（Plan 2）

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development。各步骤使用复选框（`- [ ]`）。

**目标：**在已落地的 PDA 基础（`business-pda` + `@nerv-iip/ui-mobile` + `@nerv-iip/business-core`）上，交付**收货入库**与**复核发货**两条扫码 SOP 作业页，并点亮首页应用墙对应入口——真正跑通 ScanBar→ListRow→BottomSheet→Result + StepFlow 的一线闭环。

**架构：**作业页使用 BusinessGateway 既有 WMS 门面（inbound/outbound 的列表与完成操作，经生成的 `@nerv-iip/api-client`）；数据封装进新建的 PDA 专用 `useBusinessWms` 组合式函数（沿用 business-console 同名组合式函数的模式）；org/env 取自登录主体；完成操作使用幂等键防重；流程由 `defineStepFlow` 状态机驱动。文案直接使用中文（PDA 无 i18n）。

**技术栈：**Vue 3 / `@pinia/colada` / 生成的 api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright（e2e）。

---

## 范围（受 #374 约束）

**可建设（门面已存在）**
- **收货入库** `/wms/inbound`（`wms.inbound`）：`listBusinessConsoleWmsInboundOrders` + `completeBusinessConsoleWmsInboundOrder`（请求体 `{ idempotencyKey }`）。
- **复核发货** `/wms/review`（`wms.review`）：`listBusinessConsoleWmsOutboundOrders` + `completeBusinessConsoleWmsOutboundOrder`（请求体 `{ packReviewNo, passed?, idempotencyKey }`）。

**本计划不做（被 #374 阻塞，保持 `routeReady:false`/disabled，不做半截入口）**
- 拣货 `wms.pick`、上架 `wms.putaway`、盘点 `wms.count`（缺少独立列表门面）；扫码解析路由（缺少 `/barcode/resolve`）。这些内容在 #374 落地后另起计划。
- 收货页的库存上下文（inventoryContext）作为只读增强信息可选展示，不阻塞主流程。

## 约定速查（执行者先读）

- **api-client 导出**（`@nerv-iip/api-client`）：`listBusinessConsoleWmsInboundOrdersQueryOptions`、`completeBusinessConsoleWmsInboundOrderMutationOptions`、`listBusinessConsoleWmsOutboundOrdersQueryOptions`、`completeBusinessConsoleWmsOutboundOrderMutationOptions`。请求形式为 `{ path:{inboundOrderId|outboundOrderId}, query:{organizationId, environmentId, skip, take, status?, keyword?, ...}, body:{...} }`。
- **org/env 来源**：登录主体 `useAuthStore().principal`（`ConsolePrincipalResponse` 包含 `organizationId`/`environmentId`）。scope 为空时**不发查询**并显示空状态（AGENTS.md：空 scope 不发失败请求）。
- **幂等键**：`makeIdempotencyKey()` = `crypto.randomUUID()`（不可用时为 `idem-{Date.now()}-{rand}`）；每次完成操作生成一次。
- **ui-mobile**：`ScanBar`(`placeholder`,`active`,`@scan`)、`ListRow`(`title`,`subtitle`,`interactive`,`@select`,slots `meta`/`trailing`)、`BottomSheet`(`open`,`title`,`description`,`@update:open`)、`Result`(`status`,`title`,`description`,slot `actions`)、`AppShellMobile`(slots `header`/`footer`/default)。打开 BottomSheet 时给页面 ScanBar 传 `active=false` 以免抢焦点。
- **business-core**：`defineStepFlow({id,steps:[{id,done(ctx)}]})` → `currentStep/isComplete/progress`；通过 `PDA_TASK_KINDS` 点亮 `wms.inbound`/`wms.review` 的 `routeReady`。
- **作业页路由**：放 `src/pages/wms/inbound.vue`、`src/pages/wms/review.vue`（自动路由，不被 vite exclude；`requiresAuth: true`）。
- **门禁**：`pnpm -C frontend --filter @nerv-iip/business-pda typecheck|test|build`；e2e 使用 `... exec playwright test`（真机 Chromium 经 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`）。UI 不显示工程语言或假数据；写操作提供 Result 反馈，并对危险动作进行二次确认。

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

## Task 1：文档先行 + 点亮任务字典与 SOP

**文件：**
- 修改：`docs/architecture/mobile-pda-module-product-design.md`、`docs/architecture/frontend-navigation-map.md`
- 修改：`frontend/packages/business-core/src/tasks/pdaTaskKinds.ts` + `pdaTaskKinds.test.ts`
- 创建：`frontend/packages/business-core/src/sop/wmsFlows.ts` + `wmsFlows.test.ts`；从 `src/index.ts` 导出

- [ ] **步骤 1：文档**
  - 模块文档 §5 后端缺口回填：“拣货/上架/盘点列表、个人任务、扫码解析见 #374”；分期标注“WMS 收货入库 + 复核发货已建（Plan 2），其余 WMS 待 #374”。
  - 导航图 PDA 仓储作业：收货入库/复核发货标"已落地（PDA）"，拣货/上架/盘点标"待 #374"。

- [ ] **步骤 2：点亮字典（先测后改）**
  在 `pdaTaskKinds.test.ts` 增断言：`getPdaTaskKind('wms.inbound')?.routeReady === true` 且 `getPdaTaskKind('wms.review')?.routeReady === true`；其余 wms.* 仍 `false`。跑红。
  改 `pdaTaskKinds.ts`：`wms.inbound` 与 `wms.review` 的 `routeReady` 改 `true`（其余不变）。跑绿。

- [ ] **步骤 3：同源 SOP 定义（先测后建）**
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

- [ ] **步骤 4：business-core 门禁 + 提交**
  `pnpm -C frontend --filter @nerv-iip/business-core typecheck && ... test` → 绿。
  `git add` business-core + docs；提交消息为 `feat(business-core): wms inbound/outbound step flows + light up PDA wall`。

## Task 2：PDA WMS 数据封装（组合式函数）

**文件：**创建 `frontend/apps/business-pda/src/composables/makeIdempotencyKey.ts`、`useBusinessWms.ts` + `useBusinessWms.test.ts`

- [ ] **步骤 1：幂等键**
```typescript
// makeIdempotencyKey.ts
export function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && 'randomUUID' in c) return c.randomUUID()
  return `idem-${Date.now()}-${Math.trunc(performance.now())}`
}
```
> 注：`Math.random` 在某些环境受限，用 `performance.now()` 作熵；若 lint 限制，改用计数器。执行者按仓库实际可用 API 定。

- [ ] **步骤 2：组合式函数测试（模拟 api-client + colada，沿用 business-console 测试风格）**
  编写 `useBusinessWms.test.ts`：模拟 `@nerv-iip/api-client` 的列表/完成操作选项与 `@pinia/colada` 的 `useQuery`/`useMutation`，断言：(a) principal 无 org/env 时查询 `enabled:false`（不发请求）；(b) `completeInbound(id)` 调用完成操作变更且请求体包含 `idempotencyKey`；(c) `completeOutbound(id,{packReviewNo,passed})` 请求体包含三个字段。先观察测试失败。

- [ ] **步骤 3：实现组合式函数**（读取 `frontend/apps/business-console/src/composables/useBusinessWms.ts` 并沿用其模式；org/env 改为取自 `useAuthStore().principal`）
  暴露：`useWmsInboundOrders()` → `{ filters, inboundOrders, total, pending, error, refresh, completeInbound(id), completeInboundPending }`；`useWmsOutboundOrders()` → `{ filters, outboundOrders, total, pending, error, refresh, completeOutbound(id,{packReviewNo,passed}), completeOutboundPending }`。查询 `enabled` 绑定 `Boolean(organizationId && environmentId)`。完成操作内部调用 `mutateAsync({ path, query:{organizationId,environmentId}, body })`，inbound 请求体为 `{ idempotencyKey: makeIdempotencyKey() }`，outbound 请求体为 `{ packReviewNo, passed, idempotencyKey: makeIdempotencyKey() }`。
  > 不引入假分页或假数据；total 取响应中的 `total`。

- [ ] **步骤 4：确认测试通过 + 提交**（`feat(business-pda): WMS inbound/outbound data composable + idempotency key`）

## Task 3：收货入库作业页 `/wms/inbound`

**文件：**创建 `src/pages/wms/inbound.vue` + `inbound.test.ts`

- [ ] **步骤 1：测试（先观察失败）**
  模拟 `useBusinessWms`（返回 2 条待收货单）+ `vue-router`。断言：渲染 `AppShellMobile` + `ScanBar`（placeholder 以“扫描”开头）+ 单据 `ListRow`；扫码输入单号过滤（设置 `filters.keyword`）；点击行打开 `BottomSheet`（确认完成）；确认调用 `completeInbound(id)`；成功后显示 `Result`（status 为 success）。

- [ ] **步骤 2：实现页面**
  `definePage({ meta:{ requiresAuth:true, title:'收货入库' } })`。结构：`AppShellMobile` → 页眉标题 + 返回；页面主体：`ScanBar @scan="(v)=>filters.keyword=v"`（BottomSheet 打开时 `:active="!sheetOpen"`）+ 待收货 `ListRow` 列表（标题为单号，副标题为状态/时间，`@select` 打开确认面板）+ 空状态；`BottomSheet`（确认完成，包含 `inboundReceiveFlow` 进度）→ 主操作“确认入库”（AlertDialog 二次确认或直接在面板内确认）；完成后显示 `Result`（成功/失败 + “继续下一单”/“重试”操作项）。文案使用中文，不显示工程语言。

- [ ] **步骤 3：确认测试通过 + 提交**（`feat(business-pda): WMS inbound receiving page (scan→confirm→complete)`）

## Task 4：复核发货作业页 `/wms/review`

**文件：**创建 `src/pages/wms/review.vue` + `review.test.ts`

- [ ] **步骤 1：测试（先观察失败）**与 Task 3 形态相同，但完成前需输入 `packReviewNo`（复核单号）：BottomSheet 内提供复核单号输入项与通过/不通过选项；确认调用 `completeOutbound(id,{packReviewNo,passed})`；`outboundReviewFlow` 驱动步骤（选单→输入复核号→完成）。

- [ ] **步骤 2：实现页面** `definePage({ meta:{ requiresAuth:true, title:'复核发货' } })`。结构与 inbound 相同，BottomSheet 内增加复核单号输入框与通过开关；`packReviewNo` 为空时禁用确认；完成后显示 `Result`。

- [ ] **步骤 3：确认测试通过 + 提交**（`feat(business-pda): WMS outbound pack-review page (scan→review-no→complete)`）

## Task 5：首页应用墙点亮跳转

**文件：**修改 `src/pages/index.vue` + `index.test.ts`

- [ ] **步骤 1：测试（先观察失败）**断言：`收货入库` 与 `复核发货` 应用墙按钮**不再处于 disabled 状态**，点击后调用 `router.push('/wms/inbound')` / `'/wms/review'`；其余入口（拣货/上架/盘点）仍处于 disabled 状态且不跳转。
- [ ] **步骤 2：实现**现有 `openTask(route, routeReady)` 已按 `routeReady` 控制——字典点亮后这两项自动可跳转，无需修改逻辑；仅确认或补充测试。若 index 存在本地 disabled 逻辑则同步修改。
- [ ] **步骤 3：确认测试通过 + 提交**（`test(business-pda): home wall lights up inbound/review entries`）

## Task 6：e2e（两页流程，模拟网关）

**文件：**创建 `e2e/wms.spec.ts`

- [ ] **步骤 1：编写规格**复用 `e2e/fixtures.ts`：扩展 `routeBusinessConsoleApi`，模拟 `/wms/inbound-orders`（列表）+ `/wms/inbound-orders/{id}/complete`、`/wms/outbound-orders`（列表）+ `/complete`，返回 envelope。使用 seedStoredSession（principal 包含 org/env）。
  - inbound：访问 `/wms/inbound` → 看到待收货单 → 点击单据 → 确认 → 看到成功 Result。
  - review：访问 `/wms/review` → 点击单据 → 输入复核单号 → 确认 → 看到成功 Result。
  - 从首页点击“收货入库”→ URL `/wms/inbound`。
- [ ] **步骤 2：运行 e2e（真机 Chromium）+ 提交**（`test(business-pda): e2e for WMS inbound + outbound review flows`）

## Task 7：验收 + PR

- [ ] **步骤 1：全部门禁通过**
  `pnpm -C frontend --filter @nerv-iip/business-core typecheck|test`；`pnpm -C frontend --filter @nerv-iip/business-pda typecheck|test|build`；`... exec playwright test --list` + 真机 `e2e`；`pnpm -C frontend typecheck`（工作区无回归）。
- [ ] **步骤 2：推送 + 创建 PR**（基于 main，标题如 `feat(pda): WMS 收货入库 + 复核发货 一线作业页`；正文列出范围、#374 依赖、门禁结果与组件复用情况）。

---

## 自审
- **范围受代码事实约束**：只建设门面已存在的 inbound/outbound（列表 + 完成操作）；pick/putaway/count/scan-resolve 明确归 #374，保持 disabled，不创建空跳转。
- **同源**：StepFlow 定义在 `business-core`（PC 可复用）；幂等键防重；org/env 取登录主体、空 scope 不发请求。
- **组件真实复用**：ScanBar/ListRow/BottomSheet/Result/AppShellMobile + defineStepFlow 全部用上。
- **占位符**：无 TODO；关键代码（StepFlow、幂等键、组合式函数行为、页面结构、e2e 模拟）均已给出或明确沿用来源。
- **门禁**：每个包/页面均执行 typecheck/test/build + e2e + 工作区回归防护。
```
