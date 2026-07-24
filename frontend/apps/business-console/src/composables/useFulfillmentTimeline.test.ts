import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
} from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  classifyFulfillmentFailure,
  FulfillmentNodeError,
  matchDeliveryOrders,
  matchDemandSource,
  normalizeScope,
  resolveRecordNode,
} from './useFulfillmentTimeline'

describe('classifyFulfillmentFailure', () => {
  it('maps 403 to restricted', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(403))).toEqual({
      status: 'restricted',
    })
  })

  it('maps 409 to failed/conflict', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(409))).toEqual({
      status: 'failed',
      failureKind: 'conflict',
    })
  })

  it('maps network and gateway-timeout to failed/timeout', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError('network'))).toEqual({
      status: 'failed',
      failureKind: 'timeout',
    })
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(504))).toEqual({
      status: 'failed',
      failureKind: 'timeout',
    })
  })

  it('maps other errors to failed/error', () => {
    expect(classifyFulfillmentFailure(new FulfillmentNodeError(500))).toEqual({
      status: 'failed',
      failureKind: 'error',
    })
  })

  it('reads a bare status field defensively', () => {
    expect(classifyFulfillmentFailure({ status: 403 })).toEqual({ status: 'restricted' })
    expect(classifyFulfillmentFailure({ statusCode: 409 })).toEqual({
      status: 'failed',
      failureKind: 'conflict',
    })
  })
})

describe('normalizeScope', () => {
  it('trims and blanks empty scope', () => {
    expect(normalizeScope('  SO-1 ')).toBe('SO-1')
    expect(normalizeScope('   ')).toBeUndefined()
    expect(normalizeScope(null)).toBeUndefined()
    expect(normalizeScope(undefined)).toBeUndefined()
  })
})

describe('matchDemandSource', () => {
  const items: BusinessConsoleDemandSourceItem[] = [
    { demandSourceId: 'd1', sourceReference: 'SO-OTHER', sourceStatus: 'active' },
    { demandSourceId: 'd2', sourceReference: 'SO-1', sourceStatus: 'released' },
  ]

  it('matches by sourceReference === salesOrderNo', () => {
    expect(matchDemandSource(items, 'SO-1')?.demandSourceId).toBe('d2')
  })

  it('never guesses by similar codes and suppresses empty scope', () => {
    expect(matchDemandSource(items, 'SO-2')).toBeUndefined()
    expect(matchDemandSource(items, undefined)).toBeUndefined()
  })
})

describe('matchDeliveryOrders', () => {
  const items: BusinessConsoleErpDeliveryOrderItem[] = [
    { deliveryOrderNo: 'DO-1', salesOrderNo: 'SO-1', status: 'released' },
    { deliveryOrderNo: 'DO-2', salesOrderNo: 'SO-OTHER', status: 'released' },
  ]

  it('filters by salesOrderNo', () => {
    expect(matchDeliveryOrders(items, 'SO-1').map((d) => d.deliveryOrderNo)).toEqual(['DO-1'])
  })

  it('returns empty for empty scope', () => {
    expect(matchDeliveryOrders(items, undefined)).toEqual([])
  })
})

describe('resolveRecordNode — four-state machine', () => {
  const base = {
    key: 'delivery-order' as const,
    title: '发货单',
    present: (record: { deliveryOrderNo?: string; status?: string }) => ({
      businessNo: record.deliveryOrderNo,
      detailStatus: record.status,
    }),
    pendingNote: '尚未产生规则说明',
    source: 'ERP · 发货单读面',
  }

  it('established: exposes business number and drill fields', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: undefined,
      record: { deliveryOrderNo: 'DO-1', status: 'released' },
    })
    expect(node.status).toBe('established')
    expect(node.businessNo).toBe('DO-1')
    expect(node.detailStatus).toBe('released')
  })

  it('pending (no scope): empty scope shows rule note, not established', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: false,
      loading: false,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('pending')
    expect(node.ruleNote).toBe('尚未产生规则说明')
  })

  it('pending (fetched, no record): distinct empty state', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('pending')
    expect(node.ruleNote).toBe('尚未产生规则说明')
  })

  it('loading: while fetching with no record yet', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: true,
      error: undefined,
      record: undefined,
    })
    expect(node.status).toBe('loading')
  })

  it('restricted: a 403 on a single source does not leak data', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(403),
      record: undefined,
    })
    expect(node.status).toBe('restricted')
    expect(node.businessNo).toBeUndefined()
  })

  it('failed: a single-source error carries a distinguishable failure kind', () => {
    const conflict = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(409),
      record: undefined,
    })
    expect(conflict.status).toBe('failed')
    expect(conflict.failureKind).toBe('conflict')

    const timeout = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError('network'),
      record: undefined,
    })
    expect(timeout.failureKind).toBe('timeout')
  })

  it('error wins even if a stale record is present (failure is not faked as empty)', () => {
    const node = resolveRecordNode({
      ...base,
      enabled: true,
      loading: false,
      error: new FulfillmentNodeError(500),
      record: { deliveryOrderNo: 'DO-1' },
    })
    expect(node.status).toBe('failed')
    expect(node.failureKind).toBe('error')
  })
})
