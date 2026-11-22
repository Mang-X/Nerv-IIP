import { setupMaintenanceDetailTests } from './useMaintenanceSelfWorkOrdersTestFixture'
import {
  api,
  seedPrincipal,
  requestFor,
  publishedValue,
  authoritativeDetail,
} from './useMaintenanceSelfWorkOrdersTestFixture'

import { describe, expect, it } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import { useMaintenanceSelfWorkOrderDetail } from '.'

describe('useMaintenanceSelfWorkOrderDetail', () => {
  setupMaintenanceDetailTests()

  it.each(['', 'WO-DETAIL', '00000000-0000-0000-0000-000000000000'])(
    'keeps an invalid or empty strong ID locally unavailable without downstream requests: %j',
    async (routeId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(computed(() => routeId))

      expect(result.enabled.value).toBe(false)
      expect(api.queryFactories.get('maintenance-detail')?.().enabled).toBe(false)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
      expect(api.queryFactories.get('maintenance-identities')?.().enabled).toBe(false)

      await result.refresh()

      expect(api.refetches.get('maintenance-detail')).not.toHaveBeenCalled()
      expect(api.refetches.get('device-detail')).not.toHaveBeenCalled()
      expect(api.workers).not.toHaveBeenCalled()
      expect(api.resourceDetail).not.toHaveBeenCalled()
    },
  )

  it('revalidates the strong ID with the authenticated self scope', () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrderDetail(computed(() => '019f0000-0000-7000-8000-000000000101'))

    expect(requestFor('maintenance-detail')).toEqual({
      path: { workOrderId: '019f0000-0000-7000-8000-000000000101' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
      },
    })
  })

  it('rejects and clears a matching-ID detail when authoritative fields are incomplete', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: '019f0000-0000-7000-8000-000000000101',
        deviceAssetId: 'device-1',
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
        lifecycle: [],
        assignedTechnicianUserId: 'principal-1',
        assignedTeamId: null,
      },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: '019f0000-0000-7000-8000-000000000101',
        deviceAssetId: 'device-1',
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([42, { inspectionId: 'opaque' }])(
    'rejects a malformed detail source reference without throwing: %j',
    async (sourceReferenceId) => {
      seedPrincipal()
      const workOrderId = '019f0000-0000-7000-8000-000000000101'
      const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId))

      expect(() => {
        api.data.get('maintenance-detail')!.value = {
          success: true,
          data: { ...authoritativeDetail(workOrderId, 'DEV-CNC-01'), sourceReferenceId },
        }
      }).not.toThrow()
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value).toBeUndefined()
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it('rejects a non-canonical response ID even when it exactly matches an invalid route value', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000101')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    routeId.value = 'WO-DETAIL'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-DETAIL', 'DEV-CNC-01'),
    }
    await nextTick()

    expect(result.enabled.value).toBe(false)
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['technician', { assignedTeamId: null }],
    ['team', { assignedTechnicianUserId: 'principal-1' }],
    ['both', {}],
  ])('rejects detail when the explicit %s assignment field is missing', async (_, assignments) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        assignedTechnicianUserId: undefined,
        assignedTeamId: undefined,
        ...assignments,
      },
    }
    const data = (publishedValue('maintenance-detail') as { data: Record<string, unknown> }).data
    if (!('assignedTechnicianUserId' in assignments)) delete data.assignedTechnicianUserId
    if (!('assignedTeamId' in assignments)) delete data.assignedTeamId
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([null, 'principal-other'])(
    'rejects detail assigned to a different or absent technician: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          assignedTechnicianUserId,
        },
      }
      await nextTick()

      expect(result.workOrder.value).toBeUndefined()
      expect(result.hasFailedResponse.value).toBe(true)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it.each([
    ['actor principal', { actorPrincipalId: ' ' }],
    ['reason', { reason: '' }],
    ['technician', { technicianUserId: undefined }],
    ['team', { teamId: undefined }],
  ])('rejects detail when a lifecycle event has an invalid %s audit field', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    const event: Record<string, unknown> = {
      action: 'accept',
      fromStatus: 'opened',
      toStatus: 'accepted',
      actorPrincipalId: 'principal-1',
      technicianUserId: null,
      teamId: null,
      reason: '现场接单',
      resultingVersion: 2,
      occurredAtUtc: '2026-08-02T01:00:00.000Z',
      ...override,
    }
    if (Object.hasOwn(override, 'technicianUserId')) delete event.technicianUserId
    if (Object.hasOwn(override, 'teamId')) delete event.teamId
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        lifecycle: [event],
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each(['1', '2026-02-30T01:00:00Z', '2026-08-02T01:00:00'])(
    'rejects a non-RFC3339 or impossible opened timestamp: %s',
    async (openedAtUtc) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          openedAtUtc,
        },
      }
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value).toBeUndefined()
    },
  )

  it.each(['1', '2026-02-30T01:00:00Z', '2026-08-02T01:00:00'])(
    'rejects a non-RFC3339 or impossible lifecycle timestamp: %s',
    async (occurredAtUtc) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          lifecycle: [
            {
              action: 'accept',
              fromStatus: 'open',
              toStatus: 'accepted',
              actorPrincipalId: 'principal-1',
              technicianUserId: 'principal-1',
              teamId: null,
              reason: '现场接单',
              resultingVersion: 2,
              occurredAtUtc,
            },
          ],
        },
      }
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
    },
  )

  it.each([
    ['403', { error: { status: 403 } }],
    ['timeout', { error: new Error('timeout') }],
    ['bad shape', { data: { success: true, data: { resourceType: 'device-asset' } } }],
  ])(
    'keeps authoritative assignment confirmed when device enrichment returns %s',
    async (_, outcome) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
      }
      await nextTick()
      if ('error' in outcome) api.errors.get('device-detail')!.value = outcome.error
      if ('data' in outcome) api.data.get('device-detail')!.value = outcome.data
      await nextTick()

      expect(result.authoritativeHasSuccessfulResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value?.assignedTechnicianUserId).toBe('principal-1')
      expect(result.deviceHasFailedResponse.value).toBe(true)
      expect(result.workOrder.value).toBeUndefined()
    },
  )

  it.each([
    ['closed status', { status: 'closed', allowedActions: ['start'] }],
    ['cancelled status', { status: 'cancelled', allowedActions: ['accept'] }],
    ['terminal block reason', { blockReasons: ['terminal-status'], allowedActions: ['start'] }],
    [
      'non-terminal status with terminal reason',
      { status: 'open', blockReasons: ['terminal-status'] },
    ],
    ['terminal status without terminal reason', { status: 'closed', blockReasons: [] }],
  ])('rejects contradictory terminal action facts: %s', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        ...override,
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['closed', ['terminal-status']],
    ['cancelled', ['terminal-status', 'manage-permission-required']],
    ['open', ['manage-permission-required']],
  ])('accepts consistent terminal and non-terminal facts: %s', async (status, blockReasons) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        status,
        blockReasons,
        allowedActions: [],
      },
    }
    await nextTick()

    expect(result.authoritativeHasSuccessfulResponse.value).toBe(true)
    expect(result.authoritativeWorkOrder.value?.status).toBe(status)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(true)
  })

  it.each([[null], [1]])(
    'rejects malformed block reasons without throwing during render: %j',
    async (blockReason) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )

      expect(() => {
        api.data.get('maintenance-detail')!.value = {
          success: true,
          data: {
            ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
            blockReasons: [blockReason],
          },
        }
      }).not.toThrow()
      await nextTick()

      expect(result.workOrder.value).toBeUndefined()
      expect(result.hasFailedResponse.value).toBe(true)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it('accepts an uppercase device reference resolved to a canonical strong ID', async () => {
    seedPrincipal()
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
    const workOrderId = '019f0000-0000-7000-8000-000000000101'
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId.toUpperCase()))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderId.toUpperCase(), deviceAssetId.toUpperCase()),
    }
    await nextTick()

    expect(requestFor('maintenance-detail')).toMatchObject({ path: { workOrderId } })
    expect(requestFor('device-detail')).toMatchObject({
      path: { resourceType: 'device-asset', code: deviceAssetId.toUpperCase() },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId,
        code: 'DEV-CNC-01',
        displayName: '一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()

    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.workOrder.value).toMatchObject({
      workOrderId,
      deviceAssetId: deviceAssetId.toUpperCase(),
    })
    expect(result.device.value?.code).toBe('DEV-CNC-01')
  })

  it('preserves an uppercase GUID-shaped device code through detail authority lookup', async () => {
    seedPrincipal()
    const deviceCode = '019F0000-0000-7000-8000-0000000000AA'
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
    const workOrderId = '019f0000-0000-7000-8000-000000000101'
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderId, deviceCode),
    }
    await nextTick()

    expect(requestFor('device-detail')).toMatchObject({
      path: { resourceType: 'device-asset', code: deviceCode },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: deviceAssetId.toUpperCase(),
        code: deviceCode,
        displayName: 'GUID 编码设备',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()

    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.workOrder.value?.deviceAssetId).toBe(deviceCode)
    expect(result.device.value?.deviceAssetId).toBe(deviceAssetId)
    expect(result.device.value?.code).toBe(deviceCode)
  })

  it.each([
    ['resource type', { resourceType: 42 }],
    ['organization', { organizationId: {} }],
    ['environment', { environmentId: 42 }],
    ['code', { code: {} }],
    ['device public ID', { deviceAssetId: '00000000-0000-0000-0000-000000000000' }],
    ['display name', { displayName: 42 }],
    ['missing active flag', { active: undefined }],
    ['active flag type', { active: 'true' }],
    ['inactive device', { active: false }],
    ['retired device', { retired: true }],
    ['device with retirement date', { retiredOn: '2026-08-01T00:00:00.000Z' }],
    ['missing snapshot version', { snapshotVersion: undefined }],
    ['blank snapshot version', { snapshotVersion: ' ' }],
    ['snapshot version type', { snapshotVersion: 7 }],
    ['site', { siteCode: {} }],
    ['plant', { plantCode: 42 }],
    ['workshop', { workshopCode: {} }],
    ['line', { lineCode: 42 }],
    ['work center', { workCenterCode: {} }],
    ['station', { stationCode: 42 }],
  ])('fails malformed device %s data closed without a computed exception', async (_, override) => {
    seedPrincipal()
    const workOrderId = '019f0000-0000-7000-8000-000000000101'
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderId, 'DEV-CNC-01'),
    }
    await nextTick()

    expect(() => {
      api.data.get('device-detail')!.value = {
        success: true,
        data: {
          resourceType: 'device-asset',
          deviceAssetId: '019f0000-0000-7000-8000-000000000001',
          code: 'DEV-CNC-01',
          displayName: '一号数控机床',
          active: true,
          snapshotVersion: 'device-v1',
          organizationId: 'org-001',
          environmentId: 'env-dev',
          ...override,
        },
      }
    }).not.toThrow()
    await nextTick()

    expect(result.authoritativeHasSuccessfulResponse.value).toBe(true)
    expect(result.deviceHasFailedResponse.value).toBe(true)
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.device.value).toBeUndefined()
  })
})
