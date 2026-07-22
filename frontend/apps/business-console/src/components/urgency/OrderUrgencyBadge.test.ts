import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import OrderUrgencyBadge from './OrderUrgencyBadge.vue'

const routerLinkStub = {
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
}

describe('OrderUrgencyBadge', () => {
  it('shows the unified level and all three explainable contributions', async () => {
    const wrapper = mount(OrderUrgencyBadge, {
      props: {
        orderReference: 'SO-001',
        urgency: {
          orderId: 'WO-001',
          businessReference: 'SO-001',
          level: 'urgent',
          modelVersion: 'order-urgency-v1',
          calculatedAtUtc: '2026-07-22T08:00:00Z',
          inputFingerprint: 'fingerprint',
          businessPriority: {
            level: 'p1',
            source: 'manual',
            reason: '重点客户',
            revision: 2,
            setAtUtc: '2026-07-22T07:00:00Z',
            reasonCodes: ['business.priority.p1'],
          },
          timeCriticality: {
            level: 'urgent',
            criticalRatio: 0.8,
            slackHours: -2,
            expectedDelayHours: 2,
            estimatedCompletionUtc: '2026-07-23T08:00:00Z',
            remainingCycleHours: 24,
            reasonCodes: ['time.cr.belowOne'],
          },
          executionRisk: {
            level: 'highrisk',
            isSourceMissing: false,
            isSourceStale: true,
            reasonCodes: ['material.shortage', 'urgency.source.stale'],
            facts: [],
          },
        },
      },
      global: { stubs: routerLinkStub },
    })

    expect(wrapper.text()).toContain('紧急 · CR 0.8')
    await wrapper.get('button').trigger('click')
    expect(document.body.textContent).toContain('业务优先级')
    expect(document.body.textContent).toContain('CR / Slack')
    expect(document.body.textContent).toContain('执行风险')
    expect(document.body.textContent).toContain('order-urgency-v1')
    expect(document.body.textContent).toContain('进入排产调整')
  })

  it('uses the issue vocabulary and routes to the scheduling order id without a page reload', async () => {
    const wrapper = mount(OrderUrgencyBadge, {
      props: {
        orderReference: 'SO-001',
        urgency: {
          orderId: 'WO-001',
          businessReference: 'SO-001',
          level: 'critical',
        },
      },
      global: { stubs: routerLinkStub },
    })

    expect(wrapper.text()).toContain('特急')
    await wrapper.get('button').trigger('click')
    const link = document.body.querySelector('[data-router-link]')
    expect(link?.getAttribute('data-to')).toContain('WO-001')
  })
})
