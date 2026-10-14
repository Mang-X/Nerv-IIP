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
              report: {
                reportNo: getReportNo(),
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
