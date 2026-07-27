import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import AvailabilityPage from './availability.vue'
import CountsPage from './counts.vue'
import LotsPage from './lots.vue'
import MovementsPage from './movements.vue'

const inventoryState = vi.hoisted(() => ({
  availabilityFilters: undefined as Record<string, string | undefined> | undefined,
  confirmAdjustment: vi.fn(),
  createCountTask: vi.fn(),
  postMovement: vi.fn(),
  expiryPage: undefined as { value: number } | undefined,
  expiryPageSize: undefined as { value: number } | undefined,
  expiryFilters: undefined as Record<string, string | undefined> | undefined,
  availabilityError: undefined as { value: unknown } | undefined,
  availabilityRows: undefined as { value: Array<Record<string, unknown>> } | undefined,
  // 未选物料 = 全厂库存总览态，选了物料 = 该物料台账明细态；测试要能切这两态。
  initialSkuCode: 'SKU-001',
  // 盘点 / 流水表格改为服务端读面后，页面不再持有会话内本地队列。
  countTaskRows: [] as Array<Record<string, unknown>>,
  countAdjustmentRows: [] as Array<Record<string, unknown>>,
  movementRows: [] as Array<Record<string, unknown>>,
  countTasksPage: undefined as { value: number } | undefined,
  countTasksPageSize: undefined as { value: number } | undefined,
  movementsPage: undefined as { value: number } | undefined,
  movementsPageSize: undefined as { value: number } | undefined,
  notifyError: vi.fn(),
  notifySuccess: vi.fn(),
}))

const siteStockState = vi.hoisted(() => ({
  scanMore: vi.fn(),
  refresh: vi.fn(),
  hasMore: undefined as { value: boolean } | undefined,
  scanning: undefined as { value: boolean } | undefined,
  rows: undefined as { value: Array<Record<string, unknown>> } | undefined,
}))

const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))
const routerState = vi.hoisted(() => ({ push: vi.fn() }))

vi.mock('vue-router', () => ({
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
  useRoute: () => routeState,
  useRouter: () => routerState,
}))

vi.mock('@/composables/useBusinessInventory', () => ({
  useInventoryAvailability: () => {
    const filters = {
      environmentId: 'env-dev',
      organizationId: 'org-001',
      qualityStatus: 'available',
      ownerType: 'owned',
      siteCode: 'S1',
      skuCode: inventoryState.initialSkuCode,
      uomCode: 'EA',
    }
    inventoryState.availabilityFilters = filters

    return {
      availability: computed(() => ({
        onHandQuantity: 10,
        availableQuantity: 7,
        reservedQuantity: 2,
      })),
      availabilityError: (inventoryState.availabilityError = ref(undefined)),
      availabilityLines: computed(
        () =>
          (inventoryState.availabilityRows ??= ref([
            {
              locationCode: 'A-01',
              lotNo: 'LOT-001',
              serialNo: 'SN-001',
              qualityStatus: 'available',
              ownerType: 'owned',
              reservedQuantity: 2,
              onHandQuantity: 10,
              availableQuantity: 7,
              productionDate: '2026-04-20',
              expiryDate: '2026-07-18',
              shelfLifeDays: 89,
              expiryDateSource: 'derived',
              isExpired: true,
              isBlocked: true,
              blockReason: '已过期，常规移动需授权放行。',
              movementAllowed: false,
              countAllowed: false,
              countBlockReason: '同一盘点定位存在多个生产日期或效期，请先缩小到唯一库存台账。',
            },
          ])).value,
      ),
      availabilityPending: ref(false),
      filters,
      refreshAvailability: vi.fn(),
    }
  },
  useInventoryExpiryAlerts: () => ({
    expiryAlerts: computed(() => [
      {
        skuCode: 'SKU-001',
        uomCode: 'EA',
        siteCode: 'S1',
        locationCode: 'A-01',
        lotNo: 'LOT-001',
        serialNo: 'SN-001',
        qualityStatus: 'available',
        ownerType: 'owned',
        ownerId: null,
        productionDate: '2026-06-15',
        expiryDate: '2026-07-25',
        daysUntilExpiry: 6,
        isExpired: false,
        isNearExpiry: true,
        shelfLifeDays: 40,
        expiryDateSource: 'direct',
        isBlocked: false,
        movementAllowed: true,
        countAllowed: true,
        reservedQuantity: 2,
        onHandQuantity: 10,
        availableQuantity: 7,
      },
    ]),
    expiryAlertsError: ref(undefined),
    expiryAlertsResponse: computed(() => ({
      items: [],
      totalCount: 51,
      expiredCount: 8,
      nearExpiryCount: 43,
      skuCount: 12,
      page: 1,
      pageSize: 50,
    })),
    expiryAlertsPage: (inventoryState.expiryPage = ref(1)),
    expiryAlertsPageSize: (inventoryState.expiryPageSize = ref(50)),
    expiryAlertsTotal: computed(() => 51),
    expiryAlertsPending: ref(false),
    expiryAlertsSuccessful: ref(true),
    filters: (inventoryState.expiryFilters = {
      environmentId: 'env-dev',
      organizationId: 'org-001',
      siteCode: 'S1',
    }),
    refreshExpiryAlerts: vi.fn(),
  }),
  useInventoryCounts: () => ({
    confirmAdjustment: inventoryState.confirmAdjustment,
    confirmAdjustmentError: ref(undefined),
    confirmAdjustmentPending: ref(false),
    createCountTask: inventoryState.createCountTask,
    createCountTaskError: ref(undefined),
    createCountTaskPending: ref(false),
    // 盘点表格来自服务端读面：页面不再持有会话内本地队列。
    countTasks: computed(() => ({
      items: inventoryState.countTaskRows,
      totalCount: inventoryState.countTaskRows.length,
    })),
    countTaskRows: computed(() => inventoryState.countTaskRows),
    countTasksError: ref(undefined),
    countTasksPending: ref(false),
    countTasksPage: (inventoryState.countTasksPage = ref(1)),
    countTasksPageSize: (inventoryState.countTasksPageSize = ref(50)),
    countTasksTotal: computed(() => inventoryState.countTaskRows.length),
    countAdjustments: computed(() => ({
      items: inventoryState.countAdjustmentRows,
      totalCount: inventoryState.countAdjustmentRows.length,
    })),
    countAdjustmentRows: computed(() => inventoryState.countAdjustmentRows),
    refreshCountTasks: vi.fn(),
    filters: {
      environmentId: 'env-dev',
      organizationId: 'org-001',
    },
  }),
  useInventoryMovement: () => ({
    postMovement: inventoryState.postMovement,
    postMovementError: ref(undefined),
    postMovementPending: ref(false),
    // 流水表格来自服务端读面：页面不再持有会话内本地队列。
    movements: computed(() => ({
      items: inventoryState.movementRows,
      totalCount: inventoryState.movementRows.length,
    })),
    movementRows: computed(() => inventoryState.movementRows),
    movementsError: ref(undefined),
    movementsPending: ref(false),
    movementsPage: (inventoryState.movementsPage = ref(1)),
    movementsPageSize: (inventoryState.movementsPageSize = ref(50)),
    movementsTotal: computed(() => inventoryState.movementRows.length),
    refreshMovements: vi.fn(),
    filters: {
      environmentId: 'env-dev',
      organizationId: 'org-001',
    },
  }),
}))

vi.mock('@/utils/notify', () => ({
  notifyError: inventoryState.notifyError,
  notifySuccess: inventoryState.notifySuccess,
}))

// 目录与默认值来自主数据 facade，这里给确定的目录，页面测试只关心「选择器有得选、单位自动带出」。
vi.mock('@/composables/useInventoryScope', async () => {
  const { computed, ref, watch } = await import('vue')
  const baseUomBySku: Record<string, string> = { 'SKU-001': 'EA', 'RM-BAR-45-01': 'kg' }
  const catalog = {
    siteOptions: computed(() => [{ value: 'S1', label: '上海工厂' }]),
    sitesPending: ref(false),
    skuOptions: computed(() => [
      { value: 'SKU-001', label: '前减振器总成', hint: 'EA' },
      { value: 'RM-BAR-45-01', label: '45号钢棒料', hint: 'kg' },
    ]),
    skusPending: ref(false),
  }
  return {
    FALLBACK_INVENTORY_SITE_CODE: 'SITE-001',
    FALLBACK_INVENTORY_UOM_CODE: 'pcs',
    useInventoryScopeCatalog: () => catalog,
    // 与真实实现同构：工厂缺省填默认工厂，单位始终跟随所选物料的基本单位。
    useInventoryScopeDefaults: (filters: {
      skuCode?: string
      uomCode?: string
      siteCode?: string
    }) => {
      if (!(filters.siteCode ?? '').trim()) filters.siteCode = 'S1'
      watch(
        () => filters.skuCode,
        (skuCode) => {
          const trimmed = (skuCode ?? '').trim()
          filters.uomCode = trimmed ? (baseUomBySku[trimmed] ?? 'pcs') : ''
        },
        { immediate: true },
      )
      return catalog
    },
    useInventorySiteExpiryOverview: () => ({
      overviewError: ref(undefined),
      overviewExpiredCount: computed(() => 8),
      overviewNearExpiryCount: computed(() => 43),
      overviewPending: ref(false),
      overviewSkuCount: computed(() => 12),
      overviewTotalCount: computed(() => 51),
      overviewUrgentLines: computed(() => []),
      refreshOverview: vi.fn(),
    }),
  }
})

// 全厂库存总览：真实实现会按物料目录并发扫台账，测试只关心「首屏有表、覆盖进度如实、能继续扫」。
vi.mock('@/composables/useInventorySiteStock', async () => {
  const { computed, ref } = await import('vue')
  const rows = ref([
    {
      skuCode: 'SKU-001',
      skuName: '前减振器总成',
      uomCode: 'EA',
      onHandQuantity: 10,
      reservedQuantity: 2,
      availableQuantity: 7,
      lineCount: 3,
      locationCount: 2,
      earliestExpiry: '2026-07-18',
      hasBlocked: true,
    },
    {
      skuCode: 'RM-BAR-45-01',
      skuName: '45号钢棒料',
      uomCode: 'kg',
      onHandQuantity: 1200,
      reservedQuantity: 300,
      availableQuantity: 900,
      lineCount: 1,
      locationCount: 1,
      hasBlocked: false,
    },
  ])
  // 批次与预留页吃的是逐行台账（带批次/序列号的可追溯单元），形状与可用量明细行一致。
  const trackedLines = ref([
    {
      skuCode: 'SKU-001',
      skuName: '前减振器总成',
      uomCode: 'EA',
      locationCode: 'A-01',
      lotNo: 'LOT-001',
      serialNo: 'SN-001',
      qualityStatus: 'available',
      ownerType: 'owned',
      onHandQuantity: 10,
      reservedQuantity: 2,
      availableQuantity: 7,
      expiryDate: '2026-07-18',
      isExpired: true,
      isBlocked: true,
      movementAllowed: false,
      countAllowed: false,
    },
    {
      skuCode: 'RM-BAR-45-01',
      skuName: '45号钢棒料',
      uomCode: 'kg',
      locationCode: 'B-02',
      lotNo: 'LOT-2026-0714',
      serialNo: null,
      qualityStatus: 'available',
      ownerType: 'owned',
      onHandQuantity: 1200,
      reservedQuantity: 300,
      availableQuantity: 900,
      isExpired: false,
      isBlocked: false,
      movementAllowed: true,
      countAllowed: true,
    },
  ])
  const hasMore = ref(true)
  const scanning = ref(false)
  siteStockState.rows = rows
  siteStockState.hasMore = hasMore
  siteStockState.scanning = scanning

  return {
    SITE_STOCK_SCAN_BATCH: 24,
    useInventorySiteStockOverview: () => ({
      refreshSiteStock: siteStockState.refresh,
      scanMoreSiteStock: siteStockState.scanMore,
      siteStockAllRows: computed(() => rows.value),
      siteStockCatalogPending: ref(false),
      siteStockError: ref(undefined),
      siteStockFailedCount: computed(() => 0),
      siteStockHasMore: hasMore,
      siteStockRows: computed(() => rows.value),
      siteStockScannedCount: computed(() => 24),
      siteStockScanning: scanning,
      siteStockTotalSkuCount: computed(() => 48),
      siteStockLines: computed(() => trackedLines.value),
      siteStockTrackedLines: computed(() => trackedLines.value),
    }),
  }
})

// 库位/批次/序列号后端无主数据读面，真实实现从台账与仓储作业记录派生；测试给确定目录。
vi.mock('@/composables/useWarehouseCodeCatalog', async () => {
  const { computed, ref } = await import('vue')
  return {
    WAREHOUSE_CATALOG_SOURCE_TEXT: '数据来自现有库存与仓储作业记录（暂无库位主数据）',
    WAREHOUSE_LOCATION_EMPTY_TEXT: '系统里还没有出现过库位，可直接录入新库位编码',
    WAREHOUSE_LOT_EMPTY_TEXT: '系统里还没有出现过批次',
    WAREHOUSE_SERIAL_EMPTY_TEXT: '系统里还没有出现过序列号',
    useWarehouseCodeCatalog: () => ({
      locationOptions: computed(() => [
        { value: 'A-01', label: 'A-01' },
        { value: 'B-02', label: 'B-02' },
      ]),
      lotOptions: computed(() => [{ value: 'LOT-001', label: 'LOT-001' }]),
      serialOptions: computed(() => [{ value: 'SN-001', label: 'SN-001' }]),
      warehouseCatalogPending: ref(false),
    }),
  }
})

const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  PageHeader: {
    props: ['title', 'breadcrumbs', 'count'],
    template:
      '<header><h1>{{ title }}</h1><span data-page-count>{{ count }}</span><slot name="actions" /></header>',
  },
  SectionCards: { template: '<div><slot /></div>' },
  SectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }}</div>',
  },
  Toolbar: { props: ['showSearch'], template: '<div><slot name="filters" /></div>' },
  // NvDataTable stub renders rows + the cell-actions slot, exposing a design-system table marker.
  NvDataTable: {
    props: ['rows', 'columns', 'rowKey', 'pagination', 'emptyMessage'],
    template: `<table data-ui-table :data-pagination="String(pagination)" :data-empty-message="emptyMessage"><tbody><tr v-for="(row, i) in rows" :key="i">
      <td v-for="column in columns" :key="column.key" :data-cell="column.key">
        <slot :name="'cell-' + column.key" :row="row">{{ column.accessor ? column.accessor(row) : row[column.key] }}</slot>
      </td>
    </tr></tbody></table>`,
  },
  DataTablePagination: true,
  NvPagination: {
    props: ['page', 'pageSize', 'totalItems', 'showEdges', 'siblingCount'],
    emits: ['update:page', 'update:pageSize'],
    template:
      '<div data-pagination-total :data-show-edges="String(showEdges)" :data-sibling-count="String(siblingCount)">{{ totalItems }}<button data-next-page @click="$emit(\'update:page\', page + 1)">下一页</button><button data-page-size @click="$emit(\'update:pageSize\', 100); $emit(\'update:page\', 1)">100/页</button></div>',
  },
  RowActions: { props: ['label'], template: '<div><slot /></div>' },
  DropdownMenuItem: { template: '<button v-bind="$attrs"><slot /></button>' },
  DropdownMenuSeparator: true,
  // NvDialog (reka DialogRoot) stubs render slot content unconditionally so dialog forms are testable.
  DialogRoot: { props: ['open'], template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvButton: {
    props: ['disabled'],
    template: '<button :disabled="disabled" v-bind="$attrs"><slot /></button>',
  },
  Field: { template: '<div><slot /></div>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  // 实体选择弹窗内部自带 DialogRoot；这里的 DialogRoot 桩会截断它的上下文，所以整件替换成 select。
  NvEntityPicker: {
    props: ['modelValue', 'options', 'title', 'placeholder', 'loading'],
    emits: ['update:modelValue'],
    template:
      '<select data-entity-picker v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  SelectValue: true,
  Spinner: true,
}

function mountInventoryPage(component: unknown) {
  return mount(component, {
    global: {
      plugins: [createPinia()],
      stubs: {
        ...uiStubs,
        RouterLink: {
          props: ['to'],
          template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
        },
      },
    },
  })
}

/** 一行「待实盘」的服务端盘点任务（确认差异动作只在 open 态可用）。 */
function openCountTaskRow(countTaskId: string) {
  return {
    countTaskId,
    countTaskCode: 'CNT-2026-0001',
    skuCode: 'SKU-001',
    uomCode: 'EA',
    siteCode: 'S1',
    locationCode: 'A-01',
    lotNo: 'LOT-OPENING-SKU-001',
    qualityStatus: 'unrestricted',
    ownerType: 'company',
    expectedLedgerVersion: 3,
    status: 'open',
    createdAtUtc: '2026-03-02T09:00:00Z',
    updatedAtUtc: '2026-03-02T09:00:00Z',
  }
}

describe('inventory workflow pages', () => {
  beforeEach(() => {
    routeState.query = {}
    routerState.push.mockReset()
    inventoryState.confirmAdjustment.mockReset()
    inventoryState.createCountTask.mockReset()
    inventoryState.postMovement.mockReset()
    inventoryState.notifyError.mockReset()
    inventoryState.notifySuccess.mockReset()
    inventoryState.availabilityRows = undefined
    inventoryState.initialSkuCode = 'SKU-001'
    inventoryState.countTaskRows = []
    inventoryState.countAdjustmentRows = []
    inventoryState.movementRows = []
    siteStockState.scanMore.mockReset()
    siteStockState.refresh.mockReset()
    if (siteStockState.hasMore) siteStockState.hasMore.value = true
    if (siteStockState.scanning) siteStockState.scanning.value = false
  })

  describe('全厂库存首屏（不选物料也要看到库存表）', () => {
    it('未选物料时直接渲染全厂库存表，而不是要求先填查询条件', () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      const table = wrapper.find('[data-ui-table]')
      expect(table.exists()).toBe(true)
      // 两个物料各一行，说明进页面就有货可看。
      expect(table.findAll('tbody tr')).toHaveLength(2)
      expect(wrapper.text()).toContain('前减振器总成')
      expect(wrapper.text()).toContain('45号钢棒料')
      expect(wrapper.text()).not.toContain('请选择物料')
    })

    it('如实交代扫描覆盖范围并给出继续扫描的出路', async () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      expect(wrapper.text()).toContain('已扫描 24/48 个物料')
      const scanMore = wrapper
        .findAll('button')
        .find((button) => button.text().includes('继续扫描'))
      expect(scanMore).toBeDefined()
      await scanMore!.trigger('click')
      expect(siteStockState.scanMore).toHaveBeenCalledTimes(1)
    })

    it('点总览行的物料即下钻到该物料台账，无需回到筛选条重填', async () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      const skuCell = wrapper.find('[data-cell="skuCode"] button')
      expect(skuCell.exists()).toBe(true)
      await skuCell.trigger('click')

      expect(inventoryState.availabilityFilters?.skuCode).toBe('SKU-001')
    })

    it('进入明细后提供返回全厂库存的出口，并清掉下钻带上的库位/批次条件', async () => {
      const wrapper = mountInventoryPage(AvailabilityPage)
      inventoryState.availabilityFilters!.locationCode = 'A-01'

      const back = wrapper.findAll('button').find((button) => button.text().includes('返回全厂库存'))
      expect(back).toBeDefined()
      await back!.trigger('click')

      expect(inventoryState.availabilityFilters?.skuCode).toBe('')
      expect(inventoryState.availabilityFilters?.locationCode).toBe('')
    })

    it('批次与预留页未选物料时铺全厂批次台账，而不是拦一个选择物料的空态', () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(LotsPage)

      const table = wrapper.find('[data-ui-table]')
      expect(table.exists()).toBe(true)
      expect(table.findAll('tbody tr')).toHaveLength(2)
      expect(wrapper.text()).toContain('LOT-001')
      expect(wrapper.text()).toContain('LOT-2026-0714')
      expect(wrapper.text()).not.toContain('选择物料，查看批次')
      expect(wrapper.text()).toContain('已扫描 24/48 个物料')
    })

    it('库位/批次/序列号是从既有数据派生的选择器，不再让仓管手输', () => {
      const wrapper = mountInventoryPage(AvailabilityPage)

      const pickerLabels = wrapper
        .findAll('[data-entity-picker]')
        .map((picker) => picker.attributes('aria-label'))
      expect(pickerLabels).toContain('库位')
      expect(pickerLabels).toContain('批次')
      expect(pickerLabels).toContain('序列号')
      // 这三项过去是自由文本框，改造后不允许再出现同名输入框。
      const inputLabels = wrapper.findAll('input').map((input) => input.attributes('aria-label'))
      expect(inputLabels).not.toContain('库位')
      expect(inputLabels).not.toContain('批次')
      expect(inputLabels).not.toContain('序列号')
    })
  })

  it('uses design-system table components for the stock count read face', () => {
    const wrapper = mountInventoryPage(CountsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('uses design-system table components for the stock movement read face', () => {
    const wrapper = mountInventoryPage(MovementsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  /**
   * 盘点与流水表格必须渲染服务端返回的行。
   *
   * 这两页此前只有会话内本地队列，刷新即空；#1194 补了库存域的盘点 / 流水读面之后，
   * 页面必须真的从读面取数——否则补了后端也白补。
   */
  it('renders stock count tasks and stock movements from the server read face', () => {
    inventoryState.countTaskRows = [
      { ...openCountTaskRow('COUNT-TASK-1'), status: 'pending-approval', varianceQuantity: -3 },
    ]
    inventoryState.countAdjustmentRows = [
      {
        adjustmentId: 'ADJ-1',
        countTaskCode: 'CNT-2026-0001',
        status: 'pending-approval',
        approvalChainId: 'APPR-CNT-2026-0001',
      },
    ]
    inventoryState.movementRows = [
      {
        movementId: 'MOVE-1',
        movementType: 'inbound',
        sourceService: 'seed:world-history',
        sourceDocumentId: 'PR-2026-0001',
        skuCode: 'RM-BAR-45-01',
        uomCode: 'kg',
        siteCode: 'S1',
        locationCode: 'WH-WB-RM-01',
        lotNo: 'LOT-PR-2026-0001',
        quantity: 120,
        postedAtUtc: '2026-03-02T09:00:00Z',
      },
    ]

    const counts = mountInventoryPage(CountsPage)
    expect(counts.text()).toContain('CNT-2026-0001')
    expect(counts.text()).toContain('待审批')
    expect(counts.text()).toContain('APPR-CNT-2026-0001')

    const movements = mountInventoryPage(MovementsPage)
    expect(movements.text()).toContain('PR-2026-0001')
    expect(movements.text()).toContain('入库')
    expect(movements.text()).toContain('LOT-PR-2026-0001')
  })

  it('links inventory lot context to barcode scan records', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)

    const link = wrapper
      .findAll('[data-router-link]')
      .find((item) => item.text().includes('扫码记录'))
    expect(link?.attributes('data-to')).toContain('/barcode/scans')
    expect(link?.attributes('data-to')).toContain('inventory.count')
    expect(link?.attributes('data-to')).toContain('LOT-001')
    // 生产日期 / 保质期 / 效期来源已收进效期单元格的第二行（主要列收敛）。
    const expiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(expiryCell).toContain('2026-07-18')
    expect(expiryCell).toContain('2026-04-20')
    expect(expiryCell).toContain('89 天')
    expect(expiryCell).toContain('系统推导')
    expect(wrapper.text()).toContain('已过期，常规移动需授权放行。')
    expect(wrapper.text()).toContain('同一盘点定位存在多个生产日期或效期，请先缩小到唯一库存台账。')
    expect(wrapper.text()).toContain('该库存行暂不能发起移动，请稍后重试或联系管理员。')
    expect(
      wrapper
        .findAll('button')
        .find((button) => button.text().includes('发起移动'))
        ?.attributes(),
    ).toHaveProperty('disabled')
    expect(wrapper.text()).not.toContain('facade 未提供 total')
    expect(wrapper.get('[data-ui-table]').attributes('data-pagination')).toBe('false')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    const nearExpiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(nearExpiryCell).toContain('2026-07-25')
    expect(nearExpiryCell).toContain('2026-06-15')
    expect(wrapper.get('[data-pagination-total]').text()).toContain('51')
    expect(wrapper.get('[data-pagination-total]').attributes('data-show-edges')).toBe('false')
    expect(wrapper.get('[data-pagination-total]').attributes('data-sibling-count')).toBe('0')
    await wrapper.get('[data-next-page]').trigger('click')
    expect(inventoryState.expiryPage?.value).toBe(2)
    await wrapper.get('[data-page-size]').trigger('click')
    expect(inventoryState.expiryPageSize?.value).toBe(100)
    expect(inventoryState.expiryPage?.value).toBe(1)
  })

  it('uses a business-safe toast instead of exposing raw availability errors', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    const error = new Error('HTTP 500 downstream stack trace')

    inventoryState.availabilityError!.value = error
    await nextTick()

    expect(inventoryState.notifyError).toHaveBeenCalledWith(
      error,
      '库存可用量加载失败，请稍后重试。',
    )
    expect(wrapper.get('[data-ui-table]').attributes('data-empty-message')).toBe(
      '库存可用量加载失败，请稍后重试。',
    )
    expect(wrapper.text()).not.toContain('downstream stack trace')
  })

  it('distinguishes a selected factory from business context still loading', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    inventoryState.expiryFilters!.organizationId = ''

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    await nextTick()

    expect(wrapper.get('[data-page-count]').text()).toBe('业务上下文加载中')
    expect(wrapper.get('[data-ui-table]').attributes('data-empty-message')).toBe(
      '业务上下文加载中，请稍候。',
    )
    expect(wrapper.text()).not.toContain('请选择工厂')
  })

  it('shows count-only operation reasons and the honest missing-reason fallback', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    const row = inventoryState.availabilityRows!.value[0]!
    row.movementAllowed = true
    row.countAllowed = false
    row.countBlockReason = '该定位存在多个效期台账，请缩小盘点范围。'
    await nextTick()

    expect(wrapper.get('[data-operation-block-reason]').text()).toBe(
      '该定位存在多个效期台账，请缩小盘点范围。',
    )

    row.countBlockReason = undefined
    await nextTick()
    expect(wrapper.get('[data-operation-block-reason]').text()).toBe(
      '该库存行暂不能创建盘点，请稍后重试或联系管理员。',
    )
  })

  it('renders a facade-backed lot and reservation page with traceability links', async () => {
    const wrapper = mountInventoryPage(LotsPage)

    expect(wrapper.text()).toContain('批次与预留')
    expect(wrapper.text()).toContain('LOT-001')
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.get('[data-cell="reservedQuantity"]').text()).toBe('2')
    // 生产日期 / 保质期 / 效期来源已收进效期单元格的第二行（主要列收敛）。
    const expiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(expiryCell).toContain('2026-07-18')
    expect(expiryCell).toContain('2026-04-20')
    expect(expiryCell).toContain('89 天')
    expect(expiryCell).toContain('系统推导')
    expect(inventoryState.availabilityFilters?.qualityStatus).toBeUndefined()

    const links = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(
      links.some(
        (to) =>
          to.includes('/mes/traceability') && to.includes('batchOrSerial') && to.includes('SN-001'),
      ),
    ).toBe(true)
    const disabledWms = wrapper.findAll('button').find((button) => button.text().trim() === 'WMS')
    expect(disabledWms?.attributes()).toHaveProperty('disabled')
    expect(wrapper.get('[data-operation-block-reason]').text()).toBe(
      '该库存行暂不能发起移动，请稍后重试或联系管理员。',
    )

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    const nearExpiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(nearExpiryCell).toContain('2026-07-25')
    expect(nearExpiryCell).toContain('2026-06-15')
    expect(wrapper.get('[data-pagination-total]').text()).toContain('51')
    const nearExpiryLinks = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(nearExpiryLinks.some((to) => to.includes('/barcode/scans'))).toBe(true)
    expect(
      nearExpiryLinks.some(
        (to) => to.includes('/wms/picking') && to.includes('locationCode') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(
      nearExpiryLinks.some(
        (to) =>
          to.includes('/quality/inspections') &&
          to.includes('batchNo') &&
          to.includes('materialLotId') &&
          to.includes('LOT-001'),
      ),
    ).toBe(true)
  })

  it('generates a fresh idempotency key each time the same count task is adjusted', async () => {
    // 盘点任务行来自服务端读面，不再是「本次提交后塞进本地队列」的那一行。
    inventoryState.countTaskRows = [openCountTaskRow('COUNT-TASK-1')]
    inventoryState.confirmAdjustment.mockResolvedValue({ data: { movementId: 'MOVE-1' } })

    const wrapper = mountInventoryPage(CountsPage)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认差异'))!
      .trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('5')
    await wrapper.findAll('form')[1]!.trigger('submit')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认差异'))!
      .trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('6')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).toHaveBeenCalledTimes(2)
    const firstKey = inventoryState.confirmAdjustment.mock.calls[0][1].idempotencyKey
    const secondKey = inventoryState.confirmAdjustment.mock.calls[1][1].idempotencyKey
    expect(firstKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).not.toBe(firstKey)
  })

  it('requires the adjustment action to be opened from a count task row before submitting', async () => {
    const wrapper = mountInventoryPage(CountsPage)

    // 盘点任务不再是可输入字段：只能由所选任务行带出，没有带出就不该发请求。
    expect(wrapper.find('#count-adjust-task-id').exists()).toBe(false)
    await wrapper.find('#count-adjust-quantity').setValue('7')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).not.toHaveBeenCalled()
  })
})
