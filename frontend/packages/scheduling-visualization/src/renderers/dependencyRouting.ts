import type { SchedulingScenePoint } from '../canvas/sceneTypes'

export type DependencyRouteType = 'finish-start' | 'start-start' | 'finish-finish' | 'start-finish'

export interface DependencyRouteRect {
  left: number
  top: number
  width: number
  height: number
}

export interface BuildDependencyRouteOptions {
  source: DependencyRouteRect
  target: DependencyRouteRect
  type: DependencyRouteType
  clearance?: number
  minimumX?: number
}

type RouteSide = 'left' | 'right'

function right(rect: DependencyRouteRect) {
  return rect.left + rect.width
}

function bottom(rect: DependencyRouteRect) {
  return rect.top + rect.height
}

function sideForSource(type: DependencyRouteType): RouteSide {
  return type.startsWith('start') ? 'left' : 'right'
}

function sideForTarget(type: DependencyRouteType): RouteSide {
  return type.endsWith('finish') ? 'right' : 'left'
}

function portFor(rect: DependencyRouteRect, side: RouteSide): SchedulingScenePoint {
  return {
    x: side === 'left' ? rect.left : right(rect),
    y: rect.top + rect.height / 2,
  }
}

function routeXOutside(
  portX: number,
  side: RouteSide,
  clearance: number,
  obstacles: DependencyRouteRect[],
  verticalStartY: number,
  verticalEndY: number,
) {
  let x = side === 'left' ? portX - clearance : portX + clearance
  const segmentTop = Math.min(verticalStartY, verticalEndY)
  const segmentBottom = Math.max(verticalStartY, verticalEndY)

  for (const obstacle of obstacles) {
    const obstacleLeft = obstacle.left - clearance
    const obstacleRight = right(obstacle) + clearance
    const obstacleTop = obstacle.top
    const obstacleBottom = bottom(obstacle)
    const crossesObstacleY = segmentBottom > obstacleTop && segmentTop < obstacleBottom
    if (crossesObstacleY && x > obstacleLeft && x < obstacleRight) {
      x = side === 'left' ? obstacleLeft : obstacleRight
    }
  }

  return Math.max(x, 0)
}

function chooseLaneY(
  source: DependencyRouteRect,
  target: DependencyRouteRect,
  clearance: number,
) {
  const sourceBottom = bottom(source)
  const targetBottom = bottom(target)

  if (sourceBottom + clearance <= target.top) {
    return Math.round(sourceBottom + (target.top - sourceBottom) / 2)
  }

  if (targetBottom + clearance <= source.top) {
    return Math.round(targetBottom + (source.top - targetBottom) / 2)
  }

  const above = Math.min(source.top, target.top) - clearance
  if (above >= 0) {
    return above
  }

  return Math.max(sourceBottom, targetBottom) + clearance
}

function dedupePoints(points: SchedulingScenePoint[]) {
  return points.filter((point, index) => {
    const previous = points[index - 1]
    return !previous || previous.x !== point.x || previous.y !== point.y
  })
}

export function buildDependencyRoute(options: BuildDependencyRouteOptions): SchedulingScenePoint[] {
  const clearance = options.clearance ?? 12
  const minimumX = options.minimumX ?? 0
  const sourceSide = sideForSource(options.type)
  const targetSide = sideForTarget(options.type)
  const sourcePort = portFor(options.source, sourceSide)
  const targetPort = portFor(options.target, targetSide)
  const laneY = chooseLaneY(options.source, options.target, clearance)
  const sourceExitX = Math.max(
    routeXOutside(sourcePort.x, sourceSide, clearance, [options.target], sourcePort.y, laneY),
    minimumX,
  )
  const targetEntryX = Math.max(
    routeXOutside(targetPort.x, targetSide, clearance, [options.source], laneY, targetPort.y),
    minimumX,
  )

  return dedupePoints([
    sourcePort,
    { x: sourceExitX, y: sourcePort.y },
    { x: sourceExitX, y: laneY },
    { x: targetEntryX, y: laneY },
    { x: targetEntryX, y: targetPort.y },
    targetPort,
  ])
}
