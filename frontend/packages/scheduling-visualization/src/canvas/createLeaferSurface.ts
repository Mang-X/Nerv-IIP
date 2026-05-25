import type { LeaferPathInput, LeaferRectInput, LeaferSurface, LeaferTextInput } from './leaferTypes'

type LeaferUiModule = typeof import('leafer-ui')
type LeaferRenderTarget = {
  forceRender?: () => void
  forceUpdate?: (change?: string) => void
  render?: (options?: { force?: boolean }) => void
}

let leaferUiModule: Promise<LeaferUiModule> | undefined

function loadLeaferUi() {
  leaferUiModule ??= import('leafer-ui')
  return leaferUiModule
}

function withMetadata<T extends object>(
  input: T,
  metadata?: Record<string, string | number | boolean>,
): T & { data?: Record<string, string | number | boolean> } {
  return metadata ? { ...input, data: metadata } : input
}

export async function createLeaferSurface(
  host: HTMLElement,
  width: number,
  height: number,
): Promise<LeaferSurface> {
  host.replaceChildren()
  const { Group, Leafer, Pen, Rect, Text } = await loadLeaferUi()
  const leafer = new Leafer({
    view: host,
    width,
    height,
    hittable: true,
    smooth: true,
    usePartRender: false,
    pixelRatio: window.devicePixelRatio || 1,
  })
  const root = new Group()
  leafer.add(root)
  const renderTarget = leafer as unknown as LeaferRenderTarget

  function clearCanvasPixels() {
    host.querySelectorAll('canvas').forEach((canvas) => {
      const context = canvas.getContext('2d')
      context?.clearRect(0, 0, canvas.width, canvas.height)
    })
  }

  function requestFullRender() {
    if (renderTarget.render) {
      renderTarget.render({ force: true })
      return
    }

    if (renderTarget.forceRender) {
      renderTarget.forceRender()
      return
    }

    renderTarget.forceUpdate?.('surface')
  }

  return {
    clear() {
      root.clear()
      clearCanvasPixels()
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
    flush() {
      requestFullRender()
    },
    dispose() {
      root.clear()
      const destroyable = leafer as { destroy?: () => void }
      destroyable.destroy?.()
      host.replaceChildren()
    },
  }
}
