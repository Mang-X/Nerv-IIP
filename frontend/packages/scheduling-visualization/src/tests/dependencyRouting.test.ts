import { describe, expect, it } from 'vitest'

import type { DependencyRouteRect } from '../renderers/dependencyRouting'
import { buildDependencyRoute } from '../renderers/dependencyRouting'

interface Segment {
  start: { x: number, y: number }
  end: { x: number, y: number }
}

function segments(points: { x: number, y: number }[]): Segment[] {
  return points.slice(1).map((point, index) => ({
    start: points[index],
    end: point,
  }))
}

function segmentCrossesRectInterior(segment: Segment, rect: DependencyRouteRect) {
  const left = rect.left
  const right = rect.left + rect.width
  const top = rect.top
  const bottom = rect.top + rect.height
  const minX = Math.min(segment.start.x, segment.end.x)
  const maxX = Math.max(segment.start.x, segment.end.x)
  const minY = Math.min(segment.start.y, segment.end.y)
  const maxY = Math.max(segment.start.y, segment.end.y)

  if (segment.start.y === segment.end.y) {
    return segment.start.y > top
      && segment.start.y < bottom
      && maxX > left
      && minX < right
  }

  if (segment.start.x === segment.end.x) {
    return segment.start.x > left
      && segment.start.x < right
      && maxY > top
      && minY < bottom
  }

  return false
}

function intermediateSegments(points: { x: number, y: number }[]) {
  return segments(points).slice(1, -1)
}

describe('dependency routing', () => {
  it('routes a finish-start link between rows without crossing the target task interior', () => {
    const source = { left: 220, top: 8, width: 200, height: 22 }
    const target = { left: 446, top: 52, width: 268, height: 22 }
    const route = buildDependencyRoute({ source, target, type: 'finish-start' })

    expect(route[0]).toEqual({ x: 420, y: 8 })
    expect(route.at(-1)).toEqual({ x: 446, y: 52 })
    expect(route).toContainEqual({ x: 446, y: 8 })
    expect(intermediateSegments(route).some((segment) => segmentCrossesRectInterior(segment, target))).toBe(false)
  })

  it('uses a straight boundary connector when finish and start touch across rows', () => {
    const source = { left: 249, top: 51, width: 201, height: 22 }
    const target = { left: 450, top: 95, width: 268, height: 22 }
    const route = buildDependencyRoute({ source, target, type: 'finish-start' })

    expect(route).toEqual([
      { x: 450, y: 51 },
      { x: 450, y: 95 },
    ])
  })

  it('uses a compact top-edge route when forward tasks have enough horizontal space', () => {
    const source = { left: 300, top: 52, width: 100, height: 22 }
    const target = { left: 520, top: 52, width: 120, height: 22 }
    const route = buildDependencyRoute({ source, target, type: 'finish-start' })

    expect(route).toEqual([
      { x: 400, y: 52 },
      { x: 520, y: 52 },
    ])
  })

  it('pushes route columns outside overlapping task rectangles before changing rows', () => {
    const source = { left: 300, top: 52, width: 180, height: 22 }
    const target = { left: 410, top: 52, width: 260, height: 22 }
    const route = buildDependencyRoute({ source, target, type: 'finish-start' })

    expect(route[1].x).toBeGreaterThan(target.left + target.width)
    expect(intermediateSegments(route).some((segment) => segmentCrossesRectInterior(segment, source))).toBe(false)
    expect(intermediateSegments(route).some((segment) => segmentCrossesRectInterior(segment, target))).toBe(false)
  })

  it('keeps same-row links on an external lane instead of drawing through either block', () => {
    const source = { left: 300, top: 52, width: 180, height: 22 }
    const target = { left: 430, top: 52, width: 260, height: 22 }
    const route = buildDependencyRoute({ source, target, type: 'finish-start' })

    expect(route.some((point) => point.y < source.top)).toBe(true)
    expect(intermediateSegments(route).some((segment) => segmentCrossesRectInterior(segment, source))).toBe(false)
    expect(intermediateSegments(route).some((segment) => segmentCrossesRectInterior(segment, target))).toBe(false)
  })

  it('keeps intermediate route points inside the timeline area near the frozen column', () => {
    const source = { left: 220, top: 8, width: 96, height: 22 }
    const target = { left: 220, top: 52, width: 120, height: 22 }
    const route = buildDependencyRoute({
      source,
      target,
      type: 'start-start',
      minimumX: 228,
    })

    expect(route.slice(1, -1).every((point) => point.x >= 228)).toBe(true)
  })
})
