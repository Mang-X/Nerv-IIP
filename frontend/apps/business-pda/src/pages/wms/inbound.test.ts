import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { NvBottomSheet, NvMobileDropdownMenuItem } from '@nerv-iip/ui-mobile'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toValue, type MaybeRefOrGetter } from 'vue'

const push = vi.fn(() => Promise.resolve())
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routeGuardState.guard = guard
  }),
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))
vi.mock('@/composables/useWmsOperationalCandidates', async () => {
  const { shallowRef } = await import('vue')
  return {
    useWmsOperationalCandidates: () => ({
      locationOptions: shallowRef([]),
      lotOptions: shallowRef([]),
      sourceLabel: shallowRef('当前范围仓储作业记录候选'),
      sourceKind: shallowRef(),
      asOfUtc: shallowRef(),
      freshnessUtc: shallowRef(),
      truncated: shallowRef(false),
      pending: shallowRef(false),
    }),
  }
})

type GateLine = Record<string, unknown> & { inboundOrderNo?: string }

// 单据级质检状态/上架放行来自 ListInboundOrders 派生字段（orders 项）；
// 行级明细来自按单查（useWmsReceivingLines），按单号返回。
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
      qualityGateStatus: 'pending',
      isReleasedForPutaway: false,
    },
    {
      inboundOrderId: '22222222-2222-2222-2222-222222222222',
      inboundOrderNo: 'IB-2026-0002',
      status: 'completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
      qualityGateStatus: 'passed',
      isReleasedForPutaway: true,
    },
  ] as Array<Record<string, unknown>>,
  completeInbound: vi.fn(
    (
      _inboundOrderId: string,
      _idempotencyKey: string,
      _lines?: unknown[],
      options?: { attempt: 'initial' | 'retry'; onCommandAttempt?: () => void },
    ) => {
      options?.onCommandAttempt?.()
      return Promise.resolve()
    },
  ),
  completePending: false,
  error: null as unknown,
  pending: false,
  refresh: vi.fn(() => Promise.resolve()),
  loadMore: vi.fn(() => Promise.resolve()),
  linesByOrderNo: {
    'IB-2026-0001': [
      {
        inboundOrderId: '11111111-1111-1111-1111-111111111111',
        inboundOrderNo: 'IB-2026-0001',
        inboundOrderLineId: 'line-1',
        lineNo: '1',
        skuCode: 'SKU-A',
        uomCode: 'EA',
        receivedQuantity: 20,
        lotNo: 'LOT-A',
        expiryDate: '2027-12-31',
        qualityGateStatus: 'pending',
      },
    ],
    'IB-2026-0002': [
      {
        inboundOrderId: '22222222-2222-2222-2222-222222222222',
        inboundOrderNo: 'IB-2026-0002',
        inboundOrderLineId: 'line-2',
        lineNo: '1',
        skuCode: 'SKU-B',
        uomCode: 'EA',
        receivedQuantity: 5,
        lotNo: 'LOT-B',
        qualityGateStatus: 'passed',
      },
    ],
  } as Record<string, GateLine[]>,
  linesPending: false,
  linesError: null as unknown,
  linesTotal: null as number | null,
  linesRefresh: vi.fn(() => Promise.resolve()),
}))
const scopeKey = ref<string | undefined>('self:emp049')
const scopeKind = computed(() => scopeKey.value?.split(':', 2)[0])
const scopeId = computed(() => scopeKey.value?.split(':', 2)[1])

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsInbound: () => ({
    filters: wmsState.filters,
    scopeKey,
    scopeKind,
    scopeId,
    scopeOptions: computed(() => [
      { label: '我的任务', value: 'self:emp049' },
      { label: '一号仓收货作业池', value: 'work-pool:WMS-SITE-001-RECEIVING' },
    ]),
    selectedScopeLabel: computed(() =>
      scopeKey.value === 'self:emp049' ? '我的任务' : '一号仓收货作业池',
    ),
    orders: computed(() => wmsState.orders),
    total: computed(() => wmsState.orders.length),
    organizationId: computed(() => 'org-001'),
    environmentId: computed(() => 'env-dev'),
    scopeReady: computed(() => true),
    lastUpdatedAt: computed(() => '2026-07-28T10:20:30.000Z'),
    hasSuccessfulResponse: computed(() => !wmsState.pending && !wmsState.error),
    hasFailedResponse: computed(() => false),
    pending: computed(() => wmsState.pending),
    refreshing: computed(() => false),
    loadingMore: computed(() => false),
    error: computed(() => wmsState.error),
    refresh: wmsState.refresh,
    loadMore: wmsState.loadMore,
    completeInbound: wmsState.completeInbound,
    completePending: computed(() => wmsState.completePending),
  }),
  useWmsReceivingLines: (
    orderNo: MaybeRefOrGetter<string>,
    selectedScope: {
      scopeKind: MaybeRefOrGetter<string | undefined>
      scopeId: MaybeRefOrGetter<string | undefined>
    },
  ) => ({
    lines: computed(() => wmsState.linesByOrderNo[toValue(orderNo)] ?? []),
    total: computed(
      () => wmsState.linesTotal ?? (wmsState.linesByOrderNo[toValue(orderNo)] ?? []).length,
    ),
    complete: computed(() => {
      const l = wmsState.linesByOrderNo[toValue(orderNo)] ?? []
      const t = wmsState.linesTotal ?? l.length
      return t > 0 && l.length >= t
    }),
    pending: computed(() => wmsState.linesPending),
    error: computed(() => wmsState.linesError),
    hasSuccessfulResponse: computed(
      () =>
        Boolean(toValue(orderNo)) &&
        Boolean(toValue(selectedScope.scopeKind)) &&
        Boolean(toValue(selectedScope.scopeId)) &&
        !wmsState.linesPending &&
        !wmsState.linesError,
    ),
    hasFailedResponse: computed(() => false),
    refresh: wmsState.linesRefresh,
  }),
}))

import InboundPage from './inbound.vue'

function resetState() {
  wmsState.filters.keyword = undefined
  wmsState.filters.status = undefined
  scopeKey.value = 'self:emp049'
  wmsState.orders = [
    {
      inboundOrderId: '11111111-1111-1111-1111-111111111111',
      inboundOrderNo: 'IB-2026-0001',
      status: 'open',
      createdAtUtc: '2026-06-11T08:00:00Z',
      qualityGateStatus: 'pending',
      isReleasedForPutaway: false,
    },
    {
      inboundOrderId: '22222222-2222-2222-2222-222222222222',
      inboundOrderNo: 'IB-2026-0002',
      status: 'completed',
      createdAtUtc: '2026-06-11T09:00:00Z',
      qualityGateStatus: 'passed',
      isReleasedForPutaway: true,
    },
  ]
  wmsState.completePending = false
  wmsState.error = null
  wmsState.pending = false
  wmsState.linesPending = false
  wmsState.linesError = null
  wmsState.linesTotal = null
  wmsState.linesByOrderNo = {
    'IB-2026-0001': [
      {
        inboundOrderId: '11111111-1111-1111-1111-111111111111',
        inboundOrderNo: 'IB-2026-0001',
        inboundOrderLineId: 'line-1',
        lineNo: '1',
        skuCode: 'SKU-A',
        uomCode: 'EA',
        receivedQuantity: 20,
        lotNo: 'LOT-A',
        expiryDate: '2027-12-31',
        qualityGateStatus: 'pending',
      },
    ],
    'IB-2026-0002': [
      {
        inboundOrderId: '22222222-2222-2222-2222-222222222222',
        inboundOrderNo: 'IB-2026-0002',
        inboundOrderLineId: 'line-2',
        lineNo: '1',
        skuCode: 'SKU-B',
        uomCode: 'EA',
        receivedQuantity: 5,
        lotNo: 'LOT-B',
        qualityGateStatus: 'passed',
      },
    ],
  }
  wmsState.completeInbound.mockClear()
  wmsState.refresh.mockClear()
  wmsState.loadMore.mockClear()
  wmsState.linesRefresh.mockClear()
  push.mockClear()
}

describe('WMS 收货入库', () => {
  beforeEach(() => resetState())

  it('渲染收货单号与中文状态（不出现原始状态码或 GUID）', () => {
    const wrapper = mount(InboundPage)
    const text = wrapper.text()
    expect(text).toContain('IB-2026-0001')
    expect(text).toContain('IB-2026-0002')
    expect(text).toContain('待收货')
    expect(text).toContain('已完成')
    expect(text).not.toContain('open')
    expect(text).not.toContain('11111111-1111-1111-1111-111111111111')
  })

  it('列表单据显示单据级质检状态标（待检 / 合格）', () => {
    const wrapper = mount(InboundPage)
    const text = wrapper.text()
    expect(text).toContain('待检')
    expect(text).toContain('合格')
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

  it('可从 WMS 可信目录切换作业范围和状态，不要求手输筛选值', async () => {
    const wrapper = mount(InboundPage)
    const fields = wrapper.findAllComponents(NvMobileDropdownMenuItem)

    expect(fields).toHaveLength(2)
    fields[0]!.vm.$emit('update:modelValue', 'work-pool:WMS-SITE-001-RECEIVING')
    fields[1]!.vm.$emit('update:modelValue', 'Completed')
    await wrapper.vm.$nextTick()

    expect(scopeKey.value).toBe('work-pool:WMS-SITE-001-RECEIVING')
    expect(wmsState.filters.status).toBe('Completed')
    expect(wrapper.text()).toContain('WMS 收货作业范围目录')
  })

  it('点单 → 抽屉展示行级批号+质检门禁明细 + 后端效期三色（无需扫码）', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const line = document.querySelector('[data-line]')!
    expect(line).toBeTruthy()
    expect(line.textContent).toContain('SKU-A')
    expect(line.textContent).toContain('待检')
    // 后端行携带 expiryDate → 直接三色显示，无需扫码
    expect(document.querySelector('[data-expiry-tag]')?.textContent).toContain('2027-12-31')
    // 批号手输框预填后端既有批号
    const batch = document.querySelector<HTMLInputElement>('[data-batch-input]')!
    expect(batch.value).toBe('LOT-A')
  })

  it('待检单据不出现「去上架」，显示待质检门禁提示', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-quality-gate-notice]')).toBeTruthy()
    expect(document.querySelector('[data-testid="go-putaway"]')).toBeNull()
  })

  it('合格（已放行）单据出现「去上架」引导，点按跳上架页', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[1].trigger('click')
    await flushPromises()
    const putaway = document.querySelector<HTMLButtonElement>('[data-testid="go-putaway"]')!
    expect(putaway).toBeTruthy()
    expect(document.querySelector('[data-quality-gate-notice]')).toBeNull()
    expect(document.querySelector('[data-testid="confirm-complete"]')).toBeNull()
    expect(document.body.textContent).toContain('该单已结束，仅可查看明细或进入后续上架')
    putaway.click()
    expect(push).toHaveBeenCalledWith('/wms/putaway')
  })

  it('确认完成 → 以单 id/稳定 idempotencyKey/采集 lines 调用 completeInbound（批号效期落库）', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(1)
    const [id, key, lines] = wmsState.completeInbound.mock.calls[0]
    expect(id).toBe('11111111-1111-1111-1111-111111111111')
    expect(typeof key).toBe('string')
    expect((key as string).length).toBeGreaterThan(0)
    expect(lines).toEqual([
      { lineNo: '1', lotNo: 'LOT-A', productionDate: undefined, expiryDate: '2027-12-31' },
    ])
    expect(wmsState.completeInbound.mock.calls[0][3]).toMatchObject({ attempt: 'initial' })
  })

  it('多行新批次：逐行批号手输 → 随 completeInbound 落库', async () => {
    // 行上无批号（新收货），操作者手输批号。
    wmsState.linesByOrderNo['IB-2026-0001'] = [
      {
        inboundOrderId: '11111111-1111-1111-1111-111111111111',
        inboundOrderNo: 'IB-2026-0001',
        inboundOrderLineId: 'line-1',
        lineNo: '1',
        skuCode: 'SKU-A',
        receivedQuantity: 20,
        lotNo: null,
        qualityGateStatus: 'pending',
      },
    ]
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const batch = document.querySelector<HTMLInputElement>('[data-batch-input]')!
    batch.value = 'NEW-LOT-9'
    batch.dispatchEvent(new Event('input'))
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    const lines = wmsState.completeInbound.mock.calls.at(-1)?.[2] as Array<Record<string, unknown>>
    expect(lines[0]).toMatchObject({ lineNo: '1', lotNo: 'NEW-LOT-9' })
  })

  it('重试（不重新点单）复用同一 idempotencyKey；重新点单为新操作换新键', async () => {
    wmsState.completeInbound.mockImplementationOnce(
      (
        _id: string,
        _key: string,
        _lines?: unknown[],
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new RequestTimeoutError())
      },
    )
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    const batch = document.querySelector<HTMLInputElement>('[data-batch-input]')!
    batch.value = 'LOT-CHANGED'
    batch.dispatchEvent(new Event('input'))
    await flushPromises()
    confirm.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(2)
    const firstKey = wmsState.completeInbound.mock.calls[0][1]
    expect(wmsState.completeInbound.mock.calls[1][1]).toBe(firstKey)
    expect(wmsState.completeInbound.mock.calls[0][3]).toMatchObject({ attempt: 'initial' })
    expect(wmsState.completeInbound.mock.calls[1][3]).toMatchObject({ attempt: 'retry' })

    const continueBtn = wrapper.findAll('button').find((b) => b.text() === '继续')!
    await continueBtn.trigger('click')

    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()
    expect(wmsState.completeInbound).toHaveBeenCalledTimes(3)
    expect(wmsState.completeInbound.mock.calls[2][1]).not.toBe(firstKey)
  })

  it('确定性 422 后编辑采集批号会轮换 key，并按 initial 新意图提交', async () => {
    wmsState.completeInbound.mockImplementationOnce(
      (
        _id: string,
        _key: string,
        _lines?: unknown[],
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject({ success: false, statusCode: 422, message: '批号无效' })
      },
    )
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()
    const firstKey = wmsState.completeInbound.mock.calls[0][1]
    const batch = document.querySelector<HTMLInputElement>('[data-batch-input]')!
    batch.value = 'LOT-CORRECTED'
    batch.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    confirm.click()
    await flushPromises()

    expect(wmsState.completeInbound.mock.calls[1][1]).not.toBe(firstKey)
    expect(wmsState.completeInbound.mock.calls[1][2]).toEqual([
      {
        lineNo: '1',
        lotNo: 'LOT-CORRECTED',
        productionDate: undefined,
        expiryDate: '2027-12-31',
      },
    ])
    expect(wmsState.completeInbound.mock.calls[1][3]).toMatchObject({ attempt: 'initial' })
  })

  it('结果未知时锁定采集字段，只按冻结 lines/key 原样重放', async () => {
    wmsState.completeInbound.mockImplementationOnce(
      (
        _id: string,
        _key: string,
        _lines?: unknown[],
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new RequestTimeoutError())
      },
    )
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    confirm.click()
    await flushPromises()

    const first = wmsState.completeInbound.mock.calls[0]
    expect(document.querySelector<HTMLInputElement>('[data-batch-input]')!.disabled).toBe(true)
    expect(document.body.textContent).toContain('原内容重试')
    const sheet = wrapper.findAllComponents(NvBottomSheet).find((sheet) => sheet.props('open'))!
    sheet.vm.$emit('update:open', false)
    await wrapper.vm.$nextTick()
    expect(sheet.props('open')).toBe(true)
    const cancel = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '取消',
    )
    expect(cancel?.disabled).toBe(true)
    expect(routeGuardState.guard?.()).toBe(false)
    confirm.click()
    await flushPromises()

    const second = wmsState.completeInbound.mock.calls[1]
    expect(second[1]).toBe(first[1])
    expect(second[2]).toEqual(first[2])
    expect(second[3]).toMatchObject({ attempt: 'retry' })
  })

  it('completePending 时确认按钮禁用（防重）', async () => {
    wmsState.completePending = true
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
  })

  it('行数据加载中：禁止提交并显示加载态', async () => {
    wmsState.linesPending = true
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-lines-loading]')).toBeTruthy()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
  })

  it('行数据加载失败：禁止提交并显示重试入口', async () => {
    wmsState.linesError = new Error('boom')
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-testid="lines-error"]')).toBeTruthy()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
  })

  it('明细超量未取全（total>已取回）：禁止提交并提示不完整（fail closed）', async () => {
    wmsState.linesTotal = 600 // 单行已取回但 total=600 → 被截断，未证明完整
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-lines-incomplete]')).toBeTruthy()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
  })

  it('明细为空（total=0/成功空集）：判为不完整，禁止提交并可重试（fail closed）', async () => {
    // 入库单按域约束必有行；空集 = 精确单号未命中/投影滞后/异常 → 不得空明细完成。
    wmsState.linesByOrderNo['IB-2026-0001'] = []
    wmsState.linesTotal = 0
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-lines-incomplete]')).toBeTruthy()
    expect(document.querySelector('[data-testid="lines-incomplete-retry"]')).toBeTruthy()
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!
    expect(confirm.disabled).toBe(true)
    // GS1 扫码条在不完整态不渲染
    expect(document.querySelector('input[placeholder*="GS1"]')).toBeNull()
    document.querySelector<HTMLButtonElement>('[data-testid="lines-incomplete-retry"]')!.click()
    expect(wmsState.linesRefresh).toHaveBeenCalledTimes(1)
  })

  it('多行单：点选目标行 → 扫码（仅效期，无批号）绑定到选中行并落库', async () => {
    // 两行均无批号（新收货）；扫出的码只有效期，靠先选行绑定。
    wmsState.linesByOrderNo['IB-2026-0001'] = [
      {
        inboundOrderId: '11111111-1111-1111-1111-111111111111',
        inboundOrderNo: 'IB-2026-0001',
        inboundOrderLineId: 'line-1',
        lineNo: '1',
        skuCode: 'SKU-A',
        receivedQuantity: 20,
        lotNo: null,
        qualityGateStatus: 'pending',
      },
      {
        inboundOrderId: '11111111-1111-1111-1111-111111111111',
        inboundOrderNo: 'IB-2026-0001',
        inboundOrderLineId: 'line-2',
        lineNo: '2',
        skuCode: 'SKU-C',
        receivedQuantity: 8,
        lotNo: null,
        qualityGateStatus: 'not-required',
      },
    ]
    const GS = String.fromCharCode(29)
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    // 未选行时扫码提示先选行
    const gs1Input = document.querySelector<HTMLInputElement>('input[placeholder*="GS1"]')!
    gs1Input.value = `1726123110`
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    expect(document.querySelector('[data-gs1-notice]')?.textContent).toContain('先点选目标行')
    // 用独立「选此行」按钮点选第 2 行（容器非交互）→ 扫码仅效期 → 绑定到第 2 行
    document.querySelectorAll<HTMLButtonElement>('[data-select-line]')[1]!.click()
    await flushPromises()
    gs1Input.value = `172608${'01'}${GS}`
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    const lines = wmsState.completeInbound.mock.calls.at(-1)?.[2] as Array<Record<string, unknown>>
    expect(lines).toEqual([
      { lineNo: '2', lotNo: undefined, productionDate: undefined, expiryDate: '2026-08-01' },
    ])
  })

  it('完成后显示成功 Result，文案为「入库完成，待质检」', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('入库完成，待质检')
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
    expect(wrapper.text()).toContain('暂无收货单据')
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

  it('扫 GS1 批次码带出效期 → 行显示效期三色标 + 临期黄色提示 + 落库', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const gs1Input = document.querySelector<HTMLInputElement>('input[placeholder*="GS1"]')!
    expect(gs1Input).toBeTruthy()
    // (17)260801 效期 2026-08-01（距 7/15 约 17 天 → 临近过期红），(10)LOT-A 匹配收货行。
    gs1Input.value = `17260801${GS}10LOT-A`
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    expect(document.querySelector('[data-expiry-tag]')?.textContent).toContain('2026-08-01')
    expect(document.querySelector('[data-near-expiry-notice]')).toBeTruthy()
    document.querySelector<HTMLButtonElement>('[data-testid="confirm-complete"]')!.click()
    const lines = wmsState.completeInbound.mock.calls.at(-1)?.[2] as Array<Record<string, unknown>>
    expect(lines[0]).toMatchObject({ lotNo: 'LOT-A', expiryDate: '2026-08-01' })
  })

  it('扫非 GS1 码提示未识别', async () => {
    const wrapper = mount(InboundPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const gs1Input = document.querySelector<HTMLInputElement>('input[placeholder*="GS1"]')!
    gs1Input.value = 'NOT-A-GS1'
    gs1Input.dispatchEvent(new Event('input'))
    gs1Input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await flushPromises()
    expect(document.querySelector('[data-gs1-notice]')?.textContent).toContain('未识别')
  })
})
