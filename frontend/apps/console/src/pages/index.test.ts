import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'

import IndexPage from './index.vue'

vi.mock('@nerv-iip/api-client', () => {
  const instance = {
    applicationKey: 'demo-api',
    applicationName: 'Demo API',
    version: '1.0.0',
    nodeKey: 'node-a',
    nodeName: 'Node A',
    instanceKey: 'demo-api-1',
    instanceName: 'demo-api-1',
    reportedStatus: 'running',
    healthStatus: 'healthy',
    lastHeartbeatAtUtc: '2026-05-16T00:00:00Z',
    lastStateObservedAtUtc: '2026-05-16T00:00:00Z',
  }

  return {
    getConsoleInstanceDetailQueryOptions: vi.fn(() => ({
      key: ['console-instance-detail', instance.instanceKey],
      query: vi.fn(async () => ({
        ...instance,
        capabilities: [
          {
            capabilityCode: 'runtime.restart',
            capabilityVersion: 'v1',
            category: 'operations',
            supportedOperations: ['restart'],
          },
        ],
        metadata: {
          region: 'dev',
        },
      })),
    })),
    getConsoleOperationTaskQueryOptions: vi.fn(() => ({
      key: ['console-operation-task', 'task-1'],
      query: vi.fn(async () => ({
        operationTaskId: 'task-1',
        operationCode: 'restart',
        status: 'queued',
        auditRecords: [],
      })),
    })),
    listConsoleInstancesQueryOptions: vi.fn(() => ({
      key: ['console-instances'],
      query: vi.fn(async () => ({
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        items: [instance],
      })),
    })),
    restartConsoleInstanceMutationOptions: vi.fn(() => ({
      mutation: vi.fn(async () => ({
        operationTaskId: 'task-1',
        operationCode: 'restart',
        status: 'queued',
      })),
    })),
  }
})

describe('Console index page', () => {
  it('renders the instance operation table', async () => {
    const wrapper = mount(IndexPage, {
      global: {
        plugins: [
          createPinia(),
          [PiniaColada, { queryOptions: { gcTime: 300_000 } }],
        ],
        stubs: {
          RouterLink: {
            props: ['to'],
            template: '<a><slot /></a>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Demo API')
    expect(wrapper.text()).toContain('running')
    expect(wrapper.text()).toContain('Restart')
  })
})
