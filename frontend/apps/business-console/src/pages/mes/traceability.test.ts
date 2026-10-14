import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, reactive, ref } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import TraceabilityPage from './traceability.vue'

const state = vi.hoisted(() => ({
  nodes: [
    { nodeId: 'WO-20261014-000001', nodeType: 'WorkOrder', displayName: '制管工单' },
    { nodeId: 'task-20', nodeType: 'OperationTask', displayName: 'WO-20261014-000001-OP-20' },
  ],
  edges: [] as Array<{ fromNodeId: string; toNodeId: string; relationType: string }>,
}))
vi.mock('@/composables/useBusinessMes', () => ({
  useMesTraceability: () => ({
    filters: reactive({
      organizationId: 'org-a',
      environmentId: 'production',
      mode: 'work-order',
      workOrderId: '',
      batchOrSerial: '',
      materialLotId: '',
    }),
    traceability: computed(() => ({ nodes: state.nodes, edges: state.edges })),
    traceabilityError: ref(undefined),
    traceabilityPending: ref(false),
    refreshTraceability: vi.fn(),
  }),
  useMesWorkOrders: () => ({
    filters: reactive({ keyword: '' }),
    workOrders: ref([]),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(0),
  }),
  useMesWorkOrderProducedLots: () => ({ producedLots: ref([]), producedLotsPending: ref(false) }),
}))
vi.mock('@/composables/mes/useMesReferenceLabels', () => ({
  useMesReferenceLabels: () => ({ statusLabel: (value: string) => value }),
}))

async function mountTrace(
  query = '?mode=batch&batchOrSerial=SER-001',
  permissions = ['business.mes.traceability.read', 'business.mes.work-orders.read'],
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/mes/traceability', component: TraceabilityPage },
      { path: '/mes/work-orders/:workOrderId', component: { template: '<div />' } },
      { path: '/barcode/scans', component: { template: '<div />' } },
      { path: '/mes/production-reports', component: { template: '<div />' } },
    ],
  })
  await router.push(`/mes/traceability${query}`)
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'reader',
      principalType: 'user',
      organizationId: 'org-a',
      environmentId: 'production',
      loginName: 'reader',
      permissionCodes: permissions,
    },
  })
  const options = {
    global: {
      plugins: [pinia, router],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        NvDataTable: {
          props: ['rows'],
          template:
            '<div><div v-for="row in rows" :key="row.nodeId"><slot name="cell-nodeId" :row="row">{{ row.nodeId }}</slot><slot name="cell-reportDetail" :row="row" /></div></div>',
        },
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvToolbar: { template: '<div><slot name="filters" /></div>' },
        NvEntityPicker: {
          props: ['modelValue'],
          emits: ['update:modelValue'],
          template:
            '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
        },
        NvInput: {
          props: ['modelValue'],
          emits: ['update:modelValue'],
          template:
            '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
        },
        NvSelect: {
          props: ['modelValue'],
          emits: ['update:modelValue'],
          template:
            '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="work-order">工单</option><option value="batch">批次</option><option value="material-lot">物料批</option></select>',
        },
      },
    },
  }
  return { wrapper: mount(TraceabilityPage, options), router, auth, options }
}

function nodeLink(wrapper: Awaited<ReturnType<typeof mountTrace>>['wrapper'], text: string) {
  return wrapper.findAll('a').find((link) => link.text() === text)!
}

describe('追溯工单与工序接力（#4411 fixture 页面行为）', () => {
  beforeEach(() => {
    state.edges = [
      { fromNodeId: 'WO-20261014-000001', toNodeId: 'task-20', relationType: 'has-operation' },
    ]
  })

  it('点击工单使用 nodeId 而不是显示名称，携带当前组织环境', async () => {
    const { wrapper, router } = await mountTrace()
    await nodeLink(wrapper, 'WO-20261014-000001').trigger('click')
    await flushPromises()
    expect((router.currentRoute.value.params as Record<string, string>).workOrderId).toBe(
      'WO-20261014-000001',
    )
    expect(router.currentRoute.value.query).toMatchObject({
      traceOrganizationId: 'org-a',
      traceEnvironmentId: 'production',
    })
    wrapper.unmount()
  })

  it.each(['has-operation', 'belongs-to-work-order'])(
    '点击工序按真实 %s 关系进入所属工单，不用输入框里的另一工单',
    async (relationType) => {
      state.edges = [
        relationType === 'has-operation'
          ? { fromNodeId: 'WO-20261014-000001', toNodeId: 'task-20', relationType }
          : { fromNodeId: 'task-20', toNodeId: 'WO-20261014-000001', relationType },
      ]
      const { wrapper, router } = await mountTrace('?workOrderId=WO-OTHER')
      await nodeLink(wrapper, 'task-20').trigger('click')
      await flushPromises()
      expect((router.currentRoute.value.params as Record<string, string>).workOrderId).toBe(
        'WO-20261014-000001',
      )
      expect(router.currentRoute.value.query.operationTaskId).toBe('task-20')
      wrapper.unmount()
    },
  )

  it.each(['batch', 'material-lot', 'work-order'])(
    '返回保存实际编辑后的 %s 查询与其它筛选，组织环境不变',
    async (mode) => {
      const { wrapper, router, auth, options } = await mountTrace(
        '?mode=batch&batchOrSerial=SER-OLD&filter=retained',
      )
      await wrapper.get('select').setValue(mode)
      await wrapper.get('input[aria-label="工单号"]').setValue('WO-QUERY')
      await wrapper.get('input[aria-label="批次或物料批"]').setValue('LOT-CURRENT')
      await nodeLink(wrapper, 'task-20').trigger('click')
      await flushPromises()
      const returnPath = router.currentRoute.value.query.traceReturn as string
      expect(returnPath).toContain('/mes/traceability?')
      wrapper.unmount()
      await router.push(returnPath)
      const restored = mount(TraceabilityPage, options)
      expect(router.currentRoute.value.query).toMatchObject({
        mode,
        workOrderId: 'WO-QUERY',
        batchOrSerial: 'LOT-CURRENT',
        materialLotId: 'LOT-CURRENT',
        filter: 'retained',
      })
      expect((restored.get('select').element as HTMLSelectElement).value).toBe(mode)
      expect((restored.get('input[aria-label="工单号"]').element as HTMLInputElement).value).toBe(
        'WO-QUERY',
      )
      expect(
        (restored.get('input[aria-label="批次或物料批"]').element as HTMLInputElement).value,
      ).toBe('LOT-CURRENT')
      expect(auth.principal).toMatchObject({ organizationId: 'org-a', environmentId: 'production' })
      restored.unmount()
    },
  )

  it('只有追溯权限仍显示图，工单和工序明确说明缺目标读取权限', async () => {
    const { wrapper } = await mountTrace('', ['business.mes.traceability.read'])
    expect(wrapper.text()).toContain('WO-20261014-000001')
    expect(wrapper.text()).toContain('task-20')
    expect(wrapper.text()).toContain('缺少工单读取权限')
    expect(nodeLink(wrapper, 'WO-20261014-000001')).toBeUndefined()
    expect(nodeLink(wrapper, 'task-20')).toBeUndefined()
    wrapper.unmount()
  })

  it('没有所属关系时说明原因，不猜查询工单、不把其它类型节点当工单', async () => {
    state.edges = [
      { fromNodeId: 'WO-20261014-000001', toNodeId: 'task-20', relationType: 'reported-by' },
    ]
    const { wrapper } = await mountTrace('?workOrderId=WO-20261014-000001')
    expect(wrapper.text()).toContain('未找到工序所属工单关系')
    expect(nodeLink(wrapper, 'task-20')).toBeUndefined()
    wrapper.unmount()
  })

  it('所属工单关系冲突时说明原因，不任取一条', async () => {
    state.nodes.push({ nodeId: 'WO-SECOND', nodeType: 'WorkOrder', displayName: '返工工单' })
    state.edges.push({
      fromNodeId: 'task-20',
      toNodeId: 'WO-SECOND',
      relationType: 'belongs-to-work-order',
    })
    const { wrapper } = await mountTrace()
    expect(wrapper.text()).toContain('工序关联多个工单，无法确定所属工单')
    expect(nodeLink(wrapper, 'task-20')).toBeUndefined()
    wrapper.unmount()
    state.nodes.pop()
  })
})

describe('追溯页面报工入口', () => {
  it('点击序列来源使用真实 ReportNo 并保存地址栏之外的实际查询筛选', async () => {
    const savedNodes = state.nodes
    const savedEdges = state.edges
    state.nodes = [
      { nodeId: 'SN-1002', nodeType: 'Serial', displayName: 'SN-1002' },
      { nodeId: 'PRPT-900', nodeType: 'ProductionReport', displayName: 'PRPT-900' },
    ]
    state.edges = [{ fromNodeId: 'PRPT-900', toNodeId: 'SN-1002', relationType: 'produced-serial' }]
    const { wrapper, router } = await mountTrace('?mode=batch&filter=retained', [
      'business.mes.traceability.read',
      'business.mes.reporting.read',
    ])
    try {
      await wrapper.get('input[aria-label="批次或物料批"]').setValue('SN-1002')
      const source = wrapper.findAll('a').find((link) => link.text() === '序列 · PRPT-900')!
      await source.trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.query.reportNo).toBe('PRPT-900')
      expect(router.currentRoute.value.query.sourceNodeType).toBe('Serial')
      expect(router.currentRoute.value.query.sourceNodeId).toBe('SN-1002')
      const returnTo = new URL(
        String(router.currentRoute.value.query.traceabilityReturn),
        'https://console.example',
      )
      expect(returnTo.pathname).toBe('/mes/traceability')
      expect(Object.fromEntries(returnTo.searchParams)).toEqual({
        mode: 'batch',
        filter: 'retained',
        workOrderId: '',
        batchOrSerial: 'SN-1002',
        materialLotId: 'SN-1002',
      })
    } finally {
      wrapper.unmount()
      state.nodes = savedNodes
      state.edges = savedEdges
    }
  })
})
