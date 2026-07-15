import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

const push = vi.fn(() => Promise.resolve())
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
    keyword: undefined as string | undefined,
  },
  orders: [
    {
      inboundOrderId: '11111111-1111-1111-1111-111111111111',
      inboundOrderNo: 'IB-2026-0001',
      status: 'open',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      inboundOrderId: '22222222-2222-2222-2222-222222222222',
      inboundOrderNo: 'IB-2026-0002',
      status: 'completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ],
  completeInbound: vi.fn((_inboundOrderId: string, _idempotencyKey: string) => Promise.resolve()),
  completePending: false,
  error: null as unknown,
  pending: false,
  refresh: vi.fn(() => Promise.resolve()),
  // 门禁行按单分组：单1 待检（含批号 LOT-A），单2 全合格。
  gates: new Map<string, Array<Record<string, unknown>>>([
    [
      '11111111-1111-1111-1111-111111111111',
      [
        {
          inboundOrderId: '11111111-1111-1111-1111-111111111111',
          inboundOrderLineId: 'line-1',
          lineNo: '1',
          skuCode: 'SKU-A',
          uomCode: 'EA',
          receivedQuantity: 20,
          lotNo: 'LOT-A',
          qualityGateStatus: 'pending',
        },
      ],
    ],
    [
      '22222222-2222-2222-2222-222222222222',
      [
        {
          inboundOrderId: '22222222-2222-2222-2222-222222222222',
          inboundOrderLineId: 'line-2',
          lineNo: '1',
          skuCode: 'SKU-B',
          uomCode: 'EA',
          receivedQuantity: 5,
          lotNo: 'LOT-B',
          qualityGateStatus: 'passed',
        },
      ],
    ],
  ]),
  gatesRefresh: vi.fn(() => Promise.resolve()),
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsInbound: () => ({
    filters: wmsState.filters,
    orders: computed(() => wmsState.orders),
    total: computed(() => wmsState.orders.length),
    pending: computed(() => wmsState.pending),
    error: computed(() => wmsState.error),
    refresh: wmsState.refresh,
    completeInbound: wmsState.completeInbound,
    completePending: computed(() => wmsState.completePending),
  }),
  useWmsReceivingQualityGates: () => ({
    linesByOrderId: computed(() => wmsState.gates),
    lines: computed(() => [...wmsState.gates.values()].flat()),
    refresh: wmsState.gatesRefresh,
    pending: computed(() => false),
    error: computed(() => null),
  }),
}))

import InboundPage from './inbound.vue'

function resetState() {
  wmsState.filters.keyword = undefined
  wmsState.filters.status = undefined
  wmsState.orders = [
    {
      inboundOrderId: '11111111-1111-1111-1111-111111111111',
      inboundOrderNo: 'IB-2026-0001',
      status: 'open',
      createdAtUtc: '2026-06-11T08:00:00Z',
    },
    {
      inboundOrderId: '22222222-2222-2222-2222-222222222222',
      inboundOrderNo: 'IB-2026-0002',
      status: 'completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
    },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.completeInbound.mockClear()
  wmsState.refresh.mockClear()
  wmsState.gatesRefresh.mockClear()
  push.mockClear()
}

describe('WMS 收货入库', () => {
  beforeEach(() => resetState())

  it('渲染收货单号与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(InboundPage)
    const text = wrapper.text()
    expect(text).toContain('IB-2026-0001')
    expect(text).toContain('IB-2026-0002')
    expect(text).toContain('待入库')
    expect(text).toContain('已入库')
    expect(text).not.toContain('open')
    expect(text).not.toContain('completed')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
  })

  it('列表单据显示质检状态标（待检 / 合格）', () => {
    const wrapper = mount(InboundPage)
    const text = wrapper.text()
    expect(text).toContain('待检')
    expect(text).toContain('合格')
    // 不暴露工程态码
    expect(text).not.toContain('pending')
    expect(text).not.toContain('passed')
  })

  it('扫单号写入 filters.keyword', async () => {
    const wrapper = mount(InboundPage)
    const input = wrapper.get('input[placeholder*="单号"]')
    await input.setValue('IB-2026-0002')
    await input.trigger('keydown.enter')
    expect(wmsState.filters.keyword).toBe('IB-2026-0002')
  })

  it('点单 → 抽屉展示行级批号+质检门禁明细', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const line = document.querySelector('[data-line]')!
    expect(line).toBeTruthy()
    expect(line.textContent).toContain('SKU-A')
    expect(line.textContent).toContain('LOT-A')
    expect(line.textContent).toContain('待检')
    wrapper.unmount()
  })

  it('待检单据不出现「去上架」，显示待质检门禁提示', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    expect(document.querySelector('[data-quality-gate-notice]')).toBeTruthy()
    expect(document.querySelector('[data-testid="go-putaway"]')).toBeNull()
    wrapper.unmount()
  })

  it('合格单据出现「去上架」引导，点按跳上架页', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[1].trigger('click')
    const putaway = document.querySelector<HTMLButtonElement>('[data-testid="go-putaway"]')!
    expect(putaway).toBeTruthy()
    expect(document.querySelector('[data-quality-gate-notice]')).toBeNull()
    putaway.click()
    expect(push).toHaveBeenCalledWith('/wms/putaway')
    wrapper.unmount()
  })

  it('点单 → 抽屉确认 → 以该单 id 与页面生成的稳定 idempotencyKey 调用 completeInbound', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm).toBeTruthy()
    confirm.click()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(1)
    const [id, key] = wmsState.completeInbound.mock.calls[0]
    expect(id).toBe('11111111-1111-1111-1111-111111111111')
    expect(typeof key).toBe('string')
    expect((key as string).length).toBeGreaterThan(0)
    wrapper.unmount()
  })

  it('重试（不重新点单）复用同一 idempotencyKey；重新点单为新操作换新键', async () => {
    wmsState.completeInbound.mockRejectedValueOnce(new Error('lost response'))
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    confirm.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(2)
    const firstKey = wmsState.completeInbound.mock.calls[0][1]
    const retryKey = wmsState.completeInbound.mock.calls[1][1]
    expect(retryKey).toBe(firstKey)

    const continueBtn = wrapper.findAll('button').find((b) => b.text() === '继续')!
    expect(continueBtn).toBeTruthy()
    await continueBtn.trigger('click')

    await wrapper.findAll('[data-row]')[1].trigger('click')
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(3)
    const newOpKey = wmsState.completeInbound.mock.calls[2][1]
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })

  it('completePending 时确认按钮禁用（防重）', async () => {
    wmsState.completePending = true
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    wrapper.unmount()
  })

  it('完成后显示成功 Result，文案为「入库完成，待质检」', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('入库完成，待质检')
    wrapper.unmount()
  })

  it('错误时显示错误横幅', () => {
    wmsState.error = new Error('boom')
    const wrapper = mount(InboundPage)
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })

  it('列表超时：显示"网络超时"文案且可点重试刷新（GET 重试安全）', async () => {
    wmsState.error = new RequestTimeoutError()
    const wrapper = mount(InboundPage)
    expect(wrapper.get('[data-testid="error-banner"]').text()).toContain(
      '网络超时，请检查连接后重试',
    )
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(wmsState.refresh).toHaveBeenCalledTimes(1)
  })

  it('列表离线：显示"当前离线"文案（区分于业务错误）', () => {
    wmsState.error = new OfflineError()
    const wrapper = mount(InboundPage)
    expect(wrapper.get('[data-testid="error-banner"]').text()).toContain(
      '当前离线，请检查网络连接后重试',
    )
  })

  it('无单据且无错误时显示空态', () => {
    wmsState.orders = []
    const wrapper = mount(InboundPage)
    expect(wrapper.text()).toContain('暂无待收货单据')
  })
})

describe('WMS 收货入库 · GS1 扫码效期', () => {
  const GS = String.fromCharCode(29)

  beforeEach(() => {
    resetState()
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-15T00:00:00Z'))
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('扫 GS1 批次码带出效期 → 行显示效期三色标 + 临期黄色提示', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const gs1Input = document.querySelector<HTMLInputElement>('input[placeholder*="GS1"]')!
    expect(gs1Input).toBeTruthy()
    // (17)260801 效期 2026-08-01（距 7/15 约 17 天 → 临近过期红），(10)LOT-A 匹配收货行。
    gs1Input.value = `17260801${GS}10LOT-A`
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    expect(document.querySelector('[data-expiry-tag]')?.textContent).toContain('2026-08-01')
    expect(document.querySelector('[data-near-expiry-notice]')).toBeTruthy()
    wrapper.unmount()
  })

  it('扫非 GS1 码提示未识别', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    const gs1Input = document.querySelector<HTMLInputElement>('input[placeholder*="GS1"]')!
    gs1Input.value = 'NOT-A-GS1'
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    expect(document.querySelector('[data-gs1-notice]')?.textContent).toContain('未识别')
    wrapper.unmount()
  })
})
