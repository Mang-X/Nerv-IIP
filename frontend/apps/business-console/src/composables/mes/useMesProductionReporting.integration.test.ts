import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'

import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesProductionReporting, useMesWorkScopeSelection } from '.'

const writeScope = { kind: 'work-center', id: 'WC-A' }
const readScope = { kind: 'team', id: 'TEAM-A' }
const otherReadScope = { kind: 'team', id: 'TEAM-B' }
const mes = '/api/business-console/v1/mes'
const orderPath = `${mes}/work-orders/WO-100`
const task = {
  operationTaskId: 'OP-20',
  status: 'InProgress',
  workCenterId: 'WC-A',
  teamId: 'TEAM-A',
}

function json(data: unknown) {
  return Response.json({ success: true, data })
}

function forbidden() {
  return Response.json({ success: false, code: 403, message: 'Forbidden.' }, { status: 403 })
}

type Mode = 'readable' | 'no-permission' | 'no-receipts' | 'no-scope' | 'denied' | 'pending'

async function mountReporting(mode: Mode) {
  const permissions: string[] = [P.mesReportingWrite, P.mesReceiptsRead]
  if (mode !== 'no-receipts') permissions.push(P.mesReceiptsManage)
  if (mode !== 'no-permission') permissions.push(P.mesWorkOrdersRead)
  const readScopeProbes: string[] = []
  const detailQueries: Array<[string | null, string | null]> = []
  const writeScopes: Array<[string, string]> = []
  let finishScopeRead!: () => void
  const pendingScope = new Promise<void>((resolve) => {
    finishScopeRead = resolve
  })
  configureApiClient({
    baseUrl: 'http://business-gateway.test',
    fetch: async (input) => {
      const request = new Request(input)
      const { pathname, searchParams } = new URL(request.url)
      if (pathname.endsWith('/me/work-context')) {
        const permission = searchParams.get('permissionCode')
        if (permission === P.mesReportingWrite) {
          return json({ authorizedScopes: [writeScope], selectedScope: writeScope })
        }
        if (permission === P.mesWorkOrdersRead) {
          readScopeProbes.push(permission)
          if (!permissions.includes(permission)) return forbidden()
          if (mode === 'no-scope') return json({ authorizedScopes: [], selectedScope: null })
          if (mode === 'pending') await pendingScope
          const selectedScope =
            searchParams.get('scopeId') === 'TEAM-B' ? otherReadScope : readScope
          return json({ authorizedScopes: [readScope, otherReadScope], selectedScope })
        }
      }
      if (pathname === `${mes}/reportable-operation-tasks`) {
        if (
          searchParams.get('scopeKind') !== 'work-center' ||
          searchParams.get('scopeId') !== 'WC-A'
        )
          return forbidden()
        return json({ items: [task], total: 1 })
      }
      if (pathname === `${mes}/production-reports` && request.method === 'POST') {
        const body = await request.json()
        writeScopes.push([body.scopeKind, body.scopeId])
        if (body.scopeKind !== 'work-center' || body.scopeId !== 'WC-A') return forbidden()
        return json({
          productionReportId: 'report-100',
          reportNo: 'PR-100',
          operationReceipt: {
            operationType: 'mes.production-report.record',
            authority: 'mes',
            resourceType: 'production-report',
            resourceId: 'report-100',
            idempotencyKey: body.idempotencyKey,
            outcome: 'confirmed',
            stateConfirmed: true,
            readbackRequired: false,
            changedAtUtc: '2026-11-20T00:00:00Z',
            resourceStatus: 'Completed',
          },
        })
      }
      if (pathname === `${orderPath}/produced-lots`) {
        return json({
          items: [{ reportNo: 'PR-100', operationTaskId: 'OP-20', remainingQuantity: 1 }],
        })
      }
      if (pathname === orderPath) {
        const kind = searchParams.get('scopeKind')
        const id = searchParams.get('scopeId')
        detailQueries.push([kind, id])
        if (
          mode === 'denied' ||
          !permissions.includes(P.mesWorkOrdersRead) ||
          kind !== 'team' ||
          id !== task.teamId
        )
          return forbidden()
        return json({ workOrderId: 'WO-100', skuId: 'SKU-001' })
      }
      throw new Error(`Unexpected request: ${pathname}`)
    },
  })

  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().principal = {
    principalId: `reporter-${mode}`,
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionCodes: permissions,
  }
  useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })

  let reporting!: ReturnType<typeof useMesProductionReporting>
  let selection!: ReturnType<typeof useMesWorkScopeSelection>
  const wrapper = mount(
    defineComponent({
      setup() {
        reporting = useMesProductionReporting()
        if (mode === 'readable') selection = useMesWorkScopeSelection(P.mesWorkOrdersRead)
        return () => null
      },
    }),
    { global: { plugins: [pinia, PiniaColada] } },
  )
  await flushPromises()
  return {
    reporting,
    selection,
    wrapper,
    detailQueries,
    writeScopes,
    readScopeProbes,
    finishScopeRead,
  }
}

describe('MES 报工后的完工入库交接（真实 SDK，HTTP 夹具）', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => configureApiClient())

  it.each(['readable', 'no-permission', 'no-receipts', 'no-scope', 'denied', 'pending'] as const)(
    '保持报工写范围，独立核对工单读范围（%s）',
    async (mode) => {
      const {
        reporting,
        selection,
        wrapper,
        detailQueries,
        writeScopes,
        readScopeProbes,
        finishScopeRead,
      } = await mountReporting(mode)
      try {
        const readHandoff = reporting.prepareReceiptHandoff()
        const result = await reporting.recordProductionReport({
          workOrderId: 'WO-100',
          operationTaskId: 'OP-20',
          goodQuantity: 1,
          completesOperation: true,
          idempotencyKey: 'receipt-handoff-report',
        })
        expect(result.data?.reportNo).toBe('PR-100')
        expect(writeScopes).toEqual([['work-center', 'WC-A']])
        if (mode === 'no-permission' || mode === 'no-receipts') {
          expect(readScopeProbes).toEqual([])
        } else {
          expect(readScopeProbes).toContain(P.mesWorkOrdersRead)
        }

        const handoff = await readHandoff?.('WO-100', 'OP-20', 'PR-100')
        if (mode === 'readable') {
          expect(detailQueries).toEqual([['team', 'TEAM-A']])
          expect(handoff).toMatchObject({ workOrderId: 'WO-100', skuId: 'SKU-001' })
          expect(handoff?.isCurrent()).toBe(true)
          selection.scopeSelectionValue.value = 'team:TEAM-B'
          expect(handoff?.isCurrent()).toBe(false)
          await flushPromises()
          expect(selection.selectedScope.value?.id).toBe('TEAM-B')
          expect(handoff?.isCurrent()).toBe(false)
        } else {
          expect(handoff).toBeUndefined()
          expect(detailQueries).toEqual(mode === 'denied' ? [['team', 'TEAM-A']] : [])
        }
      } finally {
        finishScopeRead()
        wrapper.unmount()
      }
    },
  )
})
