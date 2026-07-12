// 公开契约 barrel。业务层只从这里消费。

export { default as GanttChart } from './components/GanttChart.vue'
export { default as ResourceSchedulerBoard } from './components/ResourceSchedulerBoard.vue'

export { useSchedulingPlan, type SchedulingContext } from './composables/useSchedulingPlan'
export { useSchedulingEdits, type SchedulingEditsDeps } from './composables/useSchedulingEdits'

export { toModel, toLockedAssignments } from './model/aps-mapper'
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
