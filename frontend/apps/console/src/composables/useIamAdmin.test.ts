import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { listConsoleIamUsersQueryOptions } from '@nerv-iip/api-client'
import { useIamUsers } from './useIamAdmin'

const apiState = vi.hoisted(() => ({
  listFetchCount: 0,
}))

vi.mock('@nerv-iip/api-client', () => ({
  createConsoleIamRoleMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  createConsoleIamUserMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  disableConsoleIamUserMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  listConsoleIamRolesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleIamRoles' }],
    query: vi.fn(),
  })),
  listConsoleIamSessionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleIamSessions' }],
    query: vi.fn(),
  })),
  listConsoleIamUsersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleIamUsers' }],
    query: vi.fn(async () => {
      apiState.listFetchCount += 1

      return {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 20,
          totalCount: 1,
          items: [
            {
              userId: 'user-1',
              loginName: 'admin',
            },
          ],
        },
      }
    }),
  })),
  resetConsoleIamUserPasswordMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  revokeConsoleIamSessionMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  updateConsoleIamRolePermissionsMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  updateConsoleIamUserMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
}))

describe('useIamAdmin composables', () => {
  beforeEach(() => {
    apiState.listFetchCount = 0
    vi.clearAllMocks()
  })

  it('lists IAM users through Pinia Colada', async () => {
    const Probe = defineComponent({
      setup() {
        const { totalCount, users } = useIamUsers()

        return () => h('output', `${users.value.length} ${totalCount.value}`)
      },
    })

    const pinia = createPinia()
    const wrapper = mount(Probe, {
      global: {
        plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
      },
    })

    await flushPromises()

    expect(wrapper.text()).toBe('1 1')
    expect(apiState.listFetchCount).toBe(1)
    expect(listConsoleIamUsersQueryOptions).toHaveBeenCalledWith({
      query: {
        pageIndex: 1,
        pageSize: 20,
      },
    })
  })
})
