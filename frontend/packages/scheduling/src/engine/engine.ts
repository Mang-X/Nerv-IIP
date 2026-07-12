import type { ScheduleModel, TimeScale } from '../model/types'

export type { TimeScale } from '../model/types'

// 「换引擎」的唯一接缝。DHTMLX 适配器与自研适配器都实现此 headless 接口,
// 并必须共同通过 conformance.ts 的契约测试。组件层只依赖此接口,不依赖具体引擎。

export interface ThemeBinding {
  isDark: boolean
  /** 关键设计 token 的解析值(--nv-brand / --destructive / ...),引擎据此渲染,绝不内置色。 */
  tokens: Record<string, string>
}

export interface SchedulingEngineOptions {
  view: 'order' | 'resource'
  readOnly: boolean
  scale: TimeScale
  theme: ThemeBinding
  locale: 'zh' | 'en'
  /** 资源排产板的分组维度键(对应 ScheduleModel.groupDimensions);缺省按工作中心。 */
  groupBy?: string
}

export type EngineCommand =
  | { kind: 'zoomIn' }
  | { kind: 'zoomOut' }
  | { kind: 'scaleTo'; scale: TimeScale }
  | { kind: 'scrollToToday' }
  | { kind: 'fitToScreen' }
  | { kind: 'selectTask'; taskId: string }
  | { kind: 'focusConflict'; taskId: string }
  | { kind: 'setReadOnly'; readOnly: boolean }
  | { kind: 'setTheme'; theme: ThemeBinding }
  | { kind: 'setGroupBy'; groupBy: string }

/** 拖拽结束的归一化负载,不含任何引擎私有结构——接缝处的关键契约。 */
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
  /** 用户尝试拖拽已锁定的工序(被拦截)。上层据此提示「先解锁」并聚焦该块。 */
  lockedDragAttempt: { taskId: string }
}
export type EngineEventName = keyof EngineEvents
export type Unsubscribe = () => void

export interface EngineSnapshot {
  scale: TimeScale
  selectedTaskId?: string
}

export interface SchedulingEngine {
  mount(container: HTMLElement, options: SchedulingEngineOptions): void
  setData(model: ScheduleModel): void
  applyCommand(command: EngineCommand): void
  on<E extends EngineEventName>(event: E, cb: (payload: EngineEvents[E]) => void): Unsubscribe
  getState(): EngineSnapshot
  destroy(): void
}

/** 缩放级别有序表,供 zoomIn/Out 在相邻级别移动。 */
export const SCALE_ORDER: Exclude<TimeScale, 'auto'>[] = ['hour', 'day', 'week', 'month']
