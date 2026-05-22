import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import { createConsoleI18n } from '@/i18n'
import { listConsoleInstancesQueryOptions } from '@nerv-iip/api-client'
import IndexPage from './index.vue'

const apiState = vi.hoisted(() => ({
  detailFailures: [] as string[],
  detailFetchCount: 0,
  failDetail: false,
  failListRefetch: false,
  listFetchCount: 0,
}))
const routerState = vi.hoisted(() => ({
  push: vi.fn(),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRouter: () => ({
    push: routerState.push,
  }),
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
    healthStatus: 'unhealthy',
    lastHeartbeatAtUtc: '2026-05-16T00:00:00Z',
    lastStateObservedAtUtc: '2026-05-16T00:00:00Z',
  }

  return {
    getConsoleInstanceDetailQueryOptions: vi.fn(() => ({
      key: ['console-instance-detail', instance.instanceKey],
      query: vi.fn(async () => {
        apiState.detailFetchCount += 1

        const nextDetailFailure = apiState.detailFailures.shift()
        if (nextDetailFailure) {
          throw new Error(nextDetailFailure)
        }

        if (apiState.failDetail) {
          throw new Error('detail fetch failed')
        }

        return {
          success: true,
          data: {
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
          },
        }
      }),
    })),
    getConsoleOperationTaskQueryOptions: vi.fn(() => ({
      key: ['console-operation-task', 'task-1'],
      query: vi.fn(async () => ({
        success: true,
        data: {
          operationTaskId: 'task-1',
          operationCode: 'restart',
          status: 'queued',
          auditRecords: [],
        },
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
          success: true,
          data: {
            pageIndex: 1,
            pageSize: 20,
            totalCount: 1,
            items: [instance],
          },
        }
      }),
    })),
    restartConsoleInstanceMutationOptions: vi.fn(() => ({
      mutation: vi.fn(async () => ({
        success: true,
        data: {
          operationTaskId: 'task-1',
          operationCode: 'restart',
          status: 'queued',
        },
      })),
    })),
  }
})

describe('Console index page', () => {
  beforeEach(() => {
    apiState.detailFailures = []
    apiState.detailFetchCount = 0
    apiState.failDetail = false
    apiState.failListRefetch = false
    apiState.listFetchCount = 0
    routerState.push.mockReset()
  })

  function mountPage() {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)

    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
        principalType: 'user',
        loginName: 'admin',
        organizationId: 'org-page-test',
        environmentId: 'env-page-test',
      },
    })

    return mount(IndexPage, {
      global: {
        plugins: [
          pinia,
          createConsoleI18n({ locale: 'en-US' }),
          [PiniaColada, { queryOptions: { gcTime: 300_000 } }],
        ],
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
    expect(listConsoleInstancesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-page-test',
        environmentId: 'env-page-test',
        pageIndex: 1,
        pageSize: 20,
      },
    })
  })

  it('renders destructive status states', async () => {
    const wrapper = mountPage()

    await flushPromises()

    const destructiveBadges = wrapper.findAll('[data-variant="destructive"]')
    expect(destructiveBadges.some((badge) => badge.text() === 'unhealthy')).toBe(true)
  })

  it('distinguishes detail load failure from the empty detail state and retries manually', async () => {
    apiState.failDetail = true
    const wrapper = mountPage()

    await flushPromises()

    expect(wrapper.text()).toContain('Unable to load instance detail')
    expect(wrapper.text()).toContain('detail fetch failed')
    expect(wrapper.text()).not.toContain('Select an instance to inspect its runtime facts.')

    apiState.failDetail = false
    const retryButton = wrapper.findAll('button').find((button) => button.text() === 'Retry')
    expect(retryButton).toBeDefined()

    await retryButton!.trigger('click')
    await flushPromises()

    expect(apiState.detailFetchCount).toBeGreaterThanOrEqual(2)
    expect(wrapper.text()).toContain('runtime.restart')
    expect(wrapper.text()).not.toContain('Unable to load instance detail')
  })

  it('keeps the detail error state visible when a manual retry also fails', async () => {
    apiState.detailFailures = ['detail fetch failed', 'detail retry still failed']
    const wrapper = mountPage()

    await flushPromises()

    expect(wrapper.text()).toContain('detail fetch failed')

    const retryButton = wrapper.findAll('button').find((button) => button.text() === 'Retry')
    expect(retryButton).toBeDefined()

    await retryButton!.trigger('click')
    await flushPromises()

    expect(apiState.detailFetchCount).toBeGreaterThanOrEqual(2)
    expect(wrapper.text()).toContain('Unable to load instance detail')
    expect(wrapper.text()).toContain('detail retry still failed')
    expect(wrapper.text()).not.toContain('Select an instance to inspect its runtime facts.')
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
    expect(routerState.push).toHaveBeenCalledWith('/operations/task-1')
    expect(wrapper.text().match(/list refresh failed/g)).toHaveLength(1)
  })
})
