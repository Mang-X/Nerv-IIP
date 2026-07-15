import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import ConnectorsPage from './telemetry/connectors.vue'

const connectorMocks = vi.hoisted(() => ({
  refreshConnectors: vi.fn(),
  connectors: [
    {
      connectorId: 'modbus-main',
      connectorName: 'Modbus Main',
      status: 'stale',
      sourceSystem: 'modbus',
      receivedCount: 50,
      droppedCount: 9,
      errorCount: 2,
      lastHeartbeatAtUtc: '2026-07-13T01:00:00.000Z',
      metricsReportedAtUtc: '2026-07-13T01:01:00.000Z',
      lastSampleAtUtc: '2026-07-13T01:00:58.000Z',
    },
    {
      connectorId: 'opcua-main',
      connectorName: 'OPC UA Main',
      status: 'current',
      sourceSystem: 'opcua',
      receivedCount: 100,
      droppedCount: 0,
      errorCount: 0,
      lastHeartbeatAtUtc: '2026-07-13T01:09:30.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:40.000Z',
      lastSampleAtUtc: '2026-07-13T01:09:39.000Z',
    },
  ],
}))

vi.mock('@/composables/useBusinessTelemetry', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useBusinessTelemetry')>()),
  useBusinessTelemetryConnectors: () => ({
    connectors: computed(() => connectorMocks.connectors),
    connectorsError: shallowRef(),
    connectorsPending: shallowRef(false),
    connectorsTotal: computed(() => connectorMocks.connectors.length),
    refreshConnectors: connectorMocks.refreshConnectors,
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvBadge: { template: '<span><slot /></span>' },
  NvButton: { template: '<button><slot /></button>' },
  NvPageHeader: {
    props: ['title', 'count'],
    template:
      '<header><h1>{{ title }}</h1><span>{{ count }}</span><slot name="actions" /></header>',
  },
  NvSectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  NvSectionCards: { template: '<section><slot /></section>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

describe('equipment telemetry connectors page', () => {
  it('renders connector name, protocol type, throughput, and derived status', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    const text = wrapper.text()

    expect(text).toContain('Modbus Main')
    expect(text).toContain('OPC UA Main')
    expect(text).toContain('Modbus')
    expect(text).toContain('OPC UA')
    expect(text).toContain('在线')
    expect(text).toContain('断线 / 异常')
  })

  it('summarizes online vs disconnected connectors', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    expect(text).toMatch(/在线\s*1/)
    expect(text).toMatch(/断线 \/ 异常\s*1/)
  })

  it('does not expose organization or environment context', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })

    expect(wrapper.text()).not.toContain('组织')
    expect(wrapper.text()).not.toContain('环境')
    expect(wrapper.html()).not.toContain('organizationId')
    expect(wrapper.html()).not.toContain('environmentId')
  })

  it('expands a connector to reveal its collection detail without a per-tag facade', async () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    await wrapper.findAll('button[aria-expanded]')[0].trigger('click')

    expect(wrapper.text()).toContain('采集标签')
    expect(wrapper.text()).toContain('指标上报时间')
  })
})
