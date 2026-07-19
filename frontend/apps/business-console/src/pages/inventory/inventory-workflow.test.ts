import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, ref } from 'vue'
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
      skuCode: 'SKU-001',
      uomCode: 'EA',
    }
    inventoryState.availabilityFilters = filters

    return {
      availability: computed(() => ({
        onHandQuantity: 10,
        availableQuantity: 7,
        reservedQuantity: 2,
      })),
      availabilityError: ref(undefined),
      availabilityLines: computed(() => [
        {
          locationCode: 'A-01',
          lotNo: 'LOT-001',
          serialNo: 'SN-001',
          qualityStatus: 'available',
          ownerType: 'owned',
          reservedQuantity: 2,
          onHandQuantity: 10,
          availableQuantity: 7,
        },
      ]),
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
        reservedQuantity: 2,
        onHandQuantity: 10,
        availableQuantity: 7,
      },
    ]),
    expiryAlertsError: ref(undefined),
    expiryAlertsPending: ref(false),
    expiryAlertsSuccessful: ref(true),
    filters: {
      environmentId: 'env-dev',
      organizationId: 'org-001',
      siteCode: 'S1',
    },
    refreshExpiryAlerts: vi.fn(),
  }),
  useInventoryCounts: () => ({
    confirmAdjustment: inventoryState.confirmAdjustment,
    confirmAdjustmentError: ref(undefined),
    confirmAdjustmentPending: ref(false),
    createCountTask: inventoryState.createCountTask,
    createCountTaskError: ref(undefined),
    createCountTaskPending: ref(false),
    filters: {
      environmentId: 'env-dev',
      organizationId: 'org-001',
    },
  }),
  useInventoryMovement: () => ({
    postMovement: inventoryState.postMovement,
    postMovementError: ref(undefined),
    postMovementPending: ref(false),
  }),
}))

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
  RowActions: { props: ['label'], template: '<div><slot /></div>' },
  DropdownMenuItem: { template: '<button v-bind="$attrs"><slot /></button>' },
  DropdownMenuSeparator: true,
  // NvDialog (reka DialogRoot) stubs render slot content unconditionally so dialog forms are testable.
  DialogRoot: { props: ['open'], template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  Field: { template: '<div><slot /></div>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
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

describe('inventory workflow pages', () => {
  beforeEach(() => {
    routeState.query = {}
    routerState.push.mockReset()
    inventoryState.confirmAdjustment.mockReset()
    inventoryState.createCountTask.mockReset()
    inventoryState.postMovement.mockReset()
  })

  it('uses design-system table components for local stock count queue', () => {
    const wrapper = mountInventoryPage(CountsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('uses design-system table components for local movement queue', () => {
    const wrapper = mountInventoryPage(MovementsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('links inventory lot context to barcode scan records', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)

    const link = wrapper
      .findAll('[data-router-link]')
      .find((item) => item.text().includes('扫码记录'))
    expect(link?.attributes('data-to')).toContain('/barcode/scans')
    expect(link?.attributes('data-to')).toContain('inventory.count')
    expect(link?.attributes('data-to')).toContain('LOT-001')
    expect(wrapper.get('[data-cell="productionDate"]').text()).toBe('—')
    expect(wrapper.get('[data-cell="expiryDate"]').text()).toBe('—')
    expect(wrapper.text()).not.toContain('facade 未提供 total')
    expect(wrapper.get('[data-ui-table]').attributes('data-pagination')).toBe('false')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    expect(wrapper.get('[data-cell="productionDate"]').text()).toBe('2026-06-15')
    expect(wrapper.get('[data-cell="expiryDate"]').text()).toBe('2026-07-25')
    expect(wrapper.text()).toContain('返回库存明细后操作')
  })

  it('renders a facade-backed lot and reservation page with traceability links', async () => {
    const wrapper = mountInventoryPage(LotsPage)

    expect(wrapper.text()).toContain('批次与预留')
    expect(wrapper.text()).toContain('LOT-001')
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.get('[data-cell="reservedQuantity"]').text()).toBe('2')
    expect(wrapper.get('[data-cell="productionDate"]').text()).toBe('—')
    expect(wrapper.get('[data-cell="expiryDate"]').text()).toBe('—')
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

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    expect(wrapper.get('[data-cell="productionDate"]').text()).toBe('2026-06-15')
    expect(wrapper.get('[data-cell="expiryDate"]').text()).toBe('2026-07-25')
    expect(wrapper.text()).toContain('返回批次明细后操作')
    expect(links.some((to) => to.includes('/barcode/scans') && to.includes('SN-001'))).toBe(true)
    expect(
      links.some(
        (to) => to.includes('/wms/picking') && to.includes('locationCode') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(
      links.some(
        (to) =>
          to.includes('/quality/inspections') &&
          to.includes('batchNo') &&
          to.includes('materialLotId') &&
          to.includes('LOT-001'),
      ),
    ).toBe(true)
  })

  it('generates a fresh idempotency key each time the same count task is adjusted', async () => {
    inventoryState.createCountTask.mockResolvedValue({ data: { countTaskId: 'COUNT-TASK-1' } })
    inventoryState.confirmAdjustment.mockResolvedValue({ data: { movementId: 'MOVE-1' } })

    const wrapper = mountInventoryPage(CountsPage)

    await wrapper.find('#count-task-sku').setValue('SKU-001')
    await wrapper.find('#count-task-site').setValue('S1')
    await wrapper.find('#count-task-location').setValue('A-01')
    await wrapper.findAll('form')[0]!.trigger('submit')
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

    await wrapper.find('#count-adjust-task-id').setValue('COUNT-TASK-ORPHAN')
    await wrapper.find('#count-adjust-quantity').setValue('7')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).not.toHaveBeenCalled()
  })
})
