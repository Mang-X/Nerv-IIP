---
title: Scheduling 排产工作台
---

<script setup>
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'
import { computed } from 'vue'

const H = 3_600_000
const base = Date.parse('2026-06-10T08:00:00.000Z')
const iso = (h) => new Date(base + h * H).toISOString()

// 样例计划(贴合 APS SchedulePlanContract 形状;仅供文档展示)。
const demoPlan = {
  planId: 'APS-2026-0610',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: iso(0),
  assignments: [
    { assignmentId: 'WO1-10', orderId: 'WO-2026-001', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(0), endUtc: iso(3), isLocked: false },
    { assignmentId: 'WO1-20', orderId: 'WO-2026-001', operationId: '折弯', operationSequence: 20, resourceId: '折弯-02', workCenterId: '折弯-02', startUtc: iso(3), endUtc: iso(6), isLocked: false },
    { assignmentId: 'WO1-30', orderId: 'WO-2026-001', operationId: '焊接', operationSequence: 30, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(6), endUtc: iso(11), isLocked: true },
    { assignmentId: 'WO2-10', orderId: 'WO-2026-002', operationId: '下料', operationSequence: 10, resourceId: '激光切割-01', workCenterId: '激光切割-01', startUtc: iso(3), endUtc: iso(7), isLocked: false },
    { assignmentId: 'WO2-20', orderId: 'WO-2026-002', operationId: '机加工', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(7), endUtc: iso(13), isLocked: false },
    { assignmentId: 'WO3-10', orderId: 'WO-2026-003', operationId: '装配', operationSequence: 10, resourceId: '焊接-01', workCenterId: '焊接-01', startUtc: iso(11), endUtc: iso(16), isLocked: false },
    { assignmentId: 'WO3-20', orderId: 'WO-2026-003', operationId: '总装', operationSequence: 20, resourceId: '加工中心-03', workCenterId: '加工中心-03', startUtc: iso(13), endUtc: iso(20), isLocked: false },
  ],
  resourceLoads: [
    { resourceId: '激光切割-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 420, availableMinutes: 480, utilization: 0.88 },
    { resourceId: '折弯-02', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 180, availableMinutes: 480, utilization: 0.38 },
    { resourceId: '焊接-01', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 600, availableMinutes: 480, utilization: 1.25 },
    { resourceId: '加工中心-03', windowStartUtc: iso(0), windowEndUtc: iso(24), assignedMinutes: 540, availableMinutes: 480, utilization: 1.13 },
  ],
  conflicts: [
    { conflictId: 'cf1', reasonCode: 'capacity', severity: 'warning', orderId: 'WO-2026-003', operationId: '装配', resourceId: '焊接-01', message: '焊接-01 在该时段超出可用产能' },
    { conflictId: 'cf2', reasonCode: 'dueDate', severity: 'error', orderId: 'WO-2026-003', operationId: '总装', resourceId: '加工中心-03', message: '预计完工晚于交期' },
  ],
  unscheduledOperations: [
    { orderId: 'WO-2026-004', operationId: '喷涂', reasonCode: 'material', message: '面漆未齐套,等待采购到货' },
  ],
  changeSummary: [
    { orderId: 'WO-2026-001', operationId: '焊接', changeType: 'preserved', message: '锁定,保持原计划' },
    { orderId: 'WO-2026-003', operationId: '总装', changeType: 'delayed', message: '受前序占用,后移 3 小时' },
  ],
  ganttItems: [],
}

// 部分卡片字段(负责人/优先级/齐套/维度归属等)当前 APS 契约未提供;此处为文档展示补样例值。
const WC = {
  '激光切割-01': { color: 'cut', device: ['DEV-L1', '激光切割机 L1'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '折弯-02': { color: 'bend', device: ['DEV-B2', '数控折弯机 B2'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '焊接-01': { color: 'weld', device: ['DEV-W1', '焊接机器人 W1'], team: ['T-B', '乙班'], line: ['LN-WELD', '焊装线'] },
  '加工中心-03': { color: 'mach', device: ['DEV-C3', '数控机床 M3'], team: ['T-B', '乙班'], line: ['LN-MACH', '机加线'] },
}
const PRODUCT = { 'WO-2026-001': '前减振器总成', 'WO-2026-002': '后桥壳体', 'WO-2026-003': '转向节' }

const model = computed(() => {
  const m = toModel(demoPlan)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = WC[t.workCenterId] ?? WC['激光切割-01']
    t.product = PRODUCT[t.orderId] ?? '通用件'
    t.quantity = 120
    t.dueUtc = iso(20)
    t.priority = t.orderId === 'WO-2026-003' ? 'high' : 'medium'
    t.kitting = 0.95
    t.load = 0.8
    t.colorKey = wc.color
    t.isRush = t.orderId === 'WO-2026-002'
    t.status = { label: t.locked ? '进行中' : '未开始', tone: t.locked ? 'info' : 'neutral' }
    t.dimensions = {
      workCenter: t.dimensions?.workCenter ?? { id: t.workCenterId, label: t.workCenterId },
      device: { id: wc.device[0], label: wc.device[1] },
      team: { id: wc.team[0], label: wc.team[1] },
      line: { id: wc.line[0], label: wc.line[1] },
    }
  }
  m.groupDimensions = [
    { key: 'workCenter', label: '工作中心' },
    { key: 'device', label: '设备' },
    { key: 'team', label: '班组' },
    { key: 'line', label: '产线' },
  ]
  return m
})
</script>

# Scheduling 排产工作台

统一接口的排产工作台,含两个视图——**工单甘特**(工单 → 工序 WBS 时间线)与**资源排产板**(一资源一泳道)。编辑走「锁定—重预览」闭环:拖动调整、锁定关键工序、按锁定项重排、发布计划。

时间轴由 DHTMLX 引擎渲染,采用「引擎无关」适配:引擎在部署时提供(开发用试用版,交付时向客户手动分发正式版)。**未配置本地引擎包时,画布区显示占位,而工具栏、视图切换、冲突/未排/变更侧栏与图例仍照常渲染**——足以确认布局与交互。

## 基础用法

传入引擎无关的 `ScheduleModel`(用 `toModel` 从 APS `SchedulePlanContract` 映射)。工作台自带工具栏、视图切换与右侧详情/冲突面板。

<ClientOnly>
<Demo>
  <div style="height: 560px; width: 100%">
    <SchedulingWorkbench :model="model" default-view="order" />
  </div>
</Demo>
</ClientOnly>

```vue
<script setup lang="ts">
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'
import type { SchedulePlanContract } from '@nerv-iip/api-client'

// plan 来自 APS facade(BusinessGateway list/detail/gantt)
const model = toModel(plan as SchedulePlanContract)

// 后端「带锁定重预览」/「发布」由业务层注入;缺省不注入则工具栏隐藏这两个动作。
async function release(planId: string) {
  await releaseBusinessConsoleSchedulingPlan({ path: { planId } })
}
</script>

<template>
  <SchedulingWorkbench :model="model" default-view="order" :release="release" />
</template>
```

## 两个视图

| 视图 | 组件 | 用途 |
|---|---|---|
| 工单甘特 | `GanttChart` | 工单 → 工序 WBS 时间线,看依赖链、进度与瓶颈;给跟单/管理看。 |
| 资源排产板 | `ResourceSchedulerBoard` | 一资源一泳道,左轴维度可切换(工作中心 / 设备 / 班组 / 产线);看机台负载与换型。 |

两者可经 `SchedulingWorkbench` 组合(带工具栏与面板),也可单独使用,由父层自行组织工具栏与详情。

## 数据模型

组件只消费引擎无关的 `ScheduleModel`,不直接碰 APS 契约细节。用 `toModel(plan)` 从 `SchedulePlanContract` 映射;换引擎只换适配器,组件层与业务层零改动。

映射只采用契约真实字段;当前 APS 契约未提供的卡片字段(负责人、优先级、齐套率、换型时间、资源占用等)留空、**不伪造**,待后端补齐后自动填充。

## 编辑闭环

「锁定—重预览」贴合后端确定性有限产能重排:

1. 拖动工序改时间(横向)或改派资源(跨泳道);
2. 在详情面板显式**锁定**要保留的工序;
3. 工具栏「重新排程」→ 携锁定项调用后端 `preview` 重算 → 变更摘要 / 冲突刷新;
4. 「发布计划」提交 `release`。撤销/重做为前端快照栈。

`preview` / `release` 由业务层注入;**未注入时工具栏隐藏对应动作**,避免无后端时的空操作。

## 属性

### SchedulingWorkbench

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `model` | 排程数据模型(`toModel` 输出) | `ScheduleModel` | — |
| `loading` | 加载态(骨架占位) | `boolean` | `false` |
| `readOnly` | 只读(禁用拖拽编辑) | `boolean` | `false` |
| `defaultView` | 初始视图 | `'order' \| 'resource'` | `'order'` |
| `engineKind` | 渲染引擎选择 | `'auto' \| 'dhtmlx'` | `'auto'` |
| `preview` | 携锁定分配重预览(注入后端);缺省则隐藏「重新排程」 | `(locked: ScheduleAssignmentContract[]) => Promise<ScheduleModel>` | — |
| `release` | 发布当前计划;缺省则隐藏「发布」 | `(planId: string) => Promise<void>` | — |

**Emits**:`fix(orderId, operationId)` — 未排产项点击「去处理」时抛出,供页面跳转补料/改派。

### GanttChart / ResourceSchedulerBoard

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `model` | 排程数据模型 | `ScheduleModel` | — |
| `scale` | 时间刻度 | `'auto' \| 'hour' \| 'day' \| 'week' \| 'month'` | `'auto'` |
| `readOnly` | 只读 | `boolean` | `false` |
| `loading` | 加载态 | `boolean` | `false` |
| `engineKind` | 渲染引擎选择 | `'auto' \| 'dhtmlx'` | `'auto'` |

**Emits**:`taskSelect(taskId)`、`taskDragEnd(payload)`(落点归一化,不泄露引擎结构)、`conflictClick(taskId)`。
**Expose**:`command(cmd)` — 下发 `zoomIn`/`zoomOut`/`scaleTo`/`scrollToToday`/`fitToScreen`/`selectTask` 等引擎命令。

## 引擎

引擎无关设计:组件层只依赖 `SchedulingEngine` 接口(`mount`/`setData`/`applyCommand`/`on`/`destroy`),不依赖具体实现。本包对接 DHTMLX Gantt 专业版;正式自研引擎在后续 PR 落地,届时只增适配器、组件层不改动。DHTMLX 评估许可禁止分发,库文件不入库——无本地 vendor 时组件优雅占位。
