import { expect, it } from 'vitest'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import type { ScheduleModel } from '../model/types'
import type { SchedulingEngine, SchedulingEngineOptions } from './engine'

// 可复用的引擎契约套件。任意 SchedulingEngine 实现传入工厂即可断言「可替换性」契约。
// DhtmlxEngine 与 NativeEngine 共同通过 ⇒ 换引擎时组件层零改动有保障。

const baseOptions = (): SchedulingEngineOptions => ({
  view: 'order',
  readOnly: false,
  scale: 'day',
  locale: 'zh',
  theme: { isDark: true, tokens: { '--brand': 'oklch(0.62 0.17 255)' } },
})

export function runEngineConformance(makeEngine: () => SchedulingEngine): void {
  it('mounts, renders one node per task, and exposes state', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    engine.mount(el, baseOptions())
    const model: ScheduleModel = toModel(samplePlan)
    engine.setData(model)
    expect(el.querySelectorAll('[data-task-id]').length).toBe(model.tasks.length)
    expect(engine.getState().scale).toBe('day')
    engine.destroy()
  })

  it('applies scaleTo command and reports via scaleChanged', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    let reported: string | undefined
    engine.mount(el, baseOptions())
    engine.on('scaleChanged', (p) => {
      reported = p.scale
    })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'scaleTo', scale: 'week' })
    expect(reported).toBe('week')
    expect(engine.getState().scale).toBe('week')
    engine.destroy()
  })

  it('selectTask command emits taskSelected with normalized id', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    let selected: string | undefined
    engine.mount(el, baseOptions())
    engine.on('taskSelected', (p) => {
      selected = p.taskId
    })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'selectTask', taskId: 'a1' })
    expect(selected).toBe('a1')
    expect(engine.getState().selectedTaskId).toBe('a1')
    engine.destroy()
  })

  it('destroy cleans the container', () => {
    const el = document.createElement('div')
    const engine = makeEngine()
    engine.mount(el, baseOptions())
    engine.setData(toModel(samplePlan))
    engine.destroy()
    expect(el.children.length).toBe(0)
  })
}
