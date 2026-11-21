import { describe, expect, expectTypeOf, it } from 'vitest'
import * as businessConsoleClient from './business-console'
import type {
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMesCreateMaterialIssueRequest,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMesMaterialIssueRequestListResponse,
  PrevalidateBusinessConsoleMesMaterialScanErrors,
  PrevalidateBusinessConsoleMesContextScanErrors,
} from './generated/business-console/types.gen'
import type {
  BusinessConsoleMesFinishedGoodsInventoryLinkEnvelope,
  BusinessConsoleMesFinishedGoodsInventoryLinkResponse,
  BusinessConsoleAssignWmsResourceRequest,
  BusinessConsoleCompleteWmsInboundOrderRequest,
  BusinessConsoleMesProductionStatisticsBucket,
  BusinessConsoleMesProductionStatisticsDegradedReason,
  BusinessConsoleMesProductionStatisticsDimension,
  BusinessConsoleMesProductionStatisticsRequest,
  BusinessConsoleMesProductionStatisticsResolutionStatus,
  BusinessConsoleMesProductionStatisticsResponse,
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
  QueryBusinessConsoleMesProductionStatisticsData,
  QueryBusinessConsoleMesProductionStatisticsErrors,
  GetBusinessConsoleMesFinishedGoodsReceiptInventoryLinkData,
  GetBusinessConsoleMesWorkOrderTransformationData,
  GetBusinessConsoleMesWorkOrderTransformationErrors,
  GetBusinessConsoleMesWorkOrderTransformationResponse,
  ListBusinessConsoleMesOperationTasksData,
  ListBusinessConsoleWmsCountOperationalCandidatesData,
  ListBusinessConsoleWmsReceiptOperationalCandidatesData,
  ListBusinessConsoleWmsShipmentOperationalCandidatesData,
  MergeBusinessConsoleMesWorkOrdersData,
  MergeBusinessConsoleMesWorkOrdersErrors,
  MergeBusinessConsoleMesWorkOrdersResponse,
  SplitBusinessConsoleMesWorkOrderData,
  SplitBusinessConsoleMesWorkOrderErrors,
  SplitBusinessConsoleMesWorkOrderResponse,
} from './business-console'
import {
  getBusinessConsoleMesWorkOrderTransformationQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  queryBusinessConsoleMesProductionStatisticsQueryOptions,
  mergeBusinessConsoleMesWorkOrdersMutationOptions,
  splitBusinessConsoleMesWorkOrderMutationOptions,
} from './business-console'

describe('generated API client contract', () => {
  it('keeps every documented material-scan transport failure in the generated error union', () => {
    expectTypeOf<keyof PrevalidateBusinessConsoleMesMaterialScanErrors>().toEqualTypeOf<
      400 | 401 | 403 | 502 | 503 | 504
    >()
  })

  it('keeps every documented MES context-scan transport failure in the generated error union', () => {
    expectTypeOf<keyof PrevalidateBusinessConsoleMesContextScanErrors>().toEqualTypeOf<
      400 | 401 | 403 | 502 | 503 | 504
    >()
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

  it('exports MES work-order transformation operations through the stable boundary', () => {
    expectTypeOf<SplitBusinessConsoleMesWorkOrderData>().toBeObject()
    expectTypeOf<MergeBusinessConsoleMesWorkOrdersData>().toBeObject()
    expectTypeOf<GetBusinessConsoleMesWorkOrderTransformationData>().toBeObject()
    expectTypeOf<
      Extract<keyof SplitBusinessConsoleMesWorkOrderErrors, 502 | 503 | 504>
    >().toEqualTypeOf<502 | 503 | 504>()
    expectTypeOf<
      Extract<keyof MergeBusinessConsoleMesWorkOrdersErrors, 502 | 503 | 504>
    >().toEqualTypeOf<502 | 503 | 504>()
    expectTypeOf<
      Extract<keyof GetBusinessConsoleMesWorkOrderTransformationErrors, 502 | 503 | 504>
    >().toEqualTypeOf<502 | 503 | 504>()
    expectTypeOf<SplitBusinessConsoleMesWorkOrderResponse>().toBeObject()
    expectTypeOf<MergeBusinessConsoleMesWorkOrdersResponse>().toBeObject()
    expectTypeOf<GetBusinessConsoleMesWorkOrderTransformationResponse>().toBeObject()
    expectTypeOf(businessConsoleClient.splitBusinessConsoleMesWorkOrder).toBeFunction()
    expectTypeOf(businessConsoleClient.mergeBusinessConsoleMesWorkOrders).toBeFunction()
    expectTypeOf(businessConsoleClient.getBusinessConsoleMesWorkOrderTransformation).toBeFunction()
    expectTypeOf(splitBusinessConsoleMesWorkOrderMutationOptions).toBeFunction()
    expectTypeOf(mergeBusinessConsoleMesWorkOrdersMutationOptions).toBeFunction()
    expectTypeOf(getBusinessConsoleMesWorkOrderTransformationQueryOptions).toBeFunction()
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

  it('keeps exact MES operation-task ids distinct in the generated query contract and key', () => {
    const exactQuery: ListBusinessConsoleMesOperationTasksData['query'] = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-001',
      operationTaskId: 'ot-10',
    }
    const similarQuery: ListBusinessConsoleMesOperationTasksData['query'] = {
      ...exactQuery,
      operationTaskId: 'ot-100',
    }

    const exact = listBusinessConsoleMesOperationTasksQueryOptions({ query: exactQuery })
    const similar = listBusinessConsoleMesOperationTasksQueryOptions({ query: similarQuery })

    expect(exact.key[0]).toMatchObject({
      query: { workOrderId: 'wo-001', operationTaskId: 'ot-10' },
    })
    expect(similar.key[0]).toMatchObject({
      query: { workOrderId: 'wo-001', operationTaskId: 'ot-100' },
    })
    expect(exact.key).not.toEqual(similar.key)
  })

  it('types the MES supplementary material issue contract from OpenAPI', () => {
    const request: NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMesCreateMaterialIssueRequest =
      {
        isSupplementary: true,
        originalMaterialIssueRequestNo: 'MIR-000123',
      }
    const response: NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMesMaterialIssueRequestListResponse =
      {
        supplementaryCount: 1,
        items: [request],
      }

    expect(request.isSupplementary).toBe(true)
    expect(request.originalMaterialIssueRequestNo).toBe('MIR-000123')
    expect(response.supplementaryCount).toBe(1)
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

  it('exports MES production statistics through the stable api-client entry point (#2856)', () => {
    expect(businessConsoleClient.queryBusinessConsoleMesProductionStatistics).toBeTypeOf('function')
    expect(queryBusinessConsoleMesProductionStatisticsQueryOptions).toBeTypeOf('function')
    expectTypeOf<BusinessConsoleMesProductionStatisticsRequest>().toEqualTypeOf<
      QueryBusinessConsoleMesProductionStatisticsData['query']
    >()
    expectTypeOf<BusinessConsoleMesProductionStatisticsDimension>().toEqualTypeOf<
      'day' | 'shift' | 'workCenter' | 'sku'
    >()
    expectTypeOf<BusinessConsoleMesProductionStatisticsResolutionStatus>().toEqualTypeOf<
      'resolved' | 'degraded'
    >()
    expectTypeOf<BusinessConsoleMesProductionStatisticsDegradedReason>().toEqualTypeOf<
      | 'historicalDimensionLegacyUnresolved'
      | 'historicalTimezoneMissing'
      | 'historicalTimezoneInvalid'
      | 'historicalShiftDefinitionMissing'
      | 'historicalShiftDefinitionInvalid'
      | 'historicalReportOutsideShiftWindow'
      | 'historicalLocalTimeInvalid'
      | 'historicalLocalTimeAmbiguous'
      | 'historicalDimensionSnapshotDegraded'
      | 'workCenterMissing'
      | 'nonPositiveTotalOutput'
      | 'deviceMissing'
    >()
    expectTypeOf<BusinessConsoleMesProductionStatisticsResponse>().toBeObject()
    expectTypeOf<BusinessConsoleMesProductionStatisticsBucket>().toBeObject()
    expectTypeOf<keyof QueryBusinessConsoleMesProductionStatisticsErrors>().toEqualTypeOf<
      400 | 401 | 403 | 502
    >()
  })

  it('exports line-side inventory balances through the stable Business Console entry point (#2228)', () => {
    expect(businessConsoleClient.listBusinessConsoleMesLineSideInventoryBalances).toBeTypeOf(
      'function',
    )
    expect(
      businessConsoleClient.listBusinessConsoleMesLineSideInventoryBalancesQueryOptions,
    ).toBeTypeOf('function')
  })
})
