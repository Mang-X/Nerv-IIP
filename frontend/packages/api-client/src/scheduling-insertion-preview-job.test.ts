import { describe, expect, it } from 'vitest'
import {
  acceptBusinessConsoleSchedulingInsertionPreviewJob,
  getBusinessConsoleSchedulingInsertionPreviewJob,
  type BusinessConsoleSchedulingInsertionPreviewRequest,
  type BusinessConsoleSchedulingInsertionPreviewJob,
} from '@nerv-iip/api-client'

describe('插单预览稳定客户端', () => {
  // #4162 PublicContract：稳定入口贯通受理、真实状态和未保存的完整结果。
  it.each([null, '2026-10-08T12:00:00Z'])(
    '保留详细候选、影响、快照和承诺 %s',
    async (promiseUtc) => {
      const request: BusinessConsoleSchedulingInsertionPreviewRequest = {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        planId: 'plan-001',
        workOrderId: 'WO-12',
        contractVersion: 1,
      }
      const job: BusinessConsoleSchedulingInsertionPreviewJob = {
        jobId: '9d2f9640-0dfe-42a5-9876-2400bd620eba',
        status: 'created',
        input: {
          ...request,
          workOrderIds: Array.from({ length: 12 }, (_, i) => `WO-${i + 1}`),
          horizonStartUtc: '2026-10-07T00:00:00Z',
          horizonEndUtc: '2026-10-09T00:00:00Z',
        },
        createdAtUtc: '2026-10-07T00:00:00Z',
        preview: null,
        failureReason: null,
      }
      const requests: Request[] = []
      const fetch: typeof globalThis.fetch = async (value) => {
        const incoming = value as Request
        requests.push(incoming)
        if (incoming.method === 'POST') {
          expect(await incoming.json()).toEqual(request)
          return Response.json({ success: true, data: job }, { status: 202 })
        }
        return Response.json({ success: true, data: job })
      }
      const options = { baseUrl: 'http://gateway.local', fetch, throwOnError: true as const }
      const accepted = await acceptBusinessConsoleSchedulingInsertionPreviewJob({
        ...options,
        body: request,
      })
      expect(accepted.response.status).toBe(202)
      expect(accepted.data.data).toEqual(job)
      for (const status of ['running', 'completed', 'failed'] as const) {
        job.status = status
        job.startedAtUtc = '2026-10-07T00:00:01Z'
        job.finishedAtUtc = status === 'running' ? null : '2026-10-07T00:00:02Z'
        job.preview =
          status === 'completed'
            ? {
                planId: 'preview-job-001',
                status: 'preview',
                assignments: Array.from({ length: 11 }, (_, i) => ({ orderId: `WO-${i + 1}` })),
                unscheduledOperations: [
                  {
                    orderId: 'WO-12',
                    operationId: 'OP-12',
                    reasonCode: 'capacity',
                    message: '资源容量不足',
                  },
                ],
                metrics: { scheduledOperationCount: 11, unscheduledOperationCount: 1 },
                freezeContext: {
                  asOfUtc: '2026-10-07T00:00:00Z',
                  defaultWindowEndUtc: '2026-10-07T04:00:00Z',
                  workCenterWindows: [],
                  assignments: [{ assignment: { orderId: 'WO-1' }, reasons: ['stableWindow'] }],
                },
                conflicts: [{ orderId: 'WO-12', reasonCode: 'capacity', message: '资源容量不足' }],
              }
            : null
        job.acceptedBaseline = {
          baseline: { planId: request.planId, status: 'generated', assignments: [] },
          problem: {
            contractVersion: 1,
            problemId: 'problem-001',
            organizationId: request.organizationId!,
            environmentId: request.environmentId!,
          },
        }
        job.result =
          status === 'completed'
            ? {
                contractVersion: 1,
                baselinePlanId: request.planId,
                candidatePlanId: job.preview!.planId,
                inputFingerprint: 'owner-fingerprint',
                candidate: job.preview!,
                promiseUtc,
                failures: promiseUtc
                  ? []
                  : ['unknownMaterialEta', 'incompleteChain', 'blockingConflict'],
                snapshot: {
                  problem: job.acceptedBaseline!.problem,
                  baseline: job.acceptedBaseline!.baseline,
                  calculationBaseline: job.acceptedBaseline!.baseline,
                  freeze: job.preview!.freezeContext!,
                  execution: [
                    {
                      orderId: 'WO-1',
                      operationId: 'OP-1',
                      actualStartedAtUtc: '2026-10-07T00:00:00Z',
                    },
                  ],
                  fixedReservations: [],
                  materialMode: 'hard',
                  qualityMode: 'soft',
                  equipmentUnknownMode: 'soft',
                },
                orders: [
                  {
                    orderId: 'WO-1',
                    status: 'delayed',
                    isNew: false,
                    baselineCompletionUtc: '2026-10-07T08:00:00Z',
                    candidateCompletionUtc: '2026-10-08T08:00:00Z',
                    delayDays: 1,
                    baselineLate: false,
                    candidateLate: true,
                    newlyLate: true,
                  },
                  {
                    orderId: 'WO-12',
                    status: 'unscheduled',
                    isNew: true,
                    candidateCompletionUtc: null,
                    delayDays: null,
                  },
                ],
                operations: [
                  {
                    orderId: 'WO-1',
                    operationId: 'OP-1',
                    dueUtc: '2026-10-07T10:00:00Z',
                    baseline: { orderId: 'WO-1' },
                    candidate: job.preview!.assignments![0],
                    reasonCodes: ['capacity'],
                    sourceReference: 'owner-reference',
                    paths: [
                      [
                        {
                          fromOrderId: 'WO-12',
                          fromOperationId: 'OP-12',
                          toOrderId: 'WO-1',
                          toOperationId: 'OP-1',
                          reasonCode: 'capacity',
                          capacityUnits: 1,
                          competitionWindow: {
                            startUtc: '2026-10-07T00:00:00Z',
                            endUtc: '2026-10-07T01:00:00Z',
                          },
                        },
                      ],
                    ],
                  },
                ],
                kpis: {
                  onTimeRate: {
                    baseline: 0.8,
                    candidate: 0.6,
                    delta: -0.2,
                    baselineDenominator: 10,
                    candidateDenominator: 10,
                  },
                  lateOrderCount: { baseline: 1, candidate: 2, delta: 1 },
                  movedOperationCount: 3,
                  resourceUtilization: { baseline: 0.4, candidate: 0.7, delta: 0.3 },
                  unscheduledOperationCount: { baseline: 0, candidate: 1, delta: 1 },
                  lockRetention: { preserved: 1, total: 1, notPreserved: [] },
                },
              }
            : null
        job.failureReason =
          status === 'failed' ? '历史方案缺少必要问题快照，请重新生成方案。' : null
        const result = await getBusinessConsoleSchedulingInsertionPreviewJob({
          ...options,
          path: { jobId: job.jobId! },
          query: { organizationId: request.organizationId!, environmentId: request.environmentId! },
        })
        expect(result.data.data).toEqual(job)
      }
      expect(requests[0].url).toBe(
        'http://gateway.local/api/business-console/v1/scheduling/workbench/insertion-preview-jobs',
      )
      for (const incoming of requests.slice(1)) {
        expect(incoming.method).toBe('GET')
        const url = new URL(incoming.url)
        expect(url.pathname).toBe(
          `/api/business-console/v1/scheduling/workbench/insertion-preview-jobs/${job.jobId}`,
        )
        expect(Object.fromEntries(url.searchParams)).toEqual({
          organizationId: request.organizationId!,
          environmentId: request.environmentId!,
        })
      }
    },
  )
})
