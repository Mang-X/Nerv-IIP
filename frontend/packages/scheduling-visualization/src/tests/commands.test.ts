import { describe, expect, it } from 'vitest'

import { createSchedulingCommandStack } from '../state/useSchedulingCommands'

describe('createSchedulingCommandStack', () => {
  it('applies move preview commands and supports undo and redo', () => {
    const stack = createSchedulingCommandStack()

    stack.execute({
      id: 'cmd-move-routing',
      targetId: 'task-routing-review',
      kind: 'move',
      before: { start: '2026-05-04T00:00:00.000Z', end: '2026-05-08T00:00:00.000Z' },
      after: { start: '2026-05-05T00:00:00.000Z', end: '2026-05-09T00:00:00.000Z' },
    })

    expect(stack.previewById.value['task-routing-review']).toEqual({
      start: '2026-05-05T00:00:00.000Z',
      end: '2026-05-09T00:00:00.000Z',
    })

    stack.undo()
    expect(stack.previewById.value['task-routing-review']).toBeUndefined()

    stack.redo()
    expect(stack.previewById.value['task-routing-review']?.start).toBe(
      '2026-05-05T00:00:00.000Z',
    )
  })

  it('clears previews with reset', () => {
    const stack = createSchedulingCommandStack()

    stack.execute({
      id: 'cmd-resize',
      targetId: 'op-packing-1001',
      kind: 'resize',
      before: { start: '2026-05-06T08:00:00.000Z', end: '2026-05-06T10:00:00.000Z' },
      after: { start: '2026-05-06T08:00:00.000Z', end: '2026-05-06T11:00:00.000Z' },
    })
    stack.reset()

    expect(stack.canUndo.value).toBe(false)
    expect(stack.canRedo.value).toBe(false)
    expect(stack.previewById.value).toEqual({})
  })
})
