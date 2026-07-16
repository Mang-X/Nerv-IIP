import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, ref, shallowRef } from 'vue'

import { computeConnectorSampleRate } from '@/composables/useBusinessTelemetry'
import ConnectorsPage from './telemetry/connectors.vue'

const connectorMocks = vi.hoisted(() => ({
  refreshConnectors: vi.fn(),
  connectors: [
    {
      connectorId: 'modbus-main',
      connectorName: 'Modbus Main',
      status: 'stale',
      staleReason: 'heartbeat',
      sourceSystem: 'modbus',
      receivedCount: 50,
      droppedCount: 9,
      errorCount: 2,
      counterEpoch: '22222222-2222-2222-2222-222222222222',
      lastHeartbeatAtUtc: '2026-07-13T01:00:00.000Z',
      metricsReportedAtUtc: '2026-07-13T01:01:00.000Z',
      lastSampleAtUtc: '2026-07-13T01:00:58.000Z',
    },
    {
      connectorId: 'mqtt-main',
      connectorName: 'MQTT Main',
      status: 'stale',
      staleReason: 'metrics',
      sourceSystem: 'mqtt',
      receivedCount: 70,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '44444444-4444-4444-4444-444444444444',
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: '2026-07-13T01:01:30.000Z',
      lastSampleAtUtc: '2026-07-13T01:01:29.000Z',
    },
    {
      connectorId: 'opcua-main',
      connectorName: 'OPC UA Main',
      status: 'current',
      staleReason: null,
      sourceSystem: 'opcua',
      receivedCount: 100,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '11111111-1111-1111-1111-111111111111',
      lastHeartbeatAtUtc: '2026-07-13T01:09:30.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:40.000Z',
      lastSampleAtUtc: '2026-07-13T01:09:39.000Z',
    },
  ],
}))

vi.mock('@/utils/notify', () => ({ notifyError: vi.fn(), notifySuccess: vi.fn() }))

vi.mock('@/composables/useBusinessTelemetry', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useBusinessTelemetry')>()),
  useBusinessTelemetryConnectors: () => ({
    connectors: computed(() => connectorMocks.connectors),
    connectorsError: shallowRef(),
    connectorsPending: shallowRef(false),
    connectorsTotal: computed(() => connectorMocks.connectors.length),
    refreshConnectors: connectorMocks.refreshConnectors,
    sampleRateByConnector: ref<Record<string, number | null>>({ 'opcua-main': 12.5 }),
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
  it('renders connector name, protocol type, throughput rate, and derived status', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    const text = wrapper.text()

    expect(text).toContain('Modbus Main')
    expect(text).toContain('OPC UA Main')
    expect(text).toContain('OPC UA')
    expect(text).toContain('在线')
    // sampling rate (samples/s) is shown, not only the cumulative counter
    expect(text).toContain('采样速率')
    expect(text).toContain('12.5 /秒')
  })

  it('distinguishes a real disconnect (断线) from an online-but-stalled collector (采集停滞)', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    expect(text).toContain('断线')
    expect(text).toContain('采集停滞')
    // heartbeat-loss shows a disconnect duration; metrics-stall shows a stalled-metrics hint instead
    expect(text).toContain('断线时长约')
    expect(text).toContain('指标停更约')
  })

  it('summarizes online / disconnected / stalled connectors separately', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    expect(text).toMatch(/在线\s*1/)
    expect(text).toMatch(/断线\s*1/)
    expect(text).toMatch(/采集停滞\s*1/)
  })

  it('does not expose organization or environment context', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })

    expect(wrapper.text()).not.toContain('组织')
    expect(wrapper.text()).not.toContain('环境')
    expect(wrapper.html()).not.toContain('organizationId')
    expect(wrapper.html()).not.toContain('environmentId')
  })

  it('expands a connector and points the per-tag list to the tracked follow-up', async () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    await wrapper.findAll('button[aria-expanded]')[0].trigger('click')

    expect(wrapper.text()).toContain('#947')
    expect(wrapper.text()).toContain('采集标签')
  })
})

describe('computeConnectorSampleRate', () => {
  const base = {
    counterEpoch: 'e1',
    receivedCount: 100,
    metricsReportedAtUtc: '2026-07-13T01:00:00.000Z',
  }

  it('computes samples/s from consecutive polls in the same epoch', () => {
    const rate = computeConnectorSampleRate(base, {
      counterEpoch: 'e1',
      receivedCount: 220,
      metricsReportedAtUtc: '2026-07-13T01:00:10.000Z',
    })
    expect(rate).toBeCloseTo(12) // (220-100)/10s
  })

  it('returns null (baseline reset) when the counter epoch changes', () => {
    expect(
      computeConnectorSampleRate(base, {
        counterEpoch: 'e2',
        receivedCount: 5,
        metricsReportedAtUtc: '2026-07-13T01:00:10.000Z',
      }),
    ).toBeNull()
  })

  it('returns null when the counter decreases (reset within reporting)', () => {
    expect(
      computeConnectorSampleRate(base, {
        counterEpoch: 'e1',
        receivedCount: 40,
        metricsReportedAtUtc: '2026-07-13T01:00:10.000Z',
      }),
    ).toBeNull()
  })

  it('returns null on the first sample or when time has not advanced', () => {
    expect(computeConnectorSampleRate(undefined, base)).toBeNull()
    expect(
      computeConnectorSampleRate(base, {
        counterEpoch: 'e1',
        receivedCount: 200,
        metricsReportedAtUtc: base.metricsReportedAtUtc,
      }),
    ).toBeNull()
  })
})
