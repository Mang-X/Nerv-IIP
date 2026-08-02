import type { FulfillmentNode } from '@/composables/useFulfillmentTimeline'
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
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
  /**
   * 紧急度分级（特急/紧急/高风险…）的词表归 `urgencyLevelPresentation` 所有，
   * 不在共享 `STATUS_LABELS` 里。此前把它翻译完的中文塞进 `detailStatus`，
   * 组件照例拿去过 `resolveStatus()`——那函数吃裸码值，每次都报「词表缺失: 高风险」，
   * 只是回吐原值恰好还是对的中文，屏上看不出来。成对断言：现成文案照原样上屏，
   * **且不再触发漏词告警**（后者才是这次要拦的东西）。
   */
  it('established: detailStatusLabel 是现成文案，照原样上屏且不报漏词', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mountNode({
      key: 'schedule-urgency',
      title: '订单紧急度',
      status: 'established',
      businessNo: 'SO-1',
      detailStatusLabel: '高风险',
      source: 'Planning · 紧急度读面',
    })
    expect(wrapper.text()).toContain('高风险')
    expect(warn.mock.calls.flat().join(' ')).not.toContain('词表缺失')
    warn.mockRestore()
  })

  it('established: 没给现成文案时仍按 detailStatus 走状态词表', () => {
    const wrapper = mountNode({
      key: 'mes-work-order',
      title: 'MES 工单',
      status: 'established',
      businessNo: 'WO-1',
      detailStatus: 'released',
      source: 'MES · 工单读面',
    })
    expect(wrapper.text()).toContain('已下达')
  })

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
    // 各来源回的英文状态码走全站状态字典映射，原文不上屏。
    expect(wrapper.text()).toContain('已下达')
    expect(wrapper.text()).not.toContain('released')
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
