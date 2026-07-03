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
      pickingTasks: computed(() => []),
      pickingTasksError: shallowRef(undefined),
      pickingTasksPending: shallowRef(false),
      pickingTasksTotal: computed(() => 0),
      refreshPickingTasks: vi.fn(),
    }
  },
}))

const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  ButtonPro: { template: '<button v-bind="$attrs"><slot /></button>' },
  DataTablePro: { template: '<div data-ui-table />' },
  DialogPro: { template: '<div><slot /></div>' },
  DialogProClose: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProDescription: { template: '<p><slot /></p>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  FieldPro: { template: '<div><slot /></div>' },
  FieldProError: true,
  FieldProGroup: { template: '<div><slot /></div>' },
  FieldProLabel: { template: '<label><slot /></label>' },
  InputPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  PageHeader: { props: ['title', 'count'], template: '<header><h1>{{ title }}</h1>{{ count }}<slot name="actions" /></header>' },
  StatusBadgePro: { props: ['value'], template: '<span>{{ value }}</span>' },
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
      keyword: 'SKU-001 LOT-001 SN-001',
      locationCode: 'A-01',
    }))
  })
})
