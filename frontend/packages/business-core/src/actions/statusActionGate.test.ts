import { describe, expect, it } from 'vitest'
import { statusActionGate, type LifecycleActionRequest } from './statusActionGate'

function gate(request: LifecycleActionRequest) {
  return statusActionGate(request)
}

describe('statusActionGate', () => {
  it.each([
    ['start', 'Queued'],
    ['pause', 'InProgress'],
    ['resume', 'Paused'],
    ['complete', 'InProgress'],
    ['report-complete', 'InProgress'],
  ] as const)('allows MES operation action %s only from its canonical state', (action, status) => {
    expect(gate({ domain: 'mes-operation-task', action, facts: { status } })).toMatchObject({
      known: true,
      terminal: false,
      executable: true,
      legalNoop: false,
      reason: 'allowed',
    })
  })

  it.each(['Ready', 'Running', 'Started', '', ' ', 'unexpected', null, undefined])(
    'fails closed for non-canonical MES operation status %s',
    (status) => {
      expect(gate({ domain: 'mes-operation-task', action: 'start', facts: { status } })).toEqual({
        known: false,
        terminal: false,
        executable: false,
        legalNoop: false,
        reason: 'unknown-status',
      })
    },
  )

  it.each(['Completed', 'Cancelled', 'ScheduleInvalidated'] as const)(
    'marks MES operation status %s as terminal and read-only',
    (status) => {
      expect(
        gate({ domain: 'mes-operation-task', action: 'start', facts: { status } }),
      ).toMatchObject({
        known: true,
        terminal: true,
        executable: false,
        legalNoop: false,
        reason: 'terminal-status',
      })
    },
  )

  it('marks a known but wrong MES operation phase as incompatible', () => {
    expect(
      gate({
        domain: 'mes-operation-task',
        action: 'complete',
        facts: { status: 'Paused' },
      }),
    ).toMatchObject({
      known: true,
      terminal: false,
      executable: false,
      reason: 'incompatible-state',
    })
  })

  it.each([
    ['release', 'created'],
    ['release', 'started'],
    ['release', 'hold'],
    ['hold', 'created'],
    ['hold', 'released'],
    ['cancel', 'created'],
    ['cancel', 'released'],
  ] as const)('allows MES work-order %s from %s', (action, status) => {
    expect(gate({ domain: 'mes-work-order', action, facts: { status } }).executable).toBe(true)
  })

  it('preserves cancelled work-order cancel as a legal no-op', () => {
    expect(
      gate({
        domain: 'mes-work-order',
        action: 'cancel',
        facts: { status: 'cancelled' },
      }),
    ).toMatchObject({
      known: true,
      terminal: true,
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
  })

  it.each(['completed', 'closed', 'cancelled', 'scrapped'] as const)(
    'keeps terminal MES work order %s read-only for hold',
    (status) => {
      expect(gate({ domain: 'mes-work-order', action: 'hold', facts: { status } })).toMatchObject({
        terminal: true,
        executable: false,
        legalNoop: false,
        reason: 'terminal-status',
      })
    },
  )

  it.each(['Requested', 'PartiallyReceived'] as const)(
    'allows line-side receipt from %s',
    (status) => {
      expect(
        gate({
          domain: 'mes-material-issue',
          action: 'confirm-receipt',
          facts: { status },
        }).executable,
      ).toBe(true)
    },
  )

  it.each(['Received', 'Cancelled', 'ReturnRequested', 'ReservationExpired'] as const)(
    'keeps material issue status %s terminal',
    (status) => {
      expect(
        gate({
          domain: 'mes-material-issue',
          action: 'confirm-receipt',
          facts: { status },
        }),
      ).toMatchObject({
        terminal: true,
        executable: false,
        reason: 'terminal-status',
      })
    },
  )

  it.each([
    ['wms-inbound', 'complete'],
    ['wms-outbound', 'complete'],
    ['wms-count', 'complete'],
  ] as const)('allows %s %s only while open', (domain, action) => {
    expect(gate({ domain, action, facts: { status: 'Open' } }).executable).toBe(true)
  })

  it.each([
    ['wms-inbound', 'Completed'],
    ['wms-inbound', 'PendingQualityCheck'],
    ['wms-inbound', 'InventoryPostingFailed'],
    ['wms-outbound', 'Completed'],
    ['wms-outbound', 'InventoryPostingPending'],
  ] as const)('preserves matching %s replay in %s as a legal no-op', (domain, status) => {
    expect(
      gate({
        domain,
        action: 'complete',
        facts: { status, idempotentReplay: true },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
  })

  it('does not mistake a mismatched WMS replay for a legal no-op', () => {
    expect(
      gate({
        domain: 'wms-outbound',
        action: 'complete',
        facts: { status: 'Completed', idempotentReplay: false },
      }),
    ).toMatchObject({
      terminal: true,
      executable: false,
      legalNoop: false,
      reason: 'terminal-status',
    })
  })

  it('never treats a cancelled inbound order as a completion replay', () => {
    expect(
      gate({
        domain: 'wms-inbound',
        action: 'complete',
        facts: { status: 'Cancelled', idempotentReplay: true },
      }),
    ).toMatchObject({
      terminal: true,
      executable: false,
      legalNoop: false,
      reason: 'terminal-status',
    })
  })

  it('marks completed inventory count read-only without a lifecycle no-op', () => {
    expect(
      gate({ domain: 'wms-count', action: 'complete', facts: { status: 'Completed' } }),
    ).toMatchObject({
      terminal: true,
      executable: false,
      legalNoop: false,
      reason: 'terminal-status',
    })
  })

  it('does not invent a count replay when idempotentReplay is present', () => {
    expect(
      gate({
        domain: 'wms-count',
        action: 'complete',
        facts: { status: 'Completed', idempotentReplay: true },
      }),
    ).toMatchObject({
      terminal: true,
      executable: false,
      legalNoop: false,
      reason: 'terminal-status',
    })
  })

  it.each([
    [{ status: 'pending' }, true, false],
    [{ status: 'in-progress', inspectionRecordId: null }, false, false],
    [{ status: 'in-progress', inspectionRecordId: 'record-1' }, false, false],
    [{ status: 'completed', inspectionRecordId: 'record-1' }, false, true],
    [{ status: 'completed', inspectionRecordId: null }, false, false],
  ] as const)('evaluates quality inspection task facts %j', (facts, executable, legalNoop) => {
    expect(
      gate({
        domain: 'quality-inspection-task',
        action: 'create-record',
        facts,
      }),
    ).toMatchObject({ executable, legalNoop })
  })

  it('does not treat the completed-only task record link as an in-progress retry fact', () => {
    expect(
      gate({
        domain: 'quality-inspection-task',
        action: 'create-record',
        facts: { status: 'in-progress', inspectionRecordId: 'record-1' },
      }),
    ).toMatchObject({
      known: true,
      terminal: false,
      executable: false,
      legalNoop: false,
      reason: 'incompatible-state',
    })
  })

  it.each([
    [
      'submit-disposition',
      { status: 'open' },
      {
        known: true,
        terminal: false,
        executable: true,
        legalNoop: false,
        reason: 'allowed',
      },
    ],
    [
      'submit-disposition',
      { status: 'disposition-in-progress' },
      {
        known: true,
        terminal: false,
        executable: false,
        legalNoop: false,
        reason: 'incompatible-state',
      },
    ],
    [
      'close',
      { status: 'disposition-in-progress', dispositionType: 'rework' },
      {
        known: true,
        terminal: false,
        executable: true,
        legalNoop: false,
        reason: 'allowed',
      },
    ],
    [
      'close',
      { status: 'disposition-in-progress', dispositionType: null },
      {
        known: true,
        terminal: false,
        executable: false,
        legalNoop: false,
        reason: 'incompatible-state',
      },
    ],
    [
      'close',
      { status: 'open' },
      {
        known: true,
        terminal: false,
        executable: false,
        legalNoop: false,
        reason: 'incompatible-state',
      },
    ],
    [
      'close',
      { status: 'closed', dispositionType: 'scrap' },
      {
        known: true,
        terminal: true,
        executable: false,
        legalNoop: false,
        reason: 'terminal-status',
      },
    ],
  ] as const)('evaluates quality NCR %s facts %j', (action, facts, expected) => {
    expect(gate({ domain: 'quality-ncr', action, facts })).toEqual(expected)
  })

  it('allows maintenance completion only while open', () => {
    expect(
      gate({
        domain: 'maintenance-work-order',
        action: 'complete',
        facts: { status: 'Open' },
      }).executable,
    ).toBe(true)
    expect(
      gate({
        domain: 'maintenance-work-order',
        action: 'complete',
        facts: { status: 'Completed' },
      }),
    ).toMatchObject({ terminal: true, executable: false, reason: 'terminal-status' })
  })

  it('allows first alarm acknowledgement and preserves first-write-wins', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'acknowledge',
        facts: { status: 'raised', acknowledgedAtUtc: null },
      }).executable,
    ).toBe(true)
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'acknowledge',
        facts: { status: 'shelved', acknowledgedAtUtc: '2026-07-27T01:00:00Z' },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
  })

  it('allows shelving unless cleared and treats an active shelf as a legal no-op', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: 'raised',
          evaluatedAtUtc: '2026-07-27T02:00:00Z',
        },
      }).executable,
    ).toBe(true)
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: 'shelved',
          shelvedAtUtc: '2026-07-27T01:00:00Z',
          shelvedUntilUtc: '2026-07-27T03:00:00Z',
          evaluatedAtUtc: '2026-07-27T02:00:00Z',
        },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: { status: 'cleared' },
      }),
    ).toMatchObject({ terminal: true, executable: false, reason: 'terminal-status' })
  })

  it.each([
    {
      shelvedAtUtc: undefined,
      shelvedUntilUtc: '2026-07-27T03:00:00Z',
      evaluatedAtUtc: '2026-07-27T02:00:00Z',
    },
    {
      shelvedAtUtc: '2026-07-27T01:00:00Z',
      shelvedUntilUtc: undefined,
      evaluatedAtUtc: '2026-07-27T02:00:00Z',
    },
    {
      shelvedAtUtc: '2026-07-27T01:00:00Z',
      shelvedUntilUtc: '2026-07-27T03:00:00Z',
      evaluatedAtUtc: undefined,
    },
    {
      shelvedAtUtc: 'invalid',
      shelvedUntilUtc: '2026-07-27T03:00:00Z',
      evaluatedAtUtc: '2026-07-27T02:00:00Z',
    },
    {
      shelvedAtUtc: '2026-07-27T01:00:00Z',
      shelvedUntilUtc: 'invalid',
      evaluatedAtUtc: '2026-07-27T02:00:00Z',
    },
    {
      shelvedAtUtc: '2026-07-27T01:00:00Z',
      shelvedUntilUtc: '2026-07-27T03:00:00Z',
      evaluatedAtUtc: 'invalid',
    },
  ])('fails closed for incomplete or malformed shelf facts %j', (facts) => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: { status: 'shelved', ...facts },
      }),
    ).toEqual({
      known: true,
      terminal: false,
      executable: false,
      legalNoop: false,
      reason: 'incompatible-state',
    })
  })

  it('allows a new shelf when the existing shelf expires exactly at evaluation time', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: 'shelved',
          shelvedAtUtc: '2026-07-27T01:00:00Z',
          shelvedUntilUtc: '2026-07-27T02:00:00Z',
          evaluatedAtUtc: '2026-07-27T02:00:00Z',
        },
      }),
    ).toEqual({
      known: true,
      terminal: false,
      executable: true,
      legalNoop: false,
      reason: 'allowed',
    })
  })

  it('fails closed when evaluation precedes the recorded shelf start', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: 'shelved',
          shelvedAtUtc: '2026-07-27T02:00:00Z',
          shelvedUntilUtc: '2026-07-27T03:00:00Z',
          evaluatedAtUtc: '2026-07-27T01:59:59Z',
        },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: false,
      reason: 'incompatible-state',
    })
  })

  it('treats evaluation exactly at shelf start as an active shelf no-op', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'shelve',
        facts: {
          status: 'shelved',
          shelvedAtUtc: '2026-07-27T02:00:00Z',
          shelvedUntilUtc: '2026-07-27T03:00:00Z',
          evaluatedAtUtc: '2026-07-27T02:00:00Z',
        },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
  })

  it('keeps non-shelved alarm unshelve as a legal no-op', () => {
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'unshelve',
        facts: { status: 'acknowledged' },
      }),
    ).toMatchObject({
      executable: false,
      legalNoop: true,
      reason: 'already-applied-noop',
    })
    expect(
      gate({
        domain: 'iiot-alarm',
        action: 'unshelve',
        facts: { status: 'shelved' },
      }).executable,
    ).toBe(true)
  })

  const actionRequests = [
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-operation-task',
      action: 'start',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-operation-task',
      action: 'pause',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-operation-task',
      action: 'resume',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-operation-task',
      action: 'complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-operation-task',
      action: 'report-complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-work-order',
      action: 'release',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-work-order',
      action: 'hold',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-work-order',
      action: 'cancel',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'mes-material-issue',
      action: 'confirm-receipt',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'wms-inbound',
      action: 'complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'wms-outbound',
      action: 'complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'wms-count',
      action: 'complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'quality-inspection-task',
      action: 'create-record',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'quality-ncr',
      action: 'submit-disposition',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'quality-ncr',
      action: 'close',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'maintenance-work-order',
      action: 'complete',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'iiot-alarm',
      action: 'acknowledge',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'iiot-alarm',
      action: 'shelve',
      facts: { status },
    }),
    (status: string | null | undefined): LifecycleActionRequest => ({
      domain: 'iiot-alarm',
      action: 'unshelve',
      facts: { status },
    }),
  ]

  it.each(actionRequests.map((request, index) => [index, request] as const))(
    'fails closed for blank and unknown status on action contract %s',
    (_index, request) => {
      for (const status of [undefined, null, '', '   ', 'unknown']) {
        expect(gate(request(status))).toEqual({
          known: false,
          terminal: false,
          executable: false,
          legalNoop: false,
          reason: 'unknown-status',
        })
      }
    },
  )

  it.each([
    {
      domain: 'mes-operation-task',
      action: 'complete',
      facts: { status: 'Completed' },
    },
    { domain: 'mes-work-order', action: 'hold', facts: { status: 'closed' } },
    {
      domain: 'mes-material-issue',
      action: 'confirm-receipt',
      facts: { status: 'Received' },
    },
    { domain: 'wms-inbound', action: 'complete', facts: { status: 'Cancelled' } },
    {
      domain: 'wms-outbound',
      action: 'complete',
      facts: { status: 'InventoryPostingFailed' },
    },
    { domain: 'wms-count', action: 'complete', facts: { status: 'Completed' } },
    {
      domain: 'quality-inspection-task',
      action: 'create-record',
      facts: { status: 'completed', inspectionRecordId: null },
    },
    { domain: 'quality-ncr', action: 'close', facts: { status: 'closed' } },
    {
      domain: 'maintenance-work-order',
      action: 'complete',
      facts: { status: 'Completed' },
    },
    { domain: 'iiot-alarm', action: 'shelve', facts: { status: 'cleared' } },
  ] satisfies LifecycleActionRequest[])(
    'marks terminal facts for $domain/$action as read-only',
    (request) => {
      expect(gate(request)).toMatchObject({
        known: true,
        terminal: true,
        executable: false,
        legalNoop: false,
        reason: 'terminal-status',
      })
    },
  )
})
