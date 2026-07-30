import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PutawayPage from './putaway.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const state = vi.hoisted(() => ({
  routeQuery: {
    inboundOrderNo: 'IB-1',
    inboundOrderId: 'ib-1',
    create: '1',
  } as Record<string, unknown>,
  createPutaway: vi.fn(),
  permissionCodes: ['business.wms.receipts.read', 'business.wms.receipts.manage'] as string[],
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: state.routeQuery }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

vi.mock('@/composables/useWmsWorkScope', async () => {
  const { computed, shallowRef } = await import('vue')
  return {
    bindWmsWorkScopeFilters: (filters: { scopeKind?: string; scopeId?: string; skip: number }) => {
      filters.scopeKind = 'self'
      filters.scopeId = 'emp049'
      filters.skip = 0
      return {
        scopeKey: shallowRef('self:emp049'),
        scopeOptions: computed(() => [{ label: '我的任务', value: 'self:emp049' }]),
        selectedScopeLabel: computed(() => '我的任务'),
        hasSelection: computed(() => true),
        pending: shallowRef(false),
        error: shallowRef(undefined),
        refresh: vi.fn(async () => undefined),
      }
    },
  }
})

vi.mock('@/composables/useBusinessWms', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  return {
    useWmsPutawayTasks: () => ({
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      }),
      putawayTasks: computed(() => []),
      putawayTasksError: shallowRef(undefined),
      putawayTasksPending: shallowRef(false),
      putawayTasksTotal: computed(() => 0),
      putawayTasksLastUpdatedAt: shallowRef('2026-07-28T10:20:30.000Z'),
      refreshPutawayTasks: vi.fn(),
      createPutaway: state.createPutaway,
      createPutawayPending: shallowRef(false),
      createPutawayError: shallowRef(undefined),
    }),
    // 入库单选择器的目录来源：上架任务必须挂在已存在的入库单下。
    useWmsInboundOrders: () => ({
      filters: reactive({ skip: 0, take: 200 }),
      inboundOrders: computed(() => [
        { inboundOrderId: 'ib-1', inboundOrderNo: 'IB-1', status: 'Open' },
      ]),
      inboundOrdersError: shallowRef(undefined),
      inboundOrdersPending: shallowRef(false),
      inboundOrdersTotal: computed(() => 1),
      refreshInboundOrders: vi.fn(),
    }),
  }
})

// 库位目录后端无读面，真实实现从仓储作业记录派生；测试给确定选项。
vi.mock('@/composables/useWarehouseCodeCatalog', async () => {
  const { computed, shallowRef } = await import('vue')
  return {
    WAREHOUSE_CATALOG_SOURCE_TEXT: '数据来自现有库存与仓储作业记录（暂无库位主数据）',
    WAREHOUSE_LOCATION_EMPTY_TEXT: '系统里还没有出现过库位，可直接录入新库位编码',
    WAREHOUSE_LOT_EMPTY_TEXT: '系统里还没有出现过批次',
    WAREHOUSE_SERIAL_EMPTY_TEXT: '系统里还没有出现过序列号',
    useWarehouseCodeCatalog: () => ({
      locationOptions: computed(() => [
        { value: 'STAGE-01', label: 'STAGE-01' },
        { value: 'RACK-A-01-01', label: 'RACK-A-01-01' },
      ]),
      lotOptions: computed(() => [{ value: 'LOT-001', label: 'LOT-001' }]),
      serialOptions: computed(() => [{ value: 'SN-001', label: 'SN-001' }]),
      warehouseCatalogPending: shallowRef(false),
    }),
  }
})

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')
  return {
    usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('100') }),
  }
})

describe('WMS putaway route handoff', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    state.createPutaway.mockResolvedValue(undefined)
    state.permissionCodes = ['business.wms.receipts.read', 'business.wms.receipts.manage']
  })

  it('opens the create flow with the real inbound order id and submits it unchanged', async () => {
    const wrapper = mount(PutawayPage, {
      attachTo: document.body,
      global: {
        stubs: wmsStubs(),
      },
    })
    await flushPromises()

    expect(document.body.textContent).toContain('新建上架任务')
    // 带出式录入：入库单来自所选收货行 → 只读展示人读单号，不再是可编辑输入框。
    const carried = document.body.querySelector('[data-slot="carried-context"]')
    expect(carried?.textContent).toContain('IB-1')
    expect(document.body.querySelector('#wms-putaway-inbound')).toBeNull()

    await setInput('#wms-putaway-no', 'PUT-IB-1-01')
    await setInput('#wms-putaway-line', '1')
    await setInput('#wms-putaway-from', 'QA-STAGE-01')
    await setInput('#wms-putaway-to', 'RACK-A-01')
    await setInput('#wms-putaway-qty', '5')
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.createPutaway).toHaveBeenCalledWith('ib-1', {
      taskNo: 'PUT-IB-1-01',
      lineNo: '1',
      fromLocationCode: 'QA-STAGE-01',
      toLocationCode: 'RACK-A-01',
      quantity: 5,
    })

    wrapper.unmount()
  })

  it('requires a positive quantity before calling the create endpoint', async () => {
    const wrapper = mountPutaway()
    await flushPromises()

    await setInput('#wms-putaway-no', 'PUT-IB-1-01')
    await setInput('#wms-putaway-line', '1')
    await setInput('#wms-putaway-from', 'QA-STAGE-01')
    await setInput('#wms-putaway-to', 'RACK-A-01')
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.createPutaway).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('上架数量需为正数')
    wrapper.unmount()
  })

  it('does not auto-open the create form for a read-only WMS principal', async () => {
    state.permissionCodes = ['business.wms.receipts.read']
    const wrapper = mountPutaway()
    await flushPromises()

    expect(document.body.querySelector('[data-slot="carried-context"]')).toBeNull()
    expect(document.body.querySelector('#wms-putaway-no')).toBeNull()
    expect(wrapper.text()).not.toContain('新建上架任务')
    wrapper.unmount()
  })
})

function mountPutaway() {
  return mount(PutawayPage, {
    attachTo: document.body,
    global: {
      stubs: wmsStubs(),
    },
  })
}

/**
 * 库位与入库单已从自由文本输入框改成只选的实体选择器。
 * 这个用例关心的是「路由带参 → 提交体不变」，不是选择器自身的交互，
 * 所以把选择器桩成一个带同名 id 的输入位，让下面的 setInput 仍然表达「选中了某个库位」。
 */
function wmsStubs() {
  return {
    BusinessLayout: { template: '<main><slot /></main>' },
    WmsInventoryContextPanel: true,
    NvEntityPicker: {
      props: ['modelValue', 'options', 'id'],
      emits: ['update:modelValue'],
      template:
        '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
    },
    NvSearchSelect: {
      props: ['modelValue', 'options', 'id'],
      emits: ['update:modelValue'],
      template:
        '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
    },
  }
}

async function setInput(selector: string, value: string) {
  const input = document.body.querySelector<HTMLInputElement>(selector)
  expect(input).not.toBeNull()
  input!.value = value
  input!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
}
