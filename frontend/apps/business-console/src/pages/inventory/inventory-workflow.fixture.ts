import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, ref } from 'vue'
import { vi } from 'vitest'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i

const inventoryState = vi.hoisted(() => ({
  availabilityFilters: undefined as Record<string, string | undefined> | undefined,
  confirmAdjustment: vi.fn(),
  createCountTask: vi.fn(),
  restartCountTask: vi.fn(),
  cancelCountTask: vi.fn(),
  postMovement: vi.fn(),
  expiryPage: undefined as { value: number } | undefined,
  expiryPageSize: undefined as { value: number } | undefined,
  expiryFilters: undefined as Record<string, string | undefined> | undefined,
  availabilityError: undefined as { value: unknown } | undefined,
  availabilityQuantity: undefined as { value: number } | undefined,
  availabilityPending: undefined as { value: boolean } | undefined,
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
  notifyOperationFailure: vi.fn(),
  notifySuccess: vi.fn(),
  partnerResolved: true,
}))

vi.mock('@/composables/master-data/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: (id?: string | null) =>
        id && inventoryState.partnerResolved ? '华东客户' : undefined,
      resolvePartnerLabel: (id?: string | null, fallback = '未指定') =>
        id && inventoryState.partnerResolved ? '华东客户' : fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})

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

vi.mock('@/composables/inventory/useBusinessInventory', () => ({
  useInventoryAvailability: () => {
    const filters = {
      environmentId: 'env-dev',
      organizationId: 'org-001',
      qualityStatus: 'available',
      ownerType: 'owned',
      siteCode: 'S1',
      skuCode: inventoryState.initialSkuCode,
      uomCode: 'pcs',
    }
    inventoryState.availabilityFilters = filters

    const quantity = ref(7)
    inventoryState.availabilityQuantity = quantity
    return {
      availability: computed(() => ({
        onHandQuantity: 10,
        availableQuantity: quantity.value,
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
      availabilityPending: (inventoryState.availabilityPending = ref(false)),
      filters,
      refreshAvailability: vi.fn(),
    }
  },
  useInventoryExpiryAlerts: () => ({
    expiryAlerts: computed(() => [
      {
        skuCode: 'SKU-001',
        uomCode: 'pcs',
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
    restartCountTask: inventoryState.restartCountTask,
    restartCountTaskError: ref(undefined),
    restartCountTaskPending: ref(false),
    cancelCountTask: inventoryState.cancelCountTask,
    cancelCountTaskError: ref(undefined),
    cancelCountTaskPending: ref(false),
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

// 寄售货主取自业务伙伴目录：同一伙伴可兼任客户与供应商，按角色分两组。
vi.mock('@/composables/erp/useErpPickerCatalog', async () => {
  const { computed, ref } = await import('vue')
  return {
    useErpPartnerCatalog: () => ({
      customerOptions: computed(() => [{ value: 'C-EAST', label: '华东客户' }]),
      supplierOptions: computed(() => [{ value: 'V-STEEL', label: '宝钢供应' }]),
      partnersPending: ref(false),
    }),
  }
})

vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifyError: inventoryState.notifyError,
  notifyOperationFailure: inventoryState.notifyOperationFailure,
  notifySuccess: inventoryState.notifySuccess,
}))

// 目录与默认值来自主数据 facade，这里给确定的目录，页面测试只关心「选择器有得选、单位自动带出」。
vi.mock('@/composables/inventory/useInventoryScope', async () => {
  const { computed, ref, watch } = await import('vue')
  const baseUomBySku: Record<string, string> = { 'SKU-001': 'pcs', 'RM-BAR-45-01': 'kg' }
  const catalog = {
    siteOptions: computed(() => [{ value: 'S1', label: '上海工厂' }]),
    sitesPending: ref(false),
    skuOptions: computed(() => [
      { value: 'SKU-001', label: '前减振器总成', hint: 'pcs' },
      { value: 'RM-BAR-45-01', label: '45号钢棒料', hint: 'kg' },
    ]),
    skusPending: ref(false),
    resolveUomCode: (skuCode: string) => baseUomBySku[skuCode.trim()] ?? '',
  }
  return {
    FALLBACK_INVENTORY_SITE_CODE: 'SITE-001',
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
          filters.uomCode = trimmed ? (baseUomBySku[trimmed] ?? '') : ''
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
vi.mock('@/composables/inventory/useInventorySiteStock', async () => {
  const { computed, ref } = await import('vue')
  const rows = ref([
    {
      skuCode: 'SKU-001',
      skuName: '前减振器总成',
      uomCode: 'pcs',
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
      uomCode: 'pcs',
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
  // 三态入参一并暴露：页面把「读失败」「还没查」「真 0 条」交给组件区分之后，
  // 断言必须能分别看到三者，否则测试会退回「全都长成空态」的旧契约。
  NvDataTable: {
    props: [
      'rows',
      'columns',
      'rowKey',
      'pagination',
      'emptyMessage',
      'error',
      'errorMessage',
      'awaitingScope',
      'awaitingScopeMessage',
      'manual',
      'page',
      'pageSize',
      'totalItems',
    ],
    emits: ['update:page', 'update:pageSize', 'retry'],
    // 页脚按钮**放在表格外面**：行数断言按 `tr` / `[data-cell]` 数，多塞一行会把它们打歪。
    template: `<div><table data-ui-table :data-pagination="String(pagination)" :data-empty-message="emptyMessage"
      :data-has-error="String(Boolean(error))" :data-error-message="errorMessage"
      :data-awaiting-scope="String(Boolean(awaitingScope))" :data-awaiting-scope-message="awaitingScopeMessage"
      :data-manual="String(Boolean(manual))" :data-total-items="totalItems"><tbody><tr v-for="(row, i) in rows" :key="i">
      <td v-for="column in columns" :key="column.key" :data-cell="column.key">
        <slot :name="'cell-' + column.key" :row="row">{{ column.accessor ? column.accessor(row) : row[column.key] }}</slot>
      </td>
    </tr></tbody></table>
    <div data-table-pager>
      <button data-next-page @click="$emit('update:page', page + 1)">下一页</button>
      <button data-page-size @click="$emit('update:pageSize', 100); $emit('update:page', 1)">100/页</button>
    </div></div>`,
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
  // NvDialog stub renders slot content unconditionally so dialog forms are testable.
  NvDialog: { props: ['open'], template: '<div><slot /></div>' },
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
  // 实体选择弹窗内部自带 NvDialog；这里的 NvDialog 桩会截断它的上下文，所以整件替换成 select。
  NvEntityPicker: {
    props: ['modelValue', 'options', 'title', 'placeholder', 'loading'],
    emits: ['update:modelValue'],
    template:
      '<select data-entity-picker v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
  // 物料字段是目录选择器（取数、就地新增由 DirectoryPicker 自己的用例覆盖），同样换成 select。
  DirectoryPicker: {
    props: ['modelValue', 'formSiteCode'],
    emits: ['update:modelValue'],
    template:
      '<select data-directory-picker v-bind="$attrs" :data-form-site="formSiteCode" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"></select>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: true,
  NvSpinner: true,
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

async function openCreateCountTask(wrapper: ReturnType<typeof mountInventoryPage>) {
  const button = wrapper.findAll('button').find((b) => b.text().trim() === '创建盘点任务')
  await button!.trigger('click')
}

/** 一行「待实盘」的服务端盘点任务（确认差异动作只在 open 态可用）。 */
function openCountTaskRow(countTaskId: string) {
  return {
    countTaskId,
    countTaskCode: 'CNT-2026-0001',
    skuCode: 'SKU-001',
    uomCode: 'pcs',
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

/** 一行卡死在「需复盘」的服务端盘点任务：重盘是它唯一的出口。 */
function recountRequiredCountTaskRow(countTaskId: string) {
  return { ...openCountTaskRow(countTaskId), status: 'recount-required' }
}

const resetFixture = () => {
  routeState.query = {}
  routerState.push.mockReset()
  inventoryState.confirmAdjustment.mockReset()
  inventoryState.createCountTask.mockReset()
  inventoryState.restartCountTask.mockReset()
  inventoryState.cancelCountTask.mockReset()
  inventoryState.postMovement.mockReset()
  inventoryState.notifyError.mockReset()
  inventoryState.notifySuccess.mockReset()
  inventoryState.availabilityRows = undefined
  inventoryState.initialSkuCode = 'SKU-001'
  inventoryState.countTaskRows = []
  inventoryState.countAdjustmentRows = []
  inventoryState.movementRows = []
  inventoryState.partnerResolved = true
  siteStockState.scanMore.mockReset()
  siteStockState.refresh.mockReset()
  if (siteStockState.hasMore) siteStockState.hasMore.value = true
  if (siteStockState.scanning) siteStockState.scanning.value = false
}

export {
  UUID_PATTERN,
  inventoryState,
  siteStockState,
  routeState,
  mountInventoryPage,
  resetFixture,
  openCreateCountTask,
  openCountTaskRow,
  recountRequiredCountTaskRow,
  uiStubs,
}
