import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const pageContracts = [
  ['wms/inbound.vue', 'inboundOrders'],
  ['wms/outbound.vue', 'outboundOrders'],
  ['wms/putaway.vue', 'putawayTasks'],
  ['wms/picking.vue', 'pickingTasks'],
  ['wms/counts.vue', 'countExecutions'],
  ['mes/operation-tasks.vue', 'operationTasks'],
  ['quality/inspection-tasks.vue', 'tasks'],
  ['maintenance/work-orders.vue', 'workOrders'],
  ['equipment/alarms.vue', 'alarms'],
] as const

describe('scope list response-state wiring', () => {
  it.each(pageContracts)(
    '%s only declares a business empty state after a successful envelope',
    (relativePath, prefix) => {
      const source = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8')

      expect(source).toContain(`:${'empty'}="${prefix}HasSuccessfulResponse`)
      expect(source).toContain(`:${'failed'}="${prefix}HasFailedResponse"`)
    },
  )
})
