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
  kpi?: { utilization?: number }
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

  it('maps resource load utilization into the resource lane header', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    engine.mount(el(), { ...options(), view: 'resource' })
    engine.setData(toModel(samplePlan))
    expect(fake.state.parsed.data.find((task) => task.id === 'lane:WC-001')?.kpi?.utilization).toBe(
      0.25,
    )
  })

  it('aggregates underlying resource loads for a non-resource grouping dimension', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    const model = toModel(samplePlan)
    for (const task of model.tasks) {
      if (task.type === 'operation' && task.resourceId === 'WC-001') {
        task.dimensions = { device: { id: 'DEVICE-A', label: '设备 A' } }
      }
    }

    engine.mount(el(), { ...options(), view: 'resource', groupBy: 'device' })
    engine.setData(model)

    expect(
      fake.state.parsed.data.find((task) => task.id === 'lane:DEVICE-A')?.kpi?.utilization,
    ).toBe(0.25)
  })

  // 泳道播种:资源用于让空泳道常驻(拖走最后一个工序时该行不消失),但只在资源本身
  // 属于当前分组维度时才播种。工序维度 id(WC-*)与资源 id(DEV-*)分属两个空间时,
  // 全量播种会生出一批永远为空的泳道垫在最上方,把真正有工序的泳道挤出首屏。
  it('seeds work-center lanes only from resources that belong to that dimension', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    const model = toModel(samplePlan)
    // 资源空间里混入一台设备:它不是任何工序携带的 workCenter,不该长出泳道。
    model.resources = [...model.resources, { id: 'DEVICE-A', text: '设备 A' }]

    engine.mount(el(), { ...options(), view: 'resource', groupBy: 'workCenter' })
    engine.setData(model)

    const laneIds = fake.state.parsed.data
      .map((task) => task.id)
      .filter((id) => id.startsWith('lane:'))
    expect(laneIds).toContain('lane:WC-001')
    expect(laneIds).not.toContain('lane:DEVICE-A')
  })

  it('keeps every resource lane when no task carries the grouping dimension', () => {
    const fake = makeFakeGantt()
    const engine = new DhtmlxEngine({ createInstance: () => fake.gantt })
    const model = toModel(samplePlan)
    // 没有任何工序携带 workCenter 维度 → 无从判断资源是否属于该维度,
    // 此时必须保留全部资源泳道,否则整块时间轴会一行不剩。
    for (const task of model.tasks) task.dimensions = undefined
    model.resources = [...model.resources, { id: 'DEVICE-A', text: '设备 A' }]

    engine.mount(el(), { ...options(), view: 'resource', groupBy: 'workCenter' })
    engine.setData(model)

    const laneIds = fake.state.parsed.data
      .map((task) => task.id)
      .filter((id) => id.startsWith('lane:'))
    expect(laneIds).toContain('lane:DEVICE-A')
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
