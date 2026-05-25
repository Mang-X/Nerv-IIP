import type { LeaferSurface } from '../canvas/leaferTypes'
import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'

function renderRect(surface: LeaferSurface, element: SchedulingSceneElement) {
  surface.addRect({
    id: element.id,
    x: element.x,
    y: element.y,
    width: element.width ?? 1,
    height: element.height ?? 1,
    fill: element.fill,
    stroke: element.stroke,
    cornerRadius: element.kind === 'bar' || element.kind === 'progress' ? 4 : 0,
    metadata: element.metadata,
  })
}

function renderPath(surface: LeaferSurface, element: SchedulingSceneElement) {
  surface.addPath({
    id: element.id,
    points: element.points ?? [],
    fill: element.kind === 'milestone' ? element.fill : undefined,
    stroke: element.kind === 'milestone' ? element.fill : element.stroke,
    metadata: element.metadata,
  })
}

export function renderSceneToLeafer(surface: LeaferSurface, scene: SchedulingScene) {
  surface.clear()

  for (const element of scene.elements) {
    if (element.kind === 'row-label') {
      surface.addText({
        id: element.id,
        x: element.x,
        y: element.y,
        text: element.text ?? '',
        fill: element.fill,
        fontSize: 12,
        metadata: element.metadata,
      })
      continue
    }

    if (element.kind === 'dependency' || element.kind === 'milestone') {
      renderPath(surface, element)
      continue
    }

    renderRect(surface, element)
  }
}
