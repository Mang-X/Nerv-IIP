import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
} from '@nerv-iip/api-client'

import {
  useMesMaterialIssue,
  useMesOperationTasks,
  useMesProductionReports,
  useMesReceipts,
  useMesWorkOrders,
} from './useBusinessMes'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
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
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

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
    coladaState.mutateById.clear()
    authState.principal = { organizationId: 'org-001', environmentId: 'env-dev' }
  })

  it('keeps list queries disabled when the principal has no org/env scope', () => {
    authState.principal = undefined

    useMesWorkOrders()
    useMesOperationTasks()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(false)
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesOperationTasks')?.enabled).toBe(false)
  })

  it('enables list queries once a principal scope is present', () => {
    useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: 'org-001', environmentId: 'env-dev' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(true)
  })

  it('records a production report with idempotency key and business fields', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 9,
      scrapQuantity: 1,
      completesOperation: true,
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
    expect(payload.body.idempotencyKey).toBeTruthy()
    expect(payload.body.reportedAtUtc).toBeTruthy()
  })

  it('completes an operation task with an idempotency key', async () => {
    const { completeTask } = useMesOperationTasks()

    await completeTask('ot-9')

    expect(completeBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('completeBusinessConsoleMesOperationTask')
    expect(mutateAsync).toHaveBeenCalledTimes(1)
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-9' })
    expect(payload.query).toMatchObject({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(payload.body.idempotencyKey).toBeTruthy()
  })

  it('starts an operation task forwarding an optional reason code with the idempotency key', async () => {
    const { startTask } = useMesOperationTasks()

    await startTask('ot-3', 'OPERATOR_READY')

    const mutateAsync = coladaState.mutateById.get('startBusinessConsoleMesOperationTask')
    expect(startBusinessConsoleMesOperationTaskMutationOptions).toHaveBeenCalled()
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ operationTaskId: 'ot-3' })
    expect(payload.body).toMatchObject({ reasonCode: 'OPERATOR_READY' })
    expect(payload.body.idempotencyKey).toBeTruthy()
  })

  it('attaches an idempotency key when creating a material issue request', async () => {
    const { createIssue } = useMesMaterialIssue()

    await createIssue('wo-7', { materialId: 'mat-1', quantity: 5 })

    expect(createBusinessConsoleMesMaterialIssueRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesMaterialIssueRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ workOrderId: 'wo-7' })
    expect(payload.query).toMatchObject({ organizationId: 'org-001', environmentId: 'env-dev' })
    expect(payload.body).toMatchObject({ materialId: 'mat-1', quantity: 5 })
    expect(payload.body.idempotencyKey).toBeTruthy()
  })

  it('attaches an idempotency key when confirming a line-side material receipt', async () => {
    const { confirmLineSideReceipt } = useMesMaterialIssue()

    await confirmLineSideReceipt('req-2', { receivedQuantity: 4 })

    expect(confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('confirmBusinessConsoleMesLineSideMaterialReceipt')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.path).toEqual({ requestId: 'req-2' })
    expect(payload.body).toMatchObject({ receivedQuantity: 4 })
    expect(payload.body.idempotencyKey).toBeTruthy()
  })

  it('attaches an idempotency key and business fields when creating a finished-goods receipt', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({ workOrderId: 'wo-5', skuId: 'sku-1', quantity: 12, uomCode: 'EA' })

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesFinishedGoodsReceiptRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      uomCode: 'EA',
    })
    expect(payload.body.idempotencyKey).toBeTruthy()
    expect(payload.body.requestedAtUtc).toBeTruthy()
  })

  it('ignores caller attempts to override injected scope/idempotency on recordReport', async () => {
    const { recordReport } = useMesProductionReports()

    await recordReport({
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 1,
      scrapQuantity: 0,
      completesOperation: false,
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      idempotencyKey: 'evil',
      reportedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.idempotencyKey).not.toBe('evil')
    expect(payload.body.idempotencyKey).toBeTruthy()
    expect(payload.body.reportedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
  })

  it('ignores caller attempts to override injected scope/idempotency on createReceipt', async () => {
    const { createReceipt } = useMesReceipts()

    await createReceipt({
      workOrderId: 'wo-5',
      skuId: 'sku-1',
      quantity: 12,
      uomCode: 'EA',
      organizationId: 'EVIL',
      environmentId: 'EVIL',
      idempotencyKey: 'evil',
      requestedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesFinishedGoodsReceiptRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.idempotencyKey).not.toBe('evil')
    expect(payload.body.idempotencyKey).toBeTruthy()
    expect(payload.body.requestedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
  })

  it('ignores caller attempts to override the injected idempotency key on createIssue', async () => {
    const { createIssue } = useMesMaterialIssue()

    await createIssue('wo-7', { materialId: 'mat-1', idempotencyKey: 'evil' } as never)

    const mutateAsync = coladaState.mutateById.get('createBusinessConsoleMesMaterialIssueRequest')
    const payload = mutateAsync!.mock.calls[0][0]
    expect(payload.body.materialId).toBe('mat-1')
    expect(payload.body.idempotencyKey).not.toBe('evil')
    expect(payload.body.idempotencyKey).toBeTruthy()
  })
})
