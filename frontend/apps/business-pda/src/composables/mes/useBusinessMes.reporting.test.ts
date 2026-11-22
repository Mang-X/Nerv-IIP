import {
  coladaState,
  productionReportFetch,
  reactiveAuthState,
  deferred,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  listBusinessConsoleMesReportableOperationTasks,
  recordBusinessConsoleMesProductionReportMutationOptions,
} from '@nerv-iip/api-client'
import { acquirePendingBusinessIntent } from '@nerv-iip/business-core'
import { MES_WORK_SCOPE_UNAVAILABLE_MESSAGE, useMesProductionReports } from '.'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

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
      scopeKind: 'self',
      scopeId: 'user-001',
    })
    // caller-supplied key passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-1')
    expect(payload.body.reportedAtUtc).toBeTruthy()
    expect(listBusinessConsoleMesReportableOperationTasks).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      }),
    )
  })

  it('confirms a production report only when public GET returns the same report and pair', async () => {
    productionReportFetch.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          report: {
            productionReportId: 'report-id-1',
            reportNo: 'RPT-1',
            workOrderId: 'wo-1',
            operationTaskId: 'ot-1',
          },
        },
      },
    })
    const { confirmReport, reportContext } = useMesProductionReports()
    const context = reportContext.value!

    await expect(
      confirmReport({
        reportNo: 'RPT-1',
        productionReportId: 'report-id-1',
        workOrderId: 'wo-1',
        operationTaskId: 'ot-1',
        context,
      }),
    ).resolves.toMatchObject({ reportNo: 'RPT-1', workOrderId: 'wo-1', operationTaskId: 'ot-1' })
    expect(productionReportFetch).toHaveBeenCalledWith({
      path: { reportNo: 'RPT-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      throwOnError: true,
    })
  })

  it('rejects a production report public GET that returns another operation pair', async () => {
    productionReportFetch.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          report: {
            productionReportId: 'report-id-1',
            reportNo: 'RPT-1',
            workOrderId: 'wo-other',
            operationTaskId: 'ot-other',
          },
        },
      },
    })
    const { confirmReport, reportContext } = useMesProductionReports()

    await expect(
      confirmReport({
        reportNo: 'RPT-1',
        productionReportId: 'report-id-1',
        workOrderId: 'wo-1',
        operationTaskId: 'ot-1',
        context: reportContext.value!,
      }),
    ).rejects.toThrow('尚未回读到同一工单与工序')
  })

  it('rejects a production report when public GET is not yet visible', async () => {
    productionReportFetch.mockRejectedValueOnce({ status: 404 })
    const { confirmReport, reportContext } = useMesProductionReports()

    await expect(
      confirmReport({
        reportNo: 'RPT-NOT-VISIBLE',
        productionReportId: 'report-id-pending',
        workOrderId: 'wo-1',
        operationTaskId: 'ot-1',
        context: reportContext.value!,
      }),
    ).rejects.toThrow('尚未回读到同一工单与工序')
  })

  it('rejects an old receipt when principal changes before public GET confirmation', async () => {
    const { confirmReport, reportContext } = useMesProductionReports()
    const frozenContext = { ...reportContext.value! }
    reactiveAuthState.principal = {
      principalId: 'user-002',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    await nextTick()

    await expect(
      confirmReport({
        reportNo: 'RPT-A',
        productionReportId: 'report-a',
        workOrderId: 'wo-1',
        operationTaskId: 'ot-1',
        context: frozenContext,
      }),
    ).rejects.toThrow('回读上下文无效')
    expect(productionReportFetch).not.toHaveBeenCalled()
  })

  it('rejects a pending public GET after principal A→B→A changes its generation', async () => {
    const pendingGet = deferred<{
      data: {
        success: boolean
        data: { report: Record<string, string> }
      }
    }>()
    productionReportFetch.mockReturnValueOnce(pendingGet.promise)
    const { confirmReport, reportContext } = useMesProductionReports()
    const frozenContext = { ...reportContext.value! }
    const confirmation = confirmReport({
      reportNo: 'RPT-A',
      productionReportId: 'report-a',
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      context: frozenContext,
    })
    await nextTick()

    reactiveAuthState.principal = {
      principalId: 'user-002',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    await nextTick()
    reactiveAuthState.principal = {
      principalId: 'user-001',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }
    await nextTick()
    expect(reportContext.value).toMatchObject({ principalId: 'user-001' })
    expect(reportContext.value!.generation).toBeGreaterThan(frozenContext.generation)

    pendingGet.resolve({
      data: {
        success: true,
        data: {
          report: {
            reportNo: 'RPT-A',
            productionReportId: 'report-a',
            workOrderId: 'wo-1',
            operationTaskId: 'ot-1',
          },
        },
      },
    })
    await expect(confirmation).rejects.toThrow('回读上下文无效')
  })

  it('does not send a production report when no reporting scope is selected', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.reporting.write',
      { success: true, data: { selectedScope: null } },
    )
    const { recordReport, reportScopeReady } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!

    expect(reportScopeReady.value).toBe(false)
    await expect(
      recordReport({
        workOrderId: 'wo-no-scope',
        operationTaskId: 'ot-no-scope',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: 'report-no-scope',
      }),
      // 响应成功但授权清单为空 = 「一个授权范围都没有」，与「还没选」是两回事（#1297）。
    ).rejects.toThrow(MES_WORK_SCOPE_UNAVAILABLE_MESSAGE)
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('does not send a production report when the authoritative task omits report action', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [
            {
              operationTaskId: 'ot-done',
              workOrderId: 'wo-1',
              status: 'Completed',
              allowedActions: [],
            },
          ],
          total: 1,
        },
      },
    } as never)
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!

    await expect(
      recordReport({
        workOrderId: 'wo-1',
        operationTaskId: 'ot-done',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: 'report-done',
      }),
    ).rejects.toThrow('当前工序不可报工')
    expect(mutateAsync).not.toHaveBeenCalled()
  })

  it('fails closed before report mutation when frozen write scope cannot pass reportable readback', async () => {
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockRejectedValueOnce({
      status: 403,
      message: 'scope not readable',
    })
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!

    await expect(
      recordReport({
        workOrderId: 'wo-write-only',
        operationTaskId: 'ot-write-only',
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
    expect(mutateAsync).not.toHaveBeenCalled()
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
      scopeKind: 'organization',
      scopeId: 'EVIL',
      reportedAtUtc: '1999-01-01T00:00:00.000Z',
    } as never)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')
    const payload = mutateAsync!.mock.calls[0][0]
    // org/env + timestamp injected LAST from principal scope — hostile caller values lose
    expect(payload.body.organizationId).toBe('org-001')
    expect(payload.body.environmentId).toBe('env-dev')
    expect(payload.body.scopeKind).toBe('self')
    expect(payload.body.scopeId).toBe('user-001')
    expect(payload.body.reportedAtUtc).not.toBe('1999-01-01T00:00:00.000Z')
    // the idempotency key is now the caller's responsibility — it passes through verbatim
    expect(payload.body.idempotencyKey).toBe('op-report-stable')
  })

  it('clears a report intent after a determinate 422 so the next attempt uses a new key', async () => {
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    mutateAsync
      .mockRejectedValueOnce({ status: 422, message: 'invalid report' })
      .mockResolvedValueOnce({ success: true, data: {} })
    const intent = {
      workOrderId: 'wo-report-determinate',
      operationTaskId: 'ot-report-determinate',
      goodQuantity: 4,
      scrapQuantity: 0,
      completesOperation: false,
    } as const

    await expect(recordReport({ ...intent, idempotencyKey: 'report-key-1' })).rejects.toMatchObject(
      { status: 422 },
    )
    await recordReport({ ...intent, idempotencyKey: 'report-key-2' })

    expect(mutateAsync.mock.calls[0][0].body.idempotencyKey).toBe('report-key-1')
    expect(mutateAsync.mock.calls[1][0].body.idempotencyKey).toBe('report-key-2')
  })

  it('keeps the frozen reporting time and template while successful label preparation is pending', async () => {
    const { recordReport } = useMesProductionReports()
    const mutate = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    mutate.mockResolvedValueOnce({ success: true, data: { printingPreparationPending: true } })
    const input = {
      workOrderId: 'wo-label-pending',
      operationTaskId: 'ot-label-pending',
      goodQuantity: 2,
      scrapQuantity: 0,
      completesOperation: false,
      labelTemplateId: 'tpl-1',
      idempotencyKey: 'labels-original',
    }
    await recordReport(input)
    await recordReport({ ...input, idempotencyKey: 'labels-retry' })
    expect(mutate.mock.calls[1][0].body).toEqual(mutate.mock.calls[0][0].body)
  })

  it('marks an initial preflight failure as not accepted and permits a corrected new intent', async () => {
    const { recordReport } = useMesProductionReports()
    const mutate = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockRejectedValueOnce(
      new TypeError('preflight unavailable'),
    )
    const input = {
      workOrderId: 'wo-preflight-rejected',
      operationTaskId: 'ot-preflight-rejected',
      goodQuantity: 2,
      scrapQuantity: 0,
      completesOperation: false,
      idempotencyKey: 'not-dispatched',
    }
    await expect(recordReport(input)).rejects.toMatchObject({ reportNotAccepted: true })
    expect(mutate).not.toHaveBeenCalled()
    await recordReport({ ...input, idempotencyKey: 'corrected-new-key' })
    expect(mutate.mock.calls[0][0].body.idempotencyKey).toBe('corrected-new-key')
  })

  it.each(['unknown', 'accepted'])(
    'retains the %s original wire intent when its retry receives 400',
    async (state) => {
      const { recordReport } = useMesProductionReports()
      const mutate = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
      if (state === 'unknown') mutate.mockRejectedValueOnce(new TypeError('response lost'))
      else
        mutate.mockResolvedValueOnce({ success: true, data: { printingPreparationPending: true } })
      mutate.mockRejectedValueOnce({ status: 400, message: '标签模板已停用' })
      const input = {
        workOrderId: `wo-retain-${state}`,
        operationTaskId: `ot-retain-${state}`,
        goodQuantity: 2,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: 'original-possibly-accepted',
      }
      if (state === 'unknown') await expect(recordReport(input)).rejects.toThrow('response lost')
      else await recordReport(input)
      await expect(recordReport(input)).rejects.toMatchObject({ status: 400 })
      await recordReport({ ...input, idempotencyKey: 'must-not-replace-original' })
      expect(mutate.mock.calls[2][0].body).toEqual(mutate.mock.calls[0][0].body)
    },
  )

  it('does not release the frozen wire intent when a dispatched report response arrives after navigation', async () => {
    const { recordReport } = useMesProductionReports()
    const mutate = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    const response = deferred<{ success: boolean; data: { printingPreparationPending: boolean } }>()
    mutate.mockReturnValueOnce(response.promise)
    const input = {
      workOrderId: 'wo-late-response',
      operationTaskId: 'ot-late-response',
      goodQuantity: 2,
      scrapQuantity: 0,
      completesOperation: false,
      labelTemplateId: 'tpl-1',
      idempotencyKey: 'original-late-key',
    }
    let current = true
    const request = recordReport(input, () => current)
    const outcome = expect(request).rejects.toMatchObject({ indeterminate: true })
    await vi.waitFor(() => expect(mutate).toHaveBeenCalledTimes(1))
    current = false
    response.resolve({ success: true, data: { printingPreparationPending: false } })
    await outcome
    current = true
    await recordReport({ ...input, idempotencyKey: 'must-not-replace-key' }, () => current)
    expect(mutate.mock.calls[1][0].body).toEqual(mutate.mock.calls[0][0].body)
  })

  it.each(['principalId', 'organizationId', 'environmentId'])(
    'blocks a report if %s changes during task preflight',
    async (field) => {
      const { recordReport } = useMesProductionReports()
      const pending =
        deferred<Awaited<ReturnType<typeof listBusinessConsoleMesReportableOperationTasks>>>()
      vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockReturnValueOnce(
        pending.promise as never,
      )
      const mutation = recordReport({
        workOrderId: 'wo-context-switch',
        operationTaskId: 'ot-context-switch',
        goodQuantity: 1,
        scrapQuantity: 0,
        completesOperation: false,
        idempotencyKey: `context-${field}`,
      })
      const outcome = expect(mutation).rejects.toThrow('作业身份或范围已变化')
      reactiveAuthState.principal = { ...reactiveAuthState.principal, [field]: 'changed' }
      await nextTick()
      pending.resolve({
        data: {
          success: true,
          data: {
            items: [
              {
                workOrderId: 'wo-context-switch',
                operationTaskId: 'ot-context-switch',
                allowedActions: ['report'],
                status: 'InProgress',
              },
            ],
            total: 1,
          },
        },
      } as never)
      await outcome
      expect(
        coladaState.mutateById.get('recordBusinessConsoleMesProductionReport'),
      ).not.toHaveBeenCalled()
    },
  )

  it.each(['scope', 'pair'])(
    'blocks a report when the %s changes during task preflight',
    async (change) => {
      const { recordReport } = useMesProductionReports()
      const pending = deferred<never>()
      vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockReturnValueOnce(pending.promise)
      let samePair = true
      const mutation = recordReport(
        {
          workOrderId: 'wo-scope-switch',
          operationTaskId: 'ot-scope-switch',
          goodQuantity: 1,
          scrapQuantity: 0,
          completesOperation: false,
          idempotencyKey: `context-${change}`,
        },
        () => samePair,
      )
      const outcome = expect(mutation).rejects.toThrow('作业身份或范围已变化')
      if (change === 'pair') samePair = false
      else
        coladaState.queryDataRefById.get(
          'getBusinessConsolePrincipalWorkContext:business.mes.reporting.write',
        )!.value = { success: true, data: { selectedScope: { kind: 'work-center', id: 'WC-B' } } }
      await nextTick()
      pending.resolve({
        data: {
          success: true,
          data: {
            items: [
              {
                workOrderId: 'wo-scope-switch',
                operationTaskId: 'ot-scope-switch',
                allowedActions: ['report'],
                status: 'InProgress',
              },
            ],
            total: 1,
          },
        },
      } as never)
      await outcome
      expect(
        coladaState.mutateById.get('recordBusinessConsoleMesProductionReport'),
      ).not.toHaveBeenCalled()
    },
  )

  it('restores all required report fields when a pending intent has no payload snapshot', async () => {
    const input = {
      workOrderId: 'wo-report-missing-snapshot',
      operationTaskId: 'ot-report-missing-snapshot',
      goodQuantity: 7,
      scrapQuantity: 1,
      completesOperation: false,
      idempotencyKey: 'report-missing-snapshot-key',
    }
    const { idempotencyKey, ...payload } = input
    acquirePendingBusinessIntent(
      {
        principalId: 'unrestored-session',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationType: 'mes.production-report.record',
        payloadFingerprint: JSON.stringify({
          ...payload,
          scopeKind: 'self',
          scopeId: 'user-001',
        }),
      },
      () => idempotencyKey,
    )
    const { recordReport } = useMesProductionReports()

    await recordReport(input)

    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    expect(mutateAsync.mock.calls[0][0].body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: input.workOrderId,
      operationTaskId: input.operationTaskId,
      goodQuantity: input.goodQuantity,
      scrapQuantity: input.scrapQuantity,
      completesOperation: false,
      idempotencyKey,
      reportedAtUtc: expect.any(String),
      scopeKind: 'self',
      scopeId: 'user-001',
    })
  })

  it('replays only the same issued report-complete key after the task became completed', async () => {
    const { recordReport } = useMesProductionReports()
    const mutateAsync = coladaState.mutateById.get('recordBusinessConsoleMesProductionReport')!
    mutateAsync
      .mockRejectedValueOnce(new TypeError('response lost'))
      .mockResolvedValueOnce(undefined)
    const originalIntent = {
      workOrderId: 'wo-1',
      operationTaskId: 'ot-1',
      goodQuantity: 4,
      scrapQuantity: 0,
      completesOperation: true,
      idempotencyKey: 'report-complete-replay',
    }

    await expect(recordReport(originalIntent)).rejects.toThrow('response lost')
    vi.mocked(listBusinessConsoleMesReportableOperationTasks).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ operationTaskId: 'ot-1', workOrderId: 'wo-1', status: 'Completed' }],
          total: 1,
        },
      },
    } as never)

    await expect(recordReport(originalIntent)).resolves.toBeUndefined()
    await expect(
      recordReport({ ...originalIntent, idempotencyKey: 'report-complete-new-intent' }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(mutateAsync).toHaveBeenCalledTimes(2)
  })
})
