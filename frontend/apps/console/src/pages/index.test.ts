import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import IndexPage from './index.vue'

const apiState = vi.hoisted(() => ({
  failListRefetch: false,
  listFetchCount: 0,
}))

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
      key: [{ _id: 'listConsoleInstances' }],
      query: vi.fn(async () => {
        apiState.listFetchCount += 1

        if (apiState.failListRefetch && apiState.listFetchCount > 1) {
          throw new Error('list refresh failed')
        }

        return {
          pageNumber: 1,
          pageSize: 20,
          totalCount: 1,
          items: [instance],
        }
      }),
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
  beforeEach(() => {
    apiState.failListRefetch = false
    apiState.listFetchCount = 0
  })

  function mountPage() {
    return mount(IndexPage, {
      global: {
        plugins: [createPinia(), [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
        stubs: {
          RouterLink: {
            props: ['to'],
            template: '<a :href="to"><slot /></a>',
          },
        },
      },
    })
  }

  it('renders the instance operation table', async () => {
    const wrapper = mountPage()

    await flushPromises()

    expect(wrapper.text()).toContain('Demo API')
    expect(wrapper.text()).toContain('running')
    expect(wrapper.text()).toContain('Restart')
  })

  it('keeps restart success visible when list refetch fails after invalidation', async () => {
    apiState.failListRefetch = true

    const wrapper = mountPage()

    await flushPromises()

    const restartButton = wrapper.findAll('button').find((button) => button.text() === 'Restart')
    expect(restartButton).toBeDefined()

    await restartButton!.trigger('click')
    await flushPromises()

    const operationLink = wrapper.find('.console-page__operation-link')
    expect(operationLink.exists()).toBe(true)
    expect(operationLink.attributes('href')).toBe('/operations/task-1')
    expect(wrapper.text().match(/list refresh failed/g)).toHaveLength(1)
  })
})
