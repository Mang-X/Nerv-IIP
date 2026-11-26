import {
  coladaState,
  authState,
  reactiveAuthState,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  getBusinessConsoleMesMaterialIssueRequest,
  listBusinessConsoleMesMaterialIssueRequests,
} from '@nerv-iip/api-client'
import { useMesMaterialIssue, useMesReceipts } from '.'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('does not manually refresh material-issue or receipt lists without org/env scope', async () => {
    authState.principal = undefined

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    await Promise.all([materialIssue.refresh(), receipts.refresh()])

    expect(
      coladaState.refetchById.get('listBusinessConsoleMesMaterialIssueRequests'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests'),
    ).not.toHaveBeenCalled()
  })

  it('exposes failed material-issue and receipt envelopes instead of treating them as successful empty lists', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: false,
      message: '领料申请查询失败',
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: false,
      message: '完工入库申请查询失败',
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()

    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(materialIssue.hasFailedResponse.value).toBe(true)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.hasFailedResponse.value).toBe(true)
  })

  it('does not report stale successful envelopes as current success while refreshing', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: { items: [], total: 0 },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: { items: [], total: 0 },
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    expect(materialIssue.hasSuccessfulResponse.value).toBe(true)
    expect(receipts.hasSuccessfulResponse.value).toBe(true)

    coladaState.loadingById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = true
    coladaState.loadingById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value = true
    await nextTick()

    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(materialIssue.hasFailedResponse.value).toBe(false)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.hasFailedResponse.value).toBe(false)
  })

  it('removes cached issue and receipt rows outside scope and waits for the restored scope response', async () => {
    vi.useFakeTimers()
    vi.setSystemTime('2026-07-28T01:00:00.000Z')
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: {
        items: [{ requestId: 'OLD-ISSUE', workOrderId: 'OLD-WO' }],
        total: 9,
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: {
        items: [{ receiptRequestId: 'OLD-RECEIPT', workOrderId: 'OLD-WO' }],
        total: 8,
      },
    })

    const materialIssue = useMesMaterialIssue()
    const receipts = useMesReceipts()
    expect(materialIssue.requests.value).toHaveLength(1)
    expect(materialIssue.total.value).toBe(9)
    expect(receipts.receipts.value).toHaveLength(1)
    expect(receipts.total.value).toBe(8)

    reactiveAuthState.principal = undefined
    await nextTick()
    await Promise.all([materialIssue.refresh(), receipts.refresh()])

    expect(materialIssue.requests.value).toEqual([])
    expect(materialIssue.total.value).toBe(0)
    expect(receipts.receipts.value).toEqual([])
    expect(receipts.total.value).toBe(0)
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesMaterialIssueRequests'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests'),
    ).not.toHaveBeenCalled()

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    expect(materialIssue.requests.value).toEqual([])
    expect(materialIssue.total.value).toBe(0)
    expect(materialIssue.hasSuccessfulResponse.value).toBe(false)
    expect(receipts.receipts.value).toEqual([])
    expect(receipts.total.value).toBe(0)
    expect(receipts.hasSuccessfulResponse.value).toBe(false)

    coladaState.queryDataRefById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = {
      success: false,
    }
    coladaState.queryDataRefById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value =
      { success: false }
    await nextTick()

    vi.setSystemTime('2026-07-28T02:00:00.000Z')
    coladaState.queryDataRefById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = {
      success: true,
      data: {
        items: [{ requestId: 'NEW-ISSUE', workOrderId: 'NEW-WO' }],
        total: 1,
      },
    }
    coladaState.queryDataRefById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value =
      {
        success: true,
        data: {
          items: [{ receiptRequestId: 'NEW-RECEIPT', workOrderId: 'NEW-WO' }],
          total: 1,
        },
      }
    await nextTick()

    expect(materialIssue.requests.value).toEqual([
      expect.objectContaining({ requestId: 'NEW-ISSUE' }),
    ])
    expect(materialIssue.total.value).toBe(1)
    expect(receipts.receipts.value).toEqual([
      expect.objectContaining({ receiptRequestId: 'NEW-RECEIPT' }),
    ])
    expect(receipts.total.value).toBe(1)

    coladaState.loadingById.get('listBusinessConsoleMesMaterialIssueRequests')!.value = true
    coladaState.loadingById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')!.value = true
    await nextTick()

    expect(materialIssue.requests.value).toEqual([
      expect.objectContaining({ requestId: 'NEW-ISSUE' }),
    ])
    expect(receipts.receipts.value).toEqual([
      expect.objectContaining({ receiptRequestId: 'NEW-RECEIPT' }),
    ])
  })

  it('forwards the caller-supplied key when creating a material issue request', async () => {
    const { createIssue } = useMesMaterialIssue()

    await createIssue('wo-7', { materialId: 'mat-1', quantity: 5, idempotencyKey: 'op-issue-1' })

    expect(createBusinessConsoleMesMaterialIssueRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesMaterialIssueRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ workOrderId: 'wo-7' })
    expect(payload.query).toMatchObject({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(payload.body).toMatchObject({ materialId: 'mat-1', quantity: 5 })
    expect(payload.body.idempotencyKey).toBe('op-issue-1')
  })

  it('replays a keyed line-side receipt and reads back only the original issue request', async () => {
    reactiveAuthState.principal!.permissionCodes = ['business.mes.materials.manage']
    const { confirmLineSideReceipt } = useMesMaterialIssue()
    const mutate = coladaState.mutateById.get('confirmBusinessConsoleMesLineSideMaterialReceipt')!
    mutate.mockResolvedValue({
      success: true,
      data: { accepted: true, downstreamDocumentId: 'MI-002' },
    })
    const current = {
      requestId: 'req-2',
      workOrderId: 'wo-2',
      status: 'ReceiptPosting',
      receivedQuantity: 4,
    } as const
    vi.mocked(getBusinessConsoleMesMaterialIssueRequest).mockResolvedValueOnce({
      data: { success: true, data: current },
    } as Awaited<ReturnType<typeof getBusinessConsoleMesMaterialIssueRequest>>)

    const body = { receivedQuantity: 4, idempotencyKey: 'original-receipt-key' }
    await expect(
      confirmLineSideReceipt('req-2', body, { workOrderId: 'wo-2', replay: true }),
    ).resolves.toEqual({ reference: 'MI-002', current })
    expect(confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions).toHaveBeenCalled()
    expect(mutate).toHaveBeenCalledWith({
      path: { requestId: 'req-2' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body,
    })
    expect(getBusinessConsoleMesMaterialIssueRequest).toHaveBeenCalledExactlyOnceWith({
      path: { requestId: 'req-2' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      throwOnError: true,
    })
  })

  it('replays a return with the same key after the authoritative list shows zero returnable quantity', async () => {
    vi.mocked(listBusinessConsoleMesMaterialIssueRequests)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                requestId: 'req-return-1',
                receivedQuantity: 5,
                consumedQuantity: 0,
                materialLotId: 'LOT-1',
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                requestId: 'req-return-1',
                receivedQuantity: 0,
                consumedQuantity: 0,
                materialLotId: null,
              },
            ],
            total: 1,
          },
        },
      } as never)

    const { returnLineSideMaterial } = useMesMaterialIssue()
    const body = { returnedQuantity: 5, idempotencyKey: 'return-replay-1' }

    await returnLineSideMaterial('req-return-1', body)
    await returnLineSideMaterial('req-return-1', body)

    expect(
      coladaState.mutateById.get('returnBusinessConsoleMesLineSideMaterial'),
    ).toHaveBeenCalledTimes(2)
    expect(
      coladaState.mutateById.get('returnBusinessConsoleMesLineSideMaterial')!.mock.calls[1][0].body,
    ).toEqual(body)
  })

  it('forwards the caller-supplied key + injects business fields when creating a finished-goods receipt', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-1',
    })

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get(
      'createBusinessConsoleMesFinishedGoodsReceiptRequest',
    )
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      uomCode: 'EA',
    })
    expect(payload.body.idempotencyKey).toBe('op-receipt-1')
    expect(payload.body.requestedAtUtc).toBeTruthy()
  })

  it('keeps injected scope/timestamp override-proof on createReceipt (hostile org/env lose; caller key wins)', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-stable',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      requestedAtUtc: '1999-01-01T00:00:00.000Z',
      unitCost: 12.34,
    } as never)

    const mutateAsync = coladaState.mutateById.get(
      'createBusinessConsoleMesFinishedGoodsReceiptRequest',
    )
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body).not.toHaveProperty('unitCost')
    expect(payload.body.requestedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    expect(payload.body.idempotencyKey).toBe('op-receipt-stable')
  })
})
