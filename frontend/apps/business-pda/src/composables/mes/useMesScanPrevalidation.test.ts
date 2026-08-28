import type {
  BusinessConsoleBarcodeResolveCandidate,
  BusinessConsoleBarcodeResolveEnvelope,
  BusinessConsoleMesContextScanPrevalidationResponse,
  BusinessConsoleMesMaterialScanPrevalidationResponse,
} from '@nerv-iip/api-client'
import { nextTick, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useMesScanPrevalidation } from './useMesScanPrevalidation'

const scope = { organizationId: 'org-1', environmentId: 'env-1' }

function barcodeEnvelope(
  status: string,
  candidates: BusinessConsoleBarcodeResolveCandidate[] = [],
): BusinessConsoleBarcodeResolveEnvelope {
  return { success: true, data: { status, candidates, total: candidates.length } }
}

function materialAccepted(): BusinessConsoleMesMaterialScanPrevalidationResponse {
  return {
    decision: 'accepted',
    reasonCode: 'material-scan-accepted',
    materialIssueRequestId: 'MI-1',
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
    materialId: 'MAT-1',
    materialLotId: 'LOT-1',
    materialQualification: 'primary',
    evaluatedAtUtc: '2026-08-28T10:00:00Z',
  }
}

function contextAccepted(
  objectType: 'operationTask' | 'deviceAsset' | 'personnel' = 'operationTask',
  scannedObjectId = 'OP-1',
): BusinessConsoleMesContextScanPrevalidationResponse {
  return {
    decision: 'accepted',
    reasonCode: `${objectType}-scan-accepted`,
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
    objectType,
    scannedObjectId,
    evaluatedAtUtc: '2026-08-28T10:00:00Z',
  }
}

describe('useMesScanPrevalidation', () => {
  it('resolves a material issue strong ID and delegates all material rules to MES', async () => {
    const prevalidateMaterial = vi.fn().mockResolvedValue(materialAccepted())
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      resolveBarcode: vi.fn().mockResolvedValue(
        barcodeEnvelope('resolved', [
          {
            objectType: 'mes-material-issue-request',
            strongIds: { materialIssueRequestId: 'MI-1' },
          },
        ]),
      ),
      prevalidateMaterial,
    })

    await expect(scanner.scan(' MATERIAL-LABEL ')).resolves.toMatchObject({
      kind: 'material',
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      materialIssueRequestId: 'MI-1',
      materialId: 'MAT-1',
      materialLotId: 'LOT-1',
    })
    expect(prevalidateMaterial).toHaveBeenCalledWith({
      organizationId: 'org-1',
      environmentId: 'env-1',
      materialIssueRequestId: 'MI-1',
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
    })
    expect(scanner.status.value).toBe('resolved')
    expect(scanner.message.value).toContain('物料与批次已通过')
  })

  it('does not guess an ambiguous candidate and prevalidates only the explicit selection', async () => {
    const candidates: BusinessConsoleBarcodeResolveCandidate[] = [
      { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
      {
        objectType: 'mes-operation',
        strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      },
    ]
    const prevalidateContext = vi.fn().mockResolvedValue(contextAccepted())
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: {},
      resolveBarcode: vi.fn().mockResolvedValue(barcodeEnvelope('ambiguous', candidates)),
      prevalidateContext,
    })

    await expect(scanner.scan('AMB')).resolves.toBeNull()
    expect(scanner.status.value).toBe('ambiguous')
    expect(prevalidateContext).not.toHaveBeenCalled()

    await expect(scanner.selectCandidate(candidates[1]!)).resolves.toMatchObject({
      kind: 'operation-task',
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
    })
    expect(prevalidateContext).toHaveBeenCalledWith({
      organizationId: 'org-1',
      environmentId: 'env-1',
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      objectType: 'operationTask',
      scannedObjectId: 'OP-1',
    })
  })

  it.each(['unknown', 'unsupported', 'forbidden'] as const)(
    'keeps the barcode %s outcome explicit and performs no MES prevalidation',
    async (status) => {
      const prevalidateContext = vi.fn()
      const scanner = useMesScanPrevalidation({
        ...scope,
        context: {},
        resolveBarcode: vi.fn().mockResolvedValue(barcodeEnvelope(status)),
        prevalidateContext,
      })

      await scanner.scan('CODE')
      expect(scanner.status.value).toBe(status)
      expect(scanner.message.value).not.toBe('')
      expect(prevalidateContext).not.toHaveBeenCalled()
    },
  )

  it('rejects a resolved object that the current page cannot consume', async () => {
    const prevalidateContext = vi.fn()
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      acceptedKinds: ['work-order', 'operation-task'],
      resolveBarcode: vi
        .fn()
        .mockResolvedValue(
          barcodeEnvelope('resolved', [
            { objectType: 'personnel', strongIds: { userId: 'USER-1' } },
          ]),
        ),
      prevalidateContext,
    })

    await expect(scanner.scan('BADGE')).resolves.toBeNull()
    expect(scanner.status.value).toBe('unsupported')
    expect(prevalidateContext).not.toHaveBeenCalled()
  })

  it('shows the server rejection reason and does not produce an accepted context', async () => {
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      resolveBarcode: vi
        .fn()
        .mockResolvedValue(
          barcodeEnvelope('resolved', [
            { objectType: 'personnel', strongIds: { userId: 'USER-OLD' } },
          ]),
        ),
      prevalidateContext: vi.fn().mockResolvedValue({
        ...contextAccepted('personnel', 'USER-OLD'),
        decision: 'rejected',
        reasonCode: 'personnel-mismatch',
      }),
    })

    await expect(scanner.scan('BAD-BADGE')).resolves.toBeNull()
    expect(scanner.status.value).toBe('rejected')
    expect(scanner.message.value).toContain('工牌与当前工序指派人员不匹配')
  })

  it('fails closed with a readable source error', async () => {
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      resolveBarcode: vi
        .fn()
        .mockResolvedValue(
          barcodeEnvelope('resolved', [
            { objectType: 'personnel', strongIds: { userId: 'USER-1' } },
          ]),
        ),
      prevalidateContext: vi.fn().mockRejectedValue({
        status: 503,
        message: 'MES_CONTEXT_QUALIFICATION_SOURCE_UNAVAILABLE: 上岗资格来源不可用。',
      }),
    })

    await expect(scanner.scan('BADGE')).resolves.toBeNull()
    expect(scanner.status.value).toBe('error')
    expect(scanner.message.value).toContain('预校验来源暂不可用')
  })

  it('discards an older prevalidation result after a rapid newer scan', async () => {
    let settleFirst!: (value: BusinessConsoleMesContextScanPrevalidationResponse) => void
    const prevalidateContext = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<BusinessConsoleMesContextScanPrevalidationResponse>((resolve) => {
            settleFirst = resolve
          }),
      )
      .mockResolvedValueOnce(contextAccepted('deviceAsset', 'DEV-2'))
    const resolveBarcode = vi
      .fn()
      .mockResolvedValueOnce(
        barcodeEnvelope('resolved', [
          { objectType: 'equipment-device', strongIds: { deviceAssetId: 'DEV-1' } },
        ]),
      )
      .mockResolvedValueOnce(
        barcodeEnvelope('resolved', [
          { objectType: 'equipment-device', strongIds: { deviceAssetId: 'DEV-2' } },
        ]),
      )
    const scanner = useMesScanPrevalidation({
      ...scope,
      context: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      resolveBarcode,
      prevalidateContext,
    })

    const first = scanner.scan('DEV-1')
    await vi.waitUntil(() => prevalidateContext.mock.calls.length === 1)
    const second = scanner.scan('DEV-2')
    await expect(second).resolves.toMatchObject({ kind: 'device', scannedObjectId: 'DEV-2' })
    settleFirst(contextAccepted('deviceAsset', 'DEV-1'))

    await expect(first).resolves.toBeNull()
    expect(scanner.accepted.value).toMatchObject({ kind: 'device', scannedObjectId: 'DEV-2' })
    expect(scanner.scannedValue.value).toBe('DEV-2')
  })

  it('invalidates a pending result when the work-order context changes', async () => {
    const context = shallowRef({ workOrderId: 'WO-1', operationTaskId: 'OP-1' })
    let settle!: (value: BusinessConsoleMesMaterialScanPrevalidationResponse) => void
    const prevalidateMaterial = vi.fn(
      () =>
        new Promise<BusinessConsoleMesMaterialScanPrevalidationResponse>((resolve) => {
          settle = resolve
        }),
    )
    const scanner = useMesScanPrevalidation({
      ...scope,
      context,
      resolveBarcode: vi.fn().mockResolvedValue(
        barcodeEnvelope('resolved', [
          {
            objectType: 'mes-material-issue-request',
            strongIds: { materialIssueRequestId: 'MI-1' },
          },
        ]),
      ),
      prevalidateMaterial,
    })

    const pending = scanner.scan('MAT')
    await vi.waitUntil(() => prevalidateMaterial.mock.calls.length === 1)
    expect(scanner.pending.value).toBe(true)
    context.value = { workOrderId: 'WO-2', operationTaskId: 'OP-2' }
    await nextTick()
    settle(materialAccepted())

    await expect(pending).resolves.toBeNull()
    expect(scanner.status.value).toBe('idle')
    expect(scanner.accepted.value).toBeNull()
  })
})
