import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import ConnectorTagCoveragePanel from './ConnectorTagCoveragePanel.vue'

const coverageMocks = vi.hoisted(() => ({
  connectorIds: [] as string[],
  coverageRef: null as { value: unknown } | null,
  errorRef: null as { value: unknown } | null,
  pendingRef: null as { value: boolean } | null,
  refreshCoverage: vi.fn(),
}))

vi.mock('@/composables/useBusinessTelemetry', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useBusinessTelemetry')>()
  const vue = await import('vue')
  coverageMocks.coverageRef = vue.shallowRef()
  coverageMocks.errorRef = vue.shallowRef()
  coverageMocks.pendingRef = vue.shallowRef(false)
  return {
    ...original,
    useBusinessTelemetryConnectorCoverage: (connectorId: { value: string }) => {
      coverageMocks.connectorIds.push(connectorId.value)
      return {
        coverage: coverageMocks.coverageRef,
        coverageError: coverageMocks.errorRef,
        coveragePending: coverageMocks.pendingRef,
        refreshCoverage: coverageMocks.refreshCoverage,
      }
    },
  }
})

const stubs = {
  NvBadge: { template: '<span><slot /></span>' },
  NvButton: { template: '<button><slot /></button>' },
}

function mountPanel() {
  return mount(ConnectorTagCoveragePanel, {
    props: { collectionConnectorId: 'modbus-main' },
    global: { stubs },
  })
}

describe('ConnectorTagCoveragePanel', () => {
  beforeEach(() => {
    coverageMocks.connectorIds = []
    coverageMocks.coverageRef!.value = undefined
    coverageMocks.errorRef!.value = undefined
    coverageMocks.pendingRef!.value = false
    coverageMocks.refreshCoverage.mockReset()
  })

  it('renders loading and offers a local retry after a request failure', async () => {
    coverageMocks.pendingRef!.value = true
    const loading = mountPanel()
    expect(loading.text()).toContain('正在加载已配置标签')
    loading.unmount()

    coverageMocks.pendingRef!.value = false
    coverageMocks.errorRef!.value = new Error('network detail must stay hidden')
    const failed = mountPanel()
    expect(failed.text()).toContain('已配置标签加载失败')
    expect(failed.text()).not.toContain('network detail')

    await failed.get('[data-testid="coverage-retry"]').trigger('click')
    expect(coverageMocks.refreshCoverage).toHaveBeenCalledTimes(1)
  })

  it('distinguishes an unavailable manifest from a valid empty configuration', () => {
    coverageMocks.coverageRef!.value = {
      collectionConnectorId: 'modbus-main',
      manifestStatus: 'unavailable',
      items: [],
    }
    const unavailable = mountPanel()
    expect(unavailable.text()).toContain('尚未上报已配置标签清单')
    expect(unavailable.text()).not.toContain('当前未配置采集标签')
    unavailable.unmount()

    coverageMocks.coverageRef!.value = {
      collectionConnectorId: 'modbus-main',
      manifestStatus: 'current',
      configuredCount: 0,
      items: [],
    }
    const empty = mountPanel()
    expect(empty.text()).toContain('当前未配置采集标签')
    expect(empty.text()).not.toContain('尚未上报已配置标签清单')
  })

  it('renders disabled, activation error, never-sampled, and sampled facts distinctly', () => {
    coverageMocks.coverageRef!.value = {
      collectionConnectorId: 'modbus-main',
      manifestStatus: 'current',
      configuredCount: 4,
      enabledCount: 3,
      activeCount: 2,
      everSampledCount: 1,
      errorCount: 1,
      manifestRevision: 'secret-revision-must-not-render',
      items: [
        {
          deviceAssetId: 'DEV-CNC-01',
          tagKey: 'disabled-speed',
          enabled: false,
          activationStatus: 'disabled',
          lastSampleAtUtc: '2026-07-16T07:08:09.000Z',
        },
        {
          deviceAssetId: 'DEV-CNC-01',
          tagKey: 'broken-temperature',
          enabled: true,
          activationStatus: 'error',
          activationErrorCode: 'OPC_BAD_NODE',
          activationErrorMessage: 'OPC UA subscription activation failed.',
        },
        {
          deviceAssetId: 'DEV-CNC-02',
          tagKey: 'waiting-current',
          enabled: true,
          activationStatus: 'active',
          lastSampleAtUtc: null,
        },
        {
          deviceAssetId: 'DEV-CNC-02',
          tagKey: 'sampled-pressure',
          enabled: true,
          activationStatus: 'active',
          lastSampleAtUtc: '2026-07-17T08:09:10.000Z',
        },
      ],
    }

    const wrapper = mountPanel()
    const text = wrapper.text()
    const [disabled, activationError, neverSampled, sampled] = wrapper.findAll('article')

    expect(text).toContain('已停用')
    expect(text).toContain('启用失败')
    expect(text).toContain('采集程序未能启用此标签')
    expect(text).toContain('等待首条数据')
    expect(text).toContain('已收到数据')
    expect(text).toContain('最近采样')
    expect(text).not.toContain('secret-revision-must-not-render')
    expect(text).not.toContain('OPC_BAD_NODE')
    expect(text).not.toContain('OPC UA subscription activation failed.')
    expect(text).not.toContain('数据当前')
    expect(text).not.toContain('数据过期')
    expect(text).not.toContain('质量异常')
    expect(disabled.text()).toContain('最近采样')
    expect(activationError.text()).toContain('尚未收到采样')
    expect(neverSampled.text()).toContain('等待首条数据')
    expect(sampled.text()).toContain('最近采样')
  })

  it('queries only the canonical connector identity supplied by its card', async () => {
    coverageMocks.coverageRef!.value = {
      collectionConnectorId: 'modbus-main',
      manifestStatus: 'current',
      items: [],
    }

    mountPanel()
    await flushPromises()

    expect(coverageMocks.connectorIds).toEqual(['modbus-main'])
  })
})
