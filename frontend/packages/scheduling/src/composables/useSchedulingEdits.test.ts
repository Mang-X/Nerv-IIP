import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import type { ScheduleModel } from '../model/types'
import { useSchedulingEdits } from './useSchedulingEdits'

describe('useSchedulingEdits', () => {
  it('moves a task on drag without changing its lock state, marks dirty, supports undo/redo', () => {
    const model = ref<ScheduleModel>(toModel(samplePlan))
    const edits = useSchedulingEdits(model, {
      preview: async () => model.value,
      release: async () => {},
    })
    expect(edits.dirty.value).toBe(false)
    const before = model.value.tasks.find((t) => t.id === 'a1')!.locked

    edits.onTaskDragEnd({
      taskId: 'a1',
      operationId: 'op-10',
      startUtc: '2026-06-10T09:00:00.000Z',
      endUtc: '2026-06-10T11:00:00.000Z',
      kind: 'move',
      resourceId: 'WC-001',
    })
    // 拖拽不改锁定状态(否则拖一次即锁死、无法再拖)。
    expect(model.value.tasks.find((t) => t.id === 'a1')!.locked).toBe(before)
    expect(model.value.tasks.find((t) => t.id === 'a1')!.startUtc).toBe('2026-06-10T09:00:00.000Z')
    expect(edits.dirty.value).toBe(true)
    expect(edits.canUndo.value).toBe(true)

    edits.undo()
    expect(model.value.tasks.find((t) => t.id === 'a1')!.startUtc).toBe('2026-06-10T08:00:00.000Z')
    expect(edits.dirty.value).toBe(false)

    edits.redo()
    expect(model.value.tasks.find((t) => t.id === 'a1')!.startUtc).toBe('2026-06-10T09:00:00.000Z')
  })

  it('repreview replaces model with backend result and resets baseline', async () => {
    const model = ref<ScheduleModel>(toModel(samplePlan))
    const replaced: ScheduleModel = { ...toModel(samplePlan), meta: { planId: 'plan-2', status: 'preview', algorithmVersion: 'heuristic-1' } }
    let receivedLocked = 0
    const edits = useSchedulingEdits(model, {
      preview: async (locked) => {
        receivedLocked = locked.length
        return replaced
      },
      release: async () => {},
    })
    edits.onTaskDragEnd({ taskId: 'a1', operationId: 'op-10', startUtc: '2026-06-10T09:00:00.000Z', endUtc: '2026-06-10T11:00:00.000Z', kind: 'move' })
    edits.setLocked('a1', true) // 用户显式锁定该落点,再重预览
    await edits.repreview()
    expect(receivedLocked).toBeGreaterThanOrEqual(2) // a1(显式锁定) + a2(本就锁定)
    expect(model.value.meta.planId).toBe('plan-2')
    expect(edits.dirty.value).toBe(false)
  })
})
