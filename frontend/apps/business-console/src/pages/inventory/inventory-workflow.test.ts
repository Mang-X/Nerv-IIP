import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import CountsPage from './counts.vue'
import MovementsPage from './movements.vue'

const inventoryState = vi.hoisted(() => ({
  confirmAdjustment: vi.fn(),
  createCountTask: vi.fn(),
  postMovement: vi.fn(),
}))

const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))
const routerState = vi.hoisted(() => ({ push: vi.fn() }))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a data-router-link><slot /></a>' },
  useRoute: () => routeState,
  useRouter: () => routerState,
}))

vi.mock('@/composables/useBusinessInventory', () => ({
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
    template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
  },
  SectionCards: { template: '<div><slot /></div>' },
  SectionCard: { props: ['description', 'value', 'hint'], template: '<div>{{ description }} {{ value }}</div>' },
  Toolbar: { props: ['showSearch'], template: '<div><slot name="filters" /></div>' },
  // DataTablePro stub renders rows + the cell-actions slot, exposing a design-system table marker.
  DataTablePro: {
    props: ['rows', 'columns', 'rowKey'],
    template: `<table data-ui-table><tbody><tr v-for="(row, i) in rows" :key="i"><td><slot name="cell-actions" :row="row" /></td></tr></tbody></table>`,
  },
  DataTablePagination: true,
  RowActions: { props: ['label'], template: '<div><slot /></div>' },
  DropdownMenuItem: { template: '<button v-bind="$attrs"><slot /></button>' },
  DropdownMenuSeparator: true,
  // DialogPro (reka DialogRoot) stubs render slot content unconditionally so dialog forms are testable.
  DialogRoot: { props: ['open'], template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
  ButtonPro: { template: '<button v-bind="$attrs"><slot /></button>' },
  Field: { template: '<div><slot /></div>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  InputPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  SelectPro: { template: '<div><slot /></div>' },
  SelectProContent: { template: '<div><slot /></div>' },
  SelectProItem: { props: ['value'], template: '<div><slot /></div>' },
  SelectProTrigger: { template: '<button><slot /></button>' },
  SelectValue: true,
  Spinner: true,
}

function mountInventoryPage(component: unknown) {
  return mount(component, {
    global: {
      plugins: [createPinia()],
      stubs: {
        ...uiStubs,
        RouterLink: { props: ['to'], template: '<a data-router-link><slot /></a>' },
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

  it('generates a fresh idempotency key each time the same count task is adjusted', async () => {
    inventoryState.createCountTask.mockResolvedValue({ data: { countTaskId: 'COUNT-TASK-1' } })
    inventoryState.confirmAdjustment.mockResolvedValue({ data: { movementId: 'MOVE-1' } })

    const wrapper = mountInventoryPage(CountsPage)

    await wrapper.find('#count-task-sku').setValue('SKU-001')
    await wrapper.find('#count-task-site').setValue('S1')
    await wrapper.find('#count-task-location').setValue('A-01')
    await wrapper.findAll('form')[0]!.trigger('submit')
    await wrapper.findAll('button').find((button) => button.text().includes('确认差异'))!.trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('5')
    await wrapper.findAll('form')[1]!.trigger('submit')

    await wrapper.findAll('button').find((button) => button.text().includes('确认差异'))!.trigger('click')
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
