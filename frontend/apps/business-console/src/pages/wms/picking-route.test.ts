import { mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PickingPage from './picking.vue'

const routeState = vi.hoisted(() => ({
  query: {} as Record<string, string>,
}))

const wmsState = vi.hoisted(() => ({
  filters: undefined as { keyword?: string, locationCode?: string, status?: string } | undefined,
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>' },
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

vi.mock('@/composables/useBusinessWms', () => ({
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
    template: '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  PageHeader: { props: ['title', 'count'], template: '<header><h1>{{ title }}</h1>{{ count }}<slot name="actions" /></header>' },
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

    expect(wmsState.filters).toEqual(expect.objectContaining({
      keyword: 'SKU-001',
      locationCode: 'A-01',
    }))
  })

  it('renders picking row inventory links without unsupported scan workflow links', () => {
    const wrapper = mount(PickingPage, { global: { stubs: uiStubs } })

    expect(wrapper.text()).toContain('库存上下文')
    expect(wrapper.text()).toContain('SKU-001')
    expect(wrapper.text()).toContain('A-01')
    expect(wrapper.text()).toContain('OB-001')
    expect(wrapper.text()).toContain('后端缺口')

    const links = wrapper.findAll('[data-router-link]').map((link) => link.attributes('data-to') ?? '')
    expect(links.some((to) => to.includes('/inventory/availability') && to.includes('SKU-001') && to.includes('A-01'))).toBe(true)
    expect(links.some((to) => to.includes('/inventory/lots') && to.includes('SKU-001') && to.includes('A-01'))).toBe(true)
    expect(links.some((to) => to.includes('/barcode/scans'))).toBe(false)
  })
})
