import {
  coladaState,
  pagingErrorCases,
  pagingIdentityResetCases,
  deferred,
  setupBusinessWmsTests,
} from './useBusinessWmsTestFixture'
import { expect, it } from 'vitest'
import { nextTick } from 'vue'
import { useWmsCount, useWmsInbound, useWmsOutbound, useWmsPicking, useWmsPutaway } from '.'
import { describe } from 'vitest'

describe('PDA WMS composables', () => {
  setupBusinessWmsTests()

  it('unbinds task rows immediately when the selected WMS scope changes', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            allowedActions: ['start'],
            blockReasons: null,
          },
        ],
        total: 1,
      },
    })
    const result = useWmsPicking()

    expect(result.tasks.value).toHaveLength(1)
    result.scopeKey.value = 'work-pool:WMS-SITE-001'
    await nextTick()

    expect(result.tasks.value).toEqual([])
    expect(result.total.value).toBe(0)
  })

  it('normalizes task rows and refreshes the authorized query from page zero', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: [
          {
            warehouseTaskId: 'pick-1',
            taskNo: 'PICK-001',
            allowedActions: null,
            blockReasons: null,
          },
          { taskNo: '缺少可信标识' },
        ],
        total: 45,
      },
    })
    const result = useWmsPicking()

    expect(result.filters.take).toBe(20)
    expect(result.tasks.value).toEqual([
      expect.objectContaining({
        warehouseTaskId: 'pick-1',
        allowedActions: [],
        blockReasons: [],
      }),
    ])

    await result.refresh()
    expect(coladaState.refetchById.get('listBusinessConsoleWmsPickingTasks')).toHaveBeenCalledTimes(
      1,
    )
  })

  it.each([
    {
      name: 'inbound',
      id: 'listBusinessConsoleWmsInboundOrders',
      list: coladaState.listInbound,
      create: () => {
        const result = useWmsInbound()
        return { rows: result.orders, ...result }
      },
      makeItem: (index: number) => ({ inboundOrderId: `inbound-${index}`, status: 'Open' }),
    },
    {
      name: 'outbound',
      id: 'listBusinessConsoleWmsOutboundOrders',
      list: coladaState.listOutbound,
      create: () => {
        const result = useWmsOutbound()
        return { rows: result.orders, ...result }
      },
      makeItem: (index: number) => ({ outboundOrderId: `outbound-${index}`, status: 'Open' }),
    },
    {
      name: 'picking',
      id: 'listBusinessConsoleWmsPickingTasks',
      list: coladaState.listPicking,
      create: () => {
        const result = useWmsPicking()
        return { rows: result.tasks, ...result }
      },
      makeItem: (index: number) => ({
        warehouseTaskId: `picking-${index}`,
        status: 'Open',
        allowedActions: [],
      }),
    },
    {
      name: 'putaway',
      id: 'listBusinessConsoleWmsPutawayTasks',
      list: coladaState.listPutaway,
      create: () => {
        const result = useWmsPutaway()
        return { rows: result.tasks, ...result }
      },
      makeItem: (index: number) => ({
        warehouseTaskId: `putaway-${index}`,
        status: 'Open',
        allowedActions: [],
      }),
    },
    {
      name: 'count',
      id: 'listBusinessConsoleWmsCountExecutions',
      list: coladaState.listCount,
      create: () => {
        const result = useWmsCount()
        return { rows: result.executions, ...result }
      },
      makeItem: (index: number) => ({ countExecutionId: `count-${index}`, status: 'Open' }),
    },
  ])(
    'loads all 520 $name rows with fixed take and advancing skip, then stops at no-more',
    async ({ id, list, create, makeItem }) => {
      const total = 520
      const page = (skip: number) =>
        Array.from({ length: Math.min(20, total - skip) }, (_, offset) => makeItem(skip + offset))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: page(0), total },
      })
      list.mockImplementation(async ({ query }: { query: { skip?: number } }) => ({
        data: {
          success: true,
          data: { items: page(query.skip ?? 0), total },
        },
      }))
      const result = create()

      expect(result.rows.value).toHaveLength(20)
      const first = result.loadMore()
      const duplicate = result.loadMore()
      await Promise.all([first, duplicate])
      expect(list).toHaveBeenCalledTimes(1)

      for (let pageIndex = 2; pageIndex < 26; pageIndex += 1) {
        await result.loadMore()
      }

      expect(list).toHaveBeenCalledTimes(25)
      expect(list.mock.calls.map(([options]) => options.query.skip)).toEqual(
        Array.from({ length: 25 }, (_, index) => (index + 1) * 20),
      )
      expect(list.mock.calls.every(([options]) => options.query.take === 20)).toBe(true)
      expect(result.rows.value).toHaveLength(520)
      expect(new Set(result.rows.value.map((item) => JSON.stringify(item))).size).toBe(520)

      await result.loadMore()
      expect(list).toHaveBeenCalledTimes(25)
    },
  )

  it.each(pagingErrorCases)(
    'does not expose a rejected stale $name page after scope and filter reset',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      let rejectPage!: (reason?: unknown) => void
      list.mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectPage = reject
        }),
      )
      const result = create()
      const staleFailure = new Error(`${id}: stale loadMore failure`)

      const load = result.loadMore()
      result.scopeKey.value = 'work-pool:WMS-SITE-001'
      result.filters.keyword = 'NEW-FILTER'
      await nextTick()
      expect(result.error.value).toBeUndefined()

      rejectPage(staleFailure)

      await expect(load).rejects.toBe(staleFailure)
      expect(result.error.value).toBeUndefined()
    },
  )

  it('keeps the complete picking snapshot when refresh resolves with success:false', async () => {
    const oldPage = (skip: number) =>
      Array.from({ length: 20 }, (_, index) => ({
        warehouseTaskId: `picking-${skip + index}`,
        status: 'Open',
        allowedActions: [],
      }))
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: { items: oldPage(0), total: 40 },
    })
    coladaState.listPicking.mockResolvedValueOnce({
      data: { success: true, data: { items: oldPage(20), total: 40 } },
    })
    const result = useWmsPicking()
    await result.loadMore()

    coladaState.refetchById
      .get('listBusinessConsoleWmsPickingTasks')!
      .mockImplementationOnce(async () => {
        coladaState.queryDataRefById.get('listBusinessConsoleWmsPickingTasks')!.value = {
          success: false,
          message: 'refresh rejected',
        }
        await nextTick()
      })
    await result.refresh()

    expect(result.tasks.value).toHaveLength(40)
    expect(result.total.value).toBe(40)
  })

  it.each(pagingErrorCases)(
    'records a rejected current-scope $name page in the appropriate error channel',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      const currentFailure = new Error(`${id}: current loadMore failure`)
      list.mockRejectedValueOnce(currentFailure)
      const result = create()

      await expect(result.loadMore()).rejects.toBe(currentFailure)
      expect(result.loadMoreError.value).toBe(currentFailure)
      expect(result.error.value).toBeUndefined()
    },
  )

  it.each(pagingErrorCases.filter(({ name }) => name === 'picking' || name === 'putaway'))(
    'clears the split $name load-more error after a successful retry',
    async ({ id, list, create, makeItem }) => {
      coladaState.queryDataById.set(id, {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => makeItem(index)),
          total: 40,
        },
      })
      const currentFailure = new Error(`${id}: current loadMore failure`)
      list.mockRejectedValueOnce(currentFailure).mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: Array.from({ length: 20 }, (_, index) => makeItem(index + 20)),
            total: 40,
          },
        },
      })
      const result = create()

      await expect(result.loadMore()).rejects.toBe(currentFailure)
      expect(result.loadMoreError.value).toBe(currentFailure)

      await result.loadMore()

      expect(result.loadMoreError.value).toBeUndefined()
    },
  )

  it('deduplicates a repeated picking page boundary without requesting the terminal page twice', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => ({
          warehouseTaskId: `pick-${index}`,
          allowedActions: [],
        })),
        total: 39,
      },
    })
    coladaState.listPicking.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => ({
            warehouseTaskId: `pick-${index + 19}`,
            allowedActions: [],
          })),
          total: 39,
        },
      },
    })
    const result = useWmsPicking()

    await result.loadMore()
    await result.loadMore()

    expect(result.tasks.value).toHaveLength(39)
    expect(new Set(result.tasks.value.map((task) => task.warehouseTaskId)).size).toBe(39)
    expect(coladaState.listPicking).toHaveBeenCalledTimes(1)
  })

  it('discards an in-flight picking page after the keyword changes', async () => {
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => ({
          warehouseTaskId: `pick-old-${index}`,
          allowedActions: [],
        })),
        total: 40,
      },
    })
    let resolvePage!: (value: unknown) => void
    coladaState.listPicking.mockReturnValue(
      new Promise((resolve) => {
        resolvePage = resolve
      }),
    )
    const result = useWmsPicking()

    const load = result.loadMore()
    result.filters.keyword = 'PICK-NEW'
    await nextTick()
    expect(result.tasks.value).toEqual([])

    resolvePage({
      data: {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => ({
            warehouseTaskId: `pick-old-${index + 20}`,
            allowedActions: [],
          })),
          total: 40,
        },
      },
    })
    await load

    expect(result.tasks.value).toEqual([])
  })

  it('clears accumulated rows on every task scope/filter dimension change', async () => {
    const initialEnvelope = () => ({
      success: true,
      data: {
        items: [{ warehouseTaskId: 'pick-old', allowedActions: [] }],
        total: 31,
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleWmsPickingTasks', initialEnvelope())
    const result = useWmsPicking()

    for (const change of [
      () => (result.scopeKey.value = 'work-pool:WMS-SITE-001'),
      () => (result.filters.status = 'Completed'),
      () => (result.filters.keyword = 'PICK-NEW'),
      () => (result.filters.locationCode = 'A-02'),
      () => (result.filters.lotNo = 'LOT-02'),
    ]) {
      coladaState.queryDataRefById.get('listBusinessConsoleWmsPickingTasks')!.value =
        initialEnvelope()
      await nextTick()
      expect(result.tasks.value).toHaveLength(1)

      change()
      await nextTick()

      expect(result.tasks.value).toEqual([])
      expect(result.filters.skip).toBe(0)
      expect(result.filters.take).toBe(20)
    }
  })

  it.each(pagingErrorCases)(
    'keeps all loaded $name rows when same-identity refresh fails and replaces them after success',
    async ({ id, list, create, makeItem }) => {
      const oldPage = (skip: number) =>
        Array.from({ length: 20 }, (_, index) => makeItem(skip + index))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: oldPage(0), total: 40 },
      })
      list.mockResolvedValueOnce({
        data: { success: true, data: { items: oldPage(20), total: 40 } },
      })
      const result = create()
      await result.loadMore()
      expect(result.rows.value).toHaveLength(40)

      const refreshFailure = new Error(`${id}: refresh failed`)
      coladaState.refetchById.get(id)!.mockRejectedValueOnce(refreshFailure)
      await expect(result.refresh()).rejects.toBe(refreshFailure)
      expect(result.rows.value).toHaveLength(40)

      const freshPage = Array.from({ length: 20 }, (_, index) => makeItem(100 + index))
      coladaState.refetchById.get(id)!.mockImplementationOnce(async () => {
        coladaState.queryDataRefById.get(id)!.value = {
          success: true,
          data: { items: freshPage, total: 20 },
        }
        await nextTick()
      })
      await result.refresh()

      expect(result.rows.value).toHaveLength(20)
      expect(result.rows.value).toEqual(
        expect.arrayContaining(freshPage.map((item) => expect.objectContaining(item))),
      )
      expect(result.filters.skip).toBe(0)
      expect(result.filters.take).toBe(20)
    },
  )

  it.each(pagingErrorCases)(
    'keeps the current $name load-more guard owned by the newest request after refresh invalidates an older request',
    async ({ id, list, create, makeItem }) => {
      const page = (skip: number) =>
        Array.from({ length: 20 }, (_, index) => makeItem(skip + index))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: page(0), total: 60 },
      })
      const oldRequest = deferred<{ data: unknown }>()
      const newRequest = deferred<{ data: unknown }>()
      list.mockReturnValueOnce(oldRequest.promise).mockReturnValueOnce(newRequest.promise)
      const result = create()

      const oldLoad = result.loadMore()
      expect(result.loadingMore.value).toBe(true)

      await result.refresh()
      expect(result.loadingMore.value).toBe(false)

      const newLoad = result.loadMore()
      expect(result.loadingMore.value).toBe(true)
      expect(list).toHaveBeenCalledTimes(2)

      oldRequest.resolve({
        data: { success: true, data: { items: page(20), total: 60 } },
      })
      await oldLoad

      expect(result.loadingMore.value).toBe(true)
      await result.loadMore()
      expect(list).toHaveBeenCalledTimes(2)

      newRequest.resolve({
        data: { success: true, data: { items: page(20), total: 60 } },
      })
      await newLoad

      expect(result.loadingMore.value).toBe(false)
    },
  )

  it.each(pagingIdentityResetCases)(
    'lets a new $name load-more own the guard after a $resetKind reset invalidates an older request',
    async ({ id, list, create, makeItem, resetKind }) => {
      const page = (skip: number) =>
        Array.from({ length: 20 }, (_, index) => makeItem(skip + index))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: page(0), total: 60 },
      })
      const oldRequest = deferred<{ data: unknown }>()
      const newRequest = deferred<{ data: unknown }>()
      list.mockReturnValueOnce(oldRequest.promise).mockReturnValueOnce(newRequest.promise)
      const result = create()

      const oldLoad = result.loadMore()
      expect(result.loadingMore.value).toBe(true)

      if (resetKind === 'filter') {
        result.filters.keyword = 'NEW'
      } else {
        result.scopeKey.value = 'work-pool:WMS-SITE-001'
      }
      await nextTick()
      expect(result.loadingMore.value).toBe(false)

      coladaState.queryDataRefById.get(id)!.value = {
        success: true,
        data: { items: page(100), total: 60 },
      }
      await nextTick()

      const newLoad = result.loadMore()
      expect(result.loadingMore.value).toBe(true)
      expect(list).toHaveBeenCalledTimes(2)

      if (resetKind === 'filter') {
        oldRequest.resolve({
          data: { success: true, data: { items: page(20), total: 60 } },
        })
        await oldLoad
      } else {
        const staleFailure = new Error(`${id}: stale page failed`)
        oldRequest.reject(staleFailure)
        await expect(oldLoad).rejects.toBe(staleFailure)
        expect(result.loadMoreError.value).toBeUndefined()
      }

      expect(result.loadingMore.value).toBe(true)
      await result.loadMore()
      expect(list).toHaveBeenCalledTimes(2)

      newRequest.resolve({
        data: { success: true, data: { items: page(20), total: 60 } },
      })
      await newLoad

      expect(result.loadingMore.value).toBe(false)
    },
  )

  it.each(pagingErrorCases)(
    'clears the $name page error after an explicit retry succeeds and appends rows',
    async ({ id, list, create, makeItem }) => {
      const page = (skip: number) =>
        Array.from({ length: 20 }, (_, index) => makeItem(skip + index))
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: page(0), total: 60 },
      })
      const failure = new Error(`${id}: page failed`)
      list.mockRejectedValueOnce(failure).mockResolvedValueOnce({
        data: { success: true, data: { items: page(20), total: 60 } },
      })
      const result = create()

      await expect(result.loadMore()).rejects.toBe(failure)
      expect(result.loadMoreError.value).toBe(failure)
      expect(result.rows.value).toHaveLength(20)

      await result.loadMore()

      expect(result.loadMoreError.value).toBeUndefined()
      expect(result.rows.value).toHaveLength(40)
    },
  )
})
