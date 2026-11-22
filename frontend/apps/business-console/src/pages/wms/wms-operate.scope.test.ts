import { wms, candidateState, layoutStub, resetFixture } from './wms-operate.fixture'
import { useWmsInboundOrders, useWmsOutboundOrders, useWmsCountExecutions } from '@/composables/wms'
import { assertScopePageBehavior, scopePageStubs } from '@/test/wmsWorkScopePage'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import CountsPage from './counts.vue'
import InboundPage from './inbound.vue'
import OutboundPage from './outbound.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

  it.each([
    ['入库', InboundPage, 'receipts', useWmsInboundOrders],
    ['出库', OutboundPage, 'shipments', useWmsOutboundOrders],
    ['盘点', CountsPage, 'counts', useWmsCountExecutions],
  ] as const)(
    '%s 显示授权目录和真实未就绪原因，换范围时回到第一页',
    async (_, page, catalog, query) => {
      const wrapper = mount(page, { global: { stubs: scopePageStubs } })
      expect(query).toHaveBeenCalledWith({ workScopeRequired: true })
      await assertScopePageBehavior(wrapper, catalog)
    },
  )

  it.each([
    ['收货入库', InboundPage, wms.refreshInboundOrders],
    ['复核发货', OutboundPage, wms.refreshOutboundOrders],
    ['盘点执行', CountsPage, wms.refreshCountExecutions],
  ])('%s 刷新列表时同步刷新当前范围候选', async (_title, Page, refreshList) => {
    const wrapper = mount(Page, { global: { stubs: layoutStub } })
    const refreshButton = wrapper
      .findAll('button')
      .find((button) => button.text().trim() === '刷新')

    await refreshButton!.trigger('click')

    expect(refreshList).toHaveBeenCalledOnce()
    expect(candidateState.refresh).toHaveBeenCalledOnce()
    wrapper.unmount()
  })
})
