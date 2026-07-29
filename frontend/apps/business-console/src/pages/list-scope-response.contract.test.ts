import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const pageContracts = [
  ['wms/inbound.vue', 'inboundOrders', 'inboundOrdersError'],
  ['wms/outbound.vue', 'outboundOrders', 'outboundOrdersError'],
  ['wms/putaway.vue', 'putawayTasks', 'putawayTasksError'],
  ['wms/picking.vue', 'pickingTasks', 'pickingTasksError'],
  ['wms/counts.vue', 'countExecutions', 'countExecutionsError'],
  ['mes/operation-tasks.vue', 'operationTasks', 'operationTasksError'],
  ['quality/inspection-tasks.vue', 'tasks', 'error'],
  ['maintenance/work-orders.vue', 'workOrders', 'workOrdersError'],
  ['equipment/alarms.vue', 'alarms', 'alarmsError'],
] as const

describe('scope list response-state wiring', () => {
  it.each(pageContracts)(
    '%s replaces a prior successful empty state with failure on refresh transport error',
    (relativePath, prefix, errorName) => {
      const source = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8')
      const emptyBinding = source.match(/:empty="([\s\S]*?)"/)?.[1].replace(/\s+/g, ' ')
      const failedBinding = source.match(/:failed="([\s\S]*?)"/)?.[1].replace(/\s+/g, ' ')

      expect(emptyBinding).toContain(`${prefix}HasSuccessfulResponse`)
      expect(emptyBinding).toContain(`!${errorName}`)
      expect(failedBinding).toContain(`${prefix}HasFailedResponse`)
      expect(failedBinding).toContain(`Boolean(${errorName})`)
    },
  )
})
