import { describe, expect, expectTypeOf, it } from 'vitest'
import * as businessConsoleClient from './business-console'
import type {
  BusinessConsoleApprovalChainResponse,
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
  BusinessConsoleForecastInputItem,
  BusinessConsoleForecastInputItemEnvelope,
  BusinessConsoleForecastInputListEnvelope,
  BusinessConsoleForecastInputListResponse,
  BusinessConsoleMaintenanceAssetReliabilityEnvelope,
  BusinessConsoleMesQualityHoldTimelineItem,
  BusinessConsoleMesQualityHoldTimelineRequest,
  BusinessConsoleMesQualityHoldTimelineResponse,
  BusinessConsoleOpenNcrFromInspectionEnvelope,
  BusinessConsoleOpenNcrFromInspectionRequest,
  BusinessConsoleOpenNcrFromInspectionResponse,
  BusinessConsolePublishSopDocumentRequest,
  BusinessConsolePrincipalWorkContextRequest,
  BusinessConsolePrincipalWorkContextResponse,
  BusinessConsoleRescheduleEngineeringChangeRequest,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSearchResponse,
  BusinessConsoleSetMasterDataResourceEnabledRequest,
  BusinessConsoleTelemetryOeeEnvelope,
  BusinessConsoleTelemetryOeeAggregateBucket,
  BusinessConsoleTelemetryOeeAggregateDegradedReason,
  BusinessConsoleTelemetryOeeAggregateDimension,
  BusinessConsoleTelemetryOeeAggregateEnvelope,
  BusinessConsoleTelemetryOeeAggregateRequest,
  BusinessConsoleTelemetryOeeAggregateResponse,
  BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  BusinessConsoleCreateInspectionRecordFromTaskRequest,
  BusinessConsoleCreateMaintenanceWorkOrderRequest,
  BusinessConsoleMesOperationTaskActionRequest,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleWorkbenchSummaryResponse,
  BusinessConsoleWorkScopeAuthorizationPath,
  CancelBusinessConsolePlanningDemandData,
  QueryBusinessConsoleTelemetryOeeAggregatesData,
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
  GetBusinessConsolePrincipalWorkContextData,
  GetBusinessConsolePrincipalWorkContextErrors,
  ListBusinessConsoleDeviceAssetsData,
  ListBusinessConsolePlanningForecastsData,
  ListBusinessConsoleQualityInspectionRecordsData,
  OpenBusinessConsoleQualityNcrFromInspectionData,
  PreviewBusinessConsoleCodeRuleData,
  PublishBusinessConsoleEngineeringSopDocumentData,
  RescheduleBusinessConsoleEngineeringChangeData,
  ResolveBusinessConsoleEngineeringProductionVersionData,
  RevokeBusinessConsoleSchedulingPlanData,
  SearchBusinessConsoleObjectsData,
} from './business-console'
import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  claimBusinessConsoleMesOperationTaskMutationOptions,
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
  recordBusinessConsoleMesDowntimeEventV2MutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from './business-console'

describe('generated API client contract', () => {
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
    expect(claimBusinessConsoleMesOperationTaskMutationOptions).toBeTypeOf('function')
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
    expect(recordBusinessConsoleMesDowntimeEventV2MutationOptions).toBeTypeOf('function')
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
      'returnBusinessConsoleMesLineSideMaterial',
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
      'createOrUpdateBusinessConsoleTelemetryTagMutationOptions',
      'disableBusinessConsoleTelemetryTagMutationOptions',
      'recordBusinessConsoleTelemetrySampleMutationOptions',
      'listBusinessConsoleTelemetryAlarmsQueryOptions',
      'postBusinessConsoleTelemetryAlarmMutationOptions',
      'queryBusinessConsoleTelemetryDeviceHistoryQueryOptions',
      'queryBusinessConsoleTelemetryOeeQueryOptions',
      'queryBusinessConsoleTelemetryOeeAggregatesQueryOptions',
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
      'getBusinessConsoleSchedulingPlanOverridesQueryOptions',
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
      'createOrUpdateBusinessConsoleTelemetryTag',
      'disableBusinessConsoleTelemetryTag',
      'recordBusinessConsoleTelemetrySample',
      'listBusinessConsoleTelemetryAlarms',
      'postBusinessConsoleTelemetryAlarm',
      'queryBusinessConsoleTelemetryDeviceHistory',
      'queryBusinessConsoleTelemetryOee',
      'queryBusinessConsoleTelemetryOeeAggregates',
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
      'getBusinessConsoleSchedulingPlanOverrides',
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
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateEnvelope>().toBeObject()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateRequest>().toBeObject()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateRequest>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      dimension: 'device' | 'workCenter' | 'line' | 'workshop' | 'shift' | 'day'
      windowStartUtc: string
      windowEndUtc: string
      deviceAssetId?: string | null
      workCenterId?: string | null
      shiftCode?: string | null
      lineCode?: string | null
      workshopCode?: string | null
      businessDate?: string | null
      skip?: number
      take?: number
    }>()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateRequest>().toEqualTypeOf<
      QueryBusinessConsoleTelemetryOeeAggregatesData['query']
    >()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateResponse>().toBeObject()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateBucket>().toBeObject()
    expectTypeOf<QueryBusinessConsoleTelemetryOeeAggregatesData>().toBeObject()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateDimension>().toEqualTypeOf<
      'device' | 'workCenter' | 'line' | 'workshop' | 'shift' | 'day'
    >()
    expectTypeOf<BusinessConsoleTelemetryOeeAggregateDegradedReason>().toEqualTypeOf<
      | 'runtimeStateFactsMissing'
      | 'runtimeStateCoverageIncomplete'
      | 'productionUomAmbiguous'
      | 'productionOutputMissing'
      | 'theoreticalRateMissingOrAmbiguous'
      | 'productiveRuntimeMissing'
      | 'loadingRuntimeMissing'
      | 'historicalDimensionLegacyUnresolved'
      | 'historicalHierarchyMissing'
      | 'historicalTimezoneMissing'
      | 'historicalTimezoneInvalid'
      | 'historicalShiftDefinitionMissing'
      | 'historicalShiftDefinitionInvalid'
      | 'historicalReportOutsideShiftWindow'
      | 'historicalLocalTimeInvalid'
      | 'historicalLocalTimeAmbiguous'
      | 'siteDimensionMissing'
      | 'workshopDimensionMissing'
      | 'lineDimensionMissing'
      | 'siteDimensionAmbiguous'
      | 'workshopDimensionAmbiguous'
      | 'lineDimensionAmbiguous'
      | 'siteTimezoneOrDayBoundaryMissing'
      | 'shiftDefinitionOrBoundaryMissing'
    >()
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
