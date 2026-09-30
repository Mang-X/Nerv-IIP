import { describe, expect, it } from 'vitest'
import {
  getBusinessConsolePlanningMaterialDeliveries,
  type BusinessConsoleMaterialDeliveriesResponse,
} from '@nerv-iip/api-client'

describe('物料交付稳定客户端', () => {
  it('传递显式方案，并保留四日期、净缺口、来源和历史未知事实', async () => {
    const facts: BusinessConsoleMaterialDeliveriesResponse = {
      runId: 'run-1',
      planId: 'plan /1',
      evaluatedAtUtc: '2026-10-01T00:00:00Z',
      supplyCoverageScope: 'independent-per-net-requirement',
      items: [
        {
          netRequirementReference: 'net-1',
          netRequirementQuantity: 26,
          latestProcurementDate: '2026-10-02',
          latestProcurementUtc: '2026-10-02T00:00:00Z',
          expectedArrivalDate: '2026-10-04',
          expectedArrivalUtc: '2026-10-04T00:00:00Z',
          expectedStartUtc: '2026-10-05T08:00:00Z',
          latestStartUtc: '2026-10-06T08:00:00Z',
          coveredQuantity: 20,
          uncoveredQuantity: 6,
          status: 'yellow',
          reasons: ['supply-insufficient'],
          demandSources: [
            { sourceReference: 'SO-1', sourceLineReference: '1' },
            { sourceReference: 'SO-2', sourceLineReference: '2' },
          ],
          supplySources: [
            {
              purchaseOrderNo: 'PO-1',
              sources: [{ purchaseRequisitionNo: 'PR-1', suggestionId: 'suggestion-1' }],
            },
          ],
          schedulingSources: [
            {
              workOrderId: 'wo-1',
              operations: [
                {
                  earliestStartUtc: '2026-10-01T00:00:00Z',
                  assignmentStartUtc: '2026-10-05T08:00:00Z',
                },
              ],
            },
          ],
          suggestionSources: [{ suggestionId: 'suggestion-1' }, { suggestionId: 'suggestion-2' }],
        },
      ],
      unknownRequirementSuggestions: [
        {
          reason: 'net-requirement-identity-unknown',
          suggestionSource: { suggestionId: 'old' },
          rawNetRequirementSource: { netRequirementQuantity: 26 },
        },
      ],
    }
    const fetch: typeof globalThis.fetch = async (input) => {
      const request = input as Request
      const url = new URL(request.url)
      expect(url.pathname).toBe(
        '/api/business-console/v1/planning/mrp-runs/run-1/material-deliveries',
      )
      expect(url.searchParams.get('planId')).toBe('plan /1')
      expect(url.searchParams.get('organizationId')).toBe('org-1')
      expect(url.searchParams.get('environmentId')).toBe('env-1')
      return Response.json({ data: facts })
    }
    const result = await getBusinessConsolePlanningMaterialDeliveries({
      baseUrl: 'http://gateway.local',
      fetch,
      throwOnError: true,
      path: { runId: 'run-1' },
      query: { organizationId: 'org-1', environmentId: 'env-1', planId: 'plan /1' },
    })
    expect(result.data.data).toEqual(facts)
  })
})
