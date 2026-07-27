export type LifecycleDomain =
  | 'mes-operation-task'
  | 'mes-work-order'
  | 'mes-material-issue'
  | 'wms-inbound'
  | 'wms-outbound'
  | 'wms-count'
  | 'quality-inspection-task'
  | 'quality-ncr'
  | 'maintenance-work-order'
  | 'iiot-alarm'

export type LifecycleFacts = Readonly<{
  status?: string | null
  acknowledgedAtUtc?: string | null
  shelvedAtUtc?: string | null
  shelvedUntilUtc?: string | null
  evaluatedAtUtc?: string | null
  inspectionRecordId?: string | null
  dispositionType?: string | null
  idempotentReplay?: boolean
}>

export type LifecycleActionRequest =
  | Readonly<{
      domain: 'mes-operation-task'
      action: 'start' | 'pause' | 'resume' | 'complete' | 'report-complete'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'mes-work-order'
      action: 'release' | 'hold' | 'cancel'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'mes-material-issue'
      action: 'confirm-receipt'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'wms-inbound' | 'wms-outbound' | 'wms-count'
      action: 'complete'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'quality-inspection-task'
      action: 'create-record'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'quality-ncr'
      action: 'submit-disposition' | 'close'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'maintenance-work-order'
      action: 'complete'
      facts: LifecycleFacts
    }>
  | Readonly<{
      domain: 'iiot-alarm'
      action: 'acknowledge' | 'shelve' | 'unshelve'
      facts: LifecycleFacts
    }>

export type StatusActionGate = Readonly<{
  known: boolean
  terminal: boolean
  executable: boolean
  legalNoop: boolean
  reason:
    | 'allowed'
    | 'unknown-status'
    | 'terminal-status'
    | 'incompatible-state'
    | 'already-applied-noop'
}>

const allowed = (): StatusActionGate => ({
  known: true,
  terminal: false,
  executable: true,
  legalNoop: false,
  reason: 'allowed',
})

const unknown = (): StatusActionGate => ({
  known: false,
  terminal: false,
  executable: false,
  legalNoop: false,
  reason: 'unknown-status',
})

const incompatible = (): StatusActionGate => ({
  known: true,
  terminal: false,
  executable: false,
  legalNoop: false,
  reason: 'incompatible-state',
})

const terminal = (): StatusActionGate => ({
  known: true,
  terminal: true,
  executable: false,
  legalNoop: false,
  reason: 'terminal-status',
})

const noop = (isTerminal = false): StatusActionGate => ({
  known: true,
  terminal: isTerminal,
  executable: false,
  legalNoop: true,
  reason: 'already-applied-noop',
})

function normalizedStatus(facts: LifecycleFacts): string | undefined {
  const status = facts.status?.trim().toLowerCase()
  return status || undefined
}

function isKnown(status: string | undefined, statuses: ReadonlySet<string>): status is string {
  return status !== undefined && statuses.has(status)
}

function operationTaskGate(
  action: Extract<LifecycleActionRequest, { domain: 'mes-operation-task' }>['action'],
  facts: LifecycleFacts,
): StatusActionGate {
  const status = normalizedStatus(facts)
  const known = new Set([
    'queued',
    'inprogress',
    'paused',
    'completed',
    'cancelled',
    'scheduleinvalidated',
  ])
  if (!isKnown(status, known)) return unknown()
  if (new Set(['completed', 'cancelled', 'scheduleinvalidated']).has(status)) return terminal()

  const requiredStatus =
    action === 'start' ? 'queued' : action === 'resume' ? 'paused' : 'inprogress'
  return status === requiredStatus ? allowed() : incompatible()
}

function workOrderGate(
  action: Extract<LifecycleActionRequest, { domain: 'mes-work-order' }>['action'],
  facts: LifecycleFacts,
): StatusActionGate {
  const status = normalizedStatus(facts)
  const known = new Set([
    'created',
    'released',
    'started',
    'hold',
    'completed',
    'closed',
    'cancelled',
    'scrapped',
  ])
  if (!isKnown(status, known)) return unknown()
  if (action === 'cancel' && status === 'cancelled') return noop(true)

  const terminalStatuses = new Set(['completed', 'closed', 'cancelled', 'scrapped'])
  if (terminalStatuses.has(status)) return terminal()
  if (action === 'release' && !new Set(['created', 'started', 'hold']).has(status))
    return incompatible()
  return allowed()
}

function materialIssueGate(facts: LifecycleFacts): StatusActionGate {
  const status = normalizedStatus(facts)
  const known = new Set([
    'requested',
    'partiallyreceived',
    'received',
    'cancelled',
    'returnrequested',
    'reservationexpired',
  ])
  if (!isKnown(status, known)) return unknown()
  return new Set(['requested', 'partiallyreceived']).has(status) ? allowed() : terminal()
}

function wmsGate(
  domain: 'wms-inbound' | 'wms-outbound' | 'wms-count',
  facts: LifecycleFacts,
): StatusActionGate {
  const status = normalizedStatus(facts)
  const knownByDomain: Record<typeof domain, ReadonlySet<string>> = {
    'wms-inbound': new Set([
      'open',
      'completed',
      'inventorypostingfailed',
      'pendingqualitycheck',
      'cancelled',
    ]),
    'wms-outbound': new Set([
      'open',
      'completed',
      'inventorypostingfailed',
      'inventorypostingpending',
      'cancelled',
    ]),
    'wms-count': new Set(['open', 'completed']),
  }
  if (!isKnown(status, knownByDomain[domain])) return unknown()
  if (status === 'open') return allowed()

  const replayStatus =
    (domain === 'wms-inbound' &&
      new Set(['completed', 'pendingqualitycheck', 'inventorypostingfailed']).has(status)) ||
    (domain === 'wms-outbound' && new Set(['completed', 'inventorypostingpending']).has(status))
  return replayStatus && facts.idempotentReplay ? noop(true) : terminal()
}

function inspectionTaskGate(facts: LifecycleFacts): StatusActionGate {
  const status = normalizedStatus(facts)
  if (!isKnown(status, new Set(['pending', 'in-progress', 'completed']))) return unknown()
  const hasRecord = Boolean(facts.inspectionRecordId?.trim())
  if (status === 'pending') return allowed()
  if (status === 'in-progress') return incompatible()
  return hasRecord ? noop(true) : terminal()
}

function ncrGate(
  action: Extract<LifecycleActionRequest, { domain: 'quality-ncr' }>['action'],
  facts: LifecycleFacts,
): StatusActionGate {
  const status = normalizedStatus(facts)
  if (!isKnown(status, new Set(['open', 'disposition-in-progress', 'closed']))) return unknown()
  if (status === 'closed') return terminal()
  if (action === 'submit-disposition') return status === 'open' ? allowed() : incompatible()
  return status === 'disposition-in-progress' && Boolean(facts.dispositionType?.trim())
    ? allowed()
    : incompatible()
}

function maintenanceGate(facts: LifecycleFacts): StatusActionGate {
  const status = normalizedStatus(facts)
  if (!isKnown(status, new Set(['open', 'completed']))) return unknown()
  return status === 'open' ? allowed() : terminal()
}

function alarmGate(
  action: Extract<LifecycleActionRequest, { domain: 'iiot-alarm' }>['action'],
  facts: LifecycleFacts,
): StatusActionGate {
  const status = normalizedStatus(facts)
  if (!isKnown(status, new Set(['raised', 'acknowledged', 'shelved', 'cleared']))) return unknown()

  if (action === 'unshelve') return status === 'shelved' ? allowed() : noop(status === 'cleared')
  if (status === 'cleared') return terminal()

  if (action === 'acknowledge') {
    return status === 'acknowledged' || Boolean(facts.acknowledgedAtUtc?.trim())
      ? noop()
      : allowed()
  }

  if (status !== 'shelved') return allowed()
  const shelvedAt = Date.parse(facts.shelvedAtUtc ?? '')
  const until = Date.parse(facts.shelvedUntilUtc ?? '')
  const evaluatedAt = Date.parse(facts.evaluatedAtUtc ?? '')
  if (!Number.isFinite(shelvedAt) || !Number.isFinite(until) || !Number.isFinite(evaluatedAt))
    return incompatible()
  if (evaluatedAt < shelvedAt) return incompatible()
  return evaluatedAt < until ? noop() : allowed()
}

export function statusActionGate(request: LifecycleActionRequest): StatusActionGate {
  switch (request.domain) {
    case 'mes-operation-task':
      return operationTaskGate(request.action, request.facts)
    case 'mes-work-order':
      return workOrderGate(request.action, request.facts)
    case 'mes-material-issue':
      return materialIssueGate(request.facts)
    case 'wms-inbound':
    case 'wms-outbound':
    case 'wms-count':
      return wmsGate(request.domain, request.facts)
    case 'quality-inspection-task':
      return inspectionTaskGate(request.facts)
    case 'quality-ncr':
      return ncrGate(request.action, request.facts)
    case 'maintenance-work-order':
      return maintenanceGate(request.facts)
    case 'iiot-alarm':
      return alarmGate(request.action, request.facts)
  }
}
