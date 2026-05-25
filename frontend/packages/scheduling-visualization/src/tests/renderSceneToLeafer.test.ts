import { describe, expect, it, vi } from 'vitest'

import type { SchedulingScene } from '../canvas/sceneTypes'
import type { LeaferSurface } from '../canvas/leaferTypes'
import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'

describe('renderSceneToLeafer', () => {
  it('clears the surface and adds canvas elements', () => {
    const surface: LeaferSurface = {
      clear: vi.fn(),
      addRect: vi.fn(),
      addText: vi.fn(),
      addPath: vi.fn(),
      flush: vi.fn(),
      dispose: vi.fn(),
    }
    const scene: SchedulingScene = {
      width: 400,
      height: 160,
      rowHeight: 40,
      elements: [
        { id: 'row-1', kind: 'row-label', x: 0, y: 0, text: 'Work center', fill: '#111827' },
        { id: 'bar-1', kind: 'bar', x: 120, y: 12, width: 100, height: 16, fill: '#2563eb' },
        {
          id: 'dep-1',
          kind: 'dependency',
          x: 0,
          y: 0,
          stroke: '#64748b',
          points: [
            { x: 120, y: 20 },
            { x: 180, y: 20 },
            { x: 180, y: 60 },
          ],
        },
      ],
    }

    renderSceneToLeafer(surface, scene)

    expect(surface.clear).toHaveBeenCalledOnce()
    expect(surface.addText).toHaveBeenCalledWith(expect.objectContaining({ id: 'row-1' }))
    expect(surface.addRect).toHaveBeenCalledWith(expect.objectContaining({ id: 'bar-1' }))
    expect(surface.addPath).toHaveBeenCalledWith(expect.objectContaining({ id: 'dep-1' }))
    expect(surface.flush).toHaveBeenCalledOnce()
    expect(vi.mocked(surface.flush).mock.invocationCallOrder[0])
      .toBeGreaterThan(vi.mocked(surface.addPath).mock.invocationCallOrder[0])
  })
})
