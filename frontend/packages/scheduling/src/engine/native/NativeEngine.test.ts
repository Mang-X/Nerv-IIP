import { describe, expect, it } from 'vitest'
import { toModel } from '../../model/aps-mapper'
import { samplePlan } from '../../model/fixtures'
import { runEngineConformance } from '../conformance'
import { NativeEngine } from './NativeEngine'

describe('NativeEngine conformance', () => {
  runEngineConformance(() => new NativeEngine())
})

describe('NativeEngine extras', () => {
  it('marks conflicted tasks with a data-conflict attribute', () => {
    const el = document.createElement('div')
    const engine = new NativeEngine()
    engine.mount(el, {
      view: 'order',
      readOnly: false,
      scale: 'day',
      locale: 'zh',
      theme: { isDark: true, tokens: {} },
    })
    engine.setData(toModel(samplePlan))
    expect(el.querySelector('[data-task-id="a2"]')?.getAttribute('data-conflict')).toBe('capacity')
    engine.destroy()
  })

  it('clicking a task emits taskSelected', () => {
    const el = document.createElement('div')
    const engine = new NativeEngine()
    let selected: string | undefined
    engine.mount(el, {
      view: 'order',
      readOnly: true,
      scale: 'day',
      locale: 'zh',
      theme: { isDark: false, tokens: {} },
    })
    engine.on('taskSelected', (p) => {
      selected = p.taskId
    })
    engine.setData(toModel(samplePlan))
    ;(el.querySelector('[data-task-id="a1"]') as SVGGElement).dispatchEvent(
      new MouseEvent('click', { bubbles: true }),
    )
    expect(selected).toBe('a1')
    engine.destroy()
  })
})
