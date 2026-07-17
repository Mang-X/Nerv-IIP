import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { computeConnectorSampleRate } from '@/composables/useBusinessTelemetry'
import ConnectorsPage from './telemetry/connectors.vue'

const connectorMocks = vi.hoisted(() => ({
  refreshConnectors: vi.fn(),
  errorRef: null as { value: unknown } | null,
  connectors: [
    {
      connectorId: 'modbus-main',
      connectorName: 'Modbus Main',
      status: 'stale',
      staleReason: 'offline',
      offlineReason: 'field-connection',
      connection: {
        status: 'lost',
        observedAtUtc: '2026-07-13T01:00:00.000Z',
        disconnectedSinceUtc: '2026-07-13T01:00:00.000Z',
        reasonCategory: 'network',
        diagnosticCode: 'connection-lost',
      },
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
      staleReason: 'fault',
      offlineReason: null,
      connection: {
        status: 'alive',
        observedAtUtc: '2026-07-13T01:09:45.000Z',
        connectedSinceUtc: '2026-07-13T01:00:00.000Z',
      },
      sourceSystem: 'mqtt',
      receivedCount: 70,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '44444444-4444-4444-4444-444444444444',
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:40.000Z',
      lastSampleAtUtc: '2026-07-13T01:01:29.000Z',
    },
    {
      connectorId: 'opcua-main',
      connectorName: 'OPC UA Main',
      status: 'current',
      staleReason: null,
      offlineReason: null,
      connection: {
        status: 'alive',
        observedAtUtc: '2026-07-13T01:09:30.000Z',
        connectedSinceUtc: '2026-07-13T01:00:00.000Z',
      },
      sourceSystem: 'opcua',
      receivedCount: 100,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '11111111-1111-1111-1111-111111111111',
      lastHeartbeatAtUtc: '2026-07-13T01:09:30.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:40.000Z',
      lastSampleAtUtc: '2026-07-13T01:09:39.000Z',
    },
    {
      connectorId: 'modbus-empty',
      connectorName: 'Modbus Empty',
      status: 'unknown',
      staleReason: null,
      offlineReason: null,
      connection: {
        status: 'alive',
        observedAtUtc: '2026-07-13T01:09:45.000Z',
        connectedSinceUtc: '2026-07-13T01:00:00.000Z',
      },
      sourceSystem: 'modbus',
      receivedCount: null,
      droppedCount: null,
      errorCount: null,
      counterEpoch: '77777777-7777-7777-7777-777777777777',
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:46.000Z',
      lastSampleAtUtc: null,
    },
    {
      connectorId: 'opcua-host-timeout',
      connectorName: 'OPC UA Host Timeout',
      status: 'stale',
      staleReason: 'offline',
      offlineReason: 'host-liveness',
      connection: {
        status: 'alive',
        observedAtUtc: '2026-07-13T01:00:00.000Z',
        connectedSinceUtc: '2026-07-13T00:55:00.000Z',
      },
      sourceSystem: 'opcua',
      receivedCount: 200,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '88888888-8888-8888-8888-888888888888',
      lastHeartbeatAtUtc: '2026-07-13T01:00:00.000Z',
      metricsReportedAtUtc: '2026-07-13T01:00:01.000Z',
      lastSampleAtUtc: '2026-07-13T01:00:00.000Z',
    },
    {
      connectorId: 'legacy-main',
      connectorName: 'Legacy Main',
      status: 'unknown',
      staleReason: null,
      offlineReason: null,
      connection: null,
      sourceSystem: 'opcua',
      receivedCount: 12,
      droppedCount: 0,
      errorCount: 0,
      counterEpoch: '99999999-9999-9999-9999-999999999999',
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: '2026-07-13T01:09:46.000Z',
      lastSampleAtUtc: '2026-07-13T01:09:44.000Z',
    },
  ],
}))

const notifyMock = vi.hoisted(() => ({ notifyError: vi.fn(), notifySuccess: vi.fn() }))
vi.mock('@/utils/notify', () => notifyMock)

vi.mock('@/composables/useBusinessTelemetry', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useBusinessTelemetry')>()
  const vue = await import('vue')
  connectorMocks.errorRef = vue.shallowRef<unknown>(undefined)
  return {
    ...original,
    useBusinessTelemetryConnectors: () => ({
      connectors: vue.computed(() => connectorMocks.connectors),
      connectorsError: connectorMocks.errorRef,
      connectorsPending: vue.shallowRef(false),
      connectorsTotal: vue.computed(() => connectorMocks.connectors.length),
      refreshConnectors: connectorMocks.refreshConnectors,
      sampleRateByConnector: vue.ref<Record<string, number | null>>({ 'opcua-main': 12.5 }),
    }),
  }
})

function setError(error: unknown) {
  connectorMocks.errorRef!.value = error
}

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
  beforeEach(() => {
    notifyMock.notifyError.mockClear()
    setError(undefined)
  })

  it('renders connector name, protocol type, throughput rate, and derived status', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    expect(text).toContain('Modbus Main')
    expect(text).toContain('OPC UA Main')
    expect(text).toContain('OPC UA')
    expect(text).toContain('在线')
    expect(text).toContain('采样速率')
    expect(text).toContain('12.5 /秒')
  })

  it('distinguishes field loss, host timeout, collector fault, and legacy connection unknown', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    const text = wrapper.text()

    expect(text).toContain('现场连接断开')
    expect(text).toContain('采集主机离线')
    expect(text).toContain('连接状态未知')
    expect(text).toContain('异常停止')
    expect(text).toContain('现场断开约')
    expect(text).toContain('主机离线约')
    expect(text).toContain('连接器上报异常停止')
    expect(text).not.toContain('field-connection')
    expect(text).not.toContain('host-liveness')
    expect(text).not.toContain('connection-lost')
  })

  it('summarizes online / offline / fault connectors separately', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    // the never-sampled connector is 待采集, NOT counted as online
    expect(text).toMatch(/在线\s*1/)
    expect(text).toMatch(/断线\s*2/)
    expect(text).toMatch(/异常停止\s*1/)
  })

  it('shows a not-configured connector as 待采集, not as online/collecting', () => {
    const text = mount(ConnectorsPage, { global: { stubs } }).text()

    expect(text).toContain('待采集')
    expect(text).toContain('Modbus Empty')
  })

  it('does not expose organization/environment context or engineering/issue jargon', () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    const html = wrapper.html()

    expect(wrapper.text()).not.toContain('组织')
    expect(wrapper.text()).not.toContain('环境')
    expect(html).not.toContain('organizationId')
    expect(html).not.toContain('environmentId')
    // engineering/issue jargon must stay out of the field UI (docs/PR only)
    expect(html).not.toContain('#947')
    expect(html).not.toContain('github.com')
    expect(html).not.toContain('facade')
  })

  it('expands a connector to operator-facing collection detail', async () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    await wrapper.findAll('button[aria-expanded]')[0].trigger('click')

    expect(wrapper.text()).toContain('采集协议')
    expect(wrapper.text()).toContain('采集标签')
  })

  it('does not spam toast on repeated auto-refetch failures, but re-notifies after recovery', async () => {
    mount(ConnectorsPage, { global: { stubs } })

    setError(new Error('boom-1'))
    await nextTick()
    setError(new Error('boom-2')) // next poll, fresh error object
    await nextTick()
    setError(new Error('boom-3'))
    await nextTick()
    expect(notifyMock.notifyError).toHaveBeenCalledTimes(1)

    setError(undefined) // recovered
    await nextTick()
    setError(new Error('boom-4')) // new failure episode
    await nextTick()
    expect(notifyMock.notifyError).toHaveBeenCalledTimes(2)
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
