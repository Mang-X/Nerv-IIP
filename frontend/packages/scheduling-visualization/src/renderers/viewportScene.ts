import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'

export interface SceneViewport {
  scrollLeft: number
  scrollTop: number
  width: number
  height: number
}

function elementBounds(element: SchedulingSceneElement) {
  if (element.points?.length) {
    const xs = element.points.map((point) => point.x)
    const ys = element.points.map((point) => point.y)
    const minX = Math.min(...xs)
    const maxX = Math.max(...xs)
    const minY = Math.min(...ys)
    const maxY = Math.max(...ys)

    return {
      left: minX,
      top: minY,
      right: maxX,
      bottom: maxY,
    }
  }

  return {
    left: element.x,
    top: element.y,
    right: element.x + (element.width ?? 1),
    bottom: element.y + (element.height ?? 1),
  }
}

function intersectsViewport(element: SchedulingSceneElement, viewport: SceneViewport) {
  const bounds = elementBounds(element)
  const viewportRight = viewport.scrollLeft + viewport.width
  const viewportBottom = viewport.scrollTop + viewport.height

  return bounds.right >= viewport.scrollLeft
    && bounds.left <= viewportRight
    && bounds.bottom >= viewport.scrollTop
    && bounds.top <= viewportBottom
}

function translateElement(element: SchedulingSceneElement, viewport: SceneViewport): SchedulingSceneElement {
  return {
    ...element,
    x: element.x - viewport.scrollLeft,
    y: element.y - viewport.scrollTop,
    points: element.points?.map((point) => ({
      x: point.x - viewport.scrollLeft,
      y: point.y - viewport.scrollTop,
    })),
  }
}

export function buildViewportScene(scene: SchedulingScene, viewport: SceneViewport): SchedulingScene {
  return {
    ...scene,
    width: viewport.width,
    height: viewport.height,
    elements: scene.elements
      .filter((element) => intersectsViewport(element, viewport))
      .map((element) => translateElement(element, viewport)),
  }
}
