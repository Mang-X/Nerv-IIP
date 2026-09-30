import { readFileSync } from 'node:fs'
import { describe, expect, expectTypeOf, it } from 'vitest'
import {
  createBusinessConsoleSchedulingWorkbenchPlan,
  getBusinessConsoleSchedulingPlan,
  createBusinessConsoleSchedulingPlanRevision,
  type BusinessConsoleSchedulePlan,
  type BusinessConsoleSchedulingValidationContext,
  type BusinessConsoleSchedulingResourceContext,
  type BusinessConsoleSchedulingOperationContext,
  type BusinessConsoleSchedulingFixedReservation,
} from '@nerv-iip/api-client'

const resource: BusinessConsoleSchedulingResourceContext = {
  resourceId: 'res-001',
  workCenterId: 'wc-001',
  calendarId: 'cal-001',
  capacityUnits: 3,
  utilizationRate: 0.75,
}
const operation: BusinessConsoleSchedulingOperationContext = {
  orderId: 'order-001',
  operationId: 'op-002',
  predecessorOperationIds: ['op-001'],
  dueUtc: '2026-06-01T15:00:00Z',
  durationMinutes: 90,
  setupMinutes: 15,
  isFixed: true,
}
const reservation: BusinessConsoleSchedulingFixedReservation = {
  orderId: 'external-order',
  operationId: 'external-op',
  workCenterId: 'wc-001',
  startUtc: '2026-06-01T09:00:00Z',
  endUtc: '2026-06-01T10:00:00Z',
  resourceId: null,
}
const context: BusinessConsoleSchedulingValidationContext = {
  horizonStartUtc: '2026-06-01T08:00:00Z',
  horizonEndUtc: '2026-06-03T08:00:00Z',
  resources: [resource],
  operations: [operation],
  fixedReservations: [reservation],
}
const plan: BusinessConsoleSchedulePlan = { planId: 'plan-001', validationContext: context }

describe('Scheduling validation context stable consumption', () => {
  it('consumes the same frozen context from create, detail and revision SDK responses', async () => {
    const fetch: typeof globalThis.fetch = async (input) => {
      const request = input as Request
      return Response.json({
        data: request.url.endsWith('/revisions') ? { candidate: plan } : plan,
      })
    }
    const options = { baseUrl: 'http://gateway.local', fetch, throwOnError: true as const }
    const created = await createBusinessConsoleSchedulingWorkbenchPlan({
      ...options,
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        horizonStartUtc: context.horizonStartUtc!,
        horizonEndUtc: context.horizonEndUtc!,
        orders: [{ workOrderId: 'order-001', priority: 1, isRush: true }],
      },
    })
    const detail = await getBusinessConsoleSchedulingPlan({
      ...options,
      path: { planId: 'plan-001' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    const revision = await createBusinessConsoleSchedulingPlanRevision({
      ...options,
      path: { planId: 'plan-001' },
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        includedOrderIds: ['order-001'],
        lockedAssignments: [],
      },
    })
    for (const value of [
      created.data.data?.validationContext,
      detail.data.data?.validationContext,
      revision.data.data?.candidate?.validationContext,
    ]) {
      expectTypeOf(value).toEqualTypeOf<
        BusinessConsoleSchedulingValidationContext | null | undefined
      >()
      expect(value).toEqual(context)
    }
  })

  it('keeps all three OpenAPI response paths connected to the context schema', () => {
    const document = JSON.parse(readFileSync('openapi/business-gateway-console.v1.json', 'utf8'))
    const resolve = (schema: any): any => {
      if (schema.$ref) return resolve(document.components.schemas[schema.$ref.split('/').at(-1)])
      if (schema.allOf) return resolve(schema.allOf.at(-1))
      if (schema.oneOf) return resolve(schema.oneOf[0])
      return schema
    }
    const paths = [
      ['/api/business-console/v1/scheduling/workbench/plans', 'post', false],
      ['/api/business-console/v1/scheduling/plans/{planId}', 'get', false],
      ['/api/business-console/v1/scheduling/plans/{planId}/revisions', 'post', true],
    ] as const
    for (const [path, method, revision] of paths) {
      const envelope = resolve(
        document.paths[path][method].responses['200'].content['application/json'].schema,
      )
      const data = resolve(envelope.properties.data)
      const planSchema = revision ? resolve(data.properties.candidate) : data
      expect(resolve(planSchema.properties.validationContext).properties).toHaveProperty(
        'fixedReservations',
      )
      expect(resolve(planSchema.properties.validationContext).properties).toHaveProperty(
        'operations',
      )
      expect(resolve(planSchema.properties.validationContext).properties).toHaveProperty(
        'resources',
      )
    }
  })
})
