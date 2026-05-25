import { beforeEach, describe, expect, it, vi } from 'vitest'

const leaferSpies = vi.hoisted(() => ({
  clearRect: vi.fn(),
  forceRender: vi.fn(),
  forceUpdate: vi.fn(),
  render: vi.fn(),
  rootClear: vi.fn(),
}))

vi.mock('leafer-ui', () => {
  class Group {
    add = vi.fn()
    clear = leaferSpies.rootClear
  }

  class Leafer {
    add = vi.fn()
    destroy = vi.fn()
    forceRender = leaferSpies.forceRender
    forceUpdate = leaferSpies.forceUpdate
    render = leaferSpies.render

    constructor(options: { view: HTMLElement }) {
      const canvas = document.createElement('canvas')
      canvas.width = 300
      canvas.height = 120
      Object.defineProperty(canvas, 'getContext', {
        value: () => ({ clearRect: leaferSpies.clearRect }),
      })
      options.view.appendChild(canvas)
    }
  }

  class Rect {
    constructor(_input: unknown) {}
  }

  class Text {
    constructor(_input: unknown) {}
  }

  class Pen {
    closePath = vi.fn()
    lineTo = vi.fn()
    moveTo = vi.fn()
    paint = vi.fn()
    setStyle = vi.fn()
  }

  return { Group, Leafer, Pen, Rect, Text }
})

import { createLeaferSurface } from '../canvas/createLeaferSurface'

describe('createLeaferSurface', () => {
  beforeEach(() => {
    leaferSpies.clearRect.mockClear()
    leaferSpies.forceRender.mockClear()
    leaferSpies.forceUpdate.mockClear()
    leaferSpies.render.mockClear()
    leaferSpies.rootClear.mockClear()
  })

  it('clears the scene tree without manually blanking canvas pixels', async () => {
    const host = document.createElement('div')
    const surface = await createLeaferSurface(host, 300, 120)

    surface.clear()
    surface.flush()

    expect(leaferSpies.rootClear).toHaveBeenCalledOnce()
    expect(leaferSpies.clearRect).not.toHaveBeenCalled()
    expect(leaferSpies.render).toHaveBeenCalledWith({ force: true })
  })
})
