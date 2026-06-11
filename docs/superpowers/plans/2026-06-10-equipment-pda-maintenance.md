# PDA 设备运维（CMMS：报修 / 点检 / 报警查看）Implementation Plan（Plan 4）

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** 在 PDA 地基上交付设备运维一线三件套——**故障报修**（创建维修工单）、**点检**（记录点检结果）、**报警查看**（只读 + 一键报修）——点亮首页对应入口。Maintenance/Telemetry facade 已就绪，**无后端阻塞**。

**Architecture:** 消费 BusinessGateway 既有 Maintenance + Telemetry/Equipment facade（generated `@nerv-iip/api-client`）；数据封装进新建 PDA `useBusinessMaintenance` + `useBusinessEquipmentAlarms`（维修无 PC 可镜像，按 api-client 类型构建；报警镜像 business-console `useBusinessEquipment`）；org/env 取登录主体 `useAuthStore().principal`，`openedBy`/`inspector` 取 `principal.loginName`；**Maintenance 端点无服务端幂等**→ create 用 **UI 防重复提交**（pending 禁用 + 成功后离场/重置）；报修/点检多步用 `defineStepFlow`；文案中文（无 i18n）。

**Tech Stack:** Vue 3 / `@pinia/colada` / generated api-client / `@nerv-iip/ui-mobile` / `@nerv-iip/business-core` / vitest + @vue/test-utils / Playwright。

---

## 范围
- **故障报修** `/equipment/repair`（`equipment.repair`）：`createBusinessConsoleMaintenanceWorkOrder` + `listBusinessConsoleMaintenanceWorkOrders`。
- **点检** `/equipment/inspect`（`equipment.inspect`）：先选保养计划 `listBusinessConsoleMaintenancePlans` → `recordBusinessConsoleMaintenanceInspection`（+ `listBusinessConsoleMaintenanceInspections` 近期）。
- **报警查看** `/equipment/alarms`（新增 `equipment.alarms`）：只读 `listBusinessConsoleEquipmentAlarms`；行内"去报修"带 `deviceAssetId` + `sourceAlarmId` 跳报修页。

## 约定速查（执行者先读）
- **api-client 导出**（`@nerv-iip/api-client`）：`createBusinessConsoleMaintenanceWorkOrderMutationOptions`、`listBusinessConsoleMaintenanceWorkOrdersQueryOptions`、`getBusinessConsoleMaintenanceWorkOrderQueryOptions`、`completeBusinessConsoleMaintenanceWorkOrderMutationOptions`、`recordBusinessConsoleMaintenanceInspectionMutationOptions`、`listBusinessConsoleMaintenanceInspectionsQueryOptions`、`listBusinessConsoleMaintenancePlansQueryOptions`、`listBusinessConsoleEquipmentAlarmsQueryOptions`（报警；亦可 `listBusinessConsoleTelemetryAlarms`）。验证真实导出名（grep `frontend/packages/api-client/src/business-console.ts`）。
- **请求体**（均含 `organizationId`/`environmentId`，由 composable 注入）：
  - 报修 create：`{ deviceAssetId, priority, openedBy, sourceAlarmId?, assetUnavailableReason? }`（priority≤40、deviceAssetId≤100、openedBy≤100、assetUnavailableReason≤200、sourceAlarmId≤100）。**无 idempotencyKey**。
  - 点检 record：`{ inspector, result, planId? | workOrderId?（至少一个）, inspectedAtUtc? }`（inspector≤100、result≤100）。**无 idempotencyKey**。
  - list 查询：`{ organizationId, environmentId, skip(≥0), take }`（work-orders/inspections/plans take 1-200；alarms 含 `deviceAssetId?`/`status?`，take 1-500）。
- **org/env**：`useAuthStore().principal.organizationId/environmentId`；空则不发查询、空态。`openedBy`/`inspector` = `principal.loginName`。
- **无服务端幂等** → 所有 create：提交时 `disabled` 防双击 + `pending` 期间禁用 + 成功后清空/离场，避免重复工单/点检。
- **设备上下文**：报修需 `deviceAssetId`——三条路径：报警页"去报修"带入（含 `sourceAlarmId`）/ ScanBar 扫设备码 / 手输。点检需 `planId`（先选保养计划）。
- **ui-mobile**：ScanBar(`@scan`,`active`)、ListRow(`title`,`subtitle`,`interactive`,`@select`,slots)、BottomSheet(`open`,`title`,`@update:open`)、Result(`status`,`title`,`description`,actions)、AppShellMobile(header/footer/default)。
- **复用 business-core**：`mesLabels.ts` 不适用；新增设备相关标签（severity/priority/status→中文）放 business-core `labels/equipmentLabels.ts`（框架无关，PC 可复用；镜像 business-console `useBusinessEquipment` 的 severity/status 映射）。
- **页面**：`src/pages/equipment/*.vue`（自动路由，`requiresAuth:true`，不被 vite exclude）。门禁同 PDA：typecheck/test/build + e2e；UI 无工程语言（severity/status/priority 中文；alarmEventId/deviceAssetId 若为业务码可显，GUID 仅作 key）；无假数据；危险/写动作有反馈+防重。

## 文件结构
```
docs/architecture/mobile-pda-module-product-design.md / frontend-navigation-map.md   # 改：设备运维 PDA 状态
frontend/packages/business-core/src/sop/equipmentFlows.ts + test                      # 新：报修/点检 StepFlow
frontend/packages/business-core/src/labels/equipmentLabels.ts + test                  # 新：severity/status/priority 中文
frontend/packages/business-core/src/tasks/pdaTaskKinds.{ts,test.ts}                   # 改：equipment.repair/inspect routeReady=true + 新增 equipment.alarms
frontend/apps/business-pda/src/composables/
  useBusinessMaintenance.ts + test       # 新：维修工单 create/list + 点检 record/list + 保养计划 list
  useBusinessEquipmentAlarms.ts + test   # 新：报警 list（镜像 useBusinessEquipment）
frontend/apps/business-pda/src/pages/equipment/
  repair.vue  + repair.test.ts           # 故障报修
  inspect.vue + inspect.test.ts          # 点检
  alarms.vue  + alarms.test.ts           # 报警查看（只读 + 去报修）
frontend/apps/business-pda/src/pages/index.vue + index.test.ts                        # 改：点亮 3 个 equipment 入口
frontend/apps/business-pda/e2e/equipment.spec.ts                                      # 新：报修/点检/报警 e2e
```

---

## Task 1: 文档 + 点亮设备字典 + 设备 StepFlow/标签（business-core）
**Files:** docs ×2；`pdaTaskKinds.{ts,test.ts}`；新建 `sop/equipmentFlows.ts`+test、`labels/equipmentLabels.ts`+test；`src/index.ts`

- [ ] **Step 1 文档**：模块文档分期标注"设备运维 报修/点检/报警查看 已建 (Plan 4)（facade 就绪、无后端阻塞）"；导航图设备 PDA 状态同步 + 更新校验日期。
- [ ] **Step 2 字典（TDD）**：`pdaTaskKinds.test.ts` 断言 `equipment.repair`/`equipment.inspect`/`equipment.alarms` 三者 `routeReady===true`；**新增** `{ id:'equipment.alarms', label:'查看报警', group:'equipment', route:'/equipment/alarms', routeReady:true }`，并把 repair/inspect 翻 true。跑红→绿。（mes.*/wms.* 不动——本分支它们仍 false。）
- [ ] **Step 3 设备标签（TDD）**：`labels/equipmentLabels.ts`（纯 TS）导出 `alarmSeverityLabel`（critical→严重/blocked→阻塞/warning→预警/info→信息，fallback 未知）、`equipmentStateLabel`（running→运行中/idle→空闲/down→停机/faulted→故障/offline→离线/ready→就绪/stopped→停止，fallback 未知状态）、`maintenancePriorityLabel`（high→高/medium→中/low→低，fallback 原值或未知）、`maintenanceWorkOrderStatusLabel`、`inspectionResultLabel`（pass→通过/fail→不通过）。镜像 business-console `useBusinessEquipment` 的现有映射，确保 code→中文一致。test 覆盖 code→label + fallback。从 `src/index.ts` 导出。
- [ ] **Step 4 设备 StepFlow（TDD）**：`sop/equipmentFlows.ts`：
```typescript
import { defineStepFlow } from './defineStepFlow'
export interface RepairCtx { deviceAssetId?: string; priority?: string; created?: boolean }
export interface InspectCtx { planId?: string; result?: string; recorded?: boolean }
export const repairOrderFlow = defineStepFlow<RepairCtx>({
  id: 'equipment.repair',
  steps: [
    { id: 'selectDevice', done: (c) => Boolean(c.deviceAssetId) },
    { id: 'fillDetails', done: (c) => Boolean(c.priority) },
    { id: 'create', done: (c) => Boolean(c.created) },
  ],
})
export const inspectionFlow = defineStepFlow<InspectCtx>({
  id: 'equipment.inspect',
  steps: [
    { id: 'selectPlan', done: (c) => Boolean(c.planId) },
    { id: 'enterResult', done: (c) => Boolean(c.result) },
    { id: 'record', done: (c) => Boolean(c.recorded) },
  ],
})
```
  写 test（仿 `defineStepFlow.test.ts`），从 `src/index.ts` 导出 flows + ctx 类型。跑红→绿。
- [ ] **Step 5 门禁+commit**：`pnpm -C frontend --filter @nerv-iip/business-core typecheck && ... test` 绿；commit `feat(business-core): equipment flows + labels + light up PDA equipment wall`。

## Task 2: PDA 设备运维数据封装（composables）
**Files:** 新建 `business-pda/src/composables/useBusinessMaintenance.ts`+test、`useBusinessEquipmentAlarms.ts`+test

- [ ] **Step 1 composable 测试（先红，mock api-client + colada，仿 business-console useBusinessEquipment.test 风格 / 与 MES 分支同法）**：断言 (a) principal 无 org/env 时 list query `enabled:false`；(b) `createWorkOrder({deviceAssetId,priority,assetUnavailableReason?,sourceAlarmId?})` 调 create mutation，body 含注入的 `organizationId/environmentId/openedBy`（=principal.loginName）+ 入参，且**调用方不能覆盖** org/env/openedBy（注入后置 + Omit 收窄）；(c) `recordInspection({planId,result})` 同理注入 org/env/inspector；(d) 报警 `useBusinessEquipmentAlarms` 在有 scope 时查询 enabled、暴露 `alarms/pending/error/refresh`。
- [ ] **Step 2 实现**（org/env 取 `useAuthStore().principal`；scope 空不发请求）：
  - `useBusinessMaintenance()` → `{ workOrders, workOrdersTotal, workOrdersPending, workOrdersError, refreshWorkOrders, createWorkOrder(input), createPending, inspections, inspectionsPending, refreshInspections, recordInspection(input), recordPending, plans, plansPending, refreshPlans, workOrderFilters/inspectionFilters/planFilters }`。`createWorkOrder` body = `{ ...input, organizationId, environmentId, openedBy: principal.loginName }`（input 为 `Omit<CreateMaintenanceWorkOrderRequest,'organizationId'|'environmentId'|'openedBy'>`）。`recordInspection` body = `{ ...input, organizationId, environmentId, inspector: principal.loginName, inspectedAtUtc: new Date().toISOString() }`（input `Omit<...,'organizationId'|'environmentId'|'inspector'|'inspectedAtUtc'>`）。**无 idempotencyKey**（端点不支持）。
  - `useBusinessEquipmentAlarms()` → `{ filters, alarms, total, pending, error, refresh }`（消费 `listBusinessConsoleEquipmentAlarms`，可选 `deviceAssetId` 过滤）。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): maintenance + equipment-alarms data composables`。

## Task 3: 故障报修页 `/equipment/repair`
**Files:** `src/pages/equipment/repair.vue`+test
- [ ] **Step 1 测试（先红）**：mock composable + vue-router（含 `useRoute` 返回 `query:{deviceAssetId?,sourceAlarmId?}`）。断言：渲染 AppShellMobile + 近期维修工单 ListRow；新建走 `repairOrderFlow`（选/扫/带入设备→填优先级+故障描述→提交）；提交调 `createWorkOrder({deviceAssetId,priority,assetUnavailableReason,sourceAlarmId?})`（不含 org/env/openedBy）；**pending 时提交禁用**（防重）；成功 Result（"报修已提交"，actions 继续/返回）。若路由带 `deviceAssetId`/`sourceAlarmId`（来自报警页）则预填。
- [ ] **Step 2 实现**：`definePage({meta:{requiresAuth:true,title:'故障报修'}})`。设备来源：route query 预填 > ScanBar 扫码（设 deviceAssetId）> 手输。优先级用全屏/Sheet 选择（high/medium/low → 中文）。故障描述 = assetUnavailableReason。提交按钮 `:disabled="!valid || createPending"`。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): equipment repair (maintenance work order) page`。

## Task 4: 点检页 `/equipment/inspect`
**Files:** `src/pages/equipment/inspect.vue`+test
- [ ] **Step 1 测试（先红）**：`inspectionFlow` 驱动：选保养计划（`plans` list）→ 选结果（通过/不通过）→ 记录。断言提交调 `recordInspection({planId,result})`（不含注入字段）；pending 禁用防重；成功 Result。
- [ ] **Step 2 实现**：`title:'点检'`。先列 `plans` 选择（ScanBar 可扫设备/计划号过滤）→ BottomSheet 内选结果（通过/不通过）+ 可选备注 → 提交。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): equipment inspection page`。

## Task 5: 报警查看页 `/equipment/alarms`（只读 + 去报修）
**Files:** `src/pages/equipment/alarms.vue`+test
- [ ] **Step 1 测试（先红）**：渲染报警 ListRow（设备/报警码/级别中文/时间）；ScanBar 扫设备码→`filters.deviceAssetId`；行内"去报修"→`router.push('/equipment/repair', { query:{ deviceAssetId, sourceAlarmId: alarmEventId }})`；空态/加载/错误区分。只读，无写操作。
- [ ] **Step 2 实现**：`title:'查看报警'`。severity 走 `alarmSeverityLabel` 中文；alarmEventId 若 GUID 仅作 key 不外显（显示报警码/设备/时间）；"去报修" trailing 按钮带 deviceAssetId+sourceAlarmId。
- [ ] **Step 3 跑绿+commit**：`feat(business-pda): equipment alarms view + jump-to-repair`。

## Task 6: 首页点亮 3 个设备入口
**Files:** `src/pages/index.vue`+index.test.ts
- [ ] **Step 1 测试（先红）**：断言 `报修`/`点检`/`查看报警` 应用墙按钮不再 disabled、点击分别 push `/equipment/repair`|`/equipment/inspect`|`/equipment/alarms`；MES/WMS 等其余仍 disabled。
- [ ] **Step 2 实现**：`openTask(route, routeReady)` 已按 routeReady 控制——字典点亮（含新增 equipment.alarms）后自动可跳；补/确认测试（注意：home 渲染 `PDA_TASK_KINDS`，新增 alarms 条目会自动出现在墙上）。
- [ ] **Step 3 跑绿+commit**：`test(business-pda): home wall lights up equipment entries`。

## Task 7: e2e（报修/点检/报警，网关 Mock）
**Files:** `e2e/equipment.spec.ts`（+ 扩展 `e2e/fixtures.ts`）
- [ ] **Step 1**：扩展 fixtures mock `GET maintenance/work-orders|inspections|plans`、`POST maintenance/work-orders`、`POST maintenance/inspections`、`GET equipment/alarms`（realistic rows）。spec：报修（选设备→填→提交→Result）、点检（选计划→结果→Result）、报警（列表渲染→去报修带 deviceAssetId→URL `/equipment/repair?deviceAssetId=...`）、首页点"报修"→`/equipment/repair`。seedStoredSession（principal 含 org/env + loginName）。
- [ ] **Step 2**：真机 Chromium 跑 e2e + commit `test(business-pda): e2e for equipment repair/inspect/alarms`。

## Task 8: 验收 + PR
- [ ] **Step 1 门禁全绿**：business-core typecheck/test；business-pda typecheck/test/build；`... exec playwright test --list` + 真机 e2e；`pnpm -C frontend typecheck`（工作区无回归）。
- [ ] **Step 2 push + 开 PR**（base main，标题 `feat(pda): 设备运维（报修/点检/报警查看）`；body 列范围、无后端阻塞、组件复用、无服务端幂等→UI 防重、与 MES/WMS 分支并行关系）。

---

## Self-Review
- **无后端阻塞**：Maintenance（工单 create/list/detail/complete、点检 record/list、计划 list）+ Telemetry/Equipment（报警 list）facade 全就绪。
- **无服务端幂等的诚实处理**：Maintenance 端点不收 idempotencyKey → UI 层防重复提交（pending 禁用 + 成功离场），并在 PR/文档注明；不伪造幂等。
- **同源**：报修/点检 StepFlow + 设备标签落 business-core（PC 可复用）；org/env 取登录主体、空 scope 不发请求；注入字段（org/env/openedBy/inspector）调用方不可覆盖（Omit + 注入后置 + 测试）。
- **上下文穿透**：报警→报修带 deviceAssetId + sourceAlarmId（真实 facade 字段），符合导航图上下文穿透要求。
- **组件复用**：ScanBar/ListRow/BottomSheet/Result/AppShellMobile + defineStepFlow 全用上；报警映射镜像 business-console useBusinessEquipment。
- **占位符**：无 TODO；关键代码（flows/labels/composable 暴露面/各页结构/e2e mock）给出或明确镜像来源 + 真实 facade body 字段。
- **范围适中**：3 页 + 2 composable + flows/labels，8 任务；不引入 makeIdempotencyKey（端点不需要），避开与 MES 分支同名文件冲突。
```

