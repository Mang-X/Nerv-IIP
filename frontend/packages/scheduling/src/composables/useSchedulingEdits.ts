import type { ScheduleAssignmentContract } from '@nerv-iip/api-client'
import { computed, ref, type Ref } from 'vue'
import type { TaskDragPayload } from '../engine/engine'
import { toLockedAssignments } from '../model/aps-mapper'
import { cloneModel } from '../model/clone'
import type { ScheduleModel } from '../model/types'

export interface SchedulingEditsDeps {
  /** 锁定分配 → 围绕锁定项重算 → 新模型。业务层注入(对接后端 preview)。 */
  preview: (locked: ScheduleAssignmentContract[]) => Promise<ScheduleModel>
  /** 提交发布当前计划。 */
  release: (planId: string) => Promise<void>
}

/** 锁定—重预览编辑闭环 + 前端撤销/重做栈。贴合后端确定性重排能力,不做前端假持久化。 */
export function useSchedulingEdits(model: Ref<ScheduleModel>, deps: SchedulingEditsDeps) {
  const history = ref<ScheduleModel[]>([cloneModel(model.value)])
  const pointer = ref(0)
  const busy = ref(false)
  const error = ref<unknown>()

  const dirty = computed(() => pointer.value > 0)
  const canUndo = computed(() => pointer.value > 0)
  const canRedo = computed(() => pointer.value < history.value.length - 1)

  function commitSnapshot() {
    history.value = history.value.slice(0, pointer.value + 1)
    history.value.push(cloneModel(model.value))
    pointer.value = history.value.length - 1
  }

  function resetBaseline() {
    history.value = [cloneModel(model.value)]
    pointer.value = 0
  }

  /** 拖动落点:乐观更新本地模型并锁定该工序,等待重预览。 */
  function onTaskDragEnd(p: TaskDragPayload) {
    const tasks = model.value.tasks.map((t) =>
      t.id === p.taskId
        ? { ...t, startUtc: p.startUtc, endUtc: p.endUtc, resourceId: p.resourceId ?? t.resourceId, locked: true }
        : t,
    )
    model.value = { ...model.value, tasks }
    commitSnapshot()
  }

  function setLocked(taskId: string, locked: boolean) {
    const tasks = model.value.tasks.map((t) => (t.id === taskId ? { ...t, locked } : t))
    model.value = { ...model.value, tasks }
    commitSnapshot()
  }

  function undo() {
    if (!canUndo.value) return
    pointer.value -= 1
    model.value = cloneModel(history.value[pointer.value])
  }

  function redo() {
    if (!canRedo.value) return
    pointer.value += 1
    model.value = cloneModel(history.value[pointer.value])
  }

  /** 携带锁定分配重预览,用后端返回的新计划替换并以其为新基线。 */
  async function repreview() {
    busy.value = true
    error.value = undefined
    try {
      const next = await deps.preview(toLockedAssignments(model.value))
      model.value = next
      resetBaseline()
    } catch (e) {
      error.value = e
      throw e
    } finally {
      busy.value = false
    }
  }

  async function release() {
    busy.value = true
    error.value = undefined
    try {
      await deps.release(model.value.meta.planId)
      resetBaseline()
    } catch (e) {
      error.value = e
      throw e
    } finally {
      busy.value = false
    }
  }

  return { dirty, canUndo, canRedo, busy, error, onTaskDragEnd, setLocked, undo, redo, repreview, release }
}
