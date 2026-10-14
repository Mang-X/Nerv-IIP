import { flushPromises, mount } from '@vue/test-utils'
import { computed, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TraceReportLinks from './TraceReportLinks.vue'
import ProductionReportDetailDialog from './ProductionReportDetailDialog.vue'

const state = vi.hoisted(() => ({
  canRead: true,
  error: undefined as Error | undefined,
  requested: vi.fn(),
  returnedReportNo: undefined as string | undefined,
  consumedMaterialLots: [] as Array<{
    materialId: string
    materialLotId: string
    consumedQuantity: number
    uomCode: string
    materialIssueRequestNo: string
  }>,
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return { permissionCodes: state.canRead ? ['business.mes.reporting.read'] : [] }
    },
  }),
}))
vi.mock('@/composables/useBusinessMes', () => ({
  useMesProductionReportDetail: (getReportNo: () => string | null) => {
    state.requested(getReportNo())
    return {
      detail: computed(() => {
        state.requested(getReportNo())
        return getReportNo()
          ? {
              consumedMaterialLots: state.consumedMaterialLots,
              report: {
                reportNo: state.returnedReportNo ?? getReportNo(),
                workOrderNo: 'WO-20261014-000001',
                producedLotNo: 'LOT-20261014-000001',
                serialNumbers: ['SN-1001', 'SN-1002'],
                goodQuantity: 2,
              },
            }
          : undefined
      }),
      detailError: shallowRef(state.error),
      detailPending: shallowRef(false),
      refresh: vi.fn(),
    }
  },
}))
const returnTo =
  '/mes/traceability?mode=batch&workOrderId=WO-20261014-000001&batchOrSerial=SN-1002&materialLotId=MLOT-1'
const router = () =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/mes/production-reports', component: { template: '<div />' } },
      { path: '/mes/traceability', component: { template: '<div />' } },
    ],
  })
const stubs = {
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<section role="dialog"><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
}
beforeEach(() => {
  state.canRead = true
  state.error = undefined
  state.requested.mockClear()
  state.returnedReportNo = undefined
  state.consumedMaterialLots = [
    {
      materialId: 'MAT-1',
      materialLotId: 'MLOT-1',
      consumedQuantity: 7.5,
      uomCode: 'kg',
      materialIssueRequestNo: 'MIR-1',
    },
    {
      materialId: 'MAT-1',
      materialLotId: 'MLOT-2',
      consumedQuantity: 3,
      uomCode: 'kg',
      materialIssueRequestNo: 'MIR-2',
    },
    {
      materialId: 'MAT-2',
      materialLotId: 'MLOT-1',
      consumedQuantity: 2,
      uomCode: 'm',
      materialIssueRequestNo: 'MIR-3',
    },
    {
      materialId: 'MAT-3',
      materialLotId: 'MLOT-3',
      consumedQuantity: 9,
      uomCode: 'ea',
      materialIssueRequestNo: 'MIR-1',
    },
  ]
})

describe('追溯来源报工行为', () => {
  it('多来源逐项选择并把真实序列身份和原查询带到报工页', async () => {
    const r = router()
    await r.push('/mes/traceability')
    const wrapper = mount(TraceReportLinks, {
      props: {
        node: { nodeId: 'SN-1002', nodeType: 'Serial' },
        returnTo,
        graph: {
          nodes: [
            { nodeId: 'PRPT-1', nodeType: 'ProductionReport' },
            { nodeId: 'PRPT-2', nodeType: 'ProductionReport' },
          ],
          edges: [
            { fromNodeId: 'PRPT-1', toNodeId: 'SN-1002', relationType: 'produced-serial' },
            { fromNodeId: 'PRPT-2', toNodeId: 'SN-1002', relationType: 'produced-serial' },
          ],
        },
      },
      global: { plugins: [r] },
    })
    expect(wrapper.text()).toContain('选择来源报工')
    expect(wrapper.findAll('a')).toHaveLength(2)
    await wrapper.findAll('a')[1]!.trigger('click')
    await flushPromises()
    expect(r.currentRoute.value.query).toEqual({
      reportNo: 'PRPT-2',
      sourceNodeType: 'Serial',
      sourceNodeId: 'SN-1002',
      traceabilityReturn: returnTo,
    })
    wrapper.unmount()
  })
  it('缺报工读取权限时解释且不提供未授权链接', () => {
    state.canRead = false
    const wrapper = mount(TraceReportLinks, {
      props: { node: { nodeId: 'PRPT-1', nodeType: 'ProductionReport' }, returnTo },
    })
    expect(wrapper.text()).toContain('无报工读取权限')
    expect(wrapper.find('a').exists()).toBe(false)
    wrapper.unmount()
  })
  it('缺来源关系时解释无匹配，不猜测报工', () => {
    const wrapper = mount(TraceReportLinks, {
      props: { node: { nodeId: 'LOT-20261014-000001', nodeType: 'ProducedLot' }, returnTo },
    })
    expect(wrapper.text()).toContain('无匹配的来源报工')
    expect(wrapper.find('a').exists()).toBe(false)
    wrapper.unmount()
  })
  it('已全部入库批次直接呈现报工历史产出，返回恢复原查询', async () => {
    const r = router()
    await r.push('/mes/production-reports')
    const wrapper = mount(ProductionReportDetailDialog, {
      props: {
        reportNo: 'PRPT-1',
        sourceNodeType: 'ProducedLot',
        sourceNodeId: 'LOT-20261014-000001',
        returnTo,
      },
      global: { stubs, plugins: [r] },
    })
    expect(wrapper.get('[aria-label="产出事实"]').text()).toContain('LOT-20261014-000001')
    expect(wrapper.text()).toContain('良品2')
    await wrapper.get('a').trigger('click')
    await flushPromises()
    expect(r.currentRoute.value.fullPath).toBe(returnTo)
    wrapper.unmount()
  })
  it('序列定位只显示所选序列且不能用批号冒充序列', () => {
    const r = router()
    const wrapper = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1', sourceNodeType: 'Serial', sourceNodeId: 'SN-1002' },
      global: { stubs, plugins: [r] },
    })
    expect(wrapper.get('[aria-label="产出事实"]').text()).toContain('SN-1002')
    expect(wrapper.get('[aria-label="产出事实"]').text()).not.toContain('SN-1001')
    wrapper.unmount()
    const mismatch = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1', sourceNodeType: 'Serial', sourceNodeId: 'LOT-20261014-000001' },
      global: { stubs, plugins: [r] },
    })
    expect(mismatch.text()).toContain('来源节点与报工产出事实不匹配')
    expect(mismatch.find('[aria-label="产出事实"]').exists()).toBe(false)
    mismatch.unmount()
  })
  it('详情缺读取权限时不启用报工 getter', () => {
    state.canRead = false
    const wrapper = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1' },
      global: { stubs },
    })
    expect(wrapper.text()).toContain('无报工读取权限')
    expect(state.requested).toHaveBeenCalledWith(null)
    wrapper.unmount()
  })
  it('报工不存在或公开 getter 失败时保留可解释错误且不呈现产出', () => {
    state.error = new Error('报工不存在')
    const wrapper = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-404' },
      global: { stubs },
    })
    expect(wrapper.text()).toContain('报工不存在')
    expect(wrapper.find('[aria-label="产出事实"]').exists()).toBe(false)
    wrapper.unmount()
  })
})

describe('追溯耗料来源行为', () => {
  it.each([
    ['Material', 'MAT-1', 'has-lot'],
    ['MaterialLot', 'MLOT-1', ''],
    ['MaterialIssueRequest', 'MIR-1', 'received-lot'],
  ])('%s 多来源可选择并带原身份与完整查询进入报工', async (nodeType, nodeId, relationType) => {
    const r = router()
    await r.push('/mes/traceability')
    const wrapper = mount(TraceReportLinks, {
      props: {
        node: { nodeType, nodeId },
        returnTo,
        graph: {
          nodes: [
            { nodeType: 'MaterialLot', nodeId: 'MLOT-1' },
            { nodeType: 'ProductionReport', nodeId: 'PRPT-1' },
            { nodeType: 'ProductionReport', nodeId: 'PRPT-2' },
          ],
          edges: [
            ...(relationType ? [{ fromNodeId: nodeId, toNodeId: 'MLOT-1', relationType }] : []),
            { fromNodeId: 'MLOT-1', toNodeId: 'PRPT-1', relationType: 'consumed-by-report' },
            { fromNodeId: 'MLOT-1', toNodeId: 'PRPT-2', relationType: 'consumed-by-report' },
          ],
        },
      },
      global: { plugins: [r] },
    })
    expect(wrapper.text()).toContain('选择来源报工')
    expect(wrapper.findAll('a')).toHaveLength(2)
    await wrapper.findAll('a')[1]!.trigger('click')
    await flushPromises()
    expect(r.currentRoute.value.query).toEqual({
      reportNo: 'PRPT-2',
      sourceNodeType: nodeType,
      sourceNodeId: nodeId,
      traceabilityReturn: returnTo,
    })
    wrapper.unmount()
  })
  it.each([
    ['Material', 'MAT-1', [0, 1]],
    ['MaterialLot', 'MLOT-1', [0, 2]],
    ['MaterialIssueRequest', 'MIR-1', [0, 3]],
  ])(
    '%s 只显示同身份耗料明细及原单位数量并返回原查询',
    async (sourceNodeType, sourceNodeId, indices) => {
      const r = router()
      await r.push('/mes/production-reports')
      const wrapper = mount(ProductionReportDetailDialog, {
        props: { reportNo: 'PRPT-2', sourceNodeType, sourceNodeId, returnTo },
        global: { stubs, plugins: [r] },
      })
      const section = wrapper.get('[aria-label="来源耗料事实"]')
      expect(section.text()).toContain('来源耗料事实')
      const facts = section.findAll('dl')
      expect(facts).toHaveLength(2)
      for (const [i, index] of indices.entries()) {
        const row = state.consumedMaterialLots[index!]!
        expect(facts[i]!.text()).toBe(
          `物料编号${row.materialId}物料批编号${row.materialLotId}耗用数量${row.consumedQuantity}单位${row.uomCode}领料申请号${row.materialIssueRequestNo}`,
        )
      }
      expect(section.text()).not.toContain('SKU')
      await wrapper.get('a').trigger('click')
      await flushPromises()
      expect(r.currentRoute.value.fullPath).toBe(returnTo)
      wrapper.unmount()
    },
  )
  it.each([
    ['Material', 'MLOT-1'],
    ['MaterialLot', 'MAT-1'],
    ['MaterialIssueRequest', 'MLOT-1'],
  ])('%s 不能通过其他类型同值编号命中详情', (sourceNodeType, sourceNodeId) => {
    const wrapper = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1', sourceNodeType, sourceNodeId },
      global: { stubs },
    })
    expect(wrapper.text()).toContain('来源节点与报工耗料事实不匹配')
    expect(wrapper.find('[aria-label="来源耗料事实"]').exists()).toBe(false)
    wrapper.unmount()
  })
  it('图候选报工存在但无耗料事实时解释，保留返回入口', () => {
    state.consumedMaterialLots = []
    const wrapper = mount(ProductionReportDetailDialog, {
      props: {
        reportNo: 'PRPT-1',
        sourceNodeType: 'MaterialLot',
        sourceNodeId: 'MLOT-1',
        returnTo,
      },
      global: { stubs, plugins: [router()] },
    })
    expect(wrapper.text()).toContain('来源节点与报工耗料事实不匹配')
    expect(wrapper.find('[aria-label="来源耗料事实"]').exists()).toBe(false)
    expect(wrapper.get('a').text()).toBe('返回追溯')
    wrapper.unmount()
  })
  it('公开读回是另一报工时不能呈现该报工耗料', () => {
    state.returnedReportNo = 'PRPT-OTHER'
    const wrapper = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1', sourceNodeType: 'MaterialLot', sourceNodeId: 'MLOT-1' },
      global: { stubs },
    })
    expect(wrapper.text()).toContain('来源节点与报工耗料事实不匹配')
    expect(wrapper.find('[aria-label="来源耗料事实"]').exists()).toBe(false)
    wrapper.unmount()
  })
  it('耗料来源缺报工读取权限时解释且不提供链接或读取详情', () => {
    state.canRead = false
    const link = mount(TraceReportLinks, {
      props: { node: { nodeType: 'MaterialLot', nodeId: 'MLOT-1' }, returnTo },
    })
    expect(link.text()).toContain('无报工读取权限')
    expect(link.find('a').exists()).toBe(false)
    link.unmount()
    const detail = mount(ProductionReportDetailDialog, {
      props: { reportNo: 'PRPT-1', sourceNodeType: 'MaterialLot', sourceNodeId: 'MLOT-1' },
      global: { stubs },
    })
    expect(detail.text()).toContain('无报工读取权限')
    expect(state.requested).toHaveBeenCalledWith(null)
    expect(detail.find('[aria-label="来源耗料事实"]').exists()).toBe(false)
    detail.unmount()
  })
  it('无真实耗料关系时解释且不猜来源报工', () => {
    const wrapper = mount(TraceReportLinks, {
      props: { node: { nodeType: 'MaterialLot', nodeId: 'MLOT-404' }, returnTo },
    })
    expect(wrapper.text()).toContain('无匹配的来源报工')
    expect(wrapper.find('a').exists()).toBe(false)
    wrapper.unmount()
  })
})
