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

function centerX(rect: DependencyRouteRect) {
  return rect.left + rect.width / 2
}

function centerY(rect: DependencyRouteRect) {
  return rect.top + rect.height / 2
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
    y: centerY(rect),
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

function hasForwardHorizontalSpace(
  source: DependencyRouteRect,
  target: DependencyRouteRect,
  clearance: number,
) {
  return target.left - right(source) >= clearance * 2
}

function isSameRow(source: DependencyRouteRect, target: DependencyRouteRect) {
  return Math.abs(source.top - target.top) < 1
}

function isForwardOrNear(
  source: DependencyRouteRect,
  target: DependencyRouteRect,
  clearance: number,
) {
  return target.left >= right(source) - clearance * 2
}

function buildBridgeRoute(
  source: DependencyRouteRect,
  target: DependencyRouteRect,
  clearance: number,
  minimumX: number,
) {
  let sourcePort: SchedulingScenePoint
  let targetPort: SchedulingScenePoint
  let laneY: number

  if (bottom(source) <= target.top) {
    sourcePort = { x: centerX(source), y: bottom(source) }
    targetPort = { x: centerX(target), y: target.top }
    laneY = Math.round(bottom(source) + (target.top - bottom(source)) / 2)
  }
  else if (bottom(target) <= source.top) {
    sourcePort = { x: centerX(source), y: source.top }
    targetPort = { x: centerX(target), y: bottom(target) }
    laneY = Math.round(bottom(target) + (source.top - bottom(target)) / 2)
  }
  else {
    const aboveLaneY = Math.min(source.top, target.top) - clearance
    const useTopBridge = aboveLaneY >= 0
    laneY = useTopBridge
      ? aboveLaneY
      : Math.max(bottom(source), bottom(target)) + clearance
    sourcePort = { x: centerX(source), y: useTopBridge ? source.top : bottom(source) }
    targetPort = { x: centerX(target), y: useTopBridge ? target.top : bottom(target) }
  }

  return dedupePoints([
    sourcePort,
    { x: Math.max(sourcePort.x, minimumX), y: laneY },
    { x: Math.max(targetPort.x, minimumX), y: laneY },
    targetPort,
  ])
}

export function buildDependencyRoute(options: BuildDependencyRouteOptions): SchedulingScenePoint[] {
  const clearance = options.clearance ?? 12
  const minimumX = options.minimumX ?? 0
  const sourceSide = sideForSource(options.type)
  const targetSide = sideForTarget(options.type)
  const sourcePort = portFor(options.source, sourceSide)
  const targetPort = portFor(options.target, targetSide)

  if (isSameRow(options.source, options.target)) {
    if (
      sourceSide === 'right'
      && targetSide === 'left'
      && hasForwardHorizontalSpace(options.source, options.target, clearance)
    ) {
      return dedupePoints([sourcePort, targetPort])
    }

    return buildBridgeRoute(options.source, options.target, clearance, minimumX)
  }

  if (
    sourceSide === 'right'
    && targetSide === 'left'
    && !hasForwardHorizontalSpace(options.source, options.target, clearance)
    && isForwardOrNear(options.source, options.target, clearance)
  ) {
    return buildBridgeRoute(options.source, options.target, clearance, minimumX)
  }

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
