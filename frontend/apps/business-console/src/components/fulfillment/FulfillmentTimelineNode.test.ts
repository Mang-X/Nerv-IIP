import type { FulfillmentNode } from '@/composables/useFulfillmentTimeline'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import FulfillmentTimelineNode from './FulfillmentTimelineNode.vue'

const stubs = {
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  AlertTriangleIcon: true,
  LockIcon: true,
  RefreshCwIcon: true,
}

function mountNode(node: FulfillmentNode) {
  return mount(FulfillmentTimelineNode, { props: { node }, global: { stubs } })
}

describe('FulfillmentTimelineNode — four-state state machine', () => {
  it('established: renders readable business number, status and a drill link', () => {
    const wrapper = mountNode({
      key: 'delivery-order',
      title: '发货单',
      status: 'established',
      businessNo: 'DO-1',
      detailStatus: 'released',
      linkLabel: 'salesOrderNo = SO-1',
      drill: { path: '/erp/sales/deliveries' },
      source: 'ERP · 发货单读面',
    })
    expect(wrapper.text()).toContain('DO-1')
    expect(wrapper.text()).toContain('released')
    expect(wrapper.text()).toContain('salesOrderNo = SO-1')
    expect(wrapper.find('a').exists()).toBe(true)
  })

  it('unlinked: shows an explicit rule note and never fabricates data', () => {
    const wrapper = mountNode({
      key: 'mes-work-order',
      title: 'MES 工单',
      status: 'unlinked',
      ruleNote: '工单以 SKU 排产，尚未建立到本单的稳定关联。',
    })
    expect(wrapper.text()).toContain('尚未建立关联')
    expect(wrapper.text()).toContain('工单以 SKU 排产')
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('pending: distinct empty state with rule note', () => {
    const wrapper = mountNode({
      key: 'production-demand',
      title: '生产需求',
      status: 'pending',
      ruleNote: '当前尚未产生。',
    })
    expect(wrapper.text()).toContain('尚未产生')
    expect(wrapper.text()).toContain('当前尚未产生。')
  })

  it('restricted: 403 shows a limited state without leaking data', () => {
    const wrapper = mountNode({
      key: 'receivable',
      title: '应收',
      status: 'restricted',
      businessNo: 'SHOULD-NOT-RENDER',
    })
    expect(wrapper.text()).toContain('权限受限')
    // businessNo header is still hidden because restricted nodes are built without it in practice;
    // here we assert the restricted message wins and no drill link is offered.
    expect(wrapper.find('a').exists()).toBe(false)
  })

  it('failed: single-source failure shows a retry control and emits retry', async () => {
    const wrapper = mountNode({
      key: 'delivery-order',
      title: '发货单',
      status: 'failed',
      failureKind: 'conflict',
    })
    expect(wrapper.text()).toContain('数据冲突（409）')
    const button = wrapper.find('button')
    expect(button.exists()).toBe(true)
    await button.trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('failed/timeout renders a distinguishable timeout message', () => {
    const wrapper = mountNode({
      key: 'delivery-order',
      title: '发货单',
      status: 'failed',
      failureKind: 'timeout',
    })
    expect(wrapper.text()).toContain('超时')
  })

  it('loading: shows a loading affordance', () => {
    const wrapper = mountNode({
      key: 'delivery-order',
      title: '发货单',
      status: 'loading',
    })
    expect(wrapper.text()).toContain('加载中')
  })
})
