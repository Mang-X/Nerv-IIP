import { describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef, type Ref } from 'vue'

type Row = { operationTaskId: string; workOrderId: string; operationSequence?: number }

// 设备上的执行中工序与全范围可搜工序：不搜索时只回本机的，搜索时按工单号在全范围里过滤。
const deviceRows: Row[] = [{ operationTaskId: 'OP-1', workOrderId: 'WO-1', operationSequence: 10 }]
const allRows: Row[] = [
  ...deviceRows,
  { operationTaskId: 'OP-2', workOrderId: 'WO-2', operationSequence: 10 },
  { operationTaskId: 'OP-3', workOrderId: 'WO-3', operationSequence: 20 },
]

vi.mock('@/composables/useBusinessMes', () => ({
  useMesTelemetryCandidateTargetTasks: (
    _context: unknown,
    _deviceAssetId: Ref<string>,
    keyword: Ref<string>,
  ) => ({
    tasks: computed(() => {
      const search = keyword.value.trim()
      return search ? allRows.filter((row) => row.workOrderId.includes(search)) : deviceRows
    }),
    pending: shallowRef(false),
    error: shallowRef(null),
    refresh: vi.fn(),
  }),
}))
vi.mock('@/composables/useBusinessDeviceDirectory', () => ({
  useDeviceAssetNames: () => ({ resolveDeviceName: () => undefined }),
}))

import { useTelemetryCandidateTarget } from './useTelemetryCandidateTarget'

function setup(candidate: Record<string, unknown> | undefined) {
  const current = shallowRef(candidate)
  const target = useTelemetryCandidateTarget(current as never, shallowRef({}) as never)
  return { current, ...target }
}

describe('useTelemetryCandidateTarget', () => {
  it('用户显式点选优先于候选自带的工序', () => {
    const { target, choose } = setup({
      candidateId: 'A',
      deviceAssetId: 'DEV-1',
      workOrderId: 'WO-3',
      operationTaskId: 'OP-3',
    })
    expect(target.value?.operationTaskId).toBe('OP-3')

    choose(allRows[0] as never)
    expect(target.value?.operationTaskId).toBe('OP-1')
  })

  it('切换候选时清掉上一个候选的点选、关键词与改选状态', async () => {
    const { current, target, keyword, choosing, choose } = setup({
      candidateId: 'A',
      deviceAssetId: 'DEV-1',
    })
    keyword.value = 'WO-3'
    choosing.value = true
    choose(allRows[2] as never)
    expect(target.value?.operationTaskId).toBe('OP-3')

    current.value = { candidateId: 'B', deviceAssetId: 'DEV-1' }
    await nextTick()

    expect(keyword.value).toBe('')
    expect(choosing.value).toBe(false)
    // B 不带工单工序：只能落到本机唯一在制的 OP-1，绝不能继承 A 选的 OP-3。
    expect(target.value?.operationTaskId).toBe('OP-1')
  })

  it('搜索时唯一一条结果不会被自动当成目标', () => {
    const { target, keyword } = setup({ candidateId: 'A', deviceAssetId: 'DEV-1' })
    expect(target.value?.operationTaskId).toBe('OP-1')

    keyword.value = 'WO-2'
    expect(target.value).toBeNull()
  })
})
