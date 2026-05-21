import { describe, expect, expectTypeOf, it } from 'vitest'
import { client } from './generated/client.gen'
import type { ListConsoleInstancesData } from './generated/types.gen'
import {
  listConsoleNotificationMessagesQueryOptions,
  listConsoleNotificationTasksQueryOptions,
  markConsoleNotificationMessageReadMutationOptions,
  markConsoleNotificationMessagesReadMutationOptions,
  submitConsoleNotificationIntentMutationOptions,
} from './console'
import {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  disableConsoleIamUserMutationOptions,
  listConsoleIamPermissionsQueryOptions,
  listConsoleIamRolesQueryOptions,
  listConsoleIamSessionsQueryOptions,
  listConsoleIamUsersQueryOptions,
  resetConsoleIamUserPasswordMutationOptions,
  revokeConsoleIamSessionMutationOptions,
  updateConsoleIamRolePermissionsMutationOptions,
  updateConsoleIamUserMutationOptions,
} from './iam'

describe('generated API client contract', () => {
  it('defaults to a browser-relative base URL instead of the OpenAPI export server', () => {
    const config = client.getConfig()

    expect(config.baseUrl ?? '').toBe('')
    expect(config.baseUrl).not.toBe('http://127.0.0.1:5100')
  })

  it('matches generated query parameters for listing console instances', () => {
    type Query = ListConsoleInstancesData['query']

    expectTypeOf<Query>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      pageIndex?: number | null
      pageSize?: number | null
      sortBy?: string | null
      sortOrder?: string | null
      filterSearch?: string | null
    }>()
  })

  it('exports Console IAM Admin generated operations through stable api-client entry points', () => {
    expect(listConsoleIamUsersQueryOptions).toBeTypeOf('function')
    expect(createConsoleIamUserMutationOptions).toBeTypeOf('function')
    expect(updateConsoleIamUserMutationOptions).toBeTypeOf('function')
    expect(disableConsoleIamUserMutationOptions).toBeTypeOf('function')
    expect(resetConsoleIamUserPasswordMutationOptions).toBeTypeOf('function')
    expect(listConsoleIamRolesQueryOptions).toBeTypeOf('function')
    expect(createConsoleIamRoleMutationOptions).toBeTypeOf('function')
    expect(updateConsoleIamRolePermissionsMutationOptions).toBeTypeOf('function')
    expect(listConsoleIamPermissionsQueryOptions).toBeTypeOf('function')
    expect(listConsoleIamSessionsQueryOptions).toBeTypeOf('function')
    expect(revokeConsoleIamSessionMutationOptions).toBeTypeOf('function')
  })

  it('exports Console Notification generated operations through stable api-client entry points', () => {
    expect(listConsoleNotificationMessagesQueryOptions).toBeTypeOf('function')
    expect(listConsoleNotificationTasksQueryOptions).toBeTypeOf('function')
    expect(submitConsoleNotificationIntentMutationOptions).toBeTypeOf('function')
    expect(markConsoleNotificationMessageReadMutationOptions).toBeTypeOf('function')
    expect(markConsoleNotificationMessagesReadMutationOptions).toBeTypeOf('function')
  })
})
