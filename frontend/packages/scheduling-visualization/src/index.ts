export { createLargeMockScheduleFixture, createMockGanttFixture, createMockScheduleFixture } from './model/fixtures'
export type { LargeMockScheduleFixtureOptions } from './model/fixtures'
export { flattenGanttTasks } from './model/gantt'
export { groupScheduleRows } from './model/schedule'
export { buildGanttScene } from './renderers/buildGanttScene'
export { buildScheduleScene } from './renderers/buildScheduleScene'
export { buildDependencyRoute } from './renderers/dependencyRouting'
export { renderSceneToLeafer } from './renderers/renderSceneToLeafer'
export { filterGanttFixture, filterScheduleFixture } from './state/filterFixtures'
export { createSchedulingCommandStack } from './state/useSchedulingCommands'
export { useSchedulingSelection } from './state/useSchedulingSelection'
export { createTimeScale } from './time-scale/timeScale'
export {
  buildGanttBarPositions,
  buildScheduleCalendarHighlightPositions,
  buildScheduleOperationPositions,
  buildTimelineTicks,
  calculateTimelineContentWidth,
  shiftWindowByPixels,
} from './time-scale/timelineLayout'
export { calculateVisibleRowRange } from './time-scale/visibleRange'
export { default as GanttChart } from './components/GanttChart.vue'
export { default as ScheduleChart } from './components/ScheduleChart.vue'
export { default as SchedulingDetailSheet } from './components/SchedulingDetailSheet.vue'
export { default as SchedulingLegend } from './components/SchedulingLegend.vue'
export { default as SchedulingToolbar } from './components/SchedulingToolbar.vue'
export { default as SchedulingWorkspace } from './components/SchedulingWorkspace.vue'

export type {
  ConflictSeverity,
  GanttChartProps,
  GanttConflict,
  GanttDependency,
  GanttFixture,
  GanttRow,
  GanttSelection,
  GanttTask,
  SchedulingStatus,
} from './model/gantt'
export type {
  ScheduleCapacityBand,
  ScheduleCalendarHighlight,
  ScheduleChartProps,
  ScheduleConflict,
  ScheduleDependency,
  ScheduleFixture,
  ScheduleOperation,
  ScheduleResource,
  ScheduleRow,
  ScheduleSelection,
} from './model/schedule'
export type {
  LeaferPathInput,
  LeaferRectInput,
  LeaferSurface,
  LeaferTextInput,
} from './canvas/leaferTypes'
export type {
  SchedulingScene,
  SchedulingSceneElement,
  SchedulingSceneElementKind,
  SchedulingScenePoint,
} from './canvas/sceneTypes'
export type { BuildGanttSceneOptions } from './renderers/buildGanttScene'
export type { BuildScheduleSceneOptions } from './renderers/buildScheduleScene'
export type { BuildDependencyRouteOptions, DependencyRouteRect, DependencyRouteType } from './renderers/dependencyRouting'
export type {
  SchedulingCommandStack,
  SchedulingPreviewCommand,
  SchedulingPreviewWindow,
} from './state/useSchedulingCommands'
export type {
  CreateTimeScaleOptions,
  SchedulingZoom,
  TimeScale,
  TimeScaleTick,
} from './time-scale/timeScale'
export type {
  BuildTimelineTickOptions,
  GanttBarPosition,
  ScheduleCalendarHighlightPosition,
  ScheduleOperationPosition,
  TimelineTick,
} from './time-scale/timelineLayout'
export type {
  SchedulingDetailField,
  SchedulingDetailView,
  SchedulingLinkMode,
  SchedulingWorkspaceMode,
  SchedulingWorkspaceSelection,
} from './components/types'
