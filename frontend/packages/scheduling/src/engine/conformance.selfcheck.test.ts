import { describe } from 'vitest'
import { runEngineConformance } from './conformance'
import type {
  EngineCommand,
  EngineEventName,
  EngineEvents,
  SchedulingEngine,
  SchedulingEngineOptions,
  Unsubscribe,
} from './engine'
import type { ScheduleModel, TimeScale } from '../model/types'

// 契约套件的最小内联测试替身(test-double)。仅用于自校验 conformance.ts 本身仍能约束一个
// 合规实现——绝不进入产品 src、绝不导出。真正的产品引擎为 DhtmlxEngine(vendor 存在时跑)。
// NativeEngine 已删除;正式自研引擎见后续 PR。
class FakeEngine implements SchedulingEngine {
  private container?: HTMLElement
  private scale: TimeScale = 'day'
  private selectedTaskId?: string
  private readonly listeners = new Map<EngineEventName, Set<(p: unknown) => void>>()

  mount(container: HTMLElement, options: SchedulingEngineOptions): void {
    this.container = container
    this.scale = options.scale
  }

  setData(model: ScheduleModel): void {
    const el = this.container
    if (!el) return
    el.replaceChildren()
    for (const t of model.tasks) {
      const node = document.createElement('div')
      node.setAttribute('data-task-id', t.id)
      el.appendChild(node)
    }
  }

  applyCommand(command: EngineCommand): void {
    switch (command.kind) {
      case 'scaleTo':
        this.scale = command.scale
        this.emit('scaleChanged', { scale: command.scale })
        break
      case 'selectTask':
        this.selectedTaskId = command.taskId
        this.emit('taskSelected', { taskId: command.taskId })
        break
      default:
        break
    }
  }

  on<E extends EngineEventName>(event: E, cb: (payload: EngineEvents[E]) => void): Unsubscribe {
    let set = this.listeners.get(event)
    if (!set) {
      set = new Set()
      this.listeners.set(event, set)
    }
    set.add(cb as (p: unknown) => void)
    return () => set!.delete(cb as (p: unknown) => void)
  }

  getState() {
    return { scale: this.scale, selectedTaskId: this.selectedTaskId }
  }

  destroy(): void {
    this.container?.replaceChildren()
    this.listeners.clear()
    this.container = undefined
  }

  private emit<E extends EngineEventName>(event: E, payload: EngineEvents[E]): void {
    for (const cb of this.listeners.get(event) ?? []) cb(payload)
  }
}

describe('FakeEngine conformance', () => runEngineConformance(() => new FakeEngine()))
