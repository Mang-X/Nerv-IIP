import { computed, shallowRef } from 'vue'
import type { ComputedRef, ShallowRef } from 'vue'

export interface SchedulingPreviewWindow {
  start: string
  end: string
  resourceId?: string
}

export interface SchedulingPreviewCommand {
  id: string
  targetId: string
  kind: 'move' | 'resize'
  before: SchedulingPreviewWindow
  after: SchedulingPreviewWindow
}

export interface SchedulingCommandStack {
  previewById: ShallowRef<Record<string, SchedulingPreviewWindow>>
  canUndo: ComputedRef<boolean>
  canRedo: ComputedRef<boolean>
  execute(command: SchedulingPreviewCommand): void
  undo(): void
  redo(): void
  reset(): void
}

function buildPreview(commands: SchedulingPreviewCommand[]): Record<string, SchedulingPreviewWindow> {
  return commands.reduce<Record<string, SchedulingPreviewWindow>>((preview, command) => {
    preview[command.targetId] = command.after
    return preview
  }, {})
}

export function createSchedulingCommandStack(): SchedulingCommandStack {
  const done = shallowRef<SchedulingPreviewCommand[]>([])
  const undone = shallowRef<SchedulingPreviewCommand[]>([])
  const previewById = shallowRef<Record<string, SchedulingPreviewWindow>>({})

  function syncPreview() {
    previewById.value = buildPreview(done.value)
  }

  return {
    previewById,
    canUndo: computed(() => done.value.length > 0),
    canRedo: computed(() => undone.value.length > 0),
    execute(command) {
      done.value = [...done.value, command]
      undone.value = []
      syncPreview()
    },
    undo() {
      const command = done.value.at(-1)
      if (!command) {
        return
      }

      done.value = done.value.slice(0, -1)
      undone.value = [command, ...undone.value]
      syncPreview()
    },
    redo() {
      const command = undone.value[0]
      if (!command) {
        return
      }

      undone.value = undone.value.slice(1)
      done.value = [...done.value, command]
      syncPreview()
    },
    reset() {
      done.value = []
      undone.value = []
      previewById.value = {}
    },
  }
}
