import { describe, expect, expectTypeOf, it } from 'vitest'
import * as businessConsoleClient from './business-console'
import type {
  CreateBusinessConsoleCodeRuleVersionData,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleRemoveTeamMemberRequest,
  RemoveBusinessConsoleTeamMemberData,
} from './generated/business-console/types.gen'
import type {
  BusinessConsoleArchiveProductCategoryRequest,
  BusinessConsoleArchiveSkillRequest,
  BusinessConsoleAssignQualityInspectionTaskRequest,
  BusinessConsoleCreateCodeRuleVersionRequest,
  BusinessConsoleEquipmentHealthFreshness,
  BusinessConsoleEquipmentHealthLevel,
  BusinessConsoleEquipmentHealthResponse,
  BusinessConsoleEquipmentHealthRuleStatus,
  BusinessConsoleReleasedEngineeringVersionEnvelope,
  BusinessConsoleReleasedEngineeringVersionResponse,
  BusinessConsoleClaimQualityInspectionTaskRequest,
  ListBusinessConsoleMasterDataResourcesData,
} from './business-console'
import {
  dispatchBusinessConsoleBarcodePrintBatch,
  dispatchBusinessConsoleBarcodePrintBatchMutationOptions,
  reprintBusinessConsoleBarcodeLabel,
  reprintBusinessConsoleBarcodeLabelMutationOptions,
  voidBusinessConsoleBarcodeLabel,
  voidBusinessConsoleBarcodeLabelMutationOptions,
  type BusinessConsoleDispatchBarcodePrintBatchBody,
  type BusinessConsoleReprintBarcodeLabelBody,
  type BusinessConsoleVoidBarcodeLabelBody,
  type DispatchBusinessConsoleBarcodePrintBatchData,
  type DispatchBusinessConsoleBarcodePrintBatchErrors,
  type ReprintBusinessConsoleBarcodeLabelData,
  type VoidBusinessConsoleBarcodeLabelData,
  type VoidBusinessConsoleBarcodeLabelErrors,
} from './business-console'

describe('generated API client contract', () => {
  it('requires archive reasons for ProductCategory and Skill requests', () => {
    expectTypeOf<BusinessConsoleArchiveProductCategoryRequest>().toEqualTypeOf<{
      reason: string
    }>()
    expectTypeOf<BusinessConsoleArchiveSkillRequest>().toEqualTypeOf<{
      reason: string
    }>()
  })

  it('requires a non-null change reason for code-rule versions through the stable boundary', () => {
    expectTypeOf<
      Pick<BusinessConsoleCreateCodeRuleVersionRequest, 'changeReason'>
    >().toEqualTypeOf<{ changeReason: string }>()
    expectTypeOf<
      CreateBusinessConsoleCodeRuleVersionData['body']
    >().toEqualTypeOf<BusinessConsoleCreateCodeRuleVersionRequest>()

    expectTypeOf<
      Extract<keyof BusinessConsoleCreateCodeRuleVersionRequest, 'createdBy'>
    >().toEqualTypeOf<never>()

    // @ts-expect-error changeReason is a required generated request field
    const missingChangeReason: BusinessConsoleCreateCodeRuleVersionRequest = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      displayName: 'SKU 编码规则',
      segments: [],
    }
    expect(missingChangeReason).toBeDefined()
  })

  it('requires a non-null reason in the generated team-member removal request', () => {
    expectTypeOf<NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleRemoveTeamMemberRequest>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      reason: string
    }>()
    expectTypeOf<RemoveBusinessConsoleTeamMemberData['body']>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      reason: string
    }>()

    const missingReason: RemoveBusinessConsoleTeamMemberData = {
      path: { teamCode: 'T-001', userId: 'user-001' },
      // @ts-expect-error reason is a required generated request field
      body: { organizationId: 'org-001', environmentId: 'env-dev' },
      url: '/api/business-console/v1/master-data/teams/{teamCode}/members/{userId}',
    }
    expect(missingReason.body).toBeDefined()
  })

  it('exports the barcode lifecycle API through the stable business-console barrel', () => {
    expect(dispatchBusinessConsoleBarcodePrintBatch).toBeTypeOf('function')
    expect(dispatchBusinessConsoleBarcodePrintBatchMutationOptions).toBeTypeOf('function')
    expect(reprintBusinessConsoleBarcodeLabel).toBeTypeOf('function')
    expect(reprintBusinessConsoleBarcodeLabelMutationOptions).toBeTypeOf('function')
    expect(voidBusinessConsoleBarcodeLabel).toBeTypeOf('function')
    expect(voidBusinessConsoleBarcodeLabelMutationOptions).toBeTypeOf('function')
    expectTypeOf<
      DispatchBusinessConsoleBarcodePrintBatchData['body']
    >().toEqualTypeOf<BusinessConsoleDispatchBarcodePrintBatchBody>()
    expectTypeOf<
      ReprintBusinessConsoleBarcodeLabelData['body']
    >().toEqualTypeOf<BusinessConsoleReprintBarcodeLabelBody>()
    expectTypeOf<
      VoidBusinessConsoleBarcodeLabelData['body']
    >().toEqualTypeOf<BusinessConsoleVoidBarcodeLabelBody>()
    expectTypeOf<ReprintBusinessConsoleBarcodeLabelData['path']>().toEqualTypeOf<{
      printBatchId: string
      sequenceNo: number
    }>()
    expectTypeOf<VoidBusinessConsoleBarcodeLabelData['path']>().toEqualTypeOf<{
      printBatchId: string
      sequenceNo: number
    }>()
    expectTypeOf<DispatchBusinessConsoleBarcodePrintBatchErrors[502]>().not.toBeNever()
    expectTypeOf<VoidBusinessConsoleBarcodeLabelErrors[502]>().not.toBeNever()
  })

  it('requires optimistic versions for public quality assignment and claim mutations', () => {
    expectTypeOf<
      Pick<BusinessConsoleAssignQualityInspectionTaskRequest, 'expectedVersion'>
    >().toEqualTypeOf<{ expectedVersion: number }>()
    expectTypeOf<
      Pick<BusinessConsoleClaimQualityInspectionTaskRequest, 'expectedVersion'>
    >().toEqualTypeOf<{ expectedVersion: number }>()
  })

  it('exports exact MasterData device and shift filters through the stable boundary', () => {
    expectTypeOf<
      Pick<ListBusinessConsoleMasterDataResourcesData['query'], 'deviceAssetId' | 'shiftCode'>
    >().toEqualTypeOf<{
      deviceAssetId?: string | null
      shiftCode?: string | null
    }>()
    expectTypeOf(businessConsoleClient.listBusinessConsoleMasterDataResources).toBeFunction()
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

  it('exports Quality reinspection through stable api-client entry points (#954)', () => {
    const expectedFunctions = [
      'createBusinessConsoleQualityReinspectionMutationOptions',
      'createBusinessConsoleQualityReinspection',
    ] as const

    for (const functionName of expectedFunctions) {
      expect(businessConsoleClient[functionName], functionName).toBeTypeOf('function')
    }
  })
})
