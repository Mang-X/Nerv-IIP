import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import TraceabilityPage from './traceability.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: ['business.mes.reporting.read'] } }),
}))
vi.mock('@/composables/mes/useMesReferenceLabels', () => ({
  useMesReferenceLabels: () => ({ statusLabel: (value: string) => value }),
}))
vi.mock('@/composables/mes/useMesKeywordFilter', () => ({
  useMesKeywordFilter: () => ({ keyword: ref('') }),
}))
vi.mock('@/composables/useBusinessMes', () => ({
  useMesTraceability: () => ({
    filters: reactive({
      mode: 'batch',
      workOrderId: '',
      batchOrSerial: 'SN-1002',
      materialLotId: 'MLOT-1',
    }),
    refreshTraceability: vi.fn(),
    traceabilityError: shallowRef(),
    traceabilityPending: shallowRef(false),
    traceability: shallowRef({
      nodes: [
        { nodeId: 'SN-1002', nodeType: 'Serial' },
        { nodeId: 'PRPT-900', nodeType: 'ProductionReport' },
      ],
      edges: [{ fromNodeId: 'PRPT-900', toNodeId: 'SN-1002', relationType: 'produced-serial' }],
    }),
  }),
  useMesWorkOrders: () => ({
    filters: reactive({ keyword: '' }),
    workOrders: shallowRef([]),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: shallowRef(0),
  }),
  useMesWorkOrderProducedLots: () => ({
    producedLots: shallowRef([]),
    producedLotsPending: shallowRef(false),
  }),
}))
describe('追溯页面报工入口', () => {
  it('点击序列来源使用真实 ReportNo 并保存地址栏之外的实际查询筛选', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/mes/traceability', component: { template: '<div />' } },
        { path: '/mes/production-reports', component: { template: '<div />' } },
      ],
    })
    await router.push('/mes/traceability?mode=batch')
    const wrapper = mount(TraceabilityPage, {
      global: {
        plugins: [router],
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          NvPageHeader: { template: '<header><slot name="actions" /></header>' },
          NvToolbar: { template: '<div><slot name="filters" /></div>' },
          NvDataTable: {
            props: ['rows'],
            template:
              '<div><div v-for="row in rows" :key="row.nodeId"><slot name="cell-reportDetail" :row="row" /></div></div>',
          },
          NvMetricStrip: true,
          NvEntityPicker: true,
          NvSelect: true,
          NvInput: true,
        },
      },
    })
    const source = wrapper.findAll('a').find((link) => link.text() === '序列 · PRPT-900')!
    await source.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.reportNo).toBe('PRPT-900')
    expect(router.currentRoute.value.query.sourceNodeType).toBe('Serial')
    const returnTo = new URL(
      String(router.currentRoute.value.query.traceabilityReturn),
      'https://console.example',
    )
    expect(returnTo.pathname).toBe('/mes/traceability')
    expect(Object.fromEntries(returnTo.searchParams)).toEqual({
      mode: 'batch',
      workOrderId: '',
      batchOrSerial: 'SN-1002',
      materialLotId: 'MLOT-1',
    })
    wrapper.unmount()
  })
})
