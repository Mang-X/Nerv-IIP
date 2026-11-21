import { describe, expect, expectTypeOf, it } from 'vitest'
import { client } from './generated/client.gen'
import type { ListConsoleInstancesData } from './generated/types.gen'
import type {
  BusinessConsoleNotificationMessageItem,
  BusinessConsoleNotificationTaskItem,
  BusinessConsoleMarkNotificationMessageReadResponse,
} from './business-console'
import {
  getConsoleNotificationDeadLetterMetricsQueryOptions,
  getConsoleNotificationDeadLetterQueryOptions,
  ignoreConsoleNotificationDeadLetterMutationOptions,
  listConsoleNotificationDeadLettersQueryOptions,
  listConsoleNotificationMessagesQueryOptions,
  listConsoleNotificationTasksQueryOptions,
  markConsoleNotificationMessageReadMutationOptions,
  markConsoleNotificationMessagesReadMutationOptions,
  replayConsoleNotificationDeadLetterMutationOptions,
  replayConsoleNotificationDeadLettersMutationOptions,
  submitConsoleNotificationIntentMutationOptions,
  upsertConsoleNotificationPreferenceMutationOptions,
  upsertConsoleNotificationSubscriptionMutationOptions,
  upsertConsoleNotificationRecipientChannelBindingMutationOptions,
} from './console'
import {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  disableConsoleIamUserMutationOptions,
  enableConsoleIamUserMutationOptions,
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
  it('exposes notification message task and read result shapes through the stable boundary', () => {
    expectTypeOf<BusinessConsoleNotificationMessageItem>().toMatchTypeOf<{
      messageId?: string
      recipientRef?: string
      status?: string
      readAtUtc?: string | null
    }>()
    expectTypeOf<BusinessConsoleNotificationTaskItem>().toMatchTypeOf<{
      taskId?: string
      recipientRef?: string
      status?: string
    }>()
    expectTypeOf<BusinessConsoleMarkNotificationMessageReadResponse>().toMatchTypeOf<{
      messageId?: string
      status?: string
      readAtUtc?: string
    }>()
  })
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
    expect(enableConsoleIamUserMutationOptions).toBeTypeOf('function')
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
    expect(listConsoleNotificationDeadLettersQueryOptions).toBeTypeOf('function')
    expect(getConsoleNotificationDeadLetterQueryOptions).toBeTypeOf('function')
    expect(getConsoleNotificationDeadLetterMetricsQueryOptions).toBeTypeOf('function')
    expect(replayConsoleNotificationDeadLetterMutationOptions).toBeTypeOf('function')
    expect(replayConsoleNotificationDeadLettersMutationOptions).toBeTypeOf('function')
    expect(ignoreConsoleNotificationDeadLetterMutationOptions).toBeTypeOf('function')
    expect(upsertConsoleNotificationPreferenceMutationOptions).toBeTypeOf('function')
    expect(upsertConsoleNotificationSubscriptionMutationOptions).toBeTypeOf('function')
    expect(upsertConsoleNotificationRecipientChannelBindingMutationOptions).toBeTypeOf('function')
  })
})
