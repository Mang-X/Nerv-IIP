import { flushPromises, mount } from '@vue/test-utils'
import { SchedulingToolbar, type ScheduleModel } from '@nerv-iip/scheduling'
import { beforeEach, expect, it, vi } from 'vitest'
import SchedulingDraftBoard from './SchedulingDraftBoard.vue'

const runtime = vi.hoisted(() => ({
  ready: Promise.resolve(),
  release: () => {},
  engines: new Map<string, { applyCommand: ReturnType<typeof vi.fn> }>(),
}))
vi.mock('../../../../../packages/scheduling/src/engine/dhtmlx/loader', () => ({
  preloadGantt: () => runtime.ready,
  isDhtmlxAvailable: async () => true,
}))
vi.mock('../../../../../packages/scheduling/src/engine/dhtmlx/DhtmlxEngine', () => ({
  DhtmlxEngine: class {
    applyCommand = vi.fn()
    mount(_container: HTMLElement, options: { view: string }) {
      runtime.engines.set(options.view, this)
    }
    setData() {}
    on() {}
    destroy() {}
  },
}))

beforeEach(() => {
  runtime.engines.clear()
  runtime.ready = new Promise<void>((resolve) => {
    runtime.release = resolve
  })
})

// #4038：同步 command stub 不足以证明异步引擎消费；保留真实 Chart/Canvas/useEngine。
it.each(['ready', 'pending'])(
  'restores lookup after switching charts while the engine is %s',
  async (phase) => {
    const model: ScheduleModel = {
      tasks: [
        {
          id: 'A-001',
          orderId: 'WO-260930-001',
          operationId: 'OP-10',
          operationSequence: 10,
          type: 'operation',
          text: '车削',
          startUtc: '2026-09-30T02:00:00Z',
          endUtc: '2026-09-30T05:00:00Z',
          locked: false,
          hasConflict: false,
        },
      ],
      resources: [],
      links: [],
      loads: [],
      conflicts: [],
      unscheduled: [],
      changes: [],
      horizon: { startUtc: '2026-09-30T00:00:00Z', endUtc: '2026-10-01T00:00:00Z' },
      meta: { planId: 'PLAN-260930-001', status: 'generated', algorithmVersion: 'aps-lite-v1' },
    }
    const wrapper = mount(SchedulingDraftBoard, { props: { model } })
    await flushPromises()
    if (phase === 'ready') {
      runtime.release()
      await flushPromises()
    }
    wrapper.findComponent(SchedulingToolbar).vm.$emit('update:search', '车削')
    await flushPromises()
    if (phase === 'ready') {
      expect(runtime.engines.get('order')?.applyCommand).toHaveBeenCalledWith({
        kind: 'revealTask',
        taskId: 'A-001',
      })
    }
    const resourceTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text() === '资源排产板')!
    await resourceTab.trigger('focus')
    await resourceTab.trigger('mousedown')
    await flushPromises()
    if (phase === 'pending') {
      runtime.release()
      await flushPromises()
    }
    expect(runtime.engines.get('resource')?.applyCommand).toHaveBeenCalledWith({
      kind: 'setSearchHighlight',
      taskIds: ['A-001'],
    })
    expect(runtime.engines.get('resource')?.applyCommand).toHaveBeenCalledWith({
      kind: 'revealTask',
      taskId: 'A-001',
    })
    expect(runtime.engines.get('resource')?.applyCommand).toHaveBeenCalledWith({
      kind: 'selectTask',
      taskId: 'A-001',
    })
    wrapper.unmount()
  },
)
