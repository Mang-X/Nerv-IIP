import { describe, expect, it } from 'vitest'
import {
  acceptBusinessConsoleSchedulingFirstPlanJob,
  getBusinessConsoleSchedulingFirstPlanJob,
  type BusinessConsoleSchedulingFirstPlanInput,
  type BusinessConsoleSchedulingFirstPlanJob,
} from '@nerv-iip/api-client'

describe('异步首版作业稳定客户端', () => {
  it('提交 500 单并按作业身份与组织环境读取服务终态', async () => {
    const input: BusinessConsoleSchedulingFirstPlanInput = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      horizonStartUtc: '2026-10-07T00:00:00Z',
      horizonEndUtc: '2026-10-09T00:00:00Z',
      contractVersion: 1,
      orders: Array.from({ length: 500 }, (_, index) => ({
        workOrderId: `WO-${index + 1}`,
        priority: index % 5,
        isRush: false,
      })),
    }
    const job: BusinessConsoleSchedulingFirstPlanJob = {
      jobId: '9d2f9640-0dfe-42a5-9876-2400bd620eba',
      status: 'created',
      input,
      createdAtUtc: input.horizonStartUtc,
      planId: null,
      failureReason: null,
    }
    const requests: Request[] = []
    const fetch: typeof globalThis.fetch = async (request) => {
      const value = request as Request
      requests.push(value)
      if (value.method === 'POST') {
        expect(await value.json()).toEqual(input)
        return Response.json({ data: job }, { status: 202 })
      }
      return Response.json({ data: job })
    }
    const options = { baseUrl: 'http://gateway.local', fetch, throwOnError: true as const }
    const accepted = await acceptBusinessConsoleSchedulingFirstPlanJob({ ...options, body: input })
    expect(accepted.response.status).toBe(202)
    expect(accepted.data.data).toEqual(job)
    for (const status of ['running', 'completed', 'failed'] as const) {
      job.status = status
      job.startedAtUtc = '2026-10-07T00:00:01Z'
      job.finishedAtUtc = status === 'running' ? null : '2026-10-07T00:00:02Z'
      job.planId = status === 'completed' ? 'plan-001' : null
      job.failureReason = status === 'failed' ? '工艺路线不可用，请重新选择工单。' : null
      const result = await getBusinessConsoleSchedulingFirstPlanJob({
        ...options,
        path: { jobId: job.jobId! },
        query: { organizationId: input.organizationId!, environmentId: input.environmentId! },
      })
      expect(result.data.data).toEqual(job)
    }
    expect(requests[0].url).toBe(
      'http://gateway.local/api/business-console/v1/scheduling/workbench/first-plan-jobs',
    )
    for (const request of requests.slice(1)) {
      expect(request.method).toBe('GET')
      const url = new URL(request.url)
      expect(url.pathname).toBe(
        `/api/business-console/v1/scheduling/workbench/first-plan-jobs/${job.jobId}`,
      )
      expect(Object.fromEntries(url.searchParams)).toEqual({
        organizationId: input.organizationId,
        environmentId: input.environmentId,
      })
    }
  })
})
