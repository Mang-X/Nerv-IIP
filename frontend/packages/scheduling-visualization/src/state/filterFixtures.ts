import type { GanttFixture, GanttTask } from '../model/gantt'
import type { ScheduleFixture } from '../model/schedule'

function normalizeQuery(query?: string) {
  return query?.trim().toLowerCase() ?? ''
}

function includesQuery(value: string | undefined, query: string) {
  return value?.toLowerCase().includes(query) ?? false
}

function filterTaskTree(tasks: GanttTask[], query: string): GanttTask[] {
  return tasks.flatMap((task) => {
    const filteredChildren = filterTaskTree(task.children ?? [], query)
    const matches = [
      task.name,
      task.code,
      task.assignee,
      task.status,
    ].some((value) => includesQuery(value, query))

    if (matches) {
      return [{ ...task }]
    }

    if (filteredChildren.length > 0) {
      return [{ ...task, children: filteredChildren }]
    }

    return []
  })
}

function collectTaskIds(tasks: GanttTask[], ids = new Set<string>()) {
  for (const task of tasks) {
    ids.add(task.id)
    collectTaskIds(task.children ?? [], ids)
  }

  return ids
}

export function filterGanttFixture(fixture: GanttFixture, query?: string): GanttFixture {
  const normalizedQuery = normalizeQuery(query)
  if (!normalizedQuery) {
    return fixture
  }

  const tasks = filterTaskTree(fixture.tasks, normalizedQuery)
  const taskIds = collectTaskIds(tasks)
  const conflicts = fixture.conflicts.filter((conflict) => taskIds.has(conflict.taskId))
  const conflictIds = new Set(conflicts.map((conflict) => conflict.id))

  return {
    ...fixture,
    tasks,
    dependencies: fixture.dependencies.filter(
      (dependency) => taskIds.has(dependency.sourceTaskId) && taskIds.has(dependency.targetTaskId),
    ),
    conflicts,
  }
}

export function filterScheduleFixture(fixture: ScheduleFixture, query?: string): ScheduleFixture {
  const normalizedQuery = normalizeQuery(query)
  if (!normalizedQuery) {
    return fixture
  }

  const operations = fixture.operations.filter((operation) =>
    [
      operation.name,
      operation.workOrderCode,
      operation.operationCode,
      operation.skuCode,
      operation.status,
    ].some((value) => includesQuery(value, normalizedQuery)),
  )
  const matchingResourceIds = new Set(operations.map((operation) => operation.resourceId))
  for (const resource of fixture.resources) {
    if (
      [
        resource.name,
        resource.workCenterCode,
        resource.kind,
        resource.calendarLabel,
      ].some((value) => includesQuery(value, normalizedQuery))
    ) {
      matchingResourceIds.add(resource.id)
    }
  }

  const resources = fixture.resources.filter((resource) => matchingResourceIds.has(resource.id))
  const resourceIds = new Set(resources.map((resource) => resource.id))
  const operationIds = new Set(operations.map((operation) => operation.id))

  return {
    ...fixture,
    resources,
    operations: operations.filter((operation) => resourceIds.has(operation.resourceId)),
    capacityBands: fixture.capacityBands.filter((band) => resourceIds.has(band.resourceId)),
    conflicts: fixture.conflicts.filter((conflict) =>
      conflict.targetKind === 'operation'
        ? operationIds.has(conflict.targetId)
        : resourceIds.has(conflict.targetId),
    ),
  }
}

