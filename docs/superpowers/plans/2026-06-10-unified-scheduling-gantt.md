# 统一排程可视化组件 实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：** 新建 `@nerv-iip/scheduling` 包，提供引擎无关的统一接口，用 DHTMLX Gantt 试用版做 MVP，实现工单甘特与资源排产板两个组件并接入 business-console；后续切换自研引擎时只需替换适配器。

**架构：** 分为三层——Vue 组件层（稳定的 props/emits/slots）→ `SchedulingEngine` 适配器接口（DhtmlxEngine / NativeEngine）→ 数据契约层（`ScheduleModel` + `aps-mapper`）。两个适配器共同通过一套引擎契约测试，保证可替换。

**技术栈：** Vue 3 `<script setup lang="ts">` · Tailwind v4 + 设计系统 v2 令牌 · `@nerv-iip/ui` 区块 · `@nerv-iip/api-client`（business-console 排程 facade）· vite-plus（`vp`）/ vitest · Playwright · DHTMLX Gantt 9.1.4（试用、可选依赖、动态导入）。

参考规格：`docs/superpowers/specs/2026-06-10-unified-scheduling-gantt-design.md`。

---

## 文件结构

```
frontend/packages/scheduling/                 # 新包 @nerv-iip/scheduling
  package.json                                # private, type:module, exports ./src/index.ts
  tsconfig.json                               # extends ../../tsconfig.base.json
  README.md                                   # 引擎适配器契约 + 换引擎/装 DHTMLX 说明
  .gitignore                                  # vendor/(DHTMLX 试用文件不入 git)
  src/
    index.ts                                  # barrel:仅导出公开契约
    model/
      types.ts                                # ScheduleModel 及子类型 + 引擎契约类型
      aps-mapper.ts                           # toModel / toLockedAssignments(纯函数)
      aps-mapper.test.ts
      fixtures.ts                             # 测试用 SchedulePlanContract / ScheduleModel 样例
    engine/
      engine.ts                               # SchedulingEngine 接口 + 命令/事件类型
      conformance.ts                          # 引擎契约测试套件(导出 runEngineConformance)
      native/
        NativeEngine.ts                       # 轻量 SVG 渲染器(确定性,免许可)
        NativeEngine.test.ts                  # 跑 conformance
      dhtmlx/
        DhtmlxEngine.ts                       # 封装 DHTMLX vanilla 核心(动态 import)
        loader.ts                             # 动态 import + 是否可用探测
        skin.ts                               # 把设计 token 注入 DHTMLX CSS 变量
        DhtmlxEngine.conformance.test.ts      # trial 存在时跑 conformance,否则 skip
    components/
      useEngine.ts                            # provide/inject 选引擎 + 生命周期挂载
      GanttChart.vue                          # 工单甘特(view='order')
      ResourceSchedulerBoard.vue              # 资源排产板(view='resource')
      SchedulingWorkbench.vue                 # 壳:Toolbar + 视图切换 + 侧栏面板
      GanttChart.test.ts
      ResourceSchedulerBoard.test.ts
      panels/
        SchedulingToolbar.vue                 # 缩放/刻度/今天/锁定/撤销重做/重预览/发布
        ConflictPanel.vue
        UnscheduledPanel.vue
        ChangeSummaryPanel.vue
        InspectorSheet.vue
    composables/
      useSchedulingPlan.ts                    # 读计划(api-client business-console)
      useSchedulingEdits.ts                   # 锁定-重预览-发布 + 撤销栈
    styles/
      scheduling.css                          # 组件级 token 化样式(条形/网格/直方图)
  e2e-fixtures/
    plan.fixture.ts                           # E2E/视觉用确定性计划数据

frontend/apps/business-console/
  src/pages/scheduling/
    index.vue                                 # 排产工作台(默认工单甘特)
  src/composables/useScheduling.ts            # 包一层 useSchedulingPlan/Edits 给页面
  src/navigation.ts                           # + 排产工作台项
  src/pages/mes/schedules.vue                 # 导流提示到新工作台(保留规则排程触发)
  e2e/scheduling.spec.ts                      # E2E:渲染 + 交互 + 重预览
  visual/scheduling.visual.spec.ts            # 视觉回归基线
  perf/scheduling.perf.spec.ts                # 性能门禁(大数据集阈值,JSONL)

frontend/pnpm-workspace.yaml                  # 无需改(packages/* 已含)
frontend/apps/business-console/vite.config.ts # + @nerv-iip/scheduling alias
docs/architecture/scheduling-workbench-module-product-design.md  # 模块产品文档
frontend/DESIGN/components/gantt-chart.md
frontend/DESIGN/components/resource-scheduler-board.md
frontend/DESIGN/patterns/blocks/scheduling-workbench.md
docs/architecture/frontend-navigation-map.md  # + /scheduling
docs/architecture/implementation-readiness.md # 记一笔
```

---

## 阶段 P0 — 接缝先行(契约 + 映射 + 引擎接口 + 契约测试 + NativeEngine)

### Task 1：包骨架

**文件：**
- 创建： `frontend/packages/scheduling/package.json`
- 创建： `frontend/packages/scheduling/tsconfig.json`
- 创建： `frontend/packages/scheduling/.gitignore`
- 创建： `frontend/packages/scheduling/src/index.ts`

- [ ] **步骤 1：package.json**(镜像 `@nerv-iip/ui`)

```json
{
  "name": "@nerv-iip/scheduling",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": { ".": "./src/index.ts" },
  "scripts": {
    "typecheck": "vue-tsc --noEmit -p tsconfig.json",
    "test": "vp test run src"
  },
  "dependencies": {
    "@nerv-iip/api-client": "workspace:*",
    "@nerv-iip/ui": "workspace:*",
    "@vueuse/core": "^14.3.0",
    "lucide-vue-next": "1.0.0",
    "vue": "3.5.34"
  },
  "peerDependenciesMeta": { "@dhx/trial-gantt": { "optional": true } },
  "devDependencies": {
    "@vitejs/plugin-vue": "6.0.7",
    "@vue/test-utils": "2.4.10",
    "jsdom": "29.1.1",
    "vitest": "4.1.6"
  }
}
```

- [ ] **步骤 2：tsconfig.json**

```json
{
  "extends": "../../tsconfig.base.json",
  "compilerOptions": { "composite": false },
  "include": ["src/**/*.ts", "src/**/*.vue"]
}
```

- [ ] **步骤 3：.gitignore**

```
vendor/
```

- [ ] **步骤 4：src/index.ts**(占位,后续任务填充导出)

```ts
export {}
```

- [ ] **步骤 5：安装依赖并验证工作区链接**

运行：`pnpm -C frontend install`
预期：无错误，`@nerv-iip/scheduling` 出现在工作区中。

- [ ] **步骤 6：提交**

```bash
git add frontend/packages/scheduling frontend/pnpm-lock.yaml
git commit -m "feat(scheduling): scaffold @nerv-iip/scheduling package"
```

---

### Task 2：数据模型类型

**文件：**
- 创建： `frontend/packages/scheduling/src/model/types.ts`

- [ ] **步骤 1：定义 ScheduleModel 及子类型 + 引擎契约类型**

```ts
// 引擎无关的排程数据模型。所有字段为引擎可消费的归一化形态,不含任何引擎私有结构。
export type PlanStatus = 'preview' | 'generated' | 'released'
export type TimeScale = 'hour' | 'day' | 'week' | 'month' | 'auto'
export type ConflictSeverity = 'info' | 'warning' | 'error'
export type ConflictReason =
  | 'dueDate' | 'capacity' | 'calendar' | 'material' | 'quality'
  | 'equipment' | 'noEligibleResource' | 'outsideHorizon'
  | 'invalidLockedAssignment' | 'predecessorUnscheduled'
export type ChangeType = 'added' | 'moved' | 'delayed' | 'preserved' | 'blocked'

export interface ScheduleTask {
  id: string                 // assignmentId 优先,缺则 `${orderId}:${operationId}`
  orderId: string
  operationId: string
  operationSequence: number
  parentId?: string          // 工单分组父节点 id(order 视图用)
  type: 'order' | 'operation'
  text: string               // 业务化显示名(工序名/工单名)
  resourceId?: string
  workCenterId?: string
  startUtc: string
  endUtc: string
  progress?: number          // 0..1
  locked: boolean
  hasConflict: boolean
  conflictReason?: ConflictReason | null
}

export interface ScheduleLink {
  id: string
  source: string             // ScheduleTask.id
  target: string             // ScheduleTask.id
  type: 'finish_to_start'    // MVP 仅 FS,由 operationSequence 派生
}

export interface ScheduleResource {
  id: string                 // resourceId / workCenterId
  text: string               // 业务化资源名
  capacityMinutesPerDay?: number
}

export interface ResourceLoadBucket {
  resourceId: string
  windowStartUtc: string
  windowEndUtc: string
  assignedMinutes: number
  availableMinutes: number
  utilization: number        // 0..1+(>1 过载)
}

export interface ScheduleConflict {
  id: string
  reason: ConflictReason
  severity: ConflictSeverity
  orderId?: string | null
  operationId?: string | null
  resourceId?: string | null
  message: string
  taskId?: string            // 关联的 ScheduleTask.id(便于选中)
}

export interface UnscheduledItem {
  orderId: string
  operationId: string
  reason: ConflictReason
  message: string
}

export interface ScheduleChange {
  orderId: string
  operationId: string
  changeType: ChangeType
  message: string
  taskId?: string
}

export interface ScheduleModel {
  tasks: ScheduleTask[]
  links: ScheduleLink[]
  resources: ScheduleResource[]
  loads: ResourceLoadBucket[]
  conflicts: ScheduleConflict[]
  unscheduled: UnscheduledItem[]
  changes: ScheduleChange[]
  horizon: { startUtc: string; endUtc: string }
  meta: { planId: string; status: PlanStatus; algorithmVersion: string }
}
```

- [ ] **步骤 2：typecheck**

运行：`pnpm -C frontend/packages/scheduling typecheck`
预期：PASS(无类型错误)。

- [ ] **步骤 3：提交**

```bash
git add frontend/packages/scheduling/src/model/types.ts
git commit -m "feat(scheduling): add engine-agnostic ScheduleModel types"
```

---

### Task 3：APS 映射（toModel）— TDD

**文件：**
- 创建： `frontend/packages/scheduling/src/model/fixtures.ts`
- 创建： `frontend/packages/scheduling/src/model/aps-mapper.ts`
- 测试： `frontend/packages/scheduling/src/model/aps-mapper.test.ts`

- [ ] **步骤 1：fixtures.ts**(确定性样例,贴合 api-client 契约形状)

```ts
import type { NervIipContractsSchedulingSchedulePlanContract } from '@nerv-iip/api-client'

export const samplePlan: NervIipContractsSchedulingSchedulePlanContract = {
  planId: 'plan-1',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: '2026-06-10T00:00:00.000Z',
  assignments: [
    { assignmentId: 'a1', orderId: 'WO-001', operationId: 'op-10', operationSequence: 10,
      resourceId: 'WC-001', workCenterId: 'WC-001',
      startUtc: '2026-06-10T08:00:00.000Z', endUtc: '2026-06-10T10:00:00.000Z',
      isLocked: false, explanationCode: 'earliestSlot' },
    { assignmentId: 'a2', orderId: 'WO-001', operationId: 'op-20', operationSequence: 20,
      resourceId: 'WC-002', workCenterId: 'WC-002',
      startUtc: '2026-06-10T10:00:00.000Z', endUtc: '2026-06-10T12:00:00.000Z',
      isLocked: true, explanationCode: 'locked' },
  ],
  resourceLoads: [
    { resourceId: 'WC-001', windowStartUtc: '2026-06-10T00:00:00.000Z',
      windowEndUtc: '2026-06-11T00:00:00.000Z', assignedMinutes: 120, availableMinutes: 480, utilization: 0.25 },
  ],
  conflicts: [
    { conflictId: 'c1', reasonCode: 'capacity', severity: 'warning',
      orderId: 'WO-001', operationId: 'op-20', resourceId: 'WC-002', message: '产能不足' },
  ],
  unscheduledOperations: [
    { orderId: 'WO-002', operationId: 'op-10', reasonCode: 'material', message: '物料未齐套' },
  ],
  changeSummary: [
    { orderId: 'WO-001', operationId: 'op-20', changeType: 'moved', message: '后移 2 小时' },
  ],
  ganttItems: [],
}
```

- [ ] **步骤 2：写失败测试 aps-mapper.test.ts**

```ts
import { describe, expect, it } from 'vitest'
import { samplePlan } from './fixtures'
import { toModel } from './aps-mapper'

describe('toModel', () => {
  it('maps assignments to operation tasks with stable ids and grouping parents', () => {
    const m = toModel(samplePlan)
    const op = m.tasks.find((t) => t.id === 'a1')!
    expect(op.type).toBe('operation')
    expect(op.orderId).toBe('WO-001')
    expect(op.parentId).toBe('order:WO-001')
    expect(op.locked).toBe(false)
    // 工单分组父节点存在
    expect(m.tasks.some((t) => t.id === 'order:WO-001' && t.type === 'order')).toBe(true)
  })

  it('derives finish_to_start links from operationSequence within an order', () => {
    const m = toModel(samplePlan)
    expect(m.links).toEqual([{ id: 'a1->a2', source: 'a1', target: 'a2', type: 'finish_to_start' }])
  })

  it('flags conflicts onto their tasks and carries taskId', () => {
    const m = toModel(samplePlan)
    const op20 = m.tasks.find((t) => t.operationId === 'op-20')!
    expect(op20.hasConflict).toBe(true)
    expect(op20.conflictReason).toBe('capacity')
    expect(m.conflicts[0].taskId).toBe('a2')
  })

  it('maps loads, unscheduled, changes and horizon', () => {
    const m = toModel(samplePlan)
    expect(m.loads[0].utilization).toBe(0.25)
    expect(m.unscheduled[0].reason).toBe('material')
    expect(m.changes[0].changeType).toBe('moved')
    expect(m.horizon.startUtc <= '2026-06-10T08:00:00.000Z').toBe(true)
    expect(m.meta).toEqual({ planId: 'plan-1', status: 'generated', algorithmVersion: 'heuristic-1' })
  })
})
```

- [ ] **步骤 3：跑测试确认失败**

运行：`pnpm -C frontend/packages/scheduling test`
预期：FAIL("toModel is not a function")。

- [ ] **步骤 4：实现 aps-mapper.ts**

```ts
import type {
  NervIipContractsSchedulingScheduleAssignmentContract as Assignment,
  NervIipContractsSchedulingSchedulePlanContract as PlanContract,
} from '@nerv-iip/api-client'
import type {
  ConflictReason, ScheduleChange, ScheduleConflict, ScheduleLink, ScheduleModel,
  ScheduleTask, UnscheduledItem,
} from './types'

const taskId = (a: Assignment): string =>
  a.assignmentId ?? `${a.orderId ?? 'order'}:${a.operationId ?? 'op'}`
const orderNodeId = (orderId: string): string => `order:${orderId}`

export function toModel(plan: PlanContract): ScheduleModel {
  const assignments = plan.assignments ?? []
  const operations: ScheduleTask[] = assignments.map((a) => ({
    id: taskId(a),
    orderId: a.orderId ?? '',
    operationId: a.operationId ?? '',
    operationSequence: a.operationSequence ?? 0,
    parentId: a.orderId ? orderNodeId(a.orderId) : undefined,
    type: 'operation',
    text: a.operationId ?? '',
    resourceId: a.resourceId ?? a.workCenterId ?? undefined,
    workCenterId: a.workCenterId ?? undefined,
    startUtc: a.startUtc ?? '',
    endUtc: a.endUtc ?? '',
    locked: a.isLocked ?? false,
    hasConflict: false,
    conflictReason: null,
  }))

  // 工单分组父节点(order 视图):start=min,end=max
  const orderIds = [...new Set(operations.map((o) => o.orderId).filter(Boolean))]
  const orderNodes: ScheduleTask[] = orderIds.map((orderId) => {
    const kids = operations.filter((o) => o.orderId === orderId)
    return {
      id: orderNodeId(orderId), orderId, operationId: '', operationSequence: 0,
      type: 'order', text: orderId,
      startUtc: kids.reduce((m, k) => (k.startUtc < m ? k.startUtc : m), kids[0]?.startUtc ?? ''),
      endUtc: kids.reduce((m, k) => (k.endUtc > m ? k.endUtc : m), kids[0]?.endUtc ?? ''),
      locked: false, hasConflict: false, conflictReason: null,
    }
  })

  // 依赖链:同工单按 operationSequence 排序,相邻 FS
  const links: ScheduleLink[] = []
  for (const orderId of orderIds) {
    const seq = operations.filter((o) => o.orderId === orderId)
      .sort((a, b) => a.operationSequence - b.operationSequence)
    for (let i = 1; i < seq.length; i++)
      links.push({ id: `${seq[i - 1].id}->${seq[i].id}`, source: seq[i - 1].id, target: seq[i].id, type: 'finish_to_start' })
  }

  const conflicts: ScheduleConflict[] = (plan.conflicts ?? []).map((c) => {
    const t = operations.find((o) => o.orderId === c.orderId && o.operationId === c.operationId)
    return {
      id: c.conflictId ?? '', reason: (c.reasonCode ?? 'capacity') as ConflictReason,
      severity: c.severity ?? 'warning', orderId: c.orderId, operationId: c.operationId,
      resourceId: c.resourceId, message: c.message ?? '', taskId: t?.id,
    }
  })
  // 把冲突标记回 task
  for (const c of conflicts) {
    const t = operations.find((o) => o.id === c.taskId)
    if (t) { t.hasConflict = true; t.conflictReason = c.reason }
  }

  const unscheduled: UnscheduledItem[] = (plan.unscheduledOperations ?? []).map((u) => ({
    orderId: u.orderId ?? '', operationId: u.operationId ?? '',
    reason: (u.reasonCode ?? 'noEligibleResource') as ConflictReason, message: u.message ?? '',
  }))

  const changes: ScheduleChange[] = (plan.changeSummary ?? []).map((c) => {
    const t = operations.find((o) => o.orderId === c.orderId && o.operationId === c.operationId)
    return { orderId: c.orderId ?? '', operationId: c.operationId ?? '',
      changeType: c.changeType ?? 'preserved', message: c.message ?? '', taskId: t?.id }
  })

  const allStarts = operations.map((o) => o.startUtc).filter(Boolean).sort()
  const allEnds = operations.map((o) => o.endUtc).filter(Boolean).sort()

  return {
    tasks: [...orderNodes, ...operations],
    links,
    resources: [...new Set(operations.map((o) => o.resourceId).filter(Boolean) as string[])]
      .map((id) => ({ id, text: id })),
    loads: (plan.resourceLoads ?? []).map((l) => ({
      resourceId: l.resourceId ?? '', windowStartUtc: l.windowStartUtc ?? '',
      windowEndUtc: l.windowEndUtc ?? '', assignedMinutes: l.assignedMinutes ?? 0,
      availableMinutes: l.availableMinutes ?? 0, utilization: l.utilization ?? 0,
    })),
    conflicts, unscheduled, changes,
    horizon: { startUtc: allStarts[0] ?? '', endUtc: allEnds[allEnds.length - 1] ?? '' },
    meta: {
      planId: plan.planId ?? '', status: plan.status ?? 'preview',
      algorithmVersion: plan.algorithmVersion ?? '',
    },
  }
}
```

- [ ] **步骤 5：跑测试确认通过**

运行：`pnpm -C frontend/packages/scheduling test`
预期：PASS(4 个用例)。

- [ ] **步骤 6：提交**

```bash
git add frontend/packages/scheduling/src/model
git commit -m "feat(scheduling): map APS SchedulePlanContract to ScheduleModel"
```

---

### Task 4：toLockedAssignments（重预览回传）— TDD

**文件：**
- 修改： `frontend/packages/scheduling/src/model/aps-mapper.ts`
- 测试： `frontend/packages/scheduling/src/model/aps-mapper.test.ts`(追加)

- [ ] **步骤 1：追加失败测试**

```ts
import { toLockedAssignments } from './aps-mapper'

describe('toLockedAssignments', () => {
  it('emits only locked operation tasks as assignment contracts', () => {
    const m = toModel(samplePlan)
    const op = m.tasks.find((t) => t.id === 'a1')!
    op.locked = true
    op.startUtc = '2026-06-10T09:00:00.000Z'
    const out = toLockedAssignments(m)
    expect(out.map((x) => x.assignmentId)).toContain('a1')
    expect(out.map((x) => x.assignmentId)).toContain('a2') // 原本就 locked
    expect(out.find((x) => x.assignmentId === 'a1')!.startUtc).toBe('2026-06-10T09:00:00.000Z')
    // order 分组父节点不回传
    expect(out.some((x) => (x.orderId ?? '').startsWith('order:'))).toBe(false)
  })
})
```

- [ ] **步骤 2：跑测试确认失败**

运行：`pnpm -C frontend/packages/scheduling test`
预期：FAIL("toLockedAssignments is not a function")。

- [ ] **步骤 3：实现(追加到 aps-mapper.ts)**

```ts
import type { NervIipContractsSchedulingScheduleAssignmentContract as Assignment } from '@nerv-iip/api-client'

export function toLockedAssignments(model: ScheduleModel): Assignment[] {
  return model.tasks
    .filter((t) => t.type === 'operation' && t.locked)
    .map((t) => ({
      assignmentId: t.id, orderId: t.orderId, operationId: t.operationId,
      operationSequence: t.operationSequence, resourceId: t.resourceId,
      workCenterId: t.workCenterId, startUtc: t.startUtc, endUtc: t.endUtc, isLocked: true,
    }))
}
```

- [ ] **步骤 4：跑测试确认通过**

运行：`pnpm -C frontend/packages/scheduling test`
预期：PASS。

- [ ] **步骤 5：提交**

```bash
git add frontend/packages/scheduling/src/model/aps-mapper.ts frontend/packages/scheduling/src/model/aps-mapper.test.ts
git commit -m "feat(scheduling): emit locked assignments for re-preview"
```

---

### Task 5：SchedulingEngine 接口 + 命令/事件类型

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/engine.ts`

- [ ] **步骤 1：定义接口**

```ts
import type { ScheduleModel, TimeScale } from '../model/types'

export interface ThemeBinding {
  isDark: boolean
  tokens: Record<string, string>   // 关键设计 token 的解析值(--brand/--destructive/...)
}

export interface SchedulingEngineOptions {
  view: 'order' | 'resource'
  readOnly: boolean
  scale: TimeScale
  theme: ThemeBinding
  locale: 'zh' | 'en'
}

export type EngineCommand =
  | { kind: 'zoomIn' } | { kind: 'zoomOut' } | { kind: 'scaleTo'; scale: TimeScale }
  | { kind: 'scrollToToday' } | { kind: 'fitToScreen' }
  | { kind: 'selectTask'; taskId: string } | { kind: 'focusConflict'; taskId: string }
  | { kind: 'setReadOnly'; readOnly: boolean } | { kind: 'setTheme'; theme: ThemeBinding }

export interface TaskDragPayload {
  taskId: string
  operationId: string
  resourceId?: string
  startUtc: string
  endUtc: string
  kind: 'move' | 'resize' | 'reassign'
}

export interface EngineEvents {
  taskSelected: { taskId: string }
  taskDragEnd: TaskDragPayload
  scaleChanged: { scale: TimeScale }
  conflictClicked: { taskId: string }
  viewportChanged: { startUtc: string; endUtc: string }
}
export type EngineEventName = keyof EngineEvents
export type Unsubscribe = () => void

export interface EngineSnapshot { scale: TimeScale; selectedTaskId?: string }

export interface SchedulingEngine {
  mount(container: HTMLElement, options: SchedulingEngineOptions): void
  setData(model: ScheduleModel): void
  applyCommand(command: EngineCommand): void
  on<E extends EngineEventName>(event: E, cb: (payload: EngineEvents[E]) => void): Unsubscribe
  getState(): EngineSnapshot
  destroy(): void
}
```

- [ ] **步骤 2：typecheck + 提交**

运行：`pnpm -C frontend/packages/scheduling typecheck` → PASS
```bash
git add frontend/packages/scheduling/src/engine/engine.ts
git commit -m "feat(scheduling): define headless SchedulingEngine interface"
```

---

### Task 6：引擎契约测试套件

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/conformance.ts`

- [ ] **步骤 1：写可复用契约套件**(任意引擎工厂传入,断言可替换契约)

```ts
import { expect, it } from 'vitest'
import type { SchedulingEngine, SchedulingEngineOptions } from './engine'
import type { ScheduleModel } from '../model/types'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'

const baseOptions = (): SchedulingEngineOptions => ({
  view: 'order', readOnly: false, scale: 'day', locale: 'zh',
  theme: { isDark: true, tokens: { '--brand': 'oklch(0.62 0.17 255)' } },
})

export function runEngineConformance(makeEngine: () => SchedulingEngine) {
  it('mounts, renders one node per task, and exposes state', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    engine.mount(el, baseOptions())
    const model: ScheduleModel = toModel(samplePlan)
    engine.setData(model)
    expect(el.querySelectorAll('[data-task-id]').length).toBe(
      model.tasks.length,
    )
    expect(engine.getState().scale).toBe('day')
    engine.destroy()
  })

  it('applies scaleTo command and reports via scaleChanged', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    let reported: string | undefined
    engine.mount(el, baseOptions())
    engine.on('scaleChanged', (p) => { reported = p.scale })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'scaleTo', scale: 'week' })
    expect(reported).toBe('week')
    expect(engine.getState().scale).toBe('week')
    engine.destroy()
  })

  it('selectTask command emits taskSelected with normalized id', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    let selected: string | undefined
    engine.mount(el, baseOptions())
    engine.on('taskSelected', (p) => { selected = p.taskId })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'selectTask', taskId: 'a1' })
    expect(selected).toBe('a1')
    engine.destroy()
  })

  it('destroy cleans the container', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    engine.mount(el, baseOptions())
    engine.setData(toModel(samplePlan))
    engine.destroy()
    expect(el.children.length).toBe(0)
  })
}
```

- [ ] **步骤 2：提交**

```bash
git add frontend/packages/scheduling/src/engine/conformance.ts
git commit -m "test(scheduling): add engine conformance suite"
```

---

### Task 7：NativeEngine（SVG）— 通过契约套件进行 TDD

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/native/NativeEngine.ts`
- 测试： `frontend/packages/scheduling/src/engine/native/NativeEngine.test.ts`

- [ ] **步骤 1：编写测试（运行契约套件）**

```ts
import { describe } from 'vitest'
import { runEngineConformance } from '../conformance'
import { NativeEngine } from './NativeEngine'

describe('NativeEngine conformance', () => {
  runEngineConformance(() => new NativeEngine())
})
```

- [ ] **步骤 2：跑测试确认失败**

运行：`pnpm -C frontend/packages/scheduling test`
预期：FAIL("Cannot find module './NativeEngine'")。

- [ ] **步骤 3：实现 NativeEngine**（确定性 SVG；时间→x 线性映射；每个任务一个 `[data-task-id]` 矩形；行虚拟化留到性能任务）

要点(完整实现):
- `mount`：在容器中创建 `<svg>` 和两个 `<g>`（网格、条形），记录 options，并初始化事件总线 `Map<string, Set<cb>>`。
- `setData`：按 `horizon` 计算时间窗和 x 比例；工单视图按 `tasks` 顺序排列（operation 缩进到所属工单下）；每个任务绘制一个 `rect[data-task-id]`，冲突用令牌 `--destructive` 描边，选中状态使用 `--brand`；资源视图按 `resources` 排列，任务落到其 `resourceId` 行，并绘制 `loads` 直方图。点击矩形时发出 `taskSelected`/`conflictClicked` 事件。
- `applyCommand`：`scaleTo`→存储刻度并发出 `scaleChanged` 事件后重绘；`selectTask`→高亮并发出 `taskSelected` 事件；`zoomIn/Out`→在 hour↔month 之间移动刻度；`setTheme`→重写 CSS 变量并重绘；`setReadOnly`→存储标志。
- 拖拽（可编辑）：在矩形上依次触发 pointerdown→move→up，将 Δt 换算为新的 start/end（吸附到刻度粒度），再发出 `taskDragEnd`{kind}。jsdom 下没有真实指针，测试使用 `applyCommand`/合成事件覆盖此路径。
- `getState`:`{ scale, selectedTaskId }`。
- `destroy`:清空容器、清事件总线。

- [ ] **步骤 4：跑测试确认通过**

运行：`pnpm -C frontend/packages/scheduling test`
预期：PASS(conformance 4 用例)。

- [ ] **步骤 5：提交**

```bash
git add frontend/packages/scheduling/src/engine/native
git commit -m "feat(scheduling): implement deterministic NativeEngine (SVG)"
```

---

## 阶段 P1 — DHTMLX MVP(适配器 + 皮肤 + 组件)

### Task 8：DHTMLX 加载器 + 可用性探测

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/dhtmlx/loader.ts`
- 创建： `frontend/packages/scheduling/README.md`

- [ ] **步骤 1：loader.ts**（动态导入，缺失时返回 null）

```ts
// 动态加载 DHTMLX Gantt 试用核心。未安装时返回 null,使包在无许可环境仍可构建/测试。
export type GanttFactory = { getGanttInstance: () => any } | null

export async function loadGantt(): Promise<GanttFactory> {
  try {
    // @ts-expect-error 可选依赖,类型在 trial 安装后由其 .d.ts 提供
    const mod = await import('@dhx/trial-gantt')
    await import('@dhx/trial-gantt/codebase/dhtmlxgantt.css')
    const Gantt = mod.Gantt ?? mod.default?.Gantt ?? mod.default
    return Gantt && typeof Gantt.getGanttInstance === 'function' ? Gantt : null
  } catch {
    return null
  }
}

export async function isDhtmlxAvailable(): Promise<boolean> {
  return (await loadGantt()) !== null
}
```

- [ ] **步骤 2：README.md**(引擎契约 + 装 DHTMLX + 换引擎说明)

内容要点：`@nerv-iip/scheduling` 三层架构图；如何安装 DHTMLX 试用版（`npm config set @dhx:registry=https://npm.dhtmlx.com` + `pnpm add @dhx/trial-gantt`，或从 `gantt_trial/codebase` 复制到 `vendor/`）；**许可：试用版禁止分发，文件不进入 Git**；如何切换自研引擎（实现 `SchedulingEngine`，运行 `runEngineConformance`）；默认引擎选择逻辑（检测到 DHTMLX 时使用它，否则使用 NativeEngine）。

- [ ] **步骤 3：typecheck + 提交**

运行：`pnpm -C frontend/packages/scheduling typecheck` → PASS
```bash
git add frontend/packages/scheduling/src/engine/dhtmlx/loader.ts frontend/packages/scheduling/README.md
git commit -m "feat(scheduling): add DHTMLX dynamic loader and package README"
```

---

### Task 9：设计令牌 → DHTMLX 皮肤

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/dhtmlx/skin.ts`
- 创建： `frontend/packages/scheduling/src/styles/scheduling.css`

- [ ] **步骤 1：skin.ts**（把 ThemeBinding 注入 DHTMLX 容器作用域的 CSS 变量）

```ts
import type { ThemeBinding } from '../engine'

// DHTMLX 用自有 class 名;我们在容器加一个 scope class,并用 CSS 变量覆盖其关键视觉。
export const DHX_SCOPE = 'nerv-dhx-scope'

export function applySkin(container: HTMLElement, theme: ThemeBinding): void {
  container.classList.add(DHX_SCOPE)
  container.classList.toggle('nerv-dhx-dark', theme.isDark)
  for (const [k, v] of Object.entries(theme.tokens)) container.style.setProperty(k, v)
}
```

- [ ] **步骤 2：scheduling.css**（令牌化：条形/网格/今日线/直方图；`.nerv-dhx-scope .gantt_task_line` 等映射到 `var(--brand)` 等；NativeEngine 的 `.nerv-gantt-*` 也在此）

要点：所有颜色都使用 `var(--...)`，不得使用裸十六进制值或调色板颜色；冲突使用 `var(--destructive)`；关键路径辉光使用 `var(--brand)`；过载热度使用 `color-mix(in oklch, var(--warning), var(--destructive) <pct>)`；遵守 `prefers-reduced-motion`。

- [ ] **步骤 3：提交**

```bash
git add frontend/packages/scheduling/src/engine/dhtmlx/skin.ts frontend/packages/scheduling/src/styles/scheduling.css
git commit -m "feat(scheduling): bind design tokens to engine skins"
```

---

### Task 10：DhtmlxEngine 适配器

**文件：**
- 创建： `frontend/packages/scheduling/src/engine/dhtmlx/DhtmlxEngine.ts`
- 测试： `frontend/packages/scheduling/src/engine/dhtmlx/DhtmlxEngine.conformance.test.ts`

- [ ] **步骤 1：编写条件契约测试**（试用包存在时才运行，否则跳过并说明原因）

```ts
import { describe } from 'vitest'
import { runEngineConformance } from '../conformance'
import { isDhtmlxAvailable } from './loader'
import { DhtmlxEngine } from './DhtmlxEngine'

const available = await isDhtmlxAvailable()
describe.skipIf(!available)('DhtmlxEngine conformance (requires @dhx/trial-gantt)', () => {
  runEngineConformance(() => new DhtmlxEngine())
})
```

- [ ] **步骤 2：实现 DhtmlxEngine**（实现 `SchedulingEngine`；延迟加载；配置映射；事件归一化）

要点(完整实现):
- 字段：`gantt`（实例，在 mount 后由 loadGantt 延迟注入；mount 为异步操作，内部使用就绪 Promise；`setData`/`applyCommand` 在就绪后执行，并缓存最近一次入参）、事件总线、options。
- `mount`:`loadGantt()` → `Gantt.getGanttInstance()`;`applySkin(container, theme)`;config:`gantt.config.readonly=readOnly`、scale 映射、`gantt.config.layout`(order 视图标准,resource 视图加 resourcePanel + resourceLoad)、`gantt.plugins({ tooltip:true, marker:true, undo:true, critical_path: view==='order' })`;`gantt.init(container)`。绑定 DHTMLX 事件 → 归一化 emit:`onTaskClick`→taskSelected/conflictClicked;`onAfterTaskDrag`→taskDragEnd(读 `gantt.getTask`,算 kind);`onScaleAdjusted`/缩放→scaleChanged。每个 task 渲染后保证 DOM 有 `[data-task-id]`(用 `gantt.templates.task_class` 或 `task_attribute` 注入 data 属性)。
- `setData`:`toGanttData(model)`(本任务内私有函数:tasks→{id,text,start_date,duration,parent,$custom},links→{id,source,target,type:'0'});`gantt.clearAll(); gantt.parse(data)`;resource 视图额外 `gantt.serverList('resources', ...)` + assignments。
- `applyCommand`:scaleTo→改 `gantt.config.scales` + `gantt.render()` + emit;selectTask→`gantt.selectTask(id)`;zoomIn/Out→zoom 扩展或改 scale;fitToScreen→`gantt.ext.zoom`/render;setTheme→applySkin + render;scrollToToday→`gantt.showDate(new Date())`(注意:用模型 horizon 中点,避免 `Date.now` 不确定性影响测试)。
- `getState`/`destroy`(`gantt.destructor()` + 清容器/总线)。

- [ ] **步骤 3：跑测试**

运行：`pnpm -C frontend/packages/scheduling test`
预期：未安装试用包 → 跳过 DhtmlxEngine 套件（其余测试通过）；已安装试用包 → 契约测试通过。

- [ ] **步骤 4：提交**

```bash
git add frontend/packages/scheduling/src/engine/dhtmlx/DhtmlxEngine.ts frontend/packages/scheduling/src/engine/dhtmlx/DhtmlxEngine.conformance.test.ts
git commit -m "feat(scheduling): implement DHTMLX engine adapter"
```

---

### Task 11：useEngine（引擎选择 + 生命周期）

**文件：**
- 创建： `frontend/packages/scheduling/src/components/useEngine.ts`

- [ ] **步骤 1：实现**

```ts
import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { useColorMode } from '@nerv-iip/ui'   // v2 暴露的 composable
import type { ScheduleModel } from '../model/types'
import type { EngineEvents, SchedulingEngine, SchedulingEngineOptions, TimeScale } from '../engine/engine'
import { NativeEngine } from '../engine/native/NativeEngine'
import { DhtmlxEngine } from '../engine/dhtmlx/DhtmlxEngine'
import { isDhtmlxAvailable } from '../engine/dhtmlx/loader'

export type EngineKind = 'auto' | 'native' | 'dhtmlx'

function readTokens(): Record<string, string> {
  const s = getComputedStyle(document.documentElement)
  const pick = (n: string) => s.getPropertyValue(n).trim()
  return { '--brand': pick('--brand'), '--destructive': pick('--destructive'),
    '--warning': pick('--warning'), '--success': pick('--success'),
    '--muted': pick('--muted'), '--border': pick('--border'), '--foreground': pick('--foreground') }
}

export function useEngine(opts: {
  container: Ref<HTMLElement | undefined>
  model: Ref<ScheduleModel | undefined>
  view: 'order' | 'resource'
  scale: Ref<TimeScale>
  readOnly: Ref<boolean>
  engineKind?: EngineKind
  on?: Partial<{ [E in keyof EngineEvents]: (p: EngineEvents[E]) => void }>
}) {
  const engine = ref<SchedulingEngine>()
  const { isDark } = useColorMode()
  async function build(): Promise<SchedulingEngine> {
    const kind = opts.engineKind ?? 'auto'
    if (kind === 'native') return new NativeEngine()
    if (kind === 'dhtmlx') return new DhtmlxEngine()
    return (await isDhtmlxAvailable()) ? new DhtmlxEngine() : new NativeEngine()
  }
  async function init() {
    if (!opts.container.value) return
    const e = await build()
    const options: SchedulingEngineOptions = {
      view: opts.view, readOnly: opts.readOnly.value, scale: opts.scale.value,
      locale: 'zh', theme: { isDark: isDark.value, tokens: readTokens() },
    }
    e.mount(opts.container.value, options)
    for (const [name, cb] of Object.entries(opts.on ?? {})) e.on(name as keyof EngineEvents, cb as never)
    if (opts.model.value) e.setData(opts.model.value)
    engine.value = e
  }
  watch(opts.container, (el) => { if (el && !engine.value) void init() }, { immediate: true })
  watch(opts.model, (m) => { if (m) engine.value?.setData(m) })
  watch(opts.scale, (s) => engine.value?.applyCommand({ kind: 'scaleTo', scale: s }))
  watch(opts.readOnly, (r) => engine.value?.applyCommand({ kind: 'setReadOnly', readOnly: r }))
  watch(isDark, (d) => engine.value?.applyCommand({ kind: 'setTheme', theme: { isDark: d, tokens: readTokens() } }))
  onBeforeUnmount(() => engine.value?.destroy())
  return { engine }
}
```

> 注:确认 `@nerv-iip/ui` 已导出 `useColorMode`(foundation.md 列出)。若导出名不同,实施时以 `frontend/packages/ui/src/index.ts` 实际导出为准。

- [ ] **步骤 2：typecheck + 提交**

运行：`pnpm -C frontend/packages/scheduling typecheck` → PASS
```bash
git add frontend/packages/scheduling/src/components/useEngine.ts
git commit -m "feat(scheduling): add useEngine composable with auto engine selection"
```

---

### Task 12：GanttChart.vue / ResourceSchedulerBoard.vue 组件 — TDD

**文件：**
- 创建： `frontend/packages/scheduling/src/components/GanttChart.vue`
- 创建： `frontend/packages/scheduling/src/components/ResourceSchedulerBoard.vue`
- 测试： `frontend/packages/scheduling/src/components/GanttChart.test.ts`
- 测试： `frontend/packages/scheduling/src/components/ResourceSchedulerBoard.test.ts`

公开 props/emits 契约(两组件一致):
```ts
defineProps<{
  model?: ScheduleModel
  scale?: TimeScale          // 默认 'auto'
  readOnly?: boolean         // 默认 false
  loading?: boolean
  engineKind?: 'auto' | 'native' | 'dhtmlx'  // 测试注入 'native'
}>()
defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]
  conflictClick: [taskId: string]
}>()
```

- [ ] **步骤 1：写失败测试 GanttChart.test.ts**(用 `engineKind='native'`,jsdom)

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import GanttChart from './GanttChart.vue'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'

describe('GanttChart', () => {
  it('renders a node per task via native engine and emits taskSelect', async () => {
    const model = toModel(samplePlan)
    const wrapper = mount(GanttChart, { props: { model, engineKind: 'native', scale: 'day' } })
    await new Promise((r) => setTimeout(r, 0))
    const nodes = wrapper.element.querySelectorAll('[data-task-id]')
    expect(nodes.length).toBe(model.tasks.length)
  })

  it('shows loading skeleton when loading', () => {
    const wrapper = mount(GanttChart, { props: { loading: true, engineKind: 'native' } })
    expect(wrapper.find('[data-testid="gantt-skeleton"]').exists()).toBe(true)
  })
})
```

- [ ] **步骤 2：跑测试确认失败** → `pnpm -C frontend/packages/scheduling test`(FAIL:组件不存在)

- [ ] **步骤 3：实现 GanttChart.vue**（薄壳：容器 div + `useEngine({view:'order'})`，转发事件；加载时显示 `@nerv-iip/ui` 的 Skeleton，并带 `data-testid="gantt-skeleton"`；导入 `../styles/scheduling.css`）。`ResourceSchedulerBoard.vue` 使用同构实现，`view:'resource'`。

- [ ] **步骤 4：跑测试确认通过** → PASS

- [ ] **步骤 5：提交**

```bash
git add frontend/packages/scheduling/src/components/GanttChart.vue frontend/packages/scheduling/src/components/ResourceSchedulerBoard.vue frontend/packages/scheduling/src/components/*.test.ts
git commit -m "feat(scheduling): add GanttChart and ResourceSchedulerBoard components"
```

---

### Task 13：侧栏面板 + 工具栏

**文件：**
- 创建： `frontend/packages/scheduling/src/components/panels/SchedulingToolbar.vue`
- 创建： `frontend/packages/scheduling/src/components/panels/ConflictPanel.vue`
- 创建： `frontend/packages/scheduling/src/components/panels/UnscheduledPanel.vue`
- 创建： `frontend/packages/scheduling/src/components/panels/ChangeSummaryPanel.vue`
- 创建： `frontend/packages/scheduling/src/components/panels/InspectorSheet.vue`
- 测试： `frontend/packages/scheduling/src/components/panels/ConflictPanel.test.ts`

各面板契约(用 `@nerv-iip/ui` 区块:Button/Badge/StatusBadge/Sheet/ScrollArea;reason→中文业务文案映射):
- 工具栏 `SchedulingToolbar`:props `{ scale, readOnly, canUndo, canRedo, dirty }`;emits `scaleChange / zoomIn / zoomOut / today / fit / undo / redo / repreview / release / toggleReadOnly`。
- `ConflictPanel`:props `{ conflicts }`;emit `select(taskId)`;每条 reason 芯片用业务语言(capacity→"产能不足"…)。
- `UnscheduledPanel`:props `{ items }`;emit `fix(orderId, operationId)`;空态文案"全部工序已排产"。
- `ChangeSummaryPanel`:props `{ changes }`;changeType→中文(moved→"已移动"…)+ 色调语义。
- `InspectorSheet`:props `{ task, open }`;`v-model:open`;展示工单/工序/资源/起止/锁定/解释(业务语言)。

- [ ] **步骤 1：ConflictPanel 失败测试**

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ConflictPanel from './ConflictPanel.vue'

describe('ConflictPanel', () => {
  it('renders business-language reason labels and emits select', async () => {
    const wrapper = mount(ConflictPanel, { props: { conflicts: [
      { id: 'c1', reason: 'capacity', severity: 'warning', message: '产能不足', taskId: 'a2' },
    ] } })
    expect(wrapper.text()).toContain('产能')
    expect(wrapper.text()).not.toMatch(/capacity|operationId|conflictId/)
    await wrapper.find('[data-conflict-id="c1"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual(['a2'])
  })
})
```

- [ ] **步骤 2：跑失败 → 实现各面板 → 跑通过**

运行：`pnpm -C frontend/packages/scheduling test` → PASS

- [ ] **步骤 3：提交**

```bash
git add frontend/packages/scheduling/src/components/panels
git commit -m "feat(scheduling): add toolbar, conflict, unscheduled, change, inspector panels"
```

---

### Task 14：useSchedulingPlan / useSchedulingEdits 组合式函数

**文件：**
- 创建： `frontend/packages/scheduling/src/composables/useSchedulingPlan.ts`
- 创建： `frontend/packages/scheduling/src/composables/useSchedulingEdits.ts`
- 测试： `frontend/packages/scheduling/src/composables/useSchedulingEdits.test.ts`

- [ ] **步骤 1：useSchedulingPlan**（使用 api-client 的 business-console SDK 读取详情/甘特数据；返回 `{ model, loading, error, reload }`；内部调用 `toModel`）。签名：
```ts
export function useSchedulingPlan(planId: Ref<string | undefined>): {
  model: Ref<ScheduleModel | undefined>; loading: Ref<boolean>; error: Ref<unknown>; reload: () => Promise<void>
}
```

- [ ] **步骤 2：useSchedulingEdits 失败测试**（撤销/重做栈 + dirty + 乐观锁定；不访问网络，注入一个预览函数）

```ts
import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import { useSchedulingEdits } from './useSchedulingEdits'

describe('useSchedulingEdits', () => {
  it('locks a task on drag, marks dirty, supports undo/redo', () => {
    const model = ref(toModel(samplePlan))
    const edits = useSchedulingEdits(model, { preview: async (m) => m, release: async () => {} })
    expect(edits.dirty.value).toBe(false)
    edits.onTaskDragEnd({ taskId: 'a1', operationId: 'op-10', startUtc: '2026-06-10T09:00:00.000Z',
      endUtc: '2026-06-10T11:00:00.000Z', kind: 'move', resourceId: 'WC-001' })
    expect(model.value.tasks.find((t) => t.id === 'a1')!.locked).toBe(true)
    expect(edits.dirty.value).toBe(true)
    edits.undo()
    expect(model.value.tasks.find((t) => t.id === 'a1')!.startUtc).toBe('2026-06-10T08:00:00.000Z')
    edits.redo()
    expect(model.value.tasks.find((t) => t.id === 'a1')!.startUtc).toBe('2026-06-10T09:00:00.000Z')
  })
})
```

- [ ] **步骤 3：运行并确认失败 → 实现 useSchedulingEdits**（快照栈执行 push/pop；`onTaskDragEnd` 将 Δ 应用到模型 + lock + push 历史 + dirty；`repreview()` 调用注入的 `preview(toLockedAssignments)` 取回新计划 → `toModel` → 替换模型并记录冲突/变更；`release()` 调用注入的 `release`；`undo/redo` 移动指针以恢复快照）→ 运行并确认通过。

- [ ] **步骤 4：提交**

```bash
git add frontend/packages/scheduling/src/composables
git commit -m "feat(scheduling): add plan reading and edit/undo/repreview composables"
```

---

### Task 15：SchedulingWorkbench.vue + 汇总导出

**文件：**
- 创建： `frontend/packages/scheduling/src/components/SchedulingWorkbench.vue`
- 修改： `frontend/packages/scheduling/src/index.ts`
- 测试： `frontend/packages/scheduling/src/components/SchedulingWorkbench.test.ts`

- [ ] **步骤 1：SchedulingWorkbench.vue**(壳:`SchedulingToolbar` + 视图切换(工单甘特/资源排产板,用 `@nerv-iip/ui` 的 Tabs/ToggleGroup,**页内布局不进菜单**)+ 主体 GanttChart/ResourceSchedulerBoard + 右侧 ConflictPanel/UnscheduledPanel/ChangeSummaryPanel + InspectorSheet;接 `useSchedulingEdits` 事件)。props `{ planId?, model?, readOnly?, engineKind? }`。

- [ ] **步骤 2：index.ts 汇总导出**

```ts
export { default as GanttChart } from './components/GanttChart.vue'
export { default as ResourceSchedulerBoard } from './components/ResourceSchedulerBoard.vue'
export { default as SchedulingWorkbench } from './components/SchedulingWorkbench.vue'
export { useSchedulingPlan } from './composables/useSchedulingPlan'
export { useSchedulingEdits } from './composables/useSchedulingEdits'
export { toModel, toLockedAssignments } from './model/aps-mapper'
export type * from './model/types'
export type { SchedulingEngine, EngineCommand, EngineEvents, TaskDragPayload } from './engine/engine'
export { runEngineConformance } from './engine/conformance'
export { isDhtmlxAvailable } from './engine/dhtmlx/loader'
```

- [ ] **步骤 3：测试(切换视图渲染对应组件)→ 跑通过**

- [ ] **步骤 4：typecheck + test + 提交**

运行：`pnpm -C frontend/packages/scheduling typecheck && pnpm -C frontend/packages/scheduling test` → PASS
```bash
git add frontend/packages/scheduling/src/components/SchedulingWorkbench.vue frontend/packages/scheduling/src/index.ts frontend/packages/scheduling/src/components/SchedulingWorkbench.test.ts
git commit -m "feat(scheduling): add SchedulingWorkbench shell and package barrel"
```

---

## 阶段 P2 — business-console 接入

### Task 16：别名 + 依赖

**文件：**
- 修改： `frontend/apps/business-console/vite.config.ts:36`（别名配置块）
- 修改： `frontend/apps/business-console/package.json:17`（依赖项）

- [ ] **步骤 1：添加别名**（放在现有 `@nerv-iip/ui` 别名之后）

```ts
'@nerv-iip/scheduling': fileURLToPath(new URL('../../packages/scheduling/src/index.ts', import.meta.url)),
```

- [ ] **步骤 2：添加依赖**：把 `"@nerv-iip/scheduling": "workspace:*"` 加入 dependencies；运行 `pnpm -C frontend install`。

- [ ] **步骤 3：提交**

```bash
git add frontend/apps/business-console/vite.config.ts frontend/apps/business-console/package.json frontend/pnpm-lock.yaml
git commit -m "chore(business-console): wire @nerv-iip/scheduling alias and dep"
```

---

### Task 17：useScheduling 组合式函数 + 页面 + 导航

**文件：**
- 创建： `frontend/apps/business-console/src/composables/useScheduling.ts`
- 创建： `frontend/apps/business-console/src/pages/scheduling/index.vue`
- 修改： `frontend/apps/business-console/src/navigation.ts`
- 修改： `frontend/apps/business-console/src/pages/mes/schedules.vue`(导流提示)

- [ ] **步骤 1：useScheduling.ts**（封装 `useSchedulingPlan/Edits`，提供默认 planId 来源：从列表取得最新的 generated 状态计划；没有计划时显示空态）。
- [ ] **步骤 2：pages/scheduling/index.vue**(`definePage({ meta: { requiresAuth: true, title: '排产工作台' }})`;`BusinessLayout` + `PageHeader`(title 排产工作台,KPI SectionCards)+ `<SchedulingWorkbench :plan-id model ... />`;空态指引"去生成计划")。
- [ ] **步骤 3：navigation.ts** 增项(域 scheduling,路由 `/scheduling`,title 排产工作台,icon,`requiredPermissions` 与 `BusinessGatewayPermissions` 排程读权限对齐)。
- [ ] **步骤 4：schedules.vue** 顶部加 Alert/链接"前往排产工作台查看甘特与资源排产",保留规则排程触发。
- [ ] **步骤 5：类型检查 + 测试**

运行：`pnpm -C frontend/apps/business-console typecheck && pnpm -C frontend/apps/business-console test`
预期：PASS。

- [ ] **步骤 6：提交**

```bash
git add frontend/apps/business-console/src
git commit -m "feat(business-console): add scheduling workbench page, nav, composable"
```

---

## 阶段 P3 — 测试与文档

### Task 18：E2E（Playwright，默认使用 NativeEngine）

**文件：**
- 创建： `frontend/apps/business-console/e2e/scheduling.spec.ts`
- 创建： `frontend/packages/scheduling/e2e-fixtures/plan.fixture.ts`(确定性计划 JSON)

- [ ] **步骤 1：编写 E2E 测试**（沿用现有模拟模式：预置会话 + `page.route('**/api/business-console/v1/scheduling/**')` 返回固定数据响应封装；通过 URL 查询参数或 localStorage 标志强制 `engineKind=native`）

覆盖:`/scheduling` 标题"排产工作台"可见 → 甘特渲染出 `[data-task-id]` 节点 → 切换到资源排产板 → 点冲突芯片选中对应条 → 触发刻度切换(日→周)→ 拖动(合成)触发重预览出现变更摘要。

- [ ] **步骤 2：运行 E2E 测试**

运行：`pnpm -C frontend/apps/business-console e2e`(需要 chromium;无则报环境不可用,不算代码失败)
预期：scheduling.spec 在桌面端通过。

- [ ] **步骤 3：提交**

```bash
git add frontend/apps/business-console/e2e/scheduling.spec.ts frontend/packages/scheduling/e2e-fixtures
git commit -m "test(scheduling): add e2e for workbench render and interactions"
```

---

### Task 19：视觉回归基线

**文件：**
- 创建： `frontend/apps/business-console/visual/scheduling.visual.spec.ts`
- 修改： `frontend/apps/business-console/playwright.config.ts`（添加视觉测试项目 / testDir 或 grep；配置快照）

- [ ] **步骤 1：视觉规格**（NativeEngine 保证确定性；每个状态使用 `expect(page).toHaveScreenshot()`）：工单甘特亮/暗、资源排产板亮/暗、动态色变体、空态、冲突态。隐藏当前时间线等与时间相关的不确定元素（使用固定测试数据锁定 horizon，且 NativeEngine 的当前时间线使用 horizon 中点而非真实时钟）。
- [ ] **步骤 2：生成基线**

运行：`pnpm -C frontend/apps/business-console exec playwright test visual --update-snapshots`
预期：基线生成。

- [ ] **步骤 3：复跑确认稳定** → PASS
- [ ] **步骤 4：提交**（包含 `*-snapshots/`）

```bash
git add frontend/apps/business-console/visual frontend/apps/business-console/playwright.config.ts
git commit -m "test(scheduling): add visual regression baselines (light/dark/accent)"
```

---

### Task 20：性能门禁

**文件：**
- 创建： `frontend/apps/business-console/perf/scheduling.perf.spec.ts`
- 创建： `frontend/packages/scheduling/src/model/perf-fixtures.ts`(生成 ~2k 工序/200 资源)

- [ ] **步骤 1：perf-fixtures.ts**（以纯函数生成大型 ScheduleModel，使用确定性种子）
- [ ] **步骤 2：性能规格**（Playwright；测量首屏从 `setData` 到节点出现的耗时，以及滚动/缩放帧；使用 `performance.mark`；将 JSONL 写入 `frontend/apps/business-console/perf/results.jsonl`；阈值：NativeEngine 首屏 < 1500ms，超出阈值则失败）
- [ ] **步骤 3：跑 + 看 JSONL** → PASS(阈值内)
- [ ] **步骤 4：提交**

```bash
git add frontend/apps/business-console/perf frontend/packages/scheduling/src/model/perf-fixtures.ts
git commit -m "test(scheduling): add performance baseline with thresholds (JSONL)"
```

---

### Task 21：文档同步

**文件：**
- 创建： `docs/architecture/scheduling-workbench-module-product-design.md`
- 创建： `frontend/DESIGN/components/gantt-chart.md`
- 创建： `frontend/DESIGN/components/resource-scheduler-board.md`
- 创建： `frontend/DESIGN/patterns/blocks/scheduling-workbench.md`
- 修改： `frontend/DESIGN/index.md`(组件/路线图索引 + 区块表)
- 修改： `docs/architecture/frontend-navigation-map.md`(新增 /scheduling 域 + 角色矩阵)
- 修改： `docs/architecture/implementation-readiness.md`(记一笔:#78 甘特/排产前端 MVP 已落地,引擎可替换)

- [ ] **步骤 1：模块产品文档**（产品/IA/UX/分期/验收/角色/**后端缺口**：依赖关系编辑端点、产能日历端点 → 汇总 Issue 占位）。
- [ ] **步骤 2：DESIGN 组件契约**（两个组件：用途/props/emits/视觉令牌/交互/可达性/应做与不应做；增加引擎适配器契约一节，指向包 README）。
- [ ] **步骤 3：更新索引/导航图/readiness。**
- [ ] **步骤 4：提交**

```bash
git add docs/architecture frontend/DESIGN
git commit -m "docs(scheduling): module product design, DESIGN contracts, nav, readiness"
```

---

### Task 22：全门禁 + 后端缺口 Issue

- [ ] **步骤 1：全量门禁**

运行：
```
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```
预期：全绿(若遇 AGENTS 记载的既有 check/fmt 预存问题,如实区分,不算本次回归)。

- [ ] **步骤 2：创建汇总后端缺口 Issue**（依赖关系编辑端点、资源产能日历端点），在模块文档「后端缺口」中回填 Issue 编号。

```bash
gh issue create --title "Scheduling 前端 MVP 暴露的后端缺口:工序依赖编辑 + 资源产能日历" --body "..."
```

- [ ] **步骤 3：提交**（回填文档）

```bash
git add docs/architecture/scheduling-workbench-module-product-design.md
git commit -m "docs(scheduling): backfill backend gap issue references"
```

---

## 阶段 P4 — 成品确认

### Task 23：浏览器可视化确认

- [ ] **步骤 1：启动 business-console 开发服务**（`vp dev --port 5125`），使用 Claude_Preview/Chrome MCP 打开 `/scheduling`，截取工单甘特 + 资源排产板的亮色、暗色和动态色截图。
- [ ] **步骤 2：安装 DHTMLX 试用版**（`@dhx:registry` + `pnpm add @dhx/trial-gantt`，或从 `gantt_trial/codebase` 复制到 `vendor/`），切换到 `engineKind=dhtmlx`，验证真实 DHTMLX 渲染与基于设计令牌的皮肤一致。
- [ ] **步骤 3：向用户展示成品并确认**(满足高级/呼吸/创新;两视图、交互闭环、亮暗动态色)。

---

## 自审

**规格覆盖：** §3 架构→Task 5/6/7/10/11；§3.2 模型→Task 2/3/4；§4 DHTMLX/许可→Task 8/9/10；§5 UI/IA/UX→Task 12/13/15/17；§6 编辑语义→Task 14/15；§7 打包→Task 1/16；§8 测试→Task 18/19/20 + 各 TDD；§9 文档→Task 21；§10 完成定义→Task 22；§11 分期→P0–P4 对应；§12 风险（NativeEngine 兜底/不进入 Git/基于设计令牌的皮肤/性能门禁/跳过）分布于 Task 7/8/9/10/20。无遗漏。

**占位内容扫描：** 契约/映射/契约测试/关键组合式函数给出了完整代码；UI 组件给出了 props/emits 契约 + 结构 + 测试代码（薄壳，逻辑在引擎层），不含“TBD/稍后实现”。

**类型一致性：** `toModel`/`toLockedAssignments`、`SchedulingEngine`（mount/setData/applyCommand/on/getState/destroy）、`EngineEvents`（taskSelected/taskDragEnd/scaleChanged/conflictClicked/viewportChanged）、`TaskDragPayload`（taskId/operationId/resourceId/startUtc/endUtc/kind）、`ScheduleModel` 字段在各任务间一致；汇总导出与各文件定义一致。
