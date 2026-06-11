import { describe, expect, it } from 'vitest'
import { toModel } from '../../model/aps-mapper'
import { samplePlan } from '../../model/fixtures'
import type { SchedulingEngineOptions } from '../engine'
import { DhtmlxEngine } from './DhtmlxEngine'

// 用假 gantt 工厂注入,在 CI(无真实 DHTMLX)下覆盖适配器的「模型→parse / 事件归一化 / 命令」逻辑。
// 真实 DOM 渲染由 Playwright(真浏览器)验证,见 apps/business-console e2e/visual。

interface FakeTask {
  id: string
  start_date?: Date
  end_date?: Date
  $resource?: string
}

function makeFakeGantt() {
  const handlers = new Map<string, (...a: unknown[]) => unknown>()
  const state = {
    config: {} as Record<string, unknown>,
    templates: {} as Record<string, unknown>,
    parsed: { data: [] as FakeTask[], links: [] as unknown[] },
    selected: undefined as string | undefined,
    rendered: 0,
    destroyed: false,
  }
  const gantt = {
    config: state.config,
    templates: state.templates,
    plugins: (_p: Record<string, boolean>) => {},
    attachEvent: (name: string, h: (...a: unknown[]) => unknown) => {
      handlers.set(name, h)
      return name
    },
    detachEvent: (id: string) => handlers.delete(id),
    init: (_c: HTMLElement) => {},
    parse: (d: { data: FakeTask[]; links: unknown[] }) => {
      state.parsed = d
    },
    clearAll: () => {
      state.parsed = { data: [], links: [] }
    },
    getTask: (id: string | number) => state.parsed.data.find((t) => t.id === String(id)),
    isTaskExists: (id: string | number) => state.parsed.data.some((t) => t.id === String(id)),
    selectTask: (id: string | number) => {
      state.selected = String(id)
    },
    render: () => {
      state.rendered++
    },
    setSizes: () => {},
    addMarker: (_m: Record<string, unknown>) => 'marker-1',
    deleteMarker: (_id: string) => {},
    destructor: () => {
      state.destroyed = true
    },
    showDate: (_d: Date) => {},
  }
  return { gantt, state, fire: (name: string, ...args: unknown[]) => handlers.get(name)?.(...args) }
}

const options = (): SchedulingEngineOptions => ({
  view: 'order',
  readOnly: false,
  scale: 'day',
  locale: 'zh',
  theme: { isDark: true, tokens: { '--brand': 'x' } },
})

describe('DhtmlxEngine (fake factory)', () => {
  it('maps the model into gantt.parse with one task per node and FS links', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    const el = document.createElement('div')
    engine.mount(el, options())
    engine.setData(toModel(samplePlan))
    expect(fake.state.parsed.data).toHaveLength(toModel(samplePlan).tasks.length)
    expect(fake.state.parsed.links).toEqual([
      { id: 'a1->a2', source: 'a1', target: 'a2', type: '0' },
    ])
    engine.destroy()
    expect(fake.state.destroyed).toBe(true)
  })

  it('selectTask command selects in gantt and emits taskSelected', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    let selected: string | undefined
    engine.mount(el(), options())
    engine.on('taskSelected', (p) => {
      selected = p.taskId
    })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'selectTask', taskId: 'a1' })
    expect(fake.state.selected).toBe('a1')
    expect(selected).toBe('a1')
    expect(engine.getState().selectedTaskId).toBe('a1')
  })

  it('scaleTo updates scales config and emits scaleChanged', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    let scale: string | undefined
    engine.mount(el(), options())
    engine.on('scaleChanged', (p) => {
      scale = p.scale
    })
    engine.setData(toModel(samplePlan))
    engine.applyCommand({ kind: 'scaleTo', scale: 'week' })
    expect(scale).toBe('week')
    expect(engine.getState().scale).toBe('week')
  })

  it('normalizes onAfterTaskDrag into a taskDragEnd payload (deferred)', async () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    let payload: { taskId: string; startUtc: string; kind: string } | undefined
    engine.mount(el(), options())
    engine.on('taskDragEnd', (p) => {
      payload = p
    })
    engine.setData(toModel(samplePlan))
    // 模拟 DHTMLX 拖动后改写了任务时间。
    const moved = fake.state.parsed.data.find((t) => t.id === 'a1')!
    moved.start_date = new Date('2026-06-10T09:00:00.000Z')
    moved.end_date = new Date('2026-06-10T11:00:00.000Z')
    fake.fire('onAfterTaskDrag', 'a1', 'move')
    // emit 延后到 DHTMLX 处理完拖拽之后,等一拍。
    await new Promise((r) => setTimeout(r, 1))
    expect(payload?.taskId).toBe('a1')
    expect(payload?.startUtc).toBe('2026-06-10T09:00:00.000Z')
    expect(payload?.kind).toBe('move')
  })
})

function el() {
  return document.createElement('div')
}
