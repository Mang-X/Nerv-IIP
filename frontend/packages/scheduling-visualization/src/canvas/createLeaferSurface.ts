import { Group, Leafer, Pen, Rect, Text } from 'leafer-ui'

import type { LeaferPathInput, LeaferRectInput, LeaferSurface, LeaferTextInput } from './leaferTypes'

function withMetadata<T extends object>(
  input: T,
  metadata?: Record<string, string | number | boolean>,
): T & { data?: Record<string, string | number | boolean> } {
  return metadata ? { ...input, data: metadata } : input
}

export function createLeaferSurface(host: HTMLElement, width: number, height: number): LeaferSurface {
  const leafer = new Leafer({
    view: host,
    width,
    height,
    hittable: true,
    smooth: true,
    pixelRatio: window.devicePixelRatio || 1,
  })
  const root = new Group()
  leafer.add(root)

  return {
    clear() {
      root.clear()
    },
    addRect(input: LeaferRectInput) {
      root.add(
        new Rect(
          withMetadata(
            {
              x: input.x,
              y: input.y,
              width: input.width,
              height: input.height,
              fill: input.fill,
              stroke: input.stroke,
              cornerRadius: input.cornerRadius ?? 0,
            },
            input.metadata,
          ),
        ),
      )
    },
    addText(input: LeaferTextInput) {
      root.add(
        new Text(
          withMetadata(
            {
              x: input.x,
              y: input.y,
              text: input.text,
              fill: input.fill,
              fontSize: input.fontSize ?? 12,
            },
            input.metadata,
          ),
        ),
      )
    },
    addPath(input: LeaferPathInput) {
      const pen = new Pen()
      pen.setStyle({
        fill: input.fill,
        stroke: input.stroke,
        strokeWidth: input.stroke ? 2 : 0,
      })
      const [first, ...rest] = input.points
      if (first) {
        pen.moveTo(first.x, first.y)
        for (const point of rest) {
          pen.lineTo(point.x, point.y)
        }
        if (input.fill) {
          pen.closePath()
        }
      }
      pen.paint()
      if (input.metadata) {
        pen.data = input.metadata
      }
      root.add(pen)
    },
    dispose() {
      root.clear()
      const destroyable = leafer as { destroy?: () => void }
      destroyable.destroy?.()
    },
  }
}
