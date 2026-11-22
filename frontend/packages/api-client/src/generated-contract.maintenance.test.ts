import { describe, expect, expectTypeOf, it } from 'vitest'
import * as businessConsoleClient from './business-console'
import type {
  CreateBusinessConsoleMaintenanceWorkOrderData,
  CreateBusinessConsoleMaintenanceWorkOrderV2Data,
  NetCorePalExtensionsDtoResponseData,
  RecordBusinessConsoleMesDowntimeEventErrors,
} from './generated/business-console/types.gen'
import type {
  BusinessConsoleMesRecordDowntimeEventRequest,
  BusinessConsoleMesRecordDowntimeEventV2Request,
  BusinessConsoleCreateMaintenanceWorkOrderV2Request,
  BusinessConsoleCreateMaintenanceWorkOrderRequest,
} from './business-console'
import {
  assignBusinessConsoleMaintenanceWorkOrder,
  assignBusinessConsoleMaintenanceWorkOrderMutationOptions,
  transitionBusinessConsoleMaintenanceWorkOrder,
  transitionBusinessConsoleMaintenanceWorkOrderMutationOptions,
  type AssignBusinessConsoleMaintenanceWorkOrderData,
  type BusinessConsoleAssignMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenanceWorkOrderAction,
  type BusinessConsoleMaintenanceWorkOrderActionResponse,
  type BusinessConsoleMaintenanceWorkOrderLifecycleEventItem,
  type BusinessConsoleTransitionMaintenanceWorkOrderRequest,
  type TransitionBusinessConsoleMaintenanceWorkOrderData,
} from './business-console'

describe('generated API client contract', () => {
  it('keeps the legacy downtime request shape separate from the strict v2 context contract', () => {
    expectTypeOf<BusinessConsoleMesRecordDowntimeEventRequest>().toEqualTypeOf<{
      organizationId?: string
      environmentId?: string
      workOrderId?: string
      operationTaskId?: string | null
      deviceAssetId?: string | null
      reasonCode?: string
      startedAtUtc?: string
      idempotencyKey?: string
    }>()
    expectTypeOf<BusinessConsoleMesRecordDowntimeEventV2Request>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      workOrderId: string
      operationTaskId?: string | null
      workCenterId: string
      deviceAssetId?: string | null
      reasonCode: string
      startedAtUtc: string
      idempotencyKey: string
      scopeKind: string
      scopeId: string
      toUtc?: string | null
    }>()
  })

  it('keeps the v1 free-text maintenance create request separate from the v2 reason-code contract', () => {
    expectTypeOf<BusinessConsoleCreateMaintenanceWorkOrderRequest>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      deviceAssetId: string
      priority: string
      sourceAlarmId?: string | null
      openedBy?: string
      idempotencyKey: string
      assetUnavailableReason?: string | null
      assignedTechnicianUserId?: string | null
      estimatedLaborMinutes?: number | null
      expectedRestoreAtUtc?: string | null
    }>()
    expectTypeOf<BusinessConsoleCreateMaintenanceWorkOrderV2Request>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      deviceAssetId: string
      priority: string
      sourceAlarmId?: string | null
      openedBy?: string
      idempotencyKey: string
      assetUnavailableReasonCode?: string | null
      assignedTechnicianUserId?: string | null
      estimatedLaborMinutes?: number | null
      expectedRestoreAtUtc?: string | null
    }>()
  })

  // 由 vue-tsc typecheck 承担（vitest 不求值 expectTypeOf）：v2 与 v1 必须对称地在公开契约里
  // 声明标准 Idempotency-Key 头，否则 #2970 的 PDA 只能深层导入或手搓。
  it('declares the standard Idempotency-Key header on both maintenance create versions', () => {
    expectTypeOf<CreateBusinessConsoleMaintenanceWorkOrderV2Data['headers']>().toEqualTypeOf<
      CreateBusinessConsoleMaintenanceWorkOrderData['headers']
    >()
    expectTypeOf<
      NonNullable<CreateBusinessConsoleMaintenanceWorkOrderV2Data['headers']>['Idempotency-Key']
    >().toEqualTypeOf<string | undefined>()
  })

  it('exposes the maintenance v2 create mutation from the stable entry point', () => {
    expect(
      typeof businessConsoleClient.createBusinessConsoleMaintenanceWorkOrderMutationOptions,
    ).toBe('function')
    expect(
      typeof businessConsoleClient.createBusinessConsoleMaintenanceWorkOrderV2MutationOptions,
    ).toBe('function')
  })

  it('exposes the v1 downtime fail-closed 400 response in generated contracts', () => {
    type HasV1DowntimeBadRequest = RecordBusinessConsoleMesDowntimeEventErrors extends {
      400: NetCorePalExtensionsDtoResponseData
    }
      ? true
      : false

    expectTypeOf<HasV1DowntimeBadRequest>().toEqualTypeOf<true>()
  })

  it('exports the maintenance lifecycle API through the stable business-console barrel', () => {
    expect(assignBusinessConsoleMaintenanceWorkOrder).toBeTypeOf('function')
    expect(assignBusinessConsoleMaintenanceWorkOrderMutationOptions).toBeTypeOf('function')
    expect(transitionBusinessConsoleMaintenanceWorkOrder).toBeTypeOf('function')
    expect(transitionBusinessConsoleMaintenanceWorkOrderMutationOptions).toBeTypeOf('function')
    expectTypeOf<BusinessConsoleMaintenanceWorkOrderAction>().toEqualTypeOf<
      | 'accept'
      | 'start'
      | 'pause'
      | 'waitForParts'
      | 'resume'
      | 'complete'
      | 'verify'
      | 'close'
      | 'cancel'
      | 'updateExpectedRestore'
    >()
    expectTypeOf<BusinessConsoleAssignMaintenanceWorkOrderRequest>().toMatchTypeOf<
      AssignBusinessConsoleMaintenanceWorkOrderData['body']
    >()
    expectTypeOf<BusinessConsoleTransitionMaintenanceWorkOrderRequest>().toMatchTypeOf<
      TransitionBusinessConsoleMaintenanceWorkOrderData['body']
    >()
    expectTypeOf<
      BusinessConsoleTransitionMaintenanceWorkOrderRequest['expectedRestoreAtUtc']
    >().toEqualTypeOf<string | null | undefined>()
    expectTypeOf<
      businessConsoleClient.EquipmentRuntimeAvailabilityWindow['expectedRestoreAtUtc']
    >().toEqualTypeOf<string | null | undefined>()
    expectTypeOf<
      businessConsoleClient.EquipmentRuntimeAvailabilityWindow['restorePredictionSource']
    >().toEqualTypeOf<string | null | undefined>()
    expectTypeOf<
      businessConsoleClient.EquipmentRuntimeAvailabilityWindow['restorePredictionSourceVersion']
    >().toEqualTypeOf<string | null | undefined>()
    expectTypeOf<BusinessConsoleMaintenanceWorkOrderActionResponse>().not.toBeNever()
    expectTypeOf<BusinessConsoleMaintenanceWorkOrderLifecycleEventItem>().not.toBeNever()
  })
})
