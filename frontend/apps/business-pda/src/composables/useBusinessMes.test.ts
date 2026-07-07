import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from '@nerv-iip/api-client'

import {
  useMesMaterialIssue,
  useMesCurrentOperationSops,
  useMesOperationTasks,
  useMesProductionReports,
  useMesReceipts,
  useMesWorkOrders,
} from './useBusinessMes'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  queryFactoriesById: new Map<string, () => { enabled?: boolean }>(),
  mutateById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

const authState = vi.hoisted(() => ({
  principal: undefined as { organizationId?: string; environmentId?: string } | undefined,
}))

function mockQueryOptions(id: string) {
  return vi.fn(() => ({
    key: [{ _id: id }],
    query: vi.fn(),
  }))
}

function mockMutationOptions(id: string) {
  return vi.fn(() => ({
    key: [{ _id: id }],
    mutation: vi.fn(),
  }))
}

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleMesWorkOrdersQueryOptions: mockQueryOptions('listBusinessConsoleMesWorkOrders'),
  getBusinessConsoleMesCurrentOperationSopsQueryOptions: mockQueryOptions('getBusinessConsoleMesCurrentOperationSops'),
  listBusinessConsoleMesOperationTasksQueryOptions: mockQueryOptions('listBusinessConsoleMesOperationTasks'),
  listBusinessConsoleMesProductionReportsQueryOptions: mockQueryOptions('listBusinessConsoleMesProductionReports'),
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions: mockQueryOptions('listBusinessConsoleMesMaterialIssueRequests'),
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions: mockQueryOptions('listBusinessConsoleMesFinishedGoodsReceiptRequests'),
  startBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions('startBusinessConsoleMesOperationTask'),
  pauseBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions('pauseBusinessConsoleMesOperationTask'),
  resumeBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions('resumeBusinessConsoleMesOperationTask'),
  completeBusinessConsoleMesOperationTaskMutationOptions: mockMutationOptions('completeBusinessConsoleMesOperationTask'),
  recordBusinessConsoleMesProductionReportMutationOptions: mockMutationOptions('recordBusinessConsoleMesProductionReport'),
  createBusinessConsoleMesMaterialIssueRequestMutationOptions: mockMutationOptions('createBusinessConsoleMesMaterialIssueRequest'),
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions: mockMutationOptions('confirmBusinessConsoleMesLineSideMaterialReceipt'),
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions: mockMutationOptions('createBusinessConsoleMesFinishedGoodsReceiptRequest'),
  createBusinessConsoleSopFileDownloadGrantMutationOptions: mockMutationOptions('createBusinessConsoleSopFileDownloadGrant'),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)
    coladaState.queryFactoriesById.set(id, optionsFactory)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn((options) => {
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    const mutateAsync = vi.fn().mockResolvedValue(undefined)
    coladaState.mutateById.set(id, mutateAsync)

    return {
      error: shallowRef(),
      isLoading: shallowRef(false),
      mutateAsync,
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: vi.fn().mockResolvedValue(undefined),
  })),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: vi.fn(() => ({
    get principal() {
      return authState.principal
    },
  })),
}))

describe('pda useBusinessMes composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.queryFactoriesById.clear()
    coladaState.mutateById.clear()
    authState.principal = { organizationId: 'org-001', environmentId: 'env-dev' }
  })

  it('keeps list queries disabled when the principal has no org/env scope', () => {
    authState.principal = undefined

    useMesWorkOrders()
    useMesOperationTasks()
    useMesCurrentOperationSops()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesOperationTasks')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled).toBe(false)
  })

  it('enables list queries once a principal scope is present', () => {
    useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: 'org-001', environmentId: 'env-dev' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(true)
  })

  it('queries current operation SOPs only after operation code is selected', () => {
    coladaState.queryDataById.set('getBusinessConsoleMesCurrentOperationSops', {
      success: true,
      data: {
        items: [{ documentNumber: 'SOP-10', revision: 'A', operationCode: 'OP-10', fileId: 'file-10' }],
      },
    })
    const sops = useMesCurrentOperationSops()

    expect(coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled).toBe(false)

    sops.filters.operationCode = ' OP-10 '
    sops.filters.workCenterCode = ' WC-10 '
    const options = coladaState.queryFactoriesById.get('getBusinessConsoleMesCurrentOperationSops')?.()

    expect(getBusinessConsoleMesCurrentOperationSopsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationCode: 'OP-10',
        workCenterCode: 'WC-10',
      },
    })
    expect(options).toMatchObject({ enabled: true })
    expect(sops.currentSops.value[0]).toMatchObject({ fileId: 'file-10' })
  })

  it('exposes a generated SDK mutation path for SOP file download grants', () => {
    useMesCurrentOperationSops()

    expect(createBusinessConsoleSopFileDownloadGrantMutationOptions).toHaveBeenCalled()
  })

  it('records a production report forwarding the caller-supplied idempotency key + business fields', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 9,
      scrapQuantity: 1,
      completesOperation: true,
      idempotencyKey: 'op-report-1',
    })

    expect(recordBusinessConsoleMesProductionReportMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 9,
      scrapQuantity: 1,
      completesOperation: true,
    })
    // caller-supplied key passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-1')
    expect(payload.body.reportedAtUtc).toBeTruthy()
  })

  it('completes an operation task forwarding the caller-supplied idempotency key', async () => {
    const { completeTask } = useMesOperationTasks()

    await completeTask('ot-9', { idempotencyKey: 'op-complete-1' })

    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('completeBusinessConsoleMesOperationTask')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-9' })
    expect(payload.query).toMatchObject({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(payload.body.idempotencyKey).toBe('op-complete-1')
  })

  it('starts an operation task forwarding an optional reason code with the caller-supplied key', async () => {
    const { startTask } = useMesOperationTasks()

    await startTask('ot-3', { reasonCode: 'OPERATOR_READY', idempotencyKey: 'op-start-1' })

    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-3' })
    expect(payload.body).toMatchObject({ reasonCode: 'OPERATOR_READY' })
    expect(payload.body.idempotencyKey).toBe('op-start-1')
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

  it('forwards the caller-supplied key when confirming a line-side material receipt', async () => {
    const { confirmLineSideReceipt } = useMesMaterialIssue()

    await confirmLineSideReceipt('req-2', { receivedQuantity: 4, idempotencyKey: 'op-confirm-1' })

    expect(confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('confirmBusinessConsoleMesLineSideMaterialReceipt')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ requestId: 'req-2' })
    expect(payload.body).toMatchObject({ receivedQuantity: 4 })
    expect(payload.body.idempotencyKey).toBe('op-confirm-1')
  })

  it('forwards the caller-supplied key + injects business fields when creating a finished-goods receipt', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-1',
    })

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesFinishedGoodsReceiptRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
    })
    expect(payload.body.idempotencyKey).toBe('op-receipt-1')
    expect(payload.body.requestedAtUtc).toBeTruthy()
  })

  it('keeps injected scope/timestamp override-proof on recordReport (hostile org/env lose; caller key wins)', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 1,
      scrapQuantity: 0,
      completesOperation: false,
      idempotencyKey: 'op-report-stable',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      reportedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    const payload = mutateAsync!.mock.calls[0][0]
    // org/env + timestamp injected LAST from principal scope — hostile caller values lose
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.reportedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    // the idempotency key is now the caller's responsibility — it passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-stable')
  })

  it('keeps injected scope/timestamp override-proof on createReceipt (hostile org/env lose; caller key wins)', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      unitCost: 12.34,
      uomCode: 'EA',
      idempotencyKey: 'op-receipt-stable',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      requestedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesFinishedGoodsReceiptRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.requestedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    expect(payload.body.idempotencyKey).toBe('op-receipt-stable')
  })
})
