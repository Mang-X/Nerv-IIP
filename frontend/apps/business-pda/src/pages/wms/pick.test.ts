import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 真实组合式用真实的 ref/computed，贴合运行时解包行为。
const wmsState = vi.hoisted(() => ({
  filters: {
    skip: 0,
    take: 100,
    status: undefined as string | undefined,
    locationCode: undefined as string | undefined,
  },
  tasks: [
    {
      warehouseTaskId: '11111111-1111-1111-1111-111111111111',
      taskType: 'picking',
      taskNo: 'PK-2026-0001',
      sourceOrderNo: 'OB-2026-0001',
      sourceOrderLineNo: '1',
      skuCode: 'SKU-A',
      uomCode: 'EA',
      siteCode: 'S1',
      fromLocationCode: 'A-01',
      toLocationCode: 'OUT-01',
      plannedQuantity: 10,
      executedQuantity: 0,
      status: 'pending',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      warehouseTaskId: '22222222-2222-2222-2222-222222222222',
      taskType: 'picking',
      taskNo: 'PK-2026-0002',
      sourceOrderNo: 'OB-2026-0002',
      sourceOrderLineNo: '1',
      skuCode: 'SKU-B',
      uomCode: 'EA',
      siteCode: 'S1',
      fromLocationCode: 'A-02',
      toLocationCode: 'OUT-01',
      plannedQuantity: 5,
      executedQuantity: 0,
      status: 'inProgress',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ],
  error: null as unknown,
  pending: false,
  refresh: vi.fn(),
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsPicking: () => ({
    filters: wmsState.filters,
    tasks: computed(() => wmsState.tasks),
    total: computed(() => wmsState.tasks.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: wmsState.refresh,
  }),
}))

import PickPage from './pick.vue'

function freshTasks() {
  return [
    {
      warehouseTaskId: '11111111-1111-1111-1111-111111111111',
      taskType: 'picking',
      taskNo: 'PK-2026-0001',
      sourceOrderNo: 'OB-2026-0001',
      sourceOrderLineNo: '1',
      skuCode: 'SKU-A',
      uomCode: 'EA',
      siteCode: 'S1',
      fromLocationCode: 'A-01',
      toLocationCode: 'OUT-01',
      plannedQuantity: 10,
      executedQuantity: 0,
      status: 'pending',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      warehouseTaskId: '22222222-2222-2222-2222-222222222222',
      taskType: 'picking',
      taskNo: 'PK-2026-0002',
      sourceOrderNo: 'OB-2026-0002',
      sourceOrderLineNo: '1',
      skuCode: 'SKU-B',
      uomCode: 'EA',
      siteCode: 'S1',
      fromLocationCode: 'A-02',
      toLocationCode: 'OUT-01',
      plannedQuantity: 5,
      executedQuantity: 0,
      status: 'inProgress',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ]
}

function resetState() {
  wmsState.filters.status = undefined
  wmsState.filters.locationCode = undefined
  wmsState.tasks = freshTasks()
  wmsState.error = null
  wmsState.pending = false
  wmsState.refresh.mockClear()
  push.mockClear()
}

describe('WMS 拣货（只读）', () => {
  beforeEach(() => resetState())

  it('渲染拣货任务行与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(PickPage)
    const text = wrapper.text()
    expect(text).toContain('PK-2026-0001')
    expect(text).toContain('PK-2026-0002')
    expect(text).toContain('SKU-A')
    expect(text).toContain('A-01')
    expect(text).toContain('OUT-01')
    // 中文状态
    expect(text).toContain('待执行')
    expect(text).toContain('执行中')
    // 不暴露工程语言：原始状态码 / GUID
    expect(text).not.toContain('pending')
    expect(text).not.toContain('inProgress')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
    expect(text).not.toContain('22222222-2222-2222-2222-222222222222')
  })

  it('扫库位写入 filters.locationCode', async () => {
    const wrapper = mount(PickPage)
    const input = wrapper.get('input[placeholder*="库位"]')
    await input.setValue('A-02')
    await input.trigger('keydown.enter')
    expect(wmsState.filters.locationCode).toBe('A-02')
  })

  it('清除筛选可重置 filters.locationCode', async () => {
    wmsState.filters.locationCode = 'A-02'
    const wrapper = mount(PickPage)
    await wrapper.get('[data-testid="clear-filter"]').trigger('click')
    expect(wmsState.filters.locationCode).toBeUndefined()
  })

  it('错误时显示错误横幅而非空态', () => {
    wmsState.error = new Error('boom')
    wmsState.tasks = []
    const wrapper = mount(PickPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无拣货任务')
  })

  it('刷新失败但已有任务时：错误横幅与任务列表共存（列表不被隐藏）', () => {
    wmsState.error = new Error('boom')
    wmsState.tasks = freshTasks()
    const wrapper = mount(PickPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
    const text = wrapper.text()
    expect(text).toContain('PK-2026-0001')
    expect(text).toContain('PK-2026-0002')
    expect(text).not.toContain('暂无拣货任务')
  })

  it('无任务且无错误时显示空态', () => {
    wmsState.tasks = []
    const wrapper = mount(PickPage)
    expect(wrapper.text()).toContain('暂无拣货任务')
  })

  it('页面只读：无写操作按钮（无确认完成）', () => {
    const wrapper = mount(PickPage)
    expect(wrapper.find('[data-testid="confirm-complete"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('确认完成')
  })
})
