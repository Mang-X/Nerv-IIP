import type { GanttFixture } from './gantt'
import type { ScheduleFixture } from './schedule'

export interface LargeMockScheduleFixtureOptions {
  resourceCount?: number
  days?: number
  operationsPerResource?: number
  dependencyEvery?: number
}

export interface LargeMockGanttFixtureOptions {
  taskCount?: number
  days?: number
  dependencyEvery?: number
}

export function createMockGanttFixture(): GanttFixture {
  return {
    id: 'gantt-release-plan',
    name: 'Manufacturing release plan',
    rangeStart: '2026-05-01T00:00:00.000Z',
    rangeEnd: '2026-05-22T00:00:00.000Z',
    tasks: [
      {
        id: 'phase-engineering',
        name: 'Engineering release',
        code: 'ENG',
        start: '2026-05-01T00:00:00.000Z',
        end: '2026-05-08T00:00:00.000Z',
        progress: 65,
        status: 'running',
        baselineStart: '2026-05-01T00:00:00.000Z',
        baselineEnd: '2026-05-07T00:00:00.000Z',
        children: [
          {
            id: 'task-ebom-release',
            parentId: 'phase-engineering',
            name: 'EBOM release',
            code: 'EBOM-REL',
            start: '2026-05-01T00:00:00.000Z',
            end: '2026-05-04T00:00:00.000Z',
            progress: 100,
            status: 'done',
            assignee: 'Engineering',
          },
          {
            id: 'task-routing-review',
            parentId: 'phase-engineering',
            name: 'Routing review',
            code: 'ROUTE-REV',
            start: '2026-05-04T00:00:00.000Z',
            end: '2026-05-08T00:00:00.000Z',
            progress: 35,
            status: 'running',
            assignee: 'Process engineering',
            conflictIds: ['conflict-routing-capacity'],
          },
        ],
      },
      {
        id: 'milestone-production-ready',
        name: 'Production ready',
        code: 'MFG-GO',
        start: '2026-05-10T00:00:00.000Z',
        end: '2026-05-10T00:00:00.000Z',
        progress: 0,
        status: 'planned',
        isMilestone: true,
      },
    ],
    dependencies: [
      {
        id: 'dep-ebom-routing',
        sourceTaskId: 'task-ebom-release',
        targetTaskId: 'task-routing-review',
        type: 'finish-start',
      },
    ],
    conflicts: [
      {
        id: 'conflict-routing-capacity',
        taskId: 'task-routing-review',
        severity: 'warning',
        title: 'Capacity warning',
        description: 'Routing review overlaps a constrained work-center window.',
        resolutionHint: 'Move review completion before the constrained shift.',
      },
    ],
  }
}

export function createMockScheduleFixture(): ScheduleFixture {
  return {
    id: 'schedule-packaging-shift',
    name: 'Packaging shift schedule',
    rangeStart: '2026-05-06T00:00:00.000Z',
    rangeEnd: '2026-05-08T00:00:00.000Z',
    resources: [
      {
        id: 'wc-pack-01',
        name: 'Packaging Line 01',
        kind: 'line',
        workCenterCode: 'PACK-01',
        capacityPerShift: 480,
        calendarLabel: 'Two-shift calendar',
      },
      {
        id: 'wc-mix-02',
        name: 'Mixing Work Center 02',
        kind: 'work-center',
        workCenterCode: 'MIX-02',
        capacityPerShift: 420,
        calendarLabel: 'Day shift calendar',
      },
    ],
    operations: [
      {
        id: 'op-packing-1001',
        resourceId: 'wc-pack-01',
        workOrderCode: 'WO-1001',
        operationCode: 'PACK',
        name: 'Pack SKU-A',
        skuCode: 'SKU-A',
        start: '2026-05-06T08:00:00.000Z',
        end: '2026-05-06T10:00:00.000Z',
        progress: 50,
        status: 'running',
        loadPercent: 72,
      },
      {
        id: 'op-packing-1002',
        resourceId: 'wc-pack-01',
        workOrderCode: 'WO-1002',
        operationCode: 'PACK',
        name: 'Pack SKU-B',
        skuCode: 'SKU-B',
        start: '2026-05-06T09:30:00.000Z',
        end: '2026-05-06T13:30:00.000Z',
        progress: 0,
        status: 'ready',
        isLocked: true,
        loadPercent: 96,
        conflictIds: ['conflict-pack-overlap', 'conflict-pack-overload'],
      },
      {
        id: 'op-mixing-2001',
        resourceId: 'wc-mix-02',
        workOrderCode: 'WO-2001',
        operationCode: 'MIX',
        name: 'Mix formula C',
        skuCode: 'SKU-C',
        start: '2026-05-06T09:00:00.000Z',
        end: '2026-05-06T12:00:00.000Z',
        progress: 15,
        status: 'running',
        loadPercent: 64,
      },
    ],
    capacityBands: [
      {
        id: 'cap-pack-morning',
        resourceId: 'wc-pack-01',
        start: '2026-05-06T08:00:00.000Z',
        end: '2026-05-06T14:00:00.000Z',
        loadPercent: 104,
        capacityPercent: 100,
        isOverloaded: true,
      },
      {
        id: 'cap-mix-morning',
        resourceId: 'wc-mix-02',
        start: '2026-05-06T08:00:00.000Z',
        end: '2026-05-06T14:00:00.000Z',
        loadPercent: 72,
        capacityPercent: 100,
        isOverloaded: false,
      },
    ],
    dependencies: [
      {
        id: 'dep-mix-pack-1001',
        sourceOperationId: 'op-mixing-2001',
        targetOperationId: 'op-packing-1001',
        type: 'finish-start',
      },
      {
        id: 'dep-pack-sequence',
        sourceOperationId: 'op-packing-1001',
        targetOperationId: 'op-packing-1002',
        type: 'finish-start',
      },
    ],
    calendarHighlights: [
      {
        id: 'highlight-pack-maintenance',
        resourceId: 'wc-pack-01',
        start: '2026-05-06T14:00:00.000Z',
        end: '2026-05-06T16:00:00.000Z',
        kind: 'maintenance',
        label: 'Planned maintenance',
        severity: 'warning',
      },
      {
        id: 'highlight-mix-downtime',
        resourceId: 'wc-mix-02',
        start: '2026-05-06T12:00:00.000Z',
        end: '2026-05-06T13:00:00.000Z',
        kind: 'downtime',
        label: 'Resource unavailable',
        severity: 'critical',
      },
    ],
    conflicts: [
      {
        id: 'conflict-pack-overlap',
        targetId: 'op-packing-1002',
        targetKind: 'operation',
        severity: 'critical',
        submitPolicy: 'block',
        reasonCode: 'resource-exclusive-overlap',
        title: 'Exclusive resource overlap',
        description: 'Two packing orders occupy the same production line window.',
        resolutionHint: 'Move one order to an available line or a later shift.',
        relatedOperationIds: ['op-packing-1001', 'op-packing-1002'],
      },
      {
        id: 'conflict-pack-overload',
        targetId: 'op-packing-1002',
        targetKind: 'operation',
        severity: 'critical',
        submitPolicy: 'block',
        reasonCode: 'capacity-overload',
        title: 'Packaging overload',
        description: 'Packed load exceeds the current capacity band.',
        resolutionHint: 'Move one packing order to the next available shift.',
        relatedOperationIds: ['op-packing-1001', 'op-packing-1002'],
      },
    ],
  }
}

export function createLargeMockGanttFixture(options: LargeMockGanttFixtureOptions = {}): GanttFixture {
  const taskCount = options.taskCount ?? 1200
  const days = options.days ?? 730
  const dependencyEvery = options.dependencyEvery ?? 6
  const phaseSize = 40
  const phaseCount = Math.ceil(taskCount / phaseSize)
  const tasks = Array.from({ length: phaseCount }, (_, phaseIndex) => {
    const children = Array.from({ length: Math.min(phaseSize, taskCount - phaseIndex * phaseSize) }, (_, childIndex) => {
      const taskIndex = phaseIndex * phaseSize + childIndex
      const startOffset = (taskIndex * 2) % Math.max(days - 8, 1)
      const duration = 2 + (taskIndex % 6)
      const start = new Date(Date.UTC(2026, 4, 1 + startOffset)).toISOString()
      const end = new Date(Date.UTC(2026, 4, 1 + startOffset + duration)).toISOString()

      return {
        id: `task-large-${taskIndex}`,
        parentId: `phase-large-${phaseIndex}`,
        name: `Release task ${taskIndex}`,
        code: `GT-${taskIndex}`,
        start,
        end,
        progress: taskIndex % 3 === 0 ? 60 : 0,
        status: taskIndex % 3 === 0 ? 'running' as const : 'planned' as const,
        baselineStart: new Date(Date.UTC(2026, 4, 1 + Math.max(startOffset - 1, 0))).toISOString(),
        baselineEnd: new Date(Date.UTC(2026, 4, 1 + startOffset + duration)).toISOString(),
      }
    })
    const phaseStart = children[0]?.start ?? '2026-05-01T00:00:00.000Z'
    const phaseEnd = children.at(-1)?.end ?? phaseStart

    return {
      id: `phase-large-${phaseIndex}`,
      name: `Large release phase ${phaseIndex + 1}`,
      code: `PH-${phaseIndex + 1}`,
      start: phaseStart,
      end: phaseEnd,
      progress: 20 + (phaseIndex % 5) * 10,
      status: phaseIndex % 2 === 0 ? 'running' as const : 'planned' as const,
      baselineStart: phaseStart,
      baselineEnd: phaseEnd,
      children,
    }
  })
  const dependencies = Array.from(
    { length: Math.max(Math.floor((taskCount - 1) / dependencyEvery), 0) },
    (_, index) => {
      const sourceIndex = index * dependencyEvery
      const targetIndex = sourceIndex + dependencyEvery
      return {
        id: `dep-large-${sourceIndex}-${targetIndex}`,
        sourceTaskId: `task-large-${sourceIndex}`,
        targetTaskId: `task-large-${targetIndex}`,
        type: 'finish-start' as const,
      }
    },
  ).filter((dependency) => Number(dependency.targetTaskId.replace('task-large-', '')) < taskCount)

  return {
    id: 'gantt-large-validation',
    name: 'Large release validation plan',
    rangeStart: '2026-05-01T00:00:00.000Z',
    rangeEnd: new Date(Date.UTC(2026, 4, 1 + days)).toISOString(),
    tasks,
    dependencies,
    conflicts: [],
  }
}

export function createLargeMockScheduleFixture(options: LargeMockScheduleFixtureOptions = {}): ScheduleFixture {
  const resourceCount = options.resourceCount ?? 1200
  const days = options.days ?? 730
  const operationsPerResource = options.operationsPerResource ?? 2
  const dependencyEvery = Math.max(options.dependencyEvery ?? 4, 1)
  const fixture = createMockScheduleFixture()
  const resources = Array.from({ length: resourceCount }, (_, index) => ({
    id: `wc-large-${index}`,
    name: `Large Work Center ${index}`,
    kind: index % 3 === 0 ? 'line' as const : 'work-center' as const,
    workCenterCode: `LWC-${index}`,
    capacityPerShift: 480,
    calendarLabel: index % 4 === 0 ? 'Two-shift calendar' : 'Day shift calendar',
  }))
  const operations = resources.flatMap((resource, resourceIndex) =>
    Array.from({ length: operationsPerResource }, (_, operationIndex) => {
      const dayOffset = (resourceIndex * 3 + operationIndex * 11) % Math.max(days - 2, 1)
      const startHour = 6 + (operationIndex % 3) * 4
      const start = new Date(Date.UTC(2026, 4, 6 + dayOffset, startHour, 0, 0)).toISOString()
      const end = new Date(Date.UTC(2026, 4, 6 + dayOffset, startHour + 3, 30, 0)).toISOString()

      return {
        id: `op-large-${resourceIndex}-${operationIndex}`,
        resourceId: resource.id,
        workOrderCode: `WO-L-${resourceIndex}-${operationIndex}`,
        operationCode: operationIndex % 2 === 0 ? 'RUN' : 'SETUP',
        name: `Large operation ${resourceIndex}-${operationIndex}`,
        skuCode: `SKU-${resourceIndex % 12}`,
        start,
        end,
        progress: operationIndex % 2 === 0 ? 25 : 0,
        status: operationIndex % 2 === 0 ? 'running' as const : 'ready' as const,
        loadPercent: 45 + (resourceIndex % 50),
      }
    }),
  )
  const capacityBands = resources
    .filter((_, index) => index % 16 === 0)
    .map((resource, index) => ({
      id: `cap-large-${resource.id}`,
      resourceId: resource.id,
      start: '2026-05-06T06:00:00.000Z',
      end: '2026-05-06T18:00:00.000Z',
      loadPercent: index % 2 === 0 ? 108 : 76,
      capacityPercent: 100,
      isOverloaded: index % 2 === 0,
    }))
  const calendarHighlights = resources
    .filter((_, index) => index % 24 === 0)
    .map((resource, index) => ({
      id: `highlight-large-${resource.id}`,
      resourceId: resource.id,
      start: '2026-05-07T08:00:00.000Z',
      end: '2026-05-07T12:00:00.000Z',
      kind: index % 2 === 0 ? 'maintenance' as const : 'downtime' as const,
      label: index % 2 === 0 ? 'Planned maintenance' : 'Resource unavailable',
      severity: index % 2 === 0 ? 'warning' as const : 'critical' as const,
    }))
  const dependencies = Array.from(
    { length: Math.max(Math.floor((resourceCount - 1) / dependencyEvery), 0) },
    (_, index) => {
      const sourceIndex = index * dependencyEvery
      const targetIndex = sourceIndex + 1

      return {
        id: `dep-large-op-${sourceIndex}-${targetIndex}`,
        sourceOperationId: `op-large-${sourceIndex}-0`,
        targetOperationId: `op-large-${targetIndex}-0`,
        type: 'finish-start' as const,
      }
    },
  ).filter((dependency) =>
    operations.some((operation) => operation.id === dependency.sourceOperationId)
    && operations.some((operation) => operation.id === dependency.targetOperationId),
  )

  return {
    ...fixture,
    id: 'schedule-large-capacity-validation',
    name: 'Large capacity validation schedule',
    rangeEnd: new Date(Date.UTC(2026, 4, 6 + days)).toISOString(),
    resources,
    operations,
    capacityBands,
    dependencies,
    calendarHighlights,
    conflicts: [],
  }
}
