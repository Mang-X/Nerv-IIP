import { coladaState, reactiveAuthState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  getBusinessConsoleMesWipSummary,
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleMesReportableOperationTasks,
  listBusinessConsoleMesReceivableProducedLots,
  recordBusinessConsoleMesProductionReport,
} from '@nerv-iip/api-client'
import { useMesProductionReporting, useMesWorkOrders } from './useBusinessMes'
import { useBusinessContextStore } from '@/stores/businessContext'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('submits rush work orders and production reports', async () => {
    const { createRushWorkOrder, recordProductionReport } = useMesWorkOrders()

    await createRushWorkOrder({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-rush',
      skuId: 'sku-1',
      quantity: 10,
      dueUtc: '2026-05-24T00:00:00Z',
      workCenterId: 'wc-1',
      durationMinutes: 60,
    })
    await recordProductionReport({
      workOrderId: 'wo-rush',
      operationTaskId: 'op-1',
      goodQuantity: 8,
      scrapQuantity: 1,
      completesOperation: true,
      reportedAtUtc: '2026-05-24T01:00:00Z',
      idempotencyKey: 'production-report-test',
    })

    expect(createBusinessConsoleMesRushWorkOrderMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleMesRushWorkOrderMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        workOrderId: 'wo-rush',
      }),
    })
    expect(recordBusinessConsoleMesProductionReport).toHaveBeenCalledWith({
      body: expect.objectContaining({
        operationTaskId: 'op-1',
        scopeKind: 'self',
        scopeId: 'user-001',
      }),
      throwOnError: false,
    })
    // 急单 4 键 + 报工 5 键（报工弹窗现在也从工序执行页打开，工序任务列表须一起失效）。
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(9)
  })

  it('reads the cumulative quantity for the exact work order and operation', async () => {
    vi.mocked(getBusinessConsoleMesWipSummary).mockImplementationOnce(
      async ({ query }) =>
        ({
          data: {
            success: true,
            data: {
              items:
                query?.workOrderId === 'WO-100' && query.operationTaskId === 'OP-TARGET'
                  ? [{ operationTaskId: 'OP-TARGET', plannedQuantity: 20, goodQuantity: 12 }]
                  : [{ operationTaskId: 'OP-000', plannedQuantity: 90, goodQuantity: 50 }],
            },
          },
        }) as never,
    )

    const { readProductionQuantitySnapshot } = useMesProductionReporting()
    await expect(readProductionQuantitySnapshot(' WO-100 ', ' OP-TARGET ')).resolves.toEqual({
      plannedQuantity: 20,
      reportedGoodQuantity: 12,
    })
  })

  it.each([
    { reportNo: 'PR-OTHER', remainingQuantity: 3, canManage: true, opens: false },
    { reportNo: 'PR-100', remainingQuantity: 0, canManage: true, opens: false },
    { reportNo: 'PR-100', remainingQuantity: 3, canManage: false, opens: false },
    { reportNo: 'PR-100', remainingQuantity: 3, canManage: true, opens: true },
  ])(
    'offers receipt handoff only for confirmed receivable output with permission ($reportNo/$remainingQuantity/$canManage)',
    async ({ reportNo, remainingQuantity, canManage, opens }) => {
      reactiveAuthState.principal!.permissionCodes = [
        'business.mes.work-orders.read',
        'business.mes.receipts.read',
        ...(canManage ? ['business.mes.receipts.manage'] : []),
      ]
      vi.mocked(listBusinessConsoleMesReceivableProducedLots).mockResolvedValue({
        data: {
          success: true,
          data: { items: [{ reportNo, operationTaskId: 'OP-20', remainingQuantity }] },
        },
      } as never)
      vi.mocked(getBusinessConsoleMesWorkOrderDetail).mockResolvedValue({
        data: { success: true, data: { workOrderId: 'WO-100', skuId: 'SKU-001' } },
      } as never)

      const { prepareReceiptHandoff } = useMesProductionReporting()
      const readReceiptHandoff = prepareReceiptHandoff()
      const handoff = await readReceiptHandoff?.('WO-100', 'OP-20', 'PR-100')
      if (opens) {
        expect(handoff).toMatchObject({ workOrderId: 'WO-100', skuId: 'SKU-001' })
        useBusinessContextStore().patchContext({ environmentId: 'env-other' })
        expect(handoff?.isCurrent()).toBe(false)
      } else {
        expect(handoff).toBeUndefined()
      }
    },
  )

  it('does not send a production report when the write scope is not selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.write',
      { success: true, data: { selectedScope: null } },
    )
    const { recordProductionReport, reportScopeReady } = useMesProductionReporting()
    vi.mocked(recordBusinessConsoleMesProductionReport).mockClear()

    expect(reportScopeReady.value).toBe(false)
    await expect(
      recordProductionReport({
        workOrderId: 'wo-no-scope',
        operationTaskId: 'op-no-scope',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: 'report-no-scope',
      }),
      // 读到了但授权清单为空：提示「没有已授权的作业范围」并指向管理员配置授权（#1288），不再泛泛说「尚未选择」。
    ).rejects.toThrow('没有已授权的作业范围')
    expect(recordBusinessConsoleMesProductionReport).not.toHaveBeenCalled()
  })

  it('fails closed before report POST when the frozen write scope cannot pass reportable readback', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValueOnce({
      error: { status: 403, message: 'scope not readable' },
      response: { status: 403 },
    } as never)
    const { recordProductionReport } = useMesProductionReporting()
    vi.mocked(recordBusinessConsoleMesProductionReport).mockClear()

    await expect(
      recordProductionReport({
        workOrderId: 'wo-write-only',
        operationTaskId: 'op-write-only',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: true,
        idempotencyKey: 'report-write-only',
      }),
    ).rejects.toMatchObject({ status: 403 })

    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      }),
    )
    expect(recordBusinessConsoleMesProductionReport).not.toHaveBeenCalled()
  })

  it('仅重放当前实例已发出的同键报工完工，新键仍按最新 Completed 状态拦截', async () => {
    const { recordProductionReport } = useMesProductionReporting()
    vi.mocked(listBusinessConsoleMesReportableOperationTasks)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'op-replay',
                workOrderId: 'wo-replay',
                status: 'InProgress',
              },
            ],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValue({
        data: {
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'op-replay',
                workOrderId: 'wo-replay',
                status: 'Completed',
              },
            ],
            total: 1,
          },
        },
      } as never)
    vi.mocked(recordBusinessConsoleMesProductionReport)
      .mockRejectedValueOnce(new Error('response lost'))
      .mockResolvedValue({
        data: { success: true, data: { reportNo: 'PRPT-REPLAY' } },
        response: { status: 200 },
      } as never)
    const originalIntent = {
      workOrderId: 'wo-replay',
      operationTaskId: 'op-replay',
      goodQuantity: 4,
      scrapQuantity: 0,
      completesOperation: true,
      reportedAtUtc: '2026-07-28T01:00:00.000Z',
      idempotencyKey: 'report-complete-replay',
    }

    await expect(recordProductionReport(originalIntent)).rejects.toThrow('response lost')
    await expect(recordProductionReport(originalIntent)).resolves.toMatchObject({
      success: true,
    })
    await expect(
      recordProductionReport({
        ...originalIntent,
        idempotencyKey: 'report-complete-new-intent',
      }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenCalledTimes(3)
    expect(recordBusinessConsoleMesProductionReport).toHaveBeenCalledTimes(2)
  })

  it('保留打印准备待收敛的完整报工载荷，新实例恢复后只重放原模板和原时间', async () => {
    vi.mocked(recordBusinessConsoleMesProductionReport).mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          productionReportId: 'report-serial',
          reportNo: 'PRPT-SERIAL',
          serialNumbers: ['SN-H-101'],
          printBatchId: 'LPB-014',
          printStatus: 'reserved',
          printingPreparationPending: true,
        },
      },
      response: { status: 200 },
    } as never)
    const body = {
      workOrderId: 'wo-serial',
      operationTaskId: 'op-serial',
      goodQuantity: 1,
      scrapQuantity: 0,
      completesOperation: false,
      reportedAtUtc: '2026-09-14T01:00:00Z',
      labelTemplateId: 'template-housing',
      idempotencyKey: 'report-serial-intent',
    }
    const first = useMesProductionReporting()
    await first.recordProductionReport(body)
    const restored = useMesProductionReporting()
    expect(restored.restoreProductionReport('wo-serial', 'op-serial')).toMatchObject(body)
    vi.mocked(recordBusinessConsoleMesProductionReport).mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          reportNo: 'PRPT-SERIAL',
          productionReportId: 'report-serial',
          serialNumbers: ['SN-H-101'],
          printBatchId: 'LPB-014',
          printStatus: 'ready-to-print',
          printingPreparationPending: false,
        },
      },
      response: { status: 200 },
    } as never)
    await restored.recordProductionReport({
      ...body,
      goodQuantity: 9,
      labelTemplateId: 'another-template',
      idempotencyKey: 'new-key',
    })
    expect(
      vi.mocked(recordBusinessConsoleMesProductionReport).mock.calls.at(-1)?.[0]?.body,
    ).toMatchObject(body)
    expect(restored.restoreProductionReport('wo-serial', 'op-serial')).toBeUndefined()
  })
})
