import { expect, test, type APIResponse, type Page } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const evidencePath = process.env.NERV_IIP_EQUIPMENT_BRANCH_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_EQUIPMENT_BRANCH_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_EQUIPMENT_BRANCH_TRANSPORT
const persistence = process.env.NERV_IIP_EQUIPMENT_BRANCH_PERSISTENCE

test.skip(
  !baseURL ||
    !adminPassword ||
    !evidencePath ||
    !runtimeProfileSource ||
    !transport ||
    !persistence,
  'requires a managed full-stack session',
)
test.setTimeout(12 * 60 * 1000)

type JsonRecord = Record<string, unknown>
type Conclusion = 'runtime-confirmed' | 'gap' | 'not-verified'

class PublicCallError extends Error {
  constructor(
    readonly method: 'GET' | 'POST',
    readonly path: string,
    readonly status: number,
    readonly request: JsonRecord,
    readonly payload: unknown,
  ) {
    super(`${method} ${path} returned HTTP ${status}: ${safeText(JSON.stringify(payload))}`)
    this.name = 'PublicCallError'
  }
}

class PollTimeoutError extends Error {
  constructor(
    readonly request: JsonRecord | null,
    readonly lastData: JsonRecord,
    readonly poll: JsonRecord,
    message: string,
  ) {
    super(message)
    this.name = 'PollTimeoutError'
  }
}

type EvidenceEntry = {
  node: string
  sourceObject: string
  downstreamObject: string
  stableKey: string
  automationMode: 'automatic' | 'manual' | 'mixed'
  request: JsonRecord | null
  responseOrLog: JsonRecord
  conclusion: Conclusion
  demoWording: string
  responsibilityIssue: string | null
}

const requiredNodes = [
  'device-telemetry-running-baseline',
  'telemetry-threshold-alarm-raised',
  'alarm-acknowledged-and-shelved',
  'alarm-maintenance-work-order',
  'alarm-device-unavailable-block',
  'telemetry-return-to-normal-recovery',
  'equipment-reliability-readface-shift',
] as const

function asRecord(value: unknown): JsonRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as JsonRecord)
    : {}
}

function dataOf(value: unknown): unknown {
  return asRecord(value).data ?? value
}

function rowsOf(value: unknown): JsonRecord[] {
  const data = dataOf(value)
  if (Array.isArray(data)) return data.map(asRecord)
  const items = asRecord(data).items
  return Array.isArray(items) ? items.map(asRecord) : []
}

function listOf(value: unknown): JsonRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : []
}

function textOf(value: unknown): string {
  if (value === null || value === undefined) return ''
  return String(value)
}

function safeText(value: unknown): string {
  return textOf(value)
    .replace(/authorization/gi, '<redacted-header>')
    .replace(/bearer\s+[^\s"']+/gi, '<redacted-credential>')
    .replace(/password/gi, '<redacted-field>')
    .replace(/(?:access|refresh)[_-]?token/gi, '<redacted-field>')
    .slice(0, 1200)
}

function publicJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(publicJson)
  if (value === null || typeof value !== 'object')
    return typeof value === 'string' ? safeText(value) : value
  return Object.fromEntries(
    Object.entries(value as JsonRecord)
      .filter(([key]) => !/(authorization|password|access[_-]?token|refresh[_-]?token)/i.test(key))
      .map(([key, item]) => [key, publicJson(item)]),
  )
}

async function jsonOf(response: APIResponse): Promise<unknown> {
  const contentType = response.headers()['content-type'] ?? ''
  if (!contentType.includes('json')) return { text: safeText(await response.text()) }
  return response.json()
}

async function captureSessionCredential(page: Page): Promise<string> {
  const businessRequest = page.waitForRequest((request) => {
    const path = new URL(request.url()).pathname
    return (
      path === '/api/business-console/v1/master-data/skus' &&
      Boolean(request.headers().authorization)
    )
  })
  await page.goto('/master-data/skus')
  const credential = (await businessRequest).headers().authorization
  if (!credential)
    throw new Error('Authenticated public business request did not carry a session credential.')
  return credential
}

test('MAN-520 records the public equipment exception branch', async ({ page }) => {
  const now = new Date()
  const suffix = now
    .toISOString()
    .replace(/[-:TZ.]/g, '')
    .slice(0, 14)
  let organizationId = ''
  let environmentId = ''
  const uomCode = `UOM-EQ-${suffix}`
  const siteCode = `SITE-EQ-${suffix}`
  const workshopCode = `SHOP-EQ-${suffix}`
  const lineCode = `LINE-EQ-${suffix}`
  const workCenterCode = `WC-EQ-${suffix}`
  const deviceCode = `DEV-EQ-${suffix}`
  const alarmRuleCode = `AR-EQ-${suffix}`
  const alarmCode = `spindle-temperature-high-${suffix}`
  const alarmSeverity = 'critical'
  const tagKey = 'spindle.temperature'
  const tagUnitCode = 'degC'
  const thresholdValue = 90
  const normalValue = 60
  const faultValue = 120
  const recoveredValue = 55
  const sourceSystem = 'leader-equipment-branch'
  const sourceConnector = 'business-gateway'

  // A fixed, already-elapsed telemetry timeline keeps every OEE assertion exact instead of
  // racing wall-clock drift: running -> faulted -> running inside a closed ten-minute window.
  const minute = 60_000
  const timelineStartUtc = new Date(now.getTime() - 12 * minute)
  const faultOccurredAtUtc = new Date(timelineStartUtc.getTime() + 5 * minute)
  const recoveryOccurredAtUtc = new Date(timelineStartUtc.getTime() + 8 * minute)
  const oeeWindowEndUtc = new Date(timelineStartUtc.getTime() + 10 * minute)
  const reliabilityWindowEndUtc = new Date(timelineStartUtc.getTime() + 60 * minute)
  const expectedBaselineAvailabilityRate = 1
  const expectedFaultedAvailabilityRate = 0.5
  const expectedRecoveredAvailabilityRate = 0.7

  const evidence = new Map<string, EvidenceEntry>()
  const setup: JsonRecord[] = []
  let sessionCredential = ''
  let deviceAssetId = ''
  let alarmEventId = ''
  let maintenanceWorkOrderId = ''
  let baselineReliability: JsonRecord = {}

  for (const node of requiredNodes) {
    evidence.set(node, {
      node,
      sourceObject: deviceCode,
      downstreamObject: 'not-observed',
      stableKey: deviceCode,
      automationMode: 'automatic',
      request: null,
      responseOrLog: { reason: 'upstream evidence was not established in this run' },
      conclusion: 'not-verified',
      demoWording: `${node}: this run did not establish a public runtime association.`,
      responsibilityIssue: null,
    })
  }

  const record = (entry: EvidenceEntry) => evidence.set(entry.node, entry)

  const call = async (method: 'GET' | 'POST', path: string, body?: JsonRecord) => {
    const url = new URL(path, baseURL!).toString()
    const response = await page.request.fetch(url, {
      method,
      data: body,
      headers: { authorization: sessionCredential },
    })
    const payload = await jsonOf(response)
    const summary = {
      method,
      path: new URL(url).pathname + new URL(url).search,
      status: response.status(),
      correlationId:
        response.headers()['x-correlation-id'] ?? response.headers().traceparent ?? null,
      body: body ? publicJson(body) : null,
    }
    if (!response.ok()) {
      throw new PublicCallError(
        method,
        summary.path,
        response.status(),
        summary,
        publicJson(payload),
      )
    }
    return { payload, summary, publicPayload: publicJson(payload) as JsonRecord }
  }

  const queryPath = (path: string, query: JsonRecord) => {
    const url = new URL(path, baseURL!)
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined && value !== '')
        url.searchParams.set(key, String(value))
    }
    return `${url.pathname}${url.search}`
  }

  const pollRows = async (
    path: string,
    query: JsonRecord,
    predicate: (row: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastRows: JsonRecord[] = []
    let lastRequest: JsonRecord | null = null
    do {
      attempts += 1
      const response = await call('GET', queryPath(path, query))
      lastRequest = response.summary
      lastRows = rowsOf(response.payload)
      const match = lastRows.find(predicate)
      if (match) {
        return {
          match,
          call: response,
          poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
        }
      }
      const remainingMs = deadline - Date.now()
      if (remainingMs > 0) await page.waitForTimeout(Math.min(1_000, remainingMs))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(
      lastRequest,
      { items: lastRows },
      { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for a run-scoped row from ${path}; last rows=${safeText(JSON.stringify(lastRows))}.`,
    )
  }

  const pollData = async (
    path: string,
    query: JsonRecord,
    predicate: (data: JsonRecord) => boolean,
    timeoutMs = 90_000,
  ) => {
    const startedAt = Date.now()
    const deadline = startedAt + timeoutMs
    let attempts = 0
    let lastData: JsonRecord = {}
    let lastRequest: JsonRecord | null = null
    do {
      attempts += 1
      try {
        const response = await call('GET', queryPath(path, query))
        lastRequest = response.summary
        lastData = asRecord(dataOf(response.payload))
        if (predicate(lastData)) {
          return {
            data: lastData,
            call: response,
            poll: { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
          }
        }
      } catch (error) {
        if (!(error instanceof PublicCallError && error.status === 404)) throw error
        lastRequest = error.request
        lastData = asRecord(error.payload)
      }
      const remainingMs = deadline - Date.now()
      if (remainingMs > 0) await page.waitForTimeout(Math.min(1_000, remainingMs))
    } while (Date.now() < deadline)
    throw new PollTimeoutError(
      lastRequest,
      lastData,
      { attempts, elapsedMs: Date.now() - startedAt, timeoutMs },
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for run-scoped data from ${path}; last data=${safeText(JSON.stringify(lastData))}.`,
    )
  }

  const markFailure = (
    node: (typeof requiredNodes)[number],
    error: unknown,
    mode: EvidenceEntry['automationMode'] = 'automatic',
    issue: string | null = null,
  ) => {
    const current = evidence.get(node)!
    const pollFailure = error instanceof PollTimeoutError ? error : null
    const callFailure = error instanceof PublicCallError ? error : null
    record({
      ...current,
      automationMode: mode,
      request: pollFailure?.request ?? callFailure?.request ?? current.request,
      responseOrLog: pollFailure
        ? {
            error: safeText(pollFailure.message),
            poll: pollFailure.poll,
            lastData: publicJson(pollFailure.lastData),
          }
        : callFailure
          ? {
              error: safeText(callFailure.message),
              response: publicJson(callFailure.payload),
            }
          : { error: safeText(error instanceof Error ? error.message : error) },
      conclusion: 'gap',
      demoWording: `${node}: the public runtime attempt did not converge; present this as a gap, not a completed hop.`,
      responsibilityIssue: issue,
    })
  }

  const publishSample = (label: string, occurredAtUtc: Date, value: number, deviceState: string) =>
    call('POST', '/api/business-console/v1/telemetry/samples', {
      organizationId,
      environmentId,
      deviceAssetId,
      tagKey,
      bucketStartUtc: new Date(occurredAtUtc.getTime() - minute).toISOString(),
      bucketEndUtc: occurredAtUtc.toISOString(),
      sampleCount: 1,
      minValue: value,
      maxValue: value,
      averageValue: value,
      sourceSequence: `${label}-${suffix}`,
      sourceSystem,
      sourceConnector,
      deviceState,
      stateOccurredAtUtc: occurredAtUtc.toISOString(),
      firstValue: value,
      lastValue: value,
    })

  const readOee = () =>
    call(
      'GET',
      queryPath('/api/business-console/v1/telemetry/oee', {
        organizationId,
        environmentId,
        deviceAssetId,
        windowStartUtc: timelineStartUtc.toISOString(),
        windowEndUtc: oeeWindowEndUtc.toISOString(),
      }),
    )

  const readReliability = () =>
    call(
      'GET',
      queryPath(
        `/api/business-console/v1/maintenance/assets/${encodeURIComponent(deviceAssetId)}/reliability`,
        {
          organizationId,
          environmentId,
          windowStartUtc: timelineStartUtc.toISOString(),
          windowEndUtc: reliabilityWindowEndUtc.toISOString(),
        },
      ),
    )

  const readAvailability = (windowStartUtc: Date) =>
    call(
      'GET',
      queryPath('/api/business-console/v1/equipment/availability', {
        organizationId,
        environmentId,
        deviceAssetIds: deviceAssetId,
        windowStartUtc: windowStartUtc.toISOString(),
        windowEndUtc: new Date().toISOString(),
      }),
    )

  try {
    await page.goto('/login')
    const loginName = page.getByLabel('登录名')
    await expect(loginName).toBeVisible({ timeout: 120_000 })
    const loginResponse = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
    )
    await loginName.fill('admin')
    await page.getByLabel('密码').fill(adminPassword!)
    await page.getByRole('button', { name: '登录' }).click()
    const login = await loginResponse
    expect(login.ok()).toBe(true)
    const auth = asRecord(dataOf(await login.json()))
    const principal = asRecord(auth.principal)
    organizationId = textOf(principal.organizationId)
    environmentId = textOf(principal.environmentId)
    const principalType = textOf(principal.principalType).trim().toLowerCase()
    const principalId = textOf(principal.principalId).trim()
    if (!organizationId || !environmentId || !principalType || !principalId) {
      throw new Error(
        'The public login response did not expose the authenticated principal and organization/environment scope.',
      )
    }
    await expect(page).toHaveURL(new URL('/', baseURL!).toString())
    sessionCredential = await captureSessionCredential(page)

    const create = async (path: string, body: JsonRecord) => {
      const result = await call('POST', path, body)
      setup.push({ request: result.summary, response: result.publicPayload })
      return dataOf(result.payload)
    }

    let prerequisitesReady = true
    try {
      await create('/api/business-console/v1/master-data/units-of-measure', {
        organizationId,
        environmentId,
        code: uomCode,
        name: 'MAN-520 equipment branch each',
        dimensionType: 'quantity',
        precision: 3,
        roundingMode: 'half-up',
        idempotencyKey: `uom-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/sites', {
        organizationId,
        environmentId,
        code: siteCode,
        name: 'MAN-520 equipment branch site',
        timezone: 'UTC',
        idempotencyKey: `site-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/workshops', {
        organizationId,
        environmentId,
        code: workshopCode,
        name: 'MAN-520 equipment branch workshop',
        siteCode,
        idempotencyKey: `shop-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/production-lines', {
        organizationId,
        environmentId,
        code: lineCode,
        name: 'MAN-520 equipment branch line',
        siteCode,
        workshopCode,
        idempotencyKey: `line-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/work-centers', {
        organizationId,
        environmentId,
        code: workCenterCode,
        name: 'MAN-520 equipment branch work center',
        capacityMinutesPerDay: 1_440,
        resourceType: 'machine',
        plantCode: siteCode,
        lineCode,
        defaultCalendarCode: `CAL-EQ-${suffix}`,
        capacityUnit: 'minute',
        finiteCapacity: true,
        workshopCode,
        idempotencyKey: `wc-${suffix}`,
      })
      await create('/api/business-console/v1/master-data/device-assets', {
        organizationId,
        environmentId,
        code: deviceCode,
        model: 'MAN-520 equipment branch machine',
        lineCode,
        workCenterCode,
        assetClassCode: 'machine',
        manufacturer: 'Nerv Automation',
        serialNo: `SN-EQ-${suffix}`,
        minimumCapacity: 1,
        maximumCapacity: 1,
        capacityUomCode: uomCode,
        criticality: 'high',
        maintainable: true,
        telemetryEnabled: true,
        externalReferences: { equipmentBranchRun: suffix },
        idempotencyKey: `device-${suffix}`,
        siteCode,
        workshopCode,
      })
      const deviceList = await call(
        'GET',
        queryPath('/api/business-console/v1/master-data/device-assets', {
          organizationId,
          environmentId,
          workCenterCode,
          keyword: deviceCode,
          take: 100,
        }),
      )
      const device = listOf(asRecord(dataOf(deviceList.payload)).resources).find(
        (row) => textOf(row.code).trim() === deviceCode,
      )
      deviceAssetId = textOf(device?.deviceAssetId).trim()
      if (!deviceAssetId) {
        throw new Error(
          `MasterData did not expose a stable device asset id for the run-scoped device ${deviceCode}.`,
        )
      }
      setup.push({ request: deviceList.summary, response: deviceList.publicPayload })

      const alarmRule = await create('/api/business-console/v1/telemetry/alarm-rules', {
        organizationId,
        environmentId,
        deviceAssetId,
        ruleCode: alarmRuleCode,
        alarmCode,
        severity: alarmSeverity,
        tagKey,
        comparisonOperator: '>',
        thresholdValue,
        unitCode: tagUnitCode,
        isEnabled: true,
      })
      if (!textOf(asRecord(alarmRule).alarmRuleId).trim()) {
        throw new Error('IIoT did not return the run-scoped alarm rule id.')
      }
      const armedRules = await call(
        'GET',
        queryPath('/api/business-console/v1/telemetry/alarm-rules', {
          organizationId,
          environmentId,
          deviceAssetId,
          isEnabled: true,
          take: 100,
        }),
      )
      const armedRule = rowsOf(armedRules.payload).find(
        (row) => textOf(row.ruleCode) === alarmRuleCode,
      )
      if (
        !armedRule ||
        Number(armedRule.thresholdValue) !== thresholdValue ||
        textOf(armedRule.comparisonOperator) !== '>' ||
        textOf(armedRule.tagKey) !== tagKey
      ) {
        throw new Error(
          `IIoT did not arm the run-scoped alarm rule ${alarmRuleCode}: ${safeText(JSON.stringify(armedRule))}.`,
        )
      }
      setup.push({ request: armedRules.summary, response: armedRules.publicPayload })
    } catch (error) {
      prerequisitesReady = false
      const blockedReason = safeText(error instanceof Error ? error.message : error)
      setup.push({
        phase: 'public-prerequisites',
        conclusion: 'gap',
        responsibilityIssue: 'unattributed / requires follow-up issue',
        error: blockedReason,
      })
      for (const node of requiredNodes) {
        const entry = evidence.get(node)!
        if (entry.conclusion !== 'not-verified') continue
        record({
          ...entry,
          responseOrLog: { blockedBy: 'public-prerequisites', error: blockedReason },
          demoWording: `${node}: this run was blocked before the equipment branch by a public-prerequisite gap.`,
          responsibilityIssue: 'unattributed / requires follow-up issue',
        })
      }
    }

    let baselineIngested = false
    if (prerequisitesReady) {
      try {
        const baseline = await publishSample(
          'baseline-running',
          timelineStartUtc,
          normalValue,
          'running',
        )
        if (!textOf(asRecord(dataOf(baseline.payload)).deviceStateSnapshotId).trim()) {
          throw new Error(`IIoT did not persist a state snapshot for device ${deviceCode}.`)
        }
        const detail = await pollData(
          `/api/business-console/v1/equipment/devices/${encodeURIComponent(deviceAssetId)}`,
          { organizationId, environmentId },
          (data) => textOf(asRecord(data.currentState).currentState).toLowerCase() === 'running',
        )
        const currentState = asRecord(detail.data.currentState)
        if (currentState.isSourceFresh !== true) {
          throw new Error(
            `Equipment detail reported a stale source for ${deviceCode}: ${safeText(JSON.stringify(currentState))}.`,
          )
        }
        const armedAlarms = await call(
          'GET',
          queryPath('/api/business-console/v1/equipment/alarms', {
            organizationId,
            environmentId,
            deviceAssetId,
            take: 100,
          }),
        )
        if (rowsOf(armedAlarms.payload).length !== 0) {
          throw new Error(
            `A normal reading raised an unexpected alarm for ${deviceCode}: ${safeText(JSON.stringify(rowsOf(armedAlarms.payload)))}.`,
          )
        }
        const oee = await readOee()
        const oeeData = asRecord(dataOf(oee.payload))
        if (
          Number(oeeData.stateSampleCount) !== 1 ||
          Number(oeeData.availabilityRate) !== expectedBaselineAvailabilityRate
        ) {
          throw new Error(
            `Baseline OEE for ${deviceCode} was not the expected fully-available window: ${safeText(JSON.stringify(oeeData))}.`,
          )
        }
        const reliability = await readReliability()
        baselineReliability = asRecord(dataOf(reliability.payload))
        if (
          Number(baselineReliability.failureCount) !== 0 ||
          baselineReliability.mtbfHours !== null ||
          baselineReliability.mttrMinutes !== null
        ) {
          throw new Error(
            `Baseline reliability for ${deviceCode} was not empty: ${safeText(JSON.stringify(baselineReliability))}.`,
          )
        }
        baselineIngested = true
        record({
          node: 'device-telemetry-running-baseline',
          sourceObject: deviceCode,
          downstreamObject: deviceAssetId,
          stableKey: `${deviceCode} -> ${tagKey}@${timelineStartUtc.toISOString()} -> running`,
          automationMode: 'manual',
          request: baseline.summary,
          responseOrLog: {
            sample: baseline.publicPayload,
            currentState: publicJson(currentState),
            baselineOee: publicJson(oeeData),
            baselineReliability: publicJson(baselineReliability),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The device reported a normal reading through the public telemetry facade: state running, source fresh, no alarm, OEE availability 100% and no reliability history yet.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('device-telemetry-running-baseline', error, 'manual')
      }
    }

    if (baselineIngested) {
      try {
        const faulted = await publishSample(
          'fault-overheat',
          faultOccurredAtUtc,
          faultValue,
          'faulted',
        )
        const raised = await pollRows(
          '/api/business-console/v1/equipment/alarms',
          { organizationId, environmentId, deviceAssetId, take: 100 },
          (row) => textOf(row.externalAlarmId) === alarmRuleCode,
        )
        alarmEventId = textOf(raised.match.alarmEventId).trim()
        if (
          !alarmEventId ||
          textOf(raised.match.alarmCode) !== alarmCode ||
          textOf(raised.match.severity) !== alarmSeverity ||
          textOf(raised.match.status) !== 'raised' ||
          new Date(textOf(raised.match.raisedAtUtc)).getTime() !== faultOccurredAtUtc.getTime()
        ) {
          throw new Error(
            `IIoT did not raise the rule-scoped alarm at the observed bucket: ${safeText(JSON.stringify(raised.match))}.`,
          )
        }
        record({
          node: 'telemetry-threshold-alarm-raised',
          sourceObject: `${deviceCode} -> ${tagKey}=${faultValue}${tagUnitCode}`,
          downstreamObject: alarmRuleCode,
          stableKey: `${alarmRuleCode} -> ${alarmCode} -> raised@${faultOccurredAtUtc.toISOString()}`,
          automationMode: 'automatic',
          request: faulted.summary,
          responseOrLog: {
            threshold: { comparisonOperator: '>', thresholdValue, unitCode: tagUnitCode },
            observedValue: faultValue,
            alarm: publicJson(raised.match),
            alarmEventIdDisclosure:
              'externalAlarmId is the run-scoped rule code and is the human-readable alarm identifier; alarmEventId is the internal join key, not a business number.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'A reading above the armed threshold raised exactly one alarm for this device, carrying the run-scoped rule code as its readable alarm id.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('telemetry-threshold-alarm-raised', error)
      }
    }

    if (alarmEventId) {
      try {
        const acknowledgedAtUtc = new Date()
        const acknowledge = await call(
          'POST',
          `/api/business-console/v1/equipment/alarms/${encodeURIComponent(alarmEventId)}/acknowledge`,
          {
            organizationId,
            environmentId,
            acknowledgedAtUtc: acknowledgedAtUtc.toISOString(),
            acknowledgedBy: null,
          },
        )
        const acknowledged = await pollRows(
          '/api/business-console/v1/equipment/alarms',
          { organizationId, environmentId, deviceAssetId, take: 100 },
          (row) =>
            textOf(row.externalAlarmId) === alarmRuleCode &&
            textOf(row.status) === 'acknowledged' &&
            textOf(row.acknowledgedBy).length > 0,
        )
        // The request sent no acknowledgedBy, so whatever came back is the gateway-bound principal
        // actor reference; the shelving audit must resolve to the same identity.
        const gatewayActorRef = textOf(acknowledged.match.acknowledgedBy)
        const shelvedAtUtc = new Date()
        const shelveReason = `MAN-520 equipment branch shelve ${suffix}`
        const shelve = await call(
          'POST',
          `/api/business-console/v1/equipment/alarms/${encodeURIComponent(alarmEventId)}/shelve`,
          {
            organizationId,
            environmentId,
            shelvedAtUtc: shelvedAtUtc.toISOString(),
            durationMinutes: 15,
            shelvedBy: null,
            reason: shelveReason,
            idempotencyKey: `shelve-${suffix}`,
          },
        )
        const shelved = await pollRows(
          '/api/business-console/v1/equipment/alarms',
          { organizationId, environmentId, deviceAssetId, take: 100 },
          (row) =>
            textOf(row.externalAlarmId) === alarmRuleCode && textOf(row.status) === 'shelved',
        )
        if (
          textOf(shelved.match.shelveReason) !== shelveReason ||
          textOf(shelved.match.shelvedBy) !== gatewayActorRef ||
          !textOf(shelved.match.shelvedUntilUtc)
        ) {
          throw new Error(
            `IIoT did not record the shelving audit for ${alarmRuleCode}: ${safeText(JSON.stringify(shelved.match))}.`,
          )
        }
        const unshelve = await call(
          'POST',
          `/api/business-console/v1/equipment/alarms/${encodeURIComponent(alarmEventId)}/unshelve`,
          {
            organizationId,
            environmentId,
            unshelvedAtUtc: new Date().toISOString(),
          },
        )
        const unshelved = await pollRows(
          '/api/business-console/v1/equipment/alarms',
          { organizationId, environmentId, deviceAssetId, take: 100 },
          (row) =>
            textOf(row.externalAlarmId) === alarmRuleCode && textOf(row.status) === 'acknowledged',
        )
        record({
          node: 'alarm-acknowledged-and-shelved',
          sourceObject: alarmRuleCode,
          downstreamObject: gatewayActorRef,
          stableKey: `${alarmRuleCode} -> acknowledged -> shelved -> acknowledged`,
          automationMode: 'manual',
          request: acknowledge.summary,
          responseOrLog: {
            gatewayBoundActor: gatewayActorRef,
            acknowledged: publicJson(acknowledged.match),
            shelveRequest: shelve.summary,
            shelved: publicJson(shelved.match),
            unshelveRequest: unshelve.summary,
            unshelved: publicJson(unshelved.match),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The shift lead acknowledged and then shelved the alarm through the public facade; the gateway bound both audit actors to the signed-in principal, and unshelving returned it to acknowledged.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('alarm-acknowledged-and-shelved', error, 'manual')
      }
    }

    if (alarmEventId) {
      try {
        const workOrder = await pollRows(
          '/api/business-console/v1/maintenance/work-orders',
          { organizationId, environmentId, deviceAssetIds: deviceAssetId, take: 100 },
          (row) => textOf(row.sourceAlarmId) === alarmRuleCode,
        )
        maintenanceWorkOrderId = textOf(workOrder.match.workOrderId).trim()
        const openWorkOrders = rowsOf(workOrder.call.payload).filter(
          (row) => textOf(row.deviceAssetId) === deviceAssetId,
        )
        if (
          !maintenanceWorkOrderId ||
          openWorkOrders.length !== 1 ||
          textOf(workOrder.match.status).toLowerCase() !== 'open' ||
          textOf(workOrder.match.priority) !== alarmSeverity ||
          textOf(workOrder.match.deviceAssetId) !== deviceAssetId
        ) {
          throw new Error(
            `Maintenance did not open exactly one alarm-sourced work order for ${deviceCode}: ${safeText(JSON.stringify(openWorkOrders))}.`,
          )
        }
        const detail = await call(
          'GET',
          queryPath(
            `/api/business-console/v1/maintenance/work-orders/${encodeURIComponent(maintenanceWorkOrderId)}`,
            { organizationId, environmentId },
          ),
        )
        const detailData = asRecord(dataOf(detail.payload))
        if (
          textOf(detailData.sourceAlarmId) !== alarmRuleCode ||
          textOf(detailData.sourceReferenceId) !== alarmRuleCode
        ) {
          throw new Error(
            `Maintenance work order ${maintenanceWorkOrderId} did not preserve the run-scoped alarm lineage: ${safeText(JSON.stringify(detailData))}.`,
          )
        }
        record({
          node: 'alarm-maintenance-work-order',
          sourceObject: alarmRuleCode,
          downstreamObject: alarmRuleCode,
          stableKey: `${alarmRuleCode} -> maintenance work order (sourceAlarmId=${alarmRuleCode})`,
          automationMode: 'automatic',
          request: workOrder.call.summary,
          responseOrLog: {
            poll: workOrder.poll,
            workOrder: publicJson(detailData),
            documentNumberGap:
              'Maintenance identifies its work orders only by an internal uuid; sourceAlarmId (the run-scoped rule code) is the readable business key used here. A human-readable maintenance work-order number is a tracked product gap, not a harness shortcut.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The alarm crossed Redis into Maintenance and automatically opened exactly one repair work order for this device, inheriting the alarm severity as its priority.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('alarm-maintenance-work-order', error)
      }
    }

    if (maintenanceWorkOrderId) {
      try {
        const availability = await pollData(
          '/api/business-console/v1/equipment/availability',
          {
            organizationId,
            environmentId,
            deviceAssetIds: deviceAssetId,
            windowStartUtc: faultOccurredAtUtc.toISOString(),
            windowEndUtc: new Date().toISOString(),
          },
          (data) => {
            const windows = listOf(data.items)
            return (
              windows.some(
                (item) =>
                  textOf(item.reasonCode) === 'equipment.activeAlarm' &&
                  textOf(item.availabilityStatus) === 'unavailable',
              ) &&
              windows.some(
                (item) =>
                  textOf(item.reasonCode) === 'equipment.stateUnavailable' &&
                  textOf(item.availabilityStatus) === 'unavailable',
              )
            )
          },
        )
        const windows = listOf(availability.data.items)
        const alarmBlock = windows.find(
          (item) => textOf(item.reasonCode) === 'equipment.activeAlarm',
        )!
        const stateBlock = windows.find(
          (item) => textOf(item.reasonCode) === 'equipment.stateUnavailable',
        )!
        if (
          textOf(alarmBlock.sourceType) !== 'alarm' ||
          textOf(alarmBlock.sourceReferenceId) !== alarmEventId ||
          textOf(alarmBlock.severity) !== 'critical' ||
          textOf(stateBlock.sourceType) !== 'device-state'
        ) {
          throw new Error(
            `Equipment availability did not attribute the block to the run-scoped alarm and faulted state: ${safeText(JSON.stringify(windows))}.`,
          )
        }
        const overview = await call(
          'GET',
          queryPath('/api/business-console/v1/equipment/overview', {
            organizationId,
            environmentId,
            deviceAssetIds: deviceAssetId,
          }),
        )
        const overviewDevice = listOf(asRecord(dataOf(overview.payload)).devices).find(
          (item) => textOf(item.deviceAssetId) === deviceAssetId,
        )
        if (
          !overviewDevice ||
          textOf(overviewDevice.currentState).toLowerCase() !== 'faulted' ||
          Number(overviewDevice.activeAlarmCount) !== 1 ||
          Number(overviewDevice.activeBlockCount) < 1
        ) {
          throw new Error(
            `Equipment overview did not show the device as unavailable: ${safeText(JSON.stringify(overviewDevice))}.`,
          )
        }
        const faultedOee = await readOee()
        const faultedOeeData = asRecord(dataOf(faultedOee.payload))
        if (
          Number(faultedOeeData.stateSampleCount) !== 2 ||
          Number(faultedOeeData.availabilityRate) !== expectedFaultedAvailabilityRate
        ) {
          throw new Error(
            `OEE did not drop for the faulted window: ${safeText(JSON.stringify(faultedOeeData))}.`,
          )
        }
        record({
          node: 'alarm-device-unavailable-block',
          sourceObject: alarmRuleCode,
          downstreamObject: deviceAssetId,
          stableKey: `${alarmRuleCode} -> ${deviceCode} unavailable (active-alarm + faulted state)`,
          automationMode: 'automatic',
          request: availability.call.summary,
          responseOrLog: {
            poll: availability.poll,
            alarmBlock: publicJson(alarmBlock),
            stateBlock: publicJson(stateBlock),
            overview: publicJson(overviewDevice),
            faultedOee: publicJson(faultedOeeData),
            maintenanceUnavailabilityDisclosure:
              'The alarm-opened maintenance work order does not itself mark the asset unavailable because the automatic path supplies no assetUnavailableReason; the unavailability proven here is the telemetry active-alarm and faulted-state block.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'Planning and the shop-floor board both see the machine as unavailable: an active-alarm block and a faulted-state block on the exact device, with OEE availability halved.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('alarm-device-unavailable-block', error)
      }
    }

    if (evidence.get('alarm-device-unavailable-block')?.conclusion === 'runtime-confirmed') {
      try {
        const recovered = await publishSample(
          'recovery-running',
          recoveryOccurredAtUtc,
          recoveredValue,
          'running',
        )
        const cleared = await pollRows(
          '/api/business-console/v1/equipment/alarms',
          { organizationId, environmentId, deviceAssetId, status: 'cleared', take: 100 },
          (row) => textOf(row.externalAlarmId) === alarmRuleCode,
        )
        if (
          textOf(cleared.match.status) !== 'cleared' ||
          new Date(textOf(cleared.match.clearedAtUtc)).getTime() !== recoveryOccurredAtUtc.getTime()
        ) {
          throw new Error(
            `IIoT did not clear ${alarmRuleCode} on the return-to-normal reading: ${safeText(JSON.stringify(cleared.match))}.`,
          )
        }
        const restored = await pollData(
          '/api/business-console/v1/equipment/availability',
          {
            organizationId,
            environmentId,
            deviceAssetIds: deviceAssetId,
            windowStartUtc: recoveryOccurredAtUtc.toISOString(),
            windowEndUtc: new Date().toISOString(),
          },
          (data) =>
            listOf(data.items).every((item) => textOf(item.availabilityStatus) !== 'unavailable'),
        )
        const deviceDetail = await pollData(
          `/api/business-console/v1/equipment/devices/${encodeURIComponent(deviceAssetId)}`,
          { organizationId, environmentId },
          (data) =>
            textOf(asRecord(data.currentState).currentState).toLowerCase() === 'running' &&
            listOf(asRecord(data.currentState).activeAlarms).length === 0,
        )
        const recoveredOee = await readOee()
        const recoveredOeeData = asRecord(dataOf(recoveredOee.payload))
        if (
          Number(recoveredOeeData.stateSampleCount) !== 3 ||
          Number(recoveredOeeData.availabilityRate) !== expectedRecoveredAvailabilityRate
        ) {
          throw new Error(
            `OEE did not recover for the closed window: ${safeText(JSON.stringify(recoveredOeeData))}.`,
          )
        }
        record({
          node: 'telemetry-return-to-normal-recovery',
          sourceObject: `${deviceCode} -> ${tagKey}=${recoveredValue}${tagUnitCode}`,
          downstreamObject: alarmRuleCode,
          stableKey: `${alarmRuleCode} -> cleared@${recoveryOccurredAtUtc.toISOString()} -> ${deviceCode} available`,
          automationMode: 'automatic',
          request: recovered.summary,
          responseOrLog: {
            clearedAlarm: publicJson(cleared.match),
            restoredAvailability: publicJson(restored.data),
            currentState: publicJson(asRecord(deviceDetail.data.currentState)),
            oeeProgression: {
              baselineAvailabilityRate: expectedBaselineAvailabilityRate,
              faultedAvailabilityRate: expectedFaultedAvailabilityRate,
              recoveredAvailabilityRate: Number(recoveredOeeData.availabilityRate),
              window: {
                windowStartUtc: timelineStartUtc.toISOString(),
                windowEndUtc: oeeWindowEndUtc.toISOString(),
              },
            },
            recoveredOee: publicJson(recoveredOeeData),
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'A return-to-normal reading cleared the alarm automatically, the device is available again with no active blocks, and OEE availability moves 100% -> 50% -> 70% across the same closed window.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('telemetry-return-to-normal-recovery', error)
      }
    }

    if (evidence.get('telemetry-return-to-normal-recovery')?.conclusion === 'runtime-confirmed') {
      try {
        const reliability = await pollData(
          `/api/business-console/v1/maintenance/assets/${encodeURIComponent(deviceAssetId)}/reliability`,
          {
            organizationId,
            environmentId,
            windowStartUtc: timelineStartUtc.toISOString(),
            windowEndUtc: reliabilityWindowEndUtc.toISOString(),
          },
          (data) => Number(data.failureCount) === 1,
        )
        const reliabilityData = reliability.data
        if (
          Number(reliabilityData.repairCount) !== 0 ||
          reliabilityData.mttrMinutes !== null ||
          !(Number(reliabilityData.mtbfHours) > 0) ||
          !textOf(reliabilityData.mtbfRuntimeSource)
        ) {
          throw new Error(
            `Reliability did not reflect the run-scoped failure: ${safeText(JSON.stringify(reliabilityData))}.`,
          )
        }
        const summary = await call(
          'GET',
          queryPath('/api/business-console/v1/maintenance/reliability/summary', {
            organizationId,
            environmentId,
            deviceAssetId,
            windowStartUtc: timelineStartUtc.toISOString(),
            windowEndUtc: reliabilityWindowEndUtc.toISOString(),
          }),
        )
        // The cost summary counts completed repairs only; with completion unreachable through the
        // public facade it must stay empty for this device rather than invent a finished repair.
        const summaryItem = listOf(asRecord(dataOf(summary.payload)).items).find(
          (item) => textOf(item.deviceAssetId) === deviceAssetId,
        )
        if (summaryItem) {
          throw new Error(
            `Reliability summary reported a completed repair that this run never performed: ${safeText(JSON.stringify(summaryItem))}.`,
          )
        }
        record({
          node: 'equipment-reliability-readface-shift',
          sourceObject: alarmRuleCode,
          downstreamObject: deviceAssetId,
          stableKey: `${deviceCode} -> failureCount 0 -> 1 (source alarm ${alarmRuleCode})`,
          automationMode: 'automatic',
          request: reliability.call.summary,
          responseOrLog: {
            poll: reliability.poll,
            before: publicJson(baselineReliability),
            after: publicJson(reliabilityData),
            completedRepairSummaryItem: summaryItem ? publicJson(summaryItem) : null,
            mttrFacadeGap:
              'MTTR stays null because closing a maintenance work order requires a downtime-reason code, and the downtime-reason catalog has no BusinessGateway facade and is not seeded (facade-coverage-matrix marks it deferred). This run therefore proves the MTBF/failure-count shift only, and refuses to fabricate a repair completion through an internal endpoint.',
          },
          conclusion: 'runtime-confirmed',
          demoWording:
            'The reliability read face moved with the incident: no failures before, one alarm-sourced failure and a real MTBF afterwards, attributed to this exact device.',
          responsibilityIssue: '#1086',
        })
      } catch (error) {
        markFailure('equipment-reliability-readface-shift', error)
      }
    }
  } finally {
    const entries = requiredNodes.map((node) => evidence.get(node)!)
    await writeFile(
      evidencePath!,
      JSON.stringify(
        {
          issue: 'Linear MAN-520 / GitHub #1099',
          generatedAtUtc: new Date().toISOString(),
          runSuffix: suffix,
          organizationId,
          environmentId,
          deviceCode,
          alarmRuleCode,
          runtimeProfileSource,
          transport,
          persistence,
          assertionBoundary:
            'public BusinessGateway HTTP only; no database reads as business assertions',
          setup,
          entries,
          summary: Object.fromEntries(
            (['runtime-confirmed', 'gap', 'not-verified'] as const).map((conclusion) => [
              conclusion,
              entries.filter((entry) => entry.conclusion === conclusion).length,
            ]),
          ),
        },
        null,
        2,
      ),
      'utf8',
    )
    sessionCredential = ''
  }

  const entries = requiredNodes.map((node) => evidence.get(node)!)
  const unacceptableEntries = entries.filter((entry) => entry.conclusion !== 'runtime-confirmed')
  expect(
    unacceptableEntries.map((entry) => ({
      node: entry.node,
      conclusion: entry.conclusion,
      responsibilityIssue: entry.responsibilityIssue,
    })),
    'Every equipment-branch node must be runtime-confirmed through public BusinessGateway evidence.',
  ).toEqual([])
  expect(entries.some((entry) => entry.conclusion === 'runtime-confirmed')).toBe(true)
})
