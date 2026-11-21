import { coladaState, reactiveAuthState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleSearchableDirectoryQueryOptions,
  recordBusinessConsoleMesDowntimeEventV2MutationOptions,
} from '@nerv-iip/api-client'
import { useMesDowntimeEvents } from '.'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('injects current business context and the selected scope into downtime v2 writes', async () => {
    const downtime = useMesDowntimeEvents()

    await downtime.recordDowntimeEvent({
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      workCenterId: 'WC-A',
      deviceAssetId: 'DEVICE-1',
      reasonCode: 'equipment-fault',
      startedAtUtc: '2026-08-26T00:30:00.000Z',
      idempotencyKey: 'downtime-key',
      scopeKind: 'work-center',
      scopeId: 'WC-A',
    })

    const mutation = vi
      .mocked(recordBusinessConsoleMesDowntimeEventV2MutationOptions)
      .mock.results.at(-1)?.value.mutation as ReturnType<typeof vi.fn>
    expect(mutation).toHaveBeenCalledWith({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workOrderId: 'WO-1',
        operationTaskId: 'OP-1',
        workCenterId: 'WC-A',
        deviceAssetId: 'DEVICE-1',
        reasonCode: 'equipment-fault',
        startedAtUtc: '2026-08-26T00:30:00.000Z',
        idempotencyKey: 'downtime-key',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
    })
  })

  // #1947：停机读面要按原因显示、筛选与汇总。
  it('defaults downtime reads to the latest 30 days and forwards an explicitly selected window', () => {
    vi.setSystemTime('2026-08-30T08:00:00.000Z')
    const downtime = useMesDowntimeEvents()
    const listOptions = vi.mocked(listBusinessConsoleMesDowntimeEventsQueryOptions)

    expect(listOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
        windowStartUtc: '2026-07-31T08:00:00.000Z',
        windowEndUtc: '2026-08-30T08:00:00.000Z',
      },
    })

    downtime.filters.keyword = 'filter'
    downtime.filters.workCenterId = 'WC-FILTER'
    downtime.filters.shiftId = 'SHIFT-FILTER'
    downtime.filters.deviceAssetId = 'DEV-FILTER'
    downtime.filters.reasonCode = 'DT-MECH'
    downtime.filters.skip = 5
    downtime.filters.take = 25
    downtime.filters.windowStartUtc = '2026-08-01T00:00:00.000Z'
    downtime.filters.windowEndUtc = '2026-08-15T00:00:00.000Z'
    coladaState.queryFactoriesById.get('listBusinessConsoleMesDowntimeEvents')!()

    expect(listOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        keyword: 'filter',
        workCenterId: 'WC-FILTER',
        shiftId: 'SHIFT-FILTER',
        deviceAssetId: 'DEV-FILTER',
        skip: 5,
        take: 25,
        reasonCode: 'DT-MECH',
        windowStartUtc: '2026-08-01T00:00:00.000Z',
        windowEndUtc: '2026-08-15T00:00:00.000Z',
      },
    })
    vi.useRealTimers()
  })

  it('advances the default downtime window on every refresh so newly started events fall inside it', async () => {
    vi.setSystemTime('2026-08-30T08:00:00.000Z')
    try {
      const downtime = useMesDowntimeEvents()
      const listOptions = vi.mocked(listBusinessConsoleMesDowntimeEventsQueryOptions)
      const startedAtUtc = '2026-08-30T08:01:00.000Z'

      for (const now of ['2026-08-30T08:01:05.000Z', '2026-08-30T08:02:00.000Z']) {
        vi.setSystemTime(now)
        await downtime.refreshDowntimeEvents()
        coladaState.queryFactoriesById.get('listBusinessConsoleMesDowntimeEvents')!()

        const query = listOptions.mock.calls.at(-1)![0]!.query!
        expect(query.windowEndUtc).toBe(now)
        expect(query.windowStartUtc).toBe(now.replace('2026-08-30', '2026-07-31'))
        expect(Date.parse(startedAtUtc)).toBeGreaterThanOrEqual(Date.parse(query.windowStartUtc!))
        expect(Date.parse(startedAtUtc)).toBeLessThan(Date.parse(query.windowEndUtc!))
      }
      expect(
        coladaState.queryRefetchById.get('listBusinessConsoleMesDowntimeEvents'),
      ).toHaveBeenCalledTimes(2)
    } finally {
      vi.useRealTimers()
    }
  })

  it('keeps an explicitly selected historical downtime window unchanged on refresh', async () => {
    vi.setSystemTime('2026-08-30T08:00:00.000Z')
    try {
      const downtime = useMesDowntimeEvents()
      const window = {
        windowStartUtc: '2026-08-01T00:00:00.000Z',
        windowEndUtc: '2026-08-15T00:00:00.000Z',
      }
      downtime.setDowntimeWindow(window)
      vi.setSystemTime('2026-08-31T09:00:00.000Z')
      await downtime.refreshDowntimeEvents()
      coladaState.queryFactoriesById.get('listBusinessConsoleMesDowntimeEvents')!()

      expect(
        vi.mocked(listBusinessConsoleMesDowntimeEventsQueryOptions).mock.calls.at(-1)![0]!.query,
      ).toMatchObject(window)
      expect(
        coladaState.queryRefetchById.get('listBusinessConsoleMesDowntimeEvents'),
      ).toHaveBeenCalledOnce()
    } finally {
      vi.useRealTimers()
    }
  })

  it('forwards the selected downtime reason into the list query and leaves it out when unset', () => {
    const downtime = useMesDowntimeEvents()
    const listOptions = vi.mocked(listBusinessConsoleMesDowntimeEventsQueryOptions)
    expect(listOptions.mock.calls.at(-1)![0]!.query).not.toHaveProperty('reasonCode')

    downtime.filters.reasonCode = 'DT-MECH'
    coladaState.queryFactoriesById.get('listBusinessConsoleMesDowntimeEvents')!()

    expect(listOptions.mock.calls.at(-1)![0]!.query).toMatchObject({
      reasonCode: 'DT-MECH',
    })
  })

  it('exposes the reason summary facet carried by the downtime list envelope', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesDowntimeEvents', {
      success: true,
      data: {
        items: [],
        total: 3,
        reasonSummary: [
          {
            reasonCode: 'DT-MECH',
            reasonName: '机械故障（轴承/传动/密封）',
            openCount: 1,
            durationMinutes: 90,
          },
        ],
      },
    })

    expect(useMesDowntimeEvents().downtimeReasonSummary.value).toEqual([
      {
        reasonCode: 'DT-MECH',
        reasonName: '机械故障（轴承/传动/密封）',
        openCount: 1,
        durationMinutes: 90,
      },
    ])
  })

  // 原因中文名是读面要素：没有停机登记范围（只读用户）也必须能取到字典。
  it('reads the downtime-reason directory without waiting for a write scope', () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.downtime.manage',
    )

    const downtime = useMesDowntimeEvents()
    expect(downtime.downtimeWriteScopeReady.value).toBe(false)

    const directoryFactory = coladaState.queryFactoriesById.get(
      'listBusinessConsoleSearchableDirectory',
    )!
    expect(directoryFactory().enabled).toBe(true)
  })

  // #2793 裁定：`enabled` 不加权限前置。前端 principal 的权限码只是提示，网关才是权威；
  // 加了前置一旦权限码滞后就再也不发请求，页面永远显示无权限且没有证据能纠正。
  // 少发的只是一次请求，归因交由 downtime.vue 分流 403 与其它失败来解决。
  // 若有人给这条 query 加上 `permissionCodes.includes(...)` 之类的前置，本用例必红。
  it('still issues the downtime-reason directory read when the principal lacks that read permission', () => {
    reactiveAuthState.principal = {
      ...reactiveAuthState.principal!,
      // 刻意给一组「别的都齐、唯独没有目录读权限」的权限码：这样它能通过这条 query 的
      // 其它所有前置（组织/环境上下文齐备），差别只在那一个码上。
      permissionCodes: ['business.mes.downtime.read', 'business.mes.downtime.manage'],
    }

    useMesDowntimeEvents()

    const directoryFactory = coladaState.queryFactoriesById.get(
      'listBusinessConsoleSearchableDirectory',
    )!
    expect(directoryFactory().enabled).toBe(true)
  })

  it('uses the searchable-directory 1-based page and server page-size bound for downtime reasons', () => {
    useMesDowntimeEvents()

    expect(listBusinessConsoleSearchableDirectoryQueryOptions).toHaveBeenCalledWith({
      path: { directoryType: 'downtime-reason' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        pageIndex: 1,
        pageSize: 100,
        rankingMode: 'default',
      },
    })
  })

  it('maps an available downtime-reason directory response to selectable options', () => {
    coladaState.queryDataById.set('listBusinessConsoleSearchableDirectory', {
      success: true,
      data: {
        status: 'available',
        items: [
          {
            id: 'reason-1',
            code: 'equipment-fault',
            displayName: '设备故障',
            sourceService: 'BusinessMaintenance',
            authorityDirectoryType: 'downtime-reason',
          },
        ],
      },
    })

    const downtime = useMesDowntimeEvents()

    // label 是登记弹窗既有的「名称（码）」口径；name 是只读面用的纯名称。
    expect(downtime.downtimeReasonOptions.value).toEqual([
      { value: 'equipment-fault', label: '设备故障（equipment-fault）', name: '设备故障' },
    ])
    expect(downtime.downtimeReasonsError.value).toBeUndefined()
  })

  it('keeps an unavailable downtime-reason directory fail closed without fabricated options', () => {
    coladaState.queryDataById.set('listBusinessConsoleSearchableDirectory', {
      success: true,
      data: {
        status: 'unavailable',
        reasonCode: 'directory-authority-unconfigured',
        items: [],
      },
    })

    const downtime = useMesDowntimeEvents()

    expect(downtime.downtimeReasonOptions.value).toEqual([])
    expect(downtime.downtimeReasonsError.value).toBeUndefined()
  })

  it('exposes downtime-reason directory errors while keeping registration options empty', () => {
    const directoryError = new Error('directory-page-invalid')
    coladaState.queryErrorById.set('listBusinessConsoleSearchableDirectory', directoryError)

    const downtime = useMesDowntimeEvents()

    expect(downtime.downtimeReasonOptions.value).toEqual([])
    expect(downtime.downtimeReasonsError.value).toBe(directoryError)
  })
})
