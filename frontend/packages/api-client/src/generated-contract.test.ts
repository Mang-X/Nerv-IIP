import { describe, expect, expectTypeOf, it } from 'vitest'
import * as businessConsoleClient from './business-console'
import { client } from './generated/client.gen'
import type { ListConsoleInstancesData } from './generated/types.gen'
import type {
  BusinessConsoleApprovalChainResponse,
  BusinessConsoleAssignQualityInspectionTaskRequest,
  BusinessConsoleAuthorizedWorkScope,
  BusinessConsoleBarcodePrintBatchResponse,
  BusinessConsoleCancelScheduledEngineeringChangeRequest,
  BusinessConsoleConnectorCollectionHealthRequest,
  BusinessConsoleConnectorCollectionHealthResponse,
  BusinessConsoleConnectorTagCoverageItem,
  BusinessConsoleConnectorTagCoverageRequest,
  BusinessConsoleConnectorTagCoverageResponse,
  BusinessConsoleCreateErpPurchaseRequisitionEnvelope,
  BusinessConsoleCreateErpPurchaseRequisitionResponse,
  BusinessConsoleConfigureErpWorkCenterCostRateRequest,
  BusinessConsoleCreateOrUpdateForecastInputRequest,
  BusinessConsoleErpCostCandidateSourceDocumentEnvelope,
  BusinessConsoleErpCostCandidateSourceDocumentResponse,
  BusinessConsoleErpPayableSourceDocumentEnvelope,
  BusinessConsoleErpPayableSourceDocumentResponse,
  BusinessConsoleErpReceivableSourceDocumentEnvelope,
  BusinessConsoleErpReceivableSourceDocumentResponse,
  BusinessConsoleEquipmentHealthFreshness,
  BusinessConsoleEquipmentHealthLevel,
  BusinessConsoleEquipmentHealthResponse,
  BusinessConsoleEquipmentHealthRuleStatus,
  BusinessConsoleForecastInputItem,
  BusinessConsoleForecastInputItemEnvelope,
  BusinessConsoleForecastInputListEnvelope,
  BusinessConsoleForecastInputListResponse,
  BusinessConsoleMaintenanceAssetReliabilityEnvelope,
  BusinessConsoleMesFinishedGoodsInventoryLinkEnvelope,
  BusinessConsoleMesFinishedGoodsInventoryLinkResponse,
  BusinessConsoleMesQualityHoldTimelineItem,
  BusinessConsoleMesQualityHoldTimelineRequest,
  BusinessConsoleMesQualityHoldTimelineResponse,
  BusinessConsoleNotificationMessageItem,
  BusinessConsoleNotificationTaskItem,
  BusinessConsoleMarkNotificationMessageReadResponse,
  BusinessConsoleOpenNcrFromInspectionEnvelope,
  BusinessConsoleOpenNcrFromInspectionRequest,
  BusinessConsoleOpenNcrFromInspectionResponse,
  BusinessConsolePublishSopDocumentRequest,
  BusinessConsolePrincipalWorkContextRequest,
  BusinessConsolePrincipalWorkContextResponse,
  BusinessConsoleReleasedEngineeringVersionEnvelope,
  BusinessConsoleReleasedEngineeringVersionResponse,
  BusinessConsoleRescheduleEngineeringChangeRequest,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSearchResponse,
  BusinessConsoleSetMasterDataResourceEnabledRequest,
  BusinessConsoleTelemetryOeeEnvelope,
  BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  BusinessConsoleAssignWmsResourceRequest,
  BusinessConsoleCompleteWmsInboundOrderRequest,
  BusinessConsoleCreateInspectionRecordFromTaskRequest,
  BusinessConsoleClaimQualityInspectionTaskRequest,
  BusinessConsoleCreateMaintenanceWorkOrderRequest,
  BusinessConsoleMesOperationTaskActionRequest,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleCompleteWmsWarehouseTaskRequest,
  BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
  BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
  BusinessConsoleStartWmsWarehouseTaskRequest,
  BusinessConsoleWmsInboundLineCaptureInput,
  BusinessConsoleWmsInboundLineInput,
  BusinessConsoleWmsLocationCandidate,
  BusinessConsoleWmsLotCandidate,
  BusinessConsoleWmsOperationalCandidatesEnvelope,
  BusinessConsoleWmsOperationalCandidatesResponse,
  BusinessConsoleWmsReceivingQualityGateItem,
  BusinessConsoleWmsWorkScopeCatalog,
  BusinessConsoleWmsWorkScopeCatalogEnvelope,
  BusinessConsoleWorkbenchSummaryResponse,
  BusinessConsoleWorkScopeAuthorizationPath,
  CancelBusinessConsolePlanningDemandData,
  CancelScheduledBusinessConsoleEngineeringChangeData,
  CreateBusinessConsoleErpPurchaseRequisitionFromSuggestionData,
  CreateOrUpdateBusinessConsolePlanningForecastData,
  DownloadBusinessConsoleSopFileContentData,
  GetBusinessConsoleCodeRuleData,
  GetBusinessConsoleCurrentEngineeringSopDocumentsData,
  GetBusinessConsoleEngineeringStandardOperationData,
  GetBusinessConsoleEngineeringDocumentData,
  GetBusinessConsoleEngineeringItemData,
  GetBusinessConsoleEngineeringChangeData,
  GetBusinessConsoleTelemetryConnectorTagCoverageData,
  GetBusinessConsoleErpCostCandidateBySourceDocumentData,
  GetBusinessConsoleErpPayableBySourceDocumentData,
  GetBusinessConsoleErpReceivableBySourceDocumentData,
  GetBusinessConsoleMesFinishedGoodsReceiptInventoryLinkData,
  GetBusinessConsolePrincipalWorkContextData,
  GetBusinessConsolePrincipalWorkContextErrors,
  ListBusinessConsoleDeviceAssetsData,
  ListBusinessConsolePlanningForecastsData,
  ListBusinessConsoleQualityInspectionRecordsData,
  ListBusinessConsoleWmsCountOperationalCandidatesData,
  ListBusinessConsoleWmsReceiptOperationalCandidatesData,
  ListBusinessConsoleWmsShipmentOperationalCandidatesData,
  OpenBusinessConsoleQualityNcrFromInspectionData,
  PreviewBusinessConsoleCodeRuleData,
  PublishBusinessConsoleEngineeringSopDocumentData,
  RescheduleBusinessConsoleEngineeringChangeData,
  ResolveBusinessConsoleEngineeringProductionVersionData,
  RevokeBusinessConsoleSchedulingPlanData,
  SearchBusinessConsoleObjectsData,
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
  getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesProductionReportQueryOptions,
  getBusinessConsolePrincipalWorkContextQueryOptions,
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
  listBusinessConsoleMesReportableOperationTasksQueryOptions,
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
  it('requires optimistic versions for public quality assignment and claim mutations', () => {
    expectTypeOf<
      Pick<BusinessConsoleAssignQualityInspectionTaskRequest, 'expectedVersion'>
    >().toEqualTypeOf<{ expectedVersion: number }>()
    expectTypeOf<
      Pick<BusinessConsoleClaimQualityInspectionTaskRequest, 'expectedVersion'>
    >().toEqualTypeOf<{ expectedVersion: number }>()
  })

  it('requires caller idempotency keys on all governed MAN-625 public write DTOs', () => {
    expectTypeOf<
      Pick<BusinessConsoleMesOperationTaskActionRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
    expectTypeOf<
      Pick<BusinessConsoleRecordProductionReportRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
    expectTypeOf<
      Pick<BusinessConsoleCreateInspectionRecordFromTaskRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
    expectTypeOf<
      Pick<BusinessConsoleCreateMaintenanceWorkOrderRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
    expectTypeOf<
      Pick<BusinessConsoleCompleteMaintenanceWorkOrderRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
  })

  it('requires an explicit MAN-628 scope on MES task actions and production reports', () => {
    type StartOperationOptions = Parameters<
      typeof businessConsoleClient.startBusinessConsoleMesOperationTask
    >[0]

    expectTypeOf<Pick<StartOperationOptions['query'], 'scopeKind' | 'scopeId'>>().toEqualTypeOf<{
      scopeKind: string
      scopeId: string
    }>()
    expectTypeOf<
      Pick<BusinessConsoleRecordProductionReportRequest, 'scopeKind' | 'scopeId'>
    >().toEqualTypeOf<{
      scopeKind: string
      scopeId: string
    }>()
  })

  it('exports the current-principal work-context contract through the stable boundary', () => {
    expectTypeOf<GetBusinessConsolePrincipalWorkContextData>().toBeObject()
    expectTypeOf<
      Pick<
        BusinessConsolePrincipalWorkContextRequest,
        'organizationId' | 'environmentId' | 'permissionCode' | 'scopeKind' | 'scopeId'
      >
    >().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      permissionCode: string
      scopeKind?: string | null
      scopeId?: string | null
    }>()
    expectTypeOf<
      BusinessConsolePrincipalWorkContextResponse['availableScopeKinds']
    >().toEqualTypeOf<string[] | undefined>()
    expectTypeOf<BusinessConsoleAuthorizedWorkScope['authorizationPaths']>().toEqualTypeOf<
      BusinessConsoleWorkScopeAuthorizationPath[] | undefined
    >()
    expectTypeOf<
      Extract<keyof GetBusinessConsolePrincipalWorkContextErrors, 502 | 503>
    >().toEqualTypeOf<502 | 503>()
    expectTypeOf(businessConsoleClient.getBusinessConsolePrincipalWorkContext).toBeFunction()
    expectTypeOf(getBusinessConsolePrincipalWorkContextQueryOptions).toBeFunction()
  })

  it('requires the governed ERP work-center cost-rate effective start', () => {
    expectTypeOf<
      {} extends Pick<BusinessConsoleConfigureErpWorkCenterCostRateRequest, 'effectiveFromUtc'>
        ? true
        : false
    >().toEqualTypeOf<false>()
  })

  it('exposes WMS receiving shelf-life fields through the stable boundary', () => {
    expectTypeOf<
      Pick<BusinessConsoleWmsInboundLineInput, 'productionDate' | 'expiryDate'>
    >().toEqualTypeOf<{
      productionDate?: string | null
      expiryDate?: string | null
    }>()
    expectTypeOf<
      Pick<BusinessConsoleCompleteWmsInboundOrderRequest, 'idempotencyKey' | 'lines'>
    >().toEqualTypeOf<{
      idempotencyKey: string
      lines?: BusinessConsoleWmsInboundLineCaptureInput[] | null
    }>()
    expectTypeOf<Pick<BusinessConsoleWmsInboundLineCaptureInput, 'lineNo'>>().toEqualTypeOf<{
      lineNo: string
    }>()
    expectTypeOf<BusinessConsoleWmsInboundLineCaptureInput>().toEqualTypeOf<{
      lineNo: string
      lotNo?: string | null
      productionDate?: string | null
      expiryDate?: string | null
    }>()
    expectTypeOf<
      Pick<BusinessConsoleWmsReceivingQualityGateItem, 'productionDate' | 'expiryDate'>
    >().toEqualTypeOf<{
      productionDate?: string | null
      expiryDate?: string | null
    }>()
  })

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
    expect(listBusinessConsoleMesReportableOperationTasksQueryOptions).toBeTypeOf('function')
    expect(businessConsoleClient.listBusinessConsoleMesReportableOperationTasks).toBeTypeOf(
      'function',
    )
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(pauseBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(resumeBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesWipSummaryQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesProductionReportsQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesProductionReportQueryOptions).toBeTypeOf('function')
    expect(recordBusinessConsoleMesDefectMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesRelatedQualityItemsQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions).toBeTypeOf(
      'function',
    )
    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toBeTypeOf(
      'function',
    )
    expect(listBusinessConsoleMesDowntimeEventsQueryOptions).toBeTypeOf('function')
    expect(recordBusinessConsoleMesDowntimeEventMutationOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesShiftHandoversQueryOptions).toBeTypeOf('function')
    expect(createBusinessConsoleMesShiftHandoverMutationOptions).toBeTypeOf('function')
    expect(acceptBusinessConsoleMesShiftHandoverMutationOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesWorkOrderTraceabilityQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesBatchTraceabilityQueryOptions).toBeTypeOf('function')
    expect(getBusinessConsoleMesMaterialLotTraceabilityQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleMesCapacityImpactsQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleWmsOutboundOrdersQueryOptions).toBeTypeOf('function')
    expect(listBusinessConsoleWmsWcsTasksQueryOptions).toBeTypeOf('function')
  })

  it('exports lifecycle-conflict SDK operations and mutation options through the stable boundary', () => {
    const operations = [
      'startBusinessConsoleMesOperationTask',
      'pauseBusinessConsoleMesOperationTask',
      'resumeBusinessConsoleMesOperationTask',
      'completeBusinessConsoleMesOperationTask',
      'releaseBusinessConsoleMesWorkOrder',
      'holdBusinessConsoleMesWorkOrder',
      'cancelBusinessConsoleMesWorkOrder',
      'recordBusinessConsoleMesProductionReport',
      'confirmBusinessConsoleMesLineSideMaterialReceipt',
      'completeBusinessConsoleWmsInboundOrder',
      'completeBusinessConsoleWmsOutboundOrder',
      'completeBusinessConsoleWmsCountExecution',
      'startBusinessConsoleWmsPutawayTask',
      'recordBusinessConsoleWmsPutawayTaskProgress',
      'reportBusinessConsoleWmsPutawayTaskException',
      'completeBusinessConsoleWmsPutawayTask',
      'startBusinessConsoleWmsPickingTask',
      'recordBusinessConsoleWmsPickingTaskProgress',
      'reportBusinessConsoleWmsPickingTaskException',
      'completeBusinessConsoleWmsPickingTask',
      'createBusinessConsoleQualityInspectionRecordFromTask',
      'submitBusinessConsoleQualityNcrDisposition',
      'closeBusinessConsoleQualityNcr',
      'completeBusinessConsoleMaintenanceWorkOrder',
      'acknowledgeBusinessConsoleEquipmentAlarm',
      'shelveBusinessConsoleEquipmentAlarm',
      'unshelveBusinessConsoleEquipmentAlarm',
    ] as const

    for (const operation of operations) {
      expect((businessConsoleClient as Record<string, unknown>)[operation], operation).toBeTypeOf(
        'function',
      )
      expect(
        (businessConsoleClient as Record<string, unknown>)[`${operation}MutationOptions`],
        `${operation}MutationOptions`,
      ).toBeTypeOf('function')
    }
  })

  it('exports the MAN-629 WMS action request contracts through the stable boundary', () => {
    const governedOperations = [
      'getBusinessConsoleWmsReceiptWorkScopes',
      'getBusinessConsoleWmsShipmentWorkScopes',
      'getBusinessConsoleWmsCountWorkScopes',
      'listBusinessConsoleWmsReceiptOperationalCandidates',
      'listBusinessConsoleWmsShipmentOperationalCandidates',
      'listBusinessConsoleWmsCountOperationalCandidates',
      'assignBusinessConsoleWmsInboundOrder',
      'assignBusinessConsoleWmsPutawayTask',
      'assignBusinessConsoleWmsOutboundOrder',
      'assignBusinessConsoleWmsPickingTask',
      'assignBusinessConsoleWmsCountExecution',
    ] as const

    for (const operation of governedOperations) {
      expect((businessConsoleClient as Record<string, unknown>)[operation], operation).toBeTypeOf(
        'function',
      )
    }
    expect(businessConsoleClient.getBusinessConsoleWmsReceiptWorkScopesQueryOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.getBusinessConsoleWmsShipmentWorkScopesQueryOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.getBusinessConsoleWmsCountWorkScopesQueryOptions).toBeTypeOf(
      'function',
    )
    expect(
      businessConsoleClient.listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
    ).toBeTypeOf('function')
    expect(
      businessConsoleClient.listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
    ).toBeTypeOf('function')
    expect(
      businessConsoleClient.listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
    ).toBeTypeOf('function')
    expect(businessConsoleClient.assignBusinessConsoleWmsInboundOrderMutationOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.assignBusinessConsoleWmsPutawayTaskMutationOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.assignBusinessConsoleWmsOutboundOrderMutationOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.assignBusinessConsoleWmsPickingTaskMutationOptions).toBeTypeOf(
      'function',
    )
    expect(businessConsoleClient.assignBusinessConsoleWmsCountExecutionMutationOptions).toBeTypeOf(
      'function',
    )
    expectTypeOf<
      Pick<
        BusinessConsoleAssignWmsResourceRequest,
        'poolCode' | 'operatorPrincipalId' | 'idempotencyKey' | 'expectedVersion'
      >
    >().toEqualTypeOf<{
      poolCode: string
      operatorPrincipalId?: string | null
      idempotencyKey: string
      expectedVersion: number
    }>()
    expectTypeOf<BusinessConsoleWmsWorkScopeCatalogEnvelope['data']>().toEqualTypeOf<
      BusinessConsoleWmsWorkScopeCatalog | null | undefined
    >()
    expectTypeOf<ListBusinessConsoleWmsReceiptOperationalCandidatesData['query']>().toEqualTypeOf<
      ListBusinessConsoleWmsShipmentOperationalCandidatesData['query']
    >()
    expectTypeOf<ListBusinessConsoleWmsCountOperationalCandidatesData['query']>().toEqualTypeOf<
      ListBusinessConsoleWmsReceiptOperationalCandidatesData['query']
    >()
    expectTypeOf<
      Pick<
        ListBusinessConsoleWmsReceiptOperationalCandidatesData['query'],
        'organizationId' | 'environmentId' | 'scopeKind' | 'scopeId' | 'skuCode' | 'locationCode'
      >
    >().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      scopeKind?: string | null
      scopeId?: string | null
      skuCode?: string | null
      locationCode?: string | null
    }>()
    expectTypeOf<BusinessConsoleWmsOperationalCandidatesEnvelope['data']>().toEqualTypeOf<
      BusinessConsoleWmsOperationalCandidatesResponse | null | undefined
    >()
    expectTypeOf<BusinessConsoleWmsOperationalCandidatesResponse['locations']>().toEqualTypeOf<
      BusinessConsoleWmsLocationCandidate[] | undefined
    >()
    expectTypeOf<BusinessConsoleWmsOperationalCandidatesResponse['lots']>().toEqualTypeOf<
      BusinessConsoleWmsLotCandidate[] | undefined
    >()
    expectTypeOf<
      Pick<BusinessConsoleStartWmsWarehouseTaskRequest, 'idempotencyKey' | 'expectedVersion'>
    >().toEqualTypeOf<{
      idempotencyKey: string
      expectedVersion: number
    }>()
    expectTypeOf<
      Pick<
        BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
        'idempotencyKey' | 'expectedVersion' | 'executedQuantity'
      >
    >().toEqualTypeOf<{
      idempotencyKey: string
      expectedVersion: number
      executedQuantity: number
    }>()
    expectTypeOf<
      Pick<
        BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
        'idempotencyKey' | 'expectedVersion' | 'exceptionCode' | 'reason'
      >
    >().toEqualTypeOf<{
      idempotencyKey: string
      expectedVersion: number
      exceptionCode: string
      reason: string
    }>()
    expectTypeOf<
      Pick<
        BusinessConsoleCompleteWmsWarehouseTaskRequest,
        'idempotencyKey' | 'expectedVersion' | 'executedQuantity' | 'differenceReason'
      >
    >().toEqualTypeOf<{
      idempotencyKey: string
      expectedVersion: number
      executedQuantity: number
      differenceReason?: string | null
    }>()
  })

  it('exposes the MAN-528 exact MES to Inventory link through the stable boundary', () => {
    expect(businessConsoleClient.getBusinessConsoleMesFinishedGoodsReceiptInventoryLink).toBeTypeOf(
      'function',
    )
    expectTypeOf<
      GetBusinessConsoleMesFinishedGoodsReceiptInventoryLinkData['path']
    >().toEqualTypeOf<{
      requestNo: string
    }>()
    expectTypeOf<BusinessConsoleMesFinishedGoodsInventoryLinkEnvelope['data']>().toEqualTypeOf<
      BusinessConsoleMesFinishedGoodsInventoryLinkResponse | null | undefined
    >()
  })

  it('exports the required equipment health contract and controlled vocabularies', () => {
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['organizationId']>().toEqualTypeOf<string>()
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['environmentId']>().toEqualTypeOf<string>()
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['deviceAssetId']>().toEqualTypeOf<string>()
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['healthScore']>().toEqualTypeOf<number>()
    expectTypeOf<
      BusinessConsoleEquipmentHealthResponse['level']
    >().toEqualTypeOf<BusinessConsoleEquipmentHealthLevel>()
    expectTypeOf<
      BusinessConsoleEquipmentHealthResponse['calculatedAtUtc']
    >().toEqualTypeOf<string>()
    expectTypeOf<
      BusinessConsoleEquipmentHealthResponse['dataFreshness']['status']
    >().toEqualTypeOf<BusinessConsoleEquipmentHealthFreshness>()
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['riskFactors']>().toBeArray()
    expectTypeOf<BusinessConsoleEquipmentHealthResponse['ruleEvaluations']>().toBeArray()
    expectTypeOf<BusinessConsoleEquipmentHealthLevel>().toEqualTypeOf<
      'healthy' | 'watch' | 'warning' | 'critical'
    >()
    expectTypeOf<BusinessConsoleEquipmentHealthFreshness>().toEqualTypeOf<
      'fresh' | 'delayed' | 'stale' | 'unavailable'
    >()
    expectTypeOf<BusinessConsoleEquipmentHealthRuleStatus>().toEqualTypeOf<
      'normal' | 'risk' | 'accumulating'
    >()
  })

  it('exports released engineering version identity through the stable boundary', () => {
    expectTypeOf<BusinessConsoleReleasedEngineeringVersionResponse['id']>().toEqualTypeOf<
      string | undefined
    >()
    expectTypeOf<BusinessConsoleReleasedEngineeringVersionResponse['versionId']>().toEqualTypeOf<
      string | undefined
    >()
    expectTypeOf<BusinessConsoleReleasedEngineeringVersionEnvelope['data']>().toEqualTypeOf<
      BusinessConsoleReleasedEngineeringVersionResponse | null | undefined
    >()
  })

  it('exports deep Business Console generated capabilities through stable api-client entry points', () => {
    const expectedFunctions = [
      'getBusinessConsoleWorkbenchSummaryQueryOptions',
      'searchBusinessConsoleObjectsQueryOptions',
      'listBusinessConsoleBarcodeRulesQueryOptions',
      'createOrUpdateBusinessConsoleBarcodeRuleMutationOptions',
      'listBusinessConsoleBarcodeTemplatesQueryOptions',
      'createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions',
      'listBusinessConsoleBarcodePrintBatchesQueryOptions',
      'createBusinessConsoleBarcodePrintBatchMutationOptions',
      'getBusinessConsoleBarcodePrintBatchQueryOptions',
      'listBusinessConsoleBarcodeScansQueryOptions',
      'recordBusinessConsoleBarcodeScanMutationOptions',
      'listBusinessConsoleApprovalTemplatesQueryOptions',
      'createOrUpdateBusinessConsoleApprovalTemplateMutationOptions',
      'listBusinessConsoleApprovalChainsQueryOptions',
      'startBusinessConsoleApprovalChainMutationOptions',
      'getBusinessConsoleApprovalChainQueryOptions',
      'listBusinessConsoleApprovalTasksQueryOptions',
      'listBusinessConsoleApprovalDecisionsQueryOptions',
      'resolveBusinessConsoleApprovalStepMutationOptions',
      'listBusinessConsoleApprovalDelegationsQueryOptions',
      'createBusinessConsoleApprovalDelegationMutationOptions',
      'revokeBusinessConsoleApprovalDelegationMutationOptions',
      'listBusinessConsoleTelemetryTagsQueryOptions',
      'listBusinessConsoleTelemetryAlarmRulesQueryOptions',
      'createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions',
      'recordBusinessConsoleTelemetrySampleMutationOptions',
      'listBusinessConsoleTelemetryAlarmsQueryOptions',
      'postBusinessConsoleTelemetryAlarmMutationOptions',
      'queryBusinessConsoleTelemetryDeviceHistoryQueryOptions',
      'queryBusinessConsoleTelemetryOeeQueryOptions',
      'queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions',
      'queryBusinessConsoleTelemetryRuntimeHoursQueryOptions',
      'listBusinessConsoleNotificationMessagesQueryOptions',
      'listBusinessConsoleNotificationTasksQueryOptions',
      'markBusinessConsoleNotificationMessageReadMutationOptions',
      'previewBusinessConsoleSchedulingPlanMutationOptions',
      'listBusinessConsoleSchedulingPlansQueryOptions',
      'createBusinessConsoleSchedulingPlanMutationOptions',
      'getBusinessConsoleSchedulingPlanQueryOptions',
      'getBusinessConsoleSchedulingPlanGanttQueryOptions',
      'releaseBusinessConsoleSchedulingPlanMutationOptions',
      'revokeBusinessConsoleSchedulingPlanMutationOptions',
      'listBusinessConsoleMaintenanceSparePartsQueryOptions',
      'createBusinessConsoleMaintenanceSparePartMutationOptions',
      'queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions',
      'queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions',
      'getBusinessConsoleEngineeringBomQueryOptions',
      'getBusinessConsoleEngineeringManufacturingBomQueryOptions',
      'getBusinessConsoleEngineeringRoutingQueryOptions',
      'getBusinessConsoleEngineeringStandardOperationQueryOptions',
      'getBusinessConsoleEngineeringDocumentQueryOptions',
      'getBusinessConsoleEngineeringItemQueryOptions',
      'getBusinessConsoleEngineeringChangeQueryOptions',
      'resolveBusinessConsoleEngineeringProductionVersionQueryOptions',
      'cancelBusinessConsolePlanningDemandMutationOptions',
      'getBusinessConsoleWorkbenchSummary',
      'searchBusinessConsoleObjects',
      'listBusinessConsoleBarcodeRules',
      'createOrUpdateBusinessConsoleBarcodeRule',
      'listBusinessConsoleBarcodeTemplates',
      'createOrUpdateBusinessConsoleBarcodeTemplate',
      'listBusinessConsoleBarcodePrintBatches',
      'createBusinessConsoleBarcodePrintBatch',
      'getBusinessConsoleBarcodePrintBatch',
      'listBusinessConsoleBarcodeScans',
      'recordBusinessConsoleBarcodeScan',
      'listBusinessConsoleApprovalTemplates',
      'createOrUpdateBusinessConsoleApprovalTemplate',
      'listBusinessConsoleApprovalChains',
      'startBusinessConsoleApprovalChain',
      'getBusinessConsoleApprovalChain',
      'listBusinessConsoleApprovalTasks',
      'listBusinessConsoleApprovalDecisions',
      'resolveBusinessConsoleApprovalStep',
      'listBusinessConsoleApprovalDelegations',
      'createBusinessConsoleApprovalDelegation',
      'revokeBusinessConsoleApprovalDelegation',
      'listBusinessConsoleTelemetryTags',
      'listBusinessConsoleTelemetryAlarmRules',
      'createOrUpdateBusinessConsoleTelemetryAlarmRule',
      'recordBusinessConsoleTelemetrySample',
      'listBusinessConsoleTelemetryAlarms',
      'postBusinessConsoleTelemetryAlarm',
      'queryBusinessConsoleTelemetryDeviceHistory',
      'queryBusinessConsoleTelemetryOee',
      'queryBusinessConsoleTelemetryRuntimeAvailability',
      'queryBusinessConsoleTelemetryRuntimeHours',
      'listBusinessConsoleNotificationMessages',
      'listBusinessConsoleNotificationTasks',
      'markBusinessConsoleNotificationMessageRead',
      'previewBusinessConsoleSchedulingPlan',
      'listBusinessConsoleSchedulingPlans',
      'createBusinessConsoleSchedulingPlan',
      'getBusinessConsoleSchedulingPlan',
      'getBusinessConsoleSchedulingPlanGantt',
      'releaseBusinessConsoleSchedulingPlan',
      'revokeBusinessConsoleSchedulingPlan',
      'listBusinessConsoleMaintenanceSpareParts',
      'createBusinessConsoleMaintenanceSparePart',
      'queryBusinessConsoleMaintenanceAssetReliability',
      'queryBusinessConsoleMaintenanceAvailabilityWindows',
      'getBusinessConsoleEngineeringBom',
      'getBusinessConsoleEngineeringManufacturingBom',
      'getBusinessConsoleEngineeringRouting',
      'cancelBusinessConsolePlanningDemand',
      'getBusinessConsoleEngineeringStandardOperation',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })

  it('exports wave2 refreshed Business Console operations through stable api-client entry points', () => {
    const expectedFunctions = [
      'cancelScheduledBusinessConsoleEngineeringChangeMutationOptions',
      'rescheduleBusinessConsoleEngineeringChangeMutationOptions',
      'publishBusinessConsoleEngineeringSopDocumentMutationOptions',
      'getBusinessConsoleCurrentEngineeringSopDocumentsQueryOptions',
      'downloadBusinessConsoleSopFileContentQueryOptions',
      'createOrUpdateBusinessConsolePlanningForecastMutationOptions',
      'listBusinessConsolePlanningForecastsQueryOptions',
      'listBusinessConsoleQualityInspectionRecordsQueryOptions',
      'openBusinessConsoleQualityNcrFromInspectionMutationOptions',
      'listBusinessConsoleDeviceAssetsQueryOptions',
      'getBusinessConsoleCodeRuleQueryOptions',
      'previewBusinessConsoleCodeRuleMutationOptions',
      'createBusinessConsoleErpPurchaseRequisitionFromSuggestionMutationOptions',
      'getBusinessConsoleErpCostCandidateBySourceDocumentQueryOptions',
      'getBusinessConsoleErpPayableBySourceDocumentQueryOptions',
      'getBusinessConsoleErpReceivableBySourceDocumentQueryOptions',
      'cancelScheduledBusinessConsoleEngineeringChange',
      'rescheduleBusinessConsoleEngineeringChange',
      'publishBusinessConsoleEngineeringSopDocument',
      'getBusinessConsoleCurrentEngineeringSopDocuments',
      'downloadBusinessConsoleSopFileContent',
      'createOrUpdateBusinessConsolePlanningForecast',
      'listBusinessConsolePlanningForecasts',
      'listBusinessConsoleQualityInspectionRecords',
      'openBusinessConsoleQualityNcrFromInspection',
      'listBusinessConsoleDeviceAssets',
      'getBusinessConsoleCodeRule',
      'previewBusinessConsoleCodeRule',
      'createBusinessConsoleErpPurchaseRequisitionFromSuggestion',
      'getBusinessConsoleErpCostCandidateBySourceDocument',
      'getBusinessConsoleErpPayableBySourceDocument',
      'getBusinessConsoleErpReceivableBySourceDocument',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })

  it('exports Quality reinspection through stable api-client entry points (#954)', () => {
    const expectedFunctions = [
      'createBusinessConsoleQualityReinspectionMutationOptions',
      'createBusinessConsoleQualityReinspection',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })

  it('exports gateway facade backfill (#833-#838) Business Console operations through stable api-client entry points', () => {
    const expectedFunctions = [
      'holdBusinessConsoleMesWorkOrderMutationOptions',
      'cancelBusinessConsoleMesWorkOrderMutationOptions',
      'forceReleaseBusinessConsoleMesQualityHoldMutationOptions',
      'reverseBusinessConsoleMesProductionReportMutationOptions',
      'retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions',
      'listBusinessConsoleInventoryExpiryAlertsQueryOptions',
      'listBusinessConsoleQualityInspectionTasksQueryOptions',
      'createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions',
      'listBusinessConsoleWmsReceivingQualityGatesQueryOptions',
      'listBusinessConsoleWmsSupplierReturnRequestsQueryOptions',
      'createBusinessConsoleTelemetryDeviceControlCommandMutationOptions',
      'holdBusinessConsoleMesWorkOrder',
      'cancelBusinessConsoleMesWorkOrder',
      'forceReleaseBusinessConsoleMesQualityHold',
      'reverseBusinessConsoleMesProductionReport',
      'retryBusinessConsoleMesFinishedGoodsReceiptInventoryPosting',
      'listBusinessConsoleInventoryExpiryAlerts',
      'listBusinessConsoleQualityInspectionTasks',
      'createBusinessConsoleQualityInspectionRecordFromTask',
      'listBusinessConsoleWmsReceivingQualityGates',
      'listBusinessConsoleWmsSupplierReturnRequests',
      'createBusinessConsoleTelemetryDeviceControlCommand',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })

  it('exports device-control command result/history read-face (#842) Business Console operations through stable api-client entry points', () => {
    const expectedFunctions = [
      'getBusinessConsoleTelemetryDeviceControlCommandQueryOptions',
      'listBusinessConsoleTelemetryDeviceControlCommandsQueryOptions',
      'getBusinessConsoleTelemetryDeviceControlCommand',
      'listBusinessConsoleTelemetryDeviceControlCommands',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })

  it('exports connector observability and quality hold lifecycle facades through stable api-client entry points', () => {
    const expectedFunctions = [
      'queryBusinessConsoleTelemetryConnectorCollectionHealth',
      'queryBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions',
      'getBusinessConsoleTelemetryConnectorTagCoverage',
      'getBusinessConsoleTelemetryConnectorTagCoverageQueryOptions',
      'getBusinessConsoleMesQualityHoldTimeline',
      'getBusinessConsoleMesQualityHoldTimelineQueryOptions',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }

    expectTypeOf<BusinessConsoleConnectorCollectionHealthRequest>().toBeObject()
    expectTypeOf<BusinessConsoleConnectorCollectionHealthResponse>().toBeObject()
    expectTypeOf<BusinessConsoleConnectorTagCoverageRequest>().toBeObject()
    expectTypeOf<BusinessConsoleConnectorTagCoverageResponse>().toBeObject()
    expectTypeOf<BusinessConsoleConnectorTagCoverageItem>().toBeObject()
    expectTypeOf<GetBusinessConsoleTelemetryConnectorTagCoverageData>().toBeObject()
    expectTypeOf<BusinessConsoleMesQualityHoldTimelineRequest>().toBeObject()
    expectTypeOf<BusinessConsoleMesQualityHoldTimelineResponse>().toBeObject()
    expectTypeOf<BusinessConsoleMesQualityHoldTimelineItem>().toBeObject()
    expectTypeOf<
      Pick<BusinessConsoleSetMasterDataResourceEnabledRequest, 'idempotencyKey'>
    >().toEqualTypeOf<{ idempotencyKey: string }>()
  })

  it('exports wave2 refreshed Business Console request-payload Data types', () => {
    // Importing each alias already guards against removal (src/**/*.ts is
    // typechecked); the assertions additionally pin the exported shape.
    expectTypeOf<CancelScheduledBusinessConsoleEngineeringChangeData>().toBeObject()
    expectTypeOf<RescheduleBusinessConsoleEngineeringChangeData>().toBeObject()
    expectTypeOf<PublishBusinessConsoleEngineeringSopDocumentData>().toBeObject()
    expectTypeOf<GetBusinessConsoleCurrentEngineeringSopDocumentsData>().toBeObject()
    expectTypeOf<DownloadBusinessConsoleSopFileContentData>().toBeObject()
    expectTypeOf<CreateOrUpdateBusinessConsolePlanningForecastData>().toBeObject()
    expectTypeOf<ListBusinessConsolePlanningForecastsData>().toBeObject()
    expectTypeOf<ListBusinessConsoleQualityInspectionRecordsData>().toBeObject()
    expectTypeOf<OpenBusinessConsoleQualityNcrFromInspectionData>().toBeObject()
    expectTypeOf<ListBusinessConsoleDeviceAssetsData>().toBeObject()
    expectTypeOf<GetBusinessConsoleCodeRuleData>().toBeObject()
    expectTypeOf<PreviewBusinessConsoleCodeRuleData>().toBeObject()
    expectTypeOf<CreateBusinessConsoleErpPurchaseRequisitionFromSuggestionData>().toBeObject()
    expectTypeOf<GetBusinessConsoleErpCostCandidateBySourceDocumentData>().toBeObject()
    expectTypeOf<GetBusinessConsoleErpPayableBySourceDocumentData>().toBeObject()
    expectTypeOf<GetBusinessConsoleErpReceivableBySourceDocumentData>().toBeObject()
    expectTypeOf<RevokeBusinessConsoleSchedulingPlanData>().toBeObject()
  })

  it('exports wave2 refreshed Business Console request/response DTO aliases', () => {
    expectTypeOf<BusinessConsoleCancelScheduledEngineeringChangeRequest>().toBeObject()
    expectTypeOf<BusinessConsoleRescheduleEngineeringChangeRequest>().toBeObject()
    expectTypeOf<BusinessConsolePublishSopDocumentRequest>().toBeObject()
    expectTypeOf<BusinessConsoleCreateOrUpdateForecastInputRequest>().toBeObject()
    expectTypeOf<BusinessConsoleForecastInputItem>().toBeObject()
    expectTypeOf<BusinessConsoleForecastInputListResponse>().toBeObject()
    expectTypeOf<BusinessConsoleOpenNcrFromInspectionRequest>().toBeObject()
    expectTypeOf<BusinessConsoleOpenNcrFromInspectionResponse>().toBeObject()
    expectTypeOf<BusinessConsoleCreateErpPurchaseRequisitionResponse>().toBeObject()
    expectTypeOf<BusinessConsoleErpCostCandidateSourceDocumentResponse>().toBeObject()
    expectTypeOf<BusinessConsoleErpPayableSourceDocumentResponse>().toBeObject()
    expectTypeOf<BusinessConsoleErpReceivableSourceDocumentResponse>().toBeObject()
  })

  it('exports wave2 refreshed Business Console response envelope aliases', () => {
    expectTypeOf<BusinessConsoleCreateErpPurchaseRequisitionEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleErpCostCandidateSourceDocumentEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleErpPayableSourceDocumentEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleErpReceivableSourceDocumentEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleForecastInputItemEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleForecastInputListEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleOpenNcrFromInspectionEnvelope>().toBeObject()
  })

  it('exports stable Business Console deep capability types', () => {
    expectTypeOf<BusinessConsoleWorkbenchSummaryResponse>().toBeObject()
    expectTypeOf<BusinessConsoleSearchResponse>().toBeObject()
    expectTypeOf<BusinessConsoleBarcodePrintBatchResponse>().toBeObject()
    expectTypeOf<BusinessConsoleApprovalChainResponse>().toBeObject()
    expectTypeOf<BusinessConsoleTelemetryOeeEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleSchedulingPlanSummaryResponse>().toBeObject()
    expectTypeOf<BusinessConsoleMaintenanceAssetReliabilityEnvelope>().toBeObject()
    expectTypeOf<CancelBusinessConsolePlanningDemandData>().toBeObject()
    expectTypeOf<GetBusinessConsoleEngineeringStandardOperationData>().toBeObject()
    expectTypeOf<GetBusinessConsoleEngineeringDocumentData>().toBeObject()
    expectTypeOf<GetBusinessConsoleEngineeringItemData>().toBeObject()
    expectTypeOf<GetBusinessConsoleEngineeringChangeData>().toBeObject()
    expectTypeOf<ResolveBusinessConsoleEngineeringProductionVersionData>().toBeObject()
    expectTypeOf<SearchBusinessConsoleObjectsData>().toBeObject()
  })
})
