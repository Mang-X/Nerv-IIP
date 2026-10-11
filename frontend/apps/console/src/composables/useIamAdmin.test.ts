import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'

import { listConsoleIamUsersQueryOptions } from '@nerv-iip/api-client'
import { useIamUsers } from './useIamAdmin'

const apiState = vi.hoisted(() => ({
  listFetchCount: 0,
  listSuccess: true,
  disableFails: false,
}))

vi.mock('@nerv-iip/api-client', () => ({
  createConsoleIamRoleMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  createConsoleIamUserMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  disableConsoleIamUserMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async () => {
      if (apiState.disableFails) throw { message: '平台管理员账号不能停用。' }
    }),
  })),
  enableConsoleIamUserMutationOptions: vi.fn(() => ({
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
        success: apiState.listSuccess,
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
  replaceConsoleIamUserMembershipMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
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
    apiState.listSuccess = true
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

  it('rebuilds the user list query options when filters change', async () => {
    const Probe = defineComponent({
      setup() {
        const { filters } = useIamUsers()

        return () =>
          h(
            'button',
            {
              onClick: () => {
                filters.filterSearch = 'alice'
                filters.pageIndex = 2
              },
            },
            'filter',
          )
      },
    })

    const pinia = createPinia()
    const wrapper = mount(Probe, {
      global: {
        plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
      },
    })

    await flushPromises()

    await wrapper.get('button').trigger('click')
    await nextTick()
    await flushPromises()

    expect(listConsoleIamUsersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        filterSearch: 'alice',
        pageIndex: 2,
        pageSize: 20,
      },
    })
  })

  it('exposes empty users and totalCount zero for unsuccessful envelopes', async () => {
    apiState.listSuccess = false
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

    expect(wrapper.text()).toBe('0 0')
  })
  it('clears an earlier failed mutation after a different user operation succeeds', async () => {
    let users!: ReturnType<typeof useIamUsers>
    const wrapper = mount(
      defineComponent({
        setup() {
          users = useIamUsers()
          return () => h('output', users.disableUserError.value?.message ?? '')
        },
      }),
      { global: { plugins: [createPinia(), PiniaColada] } },
    )
    await flushPromises()
    apiState.disableFails = true
    await expect(users.disableUser({ path: { userId: 'user-1' } })).rejects.toBeDefined()
    await flushPromises()
    expect(wrapper.text()).toBe('平台管理员账号不能停用。')
    await users.enableUser({ path: { userId: 'user-1' } })
    await flushPromises()
    expect(wrapper.text()).toBe('')
    wrapper.unmount()
  })
})
