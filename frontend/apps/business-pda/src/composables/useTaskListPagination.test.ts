import { computed, nextTick, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { useTaskListPagination } from './useTaskListPagination'

interface Row {
  id: string
}

describe('useTaskListPagination', () => {
  it('按已加载游标取次页并以稳定 ID 去重', async () => {
    const firstPage = shallowRef({ items: [{ id: 'a' }, { id: 'b' }], total: 3 })
    const fetchPage = vi.fn().mockResolvedValue({ items: [{ id: 'b' }, { id: 'c' }], total: 3 })
    const pager = useTaskListPagination<Row>({
      identity: () => 'scope:pending',
      firstPage,
      pageSize: 2,
      itemKey: (row) => row.id,
      fetchPage,
      refreshFirstPage: vi.fn(),
    })
    await nextTick()

    await pager.loadMore()

    expect(fetchPage).toHaveBeenCalledWith({ skip: 2, take: 2 })
    expect(pager.items.value.map((row) => row.id)).toEqual(['a', 'b', 'c'])
    expect(pager.hasMore.value).toBe(false)
  })

  it('次页失败保留已加载数据并提供局部错误', async () => {
    const firstPage = shallowRef({ items: [{ id: 'a' }], total: 2 })
    const pager = useTaskListPagination<Row>({
      identity: () => 'scope:pending',
      firstPage,
      pageSize: 1,
      itemKey: (row) => row.id,
      fetchPage: vi.fn().mockRejectedValue(new Error('page-2 failed')),
      refreshFirstPage: vi.fn(),
    })
    await nextTick()

    await pager.loadMore()

    expect(pager.items.value.map((row) => row.id)).toEqual(['a'])
    expect(pager.loadMoreError.value).toBeInstanceOf(Error)
  })

  it('筛选身份切换会清空旧页且丢弃迟到响应', async () => {
    const identity = shallowRef('scope-a')
    const firstPage = shallowRef({ items: [{ id: 'a' }], total: 2 })
    let release!: (page: { items: Row[]; total: number }) => void
    const fetchPage = vi.fn(
      () =>
        new Promise<{ items: Row[]; total: number }>((resolve) => {
          release = resolve
        }),
    )
    const pager = useTaskListPagination<Row>({
      identity: computed(() => identity.value),
      firstPage,
      pageSize: 1,
      itemKey: (row) => row.id,
      fetchPage,
      refreshFirstPage: vi.fn(),
    })
    await nextTick()

    const loading = pager.loadMore()
    identity.value = 'scope-b'
    firstPage.value = { items: [{ id: 'x' }], total: 1 }
    await nextTick()
    release({ items: [{ id: 'stale' }], total: 2 })
    await loading

    expect(pager.items.value.map((row) => row.id)).toEqual(['x'])
  })
})
