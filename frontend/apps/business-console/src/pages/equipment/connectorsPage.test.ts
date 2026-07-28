import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { computeConnectorSampleRate } from '@/composables/useBusinessTelemetry'
import ConnectorHealthCard from '@/components/equipment/ConnectorHealthCard.vue'
import ConnectorsPage from './telemetry/connectors.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const connectorMocks = vi.hoisted(() => ({
  refreshConnectors: vi.fn(),
  coverageConnectorIds: [] as string[],
  errorRef: null as { value: unknown } | null,
  pendingRef: null as { value: boolean } | null,
  listRef: null as { value: unknown[] } | null,
  connectors: [
    {
      connectorId: 'modbus-main',
      connectorName: 'Modbus Main',
      status: 'stale',
      staleReason: 'offline',
      offlineReason: 'host-liveness',
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
      hostLivenessDeadlineUtc: '2026-07-13T01:00:06.000Z',
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
      hostLivenessDeadlineUtc: '2026-07-13T01:04:00.000Z',
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
    {
      connectorId: 'unknown-main',
      connectorName: 'Unknown Main',
      status: 'current',
      staleReason: null,
      offlineReason: null,
      connection: {
        status: 'unknown',
        observedAtUtc: '2026-07-13T01:09:45.000Z',
      },
      sourceSystem: 'opcua',
      receivedCount: 1,
      droppedCount: 0,
      errorCount: 0,
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: null,
      lastSampleAtUtc: null,
    },
    {
      connectorId: 'future-main',
      connectorName: 'Future Main',
      status: 'current',
      staleReason: null,
      offlineReason: null,
      connection: {
        status: 'recovering',
        observedAtUtc: '2026-07-13T01:09:45.000Z',
      },
      sourceSystem: 'opcua',
      receivedCount: 1,
      droppedCount: 0,
      errorCount: 0,
      lastHeartbeatAtUtc: '2026-07-13T01:09:45.000Z',
      metricsReportedAtUtc: null,
      lastSampleAtUtc: null,
    },
  ],
}))

const notifyMock = vi.hoisted(() => ({
  friendlyErrorMessage: (_error: unknown, fallback = '操作失败，请稍后重试。') => fallback,
  notifyError: vi.fn(),
  notifySuccess: vi.fn(),
}))
vi.mock('@/utils/notify', () => notifyMock)

// 业务上下文 store：页面用它区分「上下文未就绪」与「真的 0 个连接器」；
// 这些用例不装 Pinia，给可控桩（默认已就绪）。
const contextMock = vi.hoisted(() => ({ organizationId: 'org-001', environmentId: 'env-dev' }))
vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => contextMock,
}))

vi.mock('@/composables/useBusinessTelemetry', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useBusinessTelemetry')>()
  const vue = await import('vue')
  connectorMocks.errorRef = vue.shallowRef<unknown>(undefined)
  connectorMocks.pendingRef = vue.shallowRef(false)
  connectorMocks.listRef = vue.shallowRef<unknown[]>(connectorMocks.connectors)
  const visibleConnectors = vue.computed(() => connectorMocks.listRef!.value)
  return {
    ...original,
    useBusinessTelemetryConnectors: () => ({
      connectors: visibleConnectors,
      connectorsError: connectorMocks.errorRef,
      connectorsPending: connectorMocks.pendingRef,
      connectorsTotal: vue.computed(() => visibleConnectors.value.length),
      refreshConnectors: connectorMocks.refreshConnectors,
      sampleRateByConnector: vue.ref<Record<string, number | null>>({ 'opcua-main': 12.5 }),
    }),
    useBusinessTelemetryConnectorCoverage: (connectorId: { value: string }) => {
      connectorMocks.coverageConnectorIds.push(connectorId.value)
      return {
        coverage: vue.shallowRef({
          collectionConnectorId: connectorId.value,
          manifestStatus: 'current',
          configuredCount: 0,
          items: [],
        }),
        coverageError: vue.shallowRef(),
        coveragePending: vue.shallowRef(false),
        refreshCoverage: vi.fn(),
      }
    },
  }
})

function setError(error: unknown) {
  connectorMocks.errorRef!.value = error
}
function setPending(pending: boolean) {
  connectorMocks.pendingRef!.value = pending
}
function hideConnectors() {
  connectorMocks.listRef!.value = []
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
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-13T01:10:00.000Z'))
    notifyMock.notifyError.mockClear()
    connectorMocks.coverageConnectorIds = []
    connectorMocks.listRef!.value = connectorMocks.connectors
    connectorMocks.refreshConnectors.mockClear()
    contextMock.organizationId = 'org-001'
    contextMock.environmentId = 'env-dev'
    setError(undefined)
    setPending(false)
  })

  afterEach(() => {
    vi.useRealTimers()
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
    expect(text.match(/连接状态未知/g)).toHaveLength(3)
    expect(text).toContain('异常停止')
    expect(text).toContain('现场断开约 10 分钟')
    expect(text).toContain('主机离线约 6 分钟')
    expect(text).toContain('连接器上报异常停止')
    expect(text).not.toContain('field-connection')
    expect(text).not.toContain('host-liveness')
    expect(text).not.toContain('connection-lost')
  })

  it('does not infer a field disconnect duration from the observation time', () => {
    const wrapper = mount(ConnectorHealthCard, {
      props: {
        connector: {
          connectorId: 'missing-transition-time',
          connectorName: 'Missing Transition Time',
          status: 'stale',
          staleReason: 'offline',
          offlineReason: 'field-connection',
          connection: {
            status: 'lost',
            observedAtUtc: '2026-07-13T01:00:00.000Z',
            disconnectedSinceUtc: null,
          },
        },
        sampleRate: null,
        expanded: false,
      },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('现场连接断开')
    expect(wrapper.text()).not.toContain('现场断开约')
  })

  it('prioritizes a known host timeout over an unavailable legacy connection state', () => {
    const wrapper = mount(ConnectorHealthCard, {
      props: {
        connector: {
          connectorId: 'legacy-host-timeout',
          connectorName: 'Legacy Host Timeout',
          status: 'stale',
          staleReason: 'offline',
          offlineReason: 'host-liveness',
          connection: null,
          hostLivenessDeadlineUtc: '2026-07-13T01:04:00.000Z',
        },
        sampleRate: null,
        expanded: false,
      },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('采集主机离线')
    expect(wrapper.text()).not.toContain('连接状态未知')
  })

  it('prioritizes a known collector fault over an unavailable legacy connection state', () => {
    const wrapper = mount(ConnectorHealthCard, {
      props: {
        connector: {
          connectorId: 'legacy-fault',
          connectorName: 'Legacy Fault',
          status: 'stale',
          staleReason: 'fault',
          offlineReason: null,
          connection: null,
        },
        sampleRate: null,
        expanded: false,
      },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('异常停止')
    expect(wrapper.text()).not.toContain('连接状态未知')
  })

  it('suppresses host-offline duration when no liveness deadline was reported', () => {
    const wrapper = mount(ConnectorHealthCard, {
      props: {
        connector: {
          connectorId: 'host-timeout-without-deadline',
          connectorName: 'Host Timeout Without Deadline',
          status: 'stale',
          staleReason: 'offline',
          offlineReason: 'host-liveness',
          connection: null,
          hostLivenessDeadlineUtc: null,
        },
        sampleRate: null,
        expanded: false,
      },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('采集主机离线')
    expect(wrapper.text()).not.toContain('主机离线约')
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

  it('loads configured tags only while the canonical connector card is expanded', async () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })

    expect(connectorMocks.coverageConnectorIds).toEqual([])

    await wrapper.findAll('button[aria-expanded]')[0].trigger('click')
    expect(connectorMocks.coverageConnectorIds).toEqual(['modbus-main'])
    expect(wrapper.text()).toContain('当前未配置采集标签')

    await wrapper.findAll('button[aria-expanded]')[1].trigger('click')
    expect(connectorMocks.coverageConnectorIds).toEqual(['modbus-main', 'mqtt-main'])
    expect(wrapper.findAll('button[aria-expanded]')[0].attributes('aria-expanded')).toBe('true')
    expect(wrapper.findAll('button[aria-expanded]')[1].attributes('aria-expanded')).toBe('true')

    await wrapper.findAll('button[aria-expanded]')[1].trigger('click')
    await wrapper.findAll('button[aria-expanded]')[1].trigger('click')
    expect(connectorMocks.coverageConnectorIds).toEqual(['modbus-main', 'mqtt-main', 'mqtt-main'])
  })

  it('renders load failure as a persistent error block with retry, never as an empty state', async () => {
    hideConnectors()
    setError(new Error('boom'))
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    await nextTick()

    expect(wrapper.text()).toContain('采集健康取不到，无法判断现场采集是否正常。')
    expect(wrapper.text()).not.toContain('暂无现场采集连接')
    expect(wrapper.find('.nv-ring-card').exists()).toBe(false)
    // 计数不许在取不到时显 0
    expect(wrapper.text()).not.toContain('0 个采集连接器')

    const retry = wrapper.findAll('button').find((button) => button.text().trim() === '重试')
    expect(retry).toBeDefined()
    await retry!.trigger('click')
    expect(connectorMocks.refreshConnectors).toHaveBeenCalled()
  })

  it('keeps stale readings visible but marks them as not current after a failed refresh', async () => {
    const wrapper = mount(ConnectorsPage, { global: { stubs } })
    setError(new Error('boom'))
    await nextTick()

    expect(wrapper.text()).toContain(
      '采集健康刷新失败，下方为上一次成功读取的结果，不代表现场此刻的状态。',
    )
    expect(wrapper.text()).toContain('OPC UA Main')
  })

  it('separates not-yet-queried and loading from a genuine zero-connector site', async () => {
    hideConnectors()
    contextMock.organizationId = ''
    const idle = mount(ConnectorsPage, { global: { stubs } })
    expect(idle.text()).toContain('业务上下文未就绪，采集健康尚未查询。')
    expect(idle.text()).not.toContain('暂无现场采集连接')

    contextMock.organizationId = 'org-001'
    setPending(true)
    const loading = mount(ConnectorsPage, { global: { stubs } })
    expect(loading.text()).toContain('正在加载采集连接器…')
    expect(loading.text()).not.toContain('暂无现场采集连接')

    setPending(false)
    const empty = mount(ConnectorsPage, { global: { stubs } })
    expect(empty.text()).toContain('暂无现场采集连接')
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
