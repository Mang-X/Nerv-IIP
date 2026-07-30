// 公开契约 barrel。业务层只从这里消费。

export { default as GanttChart } from './components/GanttChart.vue'
export { default as ResourceSchedulerBoard } from './components/ResourceSchedulerBoard.vue'
export { default as SchedulingLegend } from './components/panels/SchedulingLegend.vue'
// 选中工序详情面板。业务侧的只读甘特与草案工作区共用同一份工序详情呈现,
// 不要在应用层另写一份重复实现。
export { default as TaskDetailPanel } from './components/panels/TaskDetailPanel.vue'

export { useSchedulingPlan, type SchedulingContext } from './composables/useSchedulingPlan'
export { useSchedulingEdits, type SchedulingEditsDeps } from './composables/useSchedulingEdits'

export { toModel, toLockedAssignments } from './model/aps-mapper'
export {
  deriveLegendSemantics,
  type BlockKind,
  type SchedulingLegendSemantics,
} from './model/legend'
export {
  conflictReasonLabel,
  changeTypeLabel,
  severityTone,
  changeTone,
  type StatusTone,
} from './model/labels'
export type * from './model/types'

export type {
  SchedulingEngine,
  SchedulingEngineOptions,
  EngineCommand,
  EngineEvents,
  EngineEventName,
  TaskDragPayload,
  ThemeBinding,
  EngineSnapshot,
} from './engine/engine'
export { runEngineConformance } from './engine/conformance'
export { DhtmlxEngine } from './engine/dhtmlx/DhtmlxEngine'
export { isDhtmlxAvailable, preloadGantt } from './engine/dhtmlx/loader'
