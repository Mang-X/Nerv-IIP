import { mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PickingPage from './picking.vue'

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

const routeState = vi.hoisted(() => ({
  query: {} as Record<string, string>,
}))

const wmsState = vi.hoisted(() => ({
  filters: undefined as { keyword?: string; locationCode?: string; status?: string } | undefined,
}))

vi.mock('vue-router', () => ({
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
  useRoute: () => routeState,
}))

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')

  return {
    usePagedList: () => ({
      page: shallowRef(1),
      pageSize: shallowRef(100),
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
        { value: 'A-01', label: 'A-01' },
        { value: 'STAGE-01', label: 'STAGE-01' },
      ]),
      lotOptions: computed(() => [{ value: 'LOT-001', label: 'LOT-001' }]),
      serialOptions: computed(() => [{ value: 'SN-001', label: 'SN-001' }]),
      warehouseCatalogPending: shallowRef(false),
    }),
  }
})

vi.mock('@/composables/useBusinessWms', () => ({
  // 拣货任务必须挂在已存在的出库单下：出库单选择器的目录来源。
  useWmsOutboundOrders: () => ({
    filters: reactive({ skip: 0, take: 200 }),
    outboundOrders: computed(() => [
      { outboundOrderId: 'ob-1', outboundOrderNo: 'OB-001', siteCode: 'S1' },
    ]),
    outboundOrdersError: shallowRef(undefined),
    outboundOrdersPending: shallowRef(false),
    outboundOrdersTotal: computed(() => 1),
    refreshOutboundOrders: vi.fn(),
  }),
  useWmsPickingTasks: () => {
    const filters = reactive({
      environmentId: 'env-dev',
      keyword: undefined as string | undefined,
      locationCode: undefined as string | undefined,
      organizationId: 'org-001',
      status: undefined as string | undefined,
      take: 100,
    })
    wmsState.filters = filters

    return {
      createPicking: vi.fn(),
      createPickingError: shallowRef(undefined),
      createPickingPending: shallowRef(false),
      filters,
      pickingTasks: computed(() => [
        {
          fromLocationCode: 'A-01',
          plannedQuantity: 5,
          siteCode: 'S1',
          skuCode: 'SKU-001',
          sourceOrderNo: 'OB-001',
          status: 'created',
          taskNo: 'PICK-001',
          toLocationCode: 'STAGE-01',
          uomCode: 'EA',
          warehouseTaskId: 'pick-1',
        },
      ]),
      pickingTasksError: shallowRef(undefined),
      pickingTasksPending: shallowRef(false),
      pickingTasksTotal: computed(() => 0),
      refreshPickingTasks: vi.fn(),
    }
  },
}))

const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  NvDataTable: {
    props: ['rows', 'columns'],
    template: `<table data-ui-table><tbody><tr v-for="(row, i) in rows" :key="i">
      <td v-for="column in columns" :key="column.key" :data-cell="column.key">
        <slot :name="'cell-' + column.key" :row="row">{{ column.accessor ? column.accessor(row) : row[column.key] }}</slot>
      </td>
    </tr></tbody></table>`,
  },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogClose: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldError: true,
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  PageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1>{{ count }}<slot name="actions" /></header>',
  },
  NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
  Toolbar: { template: '<div><slot name="filters" /></div>' },
}

describe('WMS picking route context', () => {
  beforeEach(() => {
    routeState.query = {}
    wmsState.filters = undefined
  })

  it('initializes picking filters from inventory lot context', () => {
    routeState.query = {
      locationCode: 'A-01',
      lotNo: 'LOT-001',
      serialNo: 'SN-001',
      skuCode: 'SKU-001',
    }

    mount(PickingPage, { global: { stubs: uiStubs } })

    expect(wmsState.filters).toEqual(
      expect.objectContaining({
        keyword: 'SKU-001',
        locationCode: 'A-01',
      }),
    )
  })

  it('renders picking row inventory links without unsupported scan workflow links', () => {
    const wrapper = mount(PickingPage, { global: { stubs: uiStubs } })

    expect(wrapper.text()).toContain('库存明细')
    expect(wrapper.text()).toContain('SKU-001')
    expect(wrapper.text()).toContain('A-01')
    expect(wrapper.text()).toContain('OB-001')
    expect(wrapper.text()).toContain('请到库存可用量或批次与预留页查看')
    expect(wrapper.text()).not.toContain('后端缺口')

    const links = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(
      links.some(
        (to) =>
          to.includes('/inventory/availability') && to.includes('SKU-001') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(
      links.some(
        (to) => to.includes('/inventory/lots') && to.includes('SKU-001') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(links.some((to) => to.includes('/barcode/scans'))).toBe(false)
  })
})
