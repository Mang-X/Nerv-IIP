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
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleMesShiftHandoverMutationOptions,
  createBusinessConsoleSkuMutationOptions,
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesProductionPlanReadinessQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesRelatedQualityItemsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
  recordBusinessConsoleMesDefectMutationOptions,
  recordBusinessConsoleMesDowntimeEventMutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from './business-console'
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

  it('exports Business Console generated operations through stable api-client entry points', () => {
    expect(listBusinessConsoleSkusQueryOptions).toBeTypeOf('function')
    expect(createBusinessConsoleSkuMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleInventoryAvailabilityQueryOptions).toBeTypeOf('function')
    expect(postBusinessConsoleInventoryMovementMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleQualityNcrsQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesFoundationReadinessQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesOverviewQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesProductionPlansQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesProductionPlanReadinessQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toBeTypeOf('function')
    expect(releaseBusinessConsoleMesWorkOrderMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesMaterialReadinessQueryOptions).toBeTypeOf('function')
    expect(createBusinessConsoleMesMaterialIssueRequestMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesMaterialIssueRequestsQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesDispatchTasksQueryOptions).toBeTypeOf('function')
    expect(assignBusinessConsoleMesDispatchTaskMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesOperationTasksQueryOptions).toBeTypeOf('function')
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(pauseBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(resumeBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesWipSummaryQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesProductionReportsQueryOptions).toBeTypeOf('function')
    expect(recordBusinessConsoleMesDefectMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesRelatedQualityItemsQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions).toBeTypeOf('function')
    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesDowntimeEventsQueryOptions).toBeTypeOf('function')
    expect(recordBusinessConsoleMesDowntimeEventMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesShiftHandoversQueryOptions).toBeTypeOf('function')
    expect(createBusinessConsoleMesShiftHandoverMutationOptions).toBeTypeOf('function')
    expect(acceptBusinessConsoleMesShiftHandoverMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesWorkOrderTraceabilityQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesBatchTraceabilityQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesMaterialLotTraceabilityQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesCapacityImpactsQueryOptions).toBeTypeOf('function')
  })

  it('exports Business Console WMS generated operations through stable api-client entry points', () => {
    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleWmsOutboundOrdersQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleWmsWcsTasksQueryOptions).toBeTypeOf('function')
  })
})
