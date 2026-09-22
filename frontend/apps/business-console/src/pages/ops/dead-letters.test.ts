import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref, shallowRef } from 'vue'

import DeadLettersPage from './dead-letters.vue'
import type { DeadLetterReplayOutcome } from '@/composables/useBusinessDeadLetters'
import { deadLetterRowKey } from '@/composables/useBusinessDeadLetters'

const notify = vi.hoisted(() => ({
  notifySuccess: vi.fn(),
  notifyWarning: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

vi.mock('@/utils/notify', () => ({
  notifySuccess: notify.notifySuccess,
  notifyWarning: notify.notifyWarning,
  notifyOperationFailure: notify.notifyOperationFailure,
  inlineErrorMessage: (error: unknown) => (error ? '加载失败' : ''),
}))

const state = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  unavailableSources: [] as Array<{ service: string; reason?: string }>,
  replayOutcomes: new Map<string, DeadLetterReplayOutcome>(),
  /** 下一次重放的返回值——`noHandler` 是「按了也不会有任何变化」那一档。 */
  nextOutcome: { status: 'replayed', succeeded: true } as DeadLetterReplayOutcome,
  filters: { service: 'all', eventType: '', status: 'all' },
}))

vi.mock('@/composables/useBusinessDeadLetters', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/composables/useBusinessDeadLetters')>()
  const { computed, reactive, ref } = await import('vue')
  return {
    ...actual,
    useBusinessDeadLetters: () => {
      const unavailableSources = computed(() => state.unavailableSources)
      return {
        availableServices: computed(() => ['Erp', 'Mes']),
        contextReady: computed(() => true),
        filteredServiceUnavailable: computed(() =>
          state.unavailableSources.some((source) => source.service === state.filters.service),
        ),
        filters: reactive(state.filters),
        hasUnavailableSource: computed(() => state.unavailableSources.length > 0),
        ignore: vi.fn(async () => undefined),
        ignorePending: ref(false),
        items: computed(() => state.items),
        listError: shallowRef(),
        listPending: ref(false),
        metrics: computed(() => ({ actionableCount: 3, pendingCount: 2, failedCount: 1 })),
        metricsError: shallowRef(),
        metricsPending: ref(false),
        refresh: vi.fn(async () => undefined),
        replayOne: vi.fn(async (service: string, deadLetterId: string) => {
          state.replayOutcomes.set(deadLetterRowKey(service, deadLetterId), state.nextOutcome)
          return state.nextOutcome
        }),
        replayOutcomes: state.replayOutcomes,
        replayPending: ref(false),
        replaySelected: vi.fn(async () => []),
        selectedDeadLetter: computed(() => undefined),
        detailError: shallowRef(),
        detailPending: ref(false),
        selectedRowKey: ref(''),
        selectedRowKeys: ref<string[]>([]),
        selectedTarget: computed(() => undefined),
        serviceMetrics: computed(() => []),
        unavailableSources,
      }
    },
  }
})

const stubs = { BusinessLayout: { template: '<main><slot /></main>' } }

function seedRow(service = 'Erp', id = 'dl-1') {
  state.items = [
    {
      service,
      deadLetter: {
        id,
        eventType: 'erp.OperationActualTimeLaborCost',
        consumerName: 'business-erp.operation-actual-time-labor-cost',
        failureCode: 'missing-work-center-cost-rate',
        status: 'pending',
        deadLetteredAtUtc: '2026-09-20T02:00:00Z',
      },
    },
  ]
}

async function mountPage() {
  const wrapper = mount(DeadLettersPage, { global: { stubs } })
  await flushPromises()
  return wrapper
}

describe('集成事件死信运维页', () => {
  beforeEach(() => {
    state.items = []
    state.unavailableSources = []
    // 真实 composable 暴露的是 reactive Map；用普通 Map 会让「结果写进去了但没重渲染」看起来像页面缺陷。
    state.replayOutcomes = reactive(new Map())
    state.nextOutcome = { status: 'replayed', succeeded: true }
    state.filters = { service: 'all', eventType: '', status: 'all' }
    notify.notifySuccess.mockClear()
    notify.notifyWarning.mockClear()
    notify.notifyOperationFailure.mockClear()
  })

  it('有来源没答上来时点名该服务，并说明计数不含它', async () => {
    state.unavailableSources = [{ service: 'Erp', reason: 'sourceUnavailable' }]
    const text = (await mountPage()).text()

    expect(text).toContain('Erp')
    expect(text).toContain('未读到')
    expect(text).toContain('计数因此偏小')
  })

  it('超时与不可用对用户不是同一句话', async () => {
    state.unavailableSources = [{ service: 'Erp', reason: 'sourceTimeout' }]
    const timeoutText = (await mountPage()).text()

    state.unavailableSources = [{ service: 'Erp', reason: 'sourceUnavailable' }]
    const unavailableText = (await mountPage()).text()

    expect(timeoutText).toContain('响应超时')
    expect(unavailableText).toContain('服务不可用')
    expect(timeoutText).not.toBe(unavailableText)
  })

  it('全部来源都答上来的空列表说「没有死信」，而不是含糊其辞', async () => {
    const text = (await mountPage()).text()

    expect(text).toContain('当前条件下没有死信')
    expect(text).not.toContain('未读到')
  })

  it('筛到的服务本身没答上来时，空态说的是「未读到」而不是「没有死信」', async () => {
    state.filters = { service: 'Erp', eventType: '', status: 'all' }
    state.unavailableSources = [{ service: 'Erp', reason: 'sourceUnavailable' }]
    const text = (await mountPage()).text()

    expect(text).toContain('本次未读到 Erp 的死信')
    expect(text).toContain('这不代表它没有死信')
    expect(text).not.toContain('当前条件下没有死信')
  })

  it('重放没能重放成功时不报成功，并把原因显示在行上', async () => {
    seedRow()
    state.nextOutcome = { status: 'noHandler', succeeded: false }
    const wrapper = await mountPage()

    await wrapper.find('[aria-label^="重放死信"]').trigger('click')
    await flushPromises()

    expect(notify.notifySuccess).not.toHaveBeenCalled()
    expect(notify.notifyWarning).toHaveBeenCalledWith(expect.stringContaining('该服务无重放能力'))
    expect(wrapper.text()).toContain('该服务无重放能力')
  })

  it('重放真的成功时才报成功', async () => {
    seedRow()
    state.nextOutcome = { status: 'replayed', succeeded: true }
    const wrapper = await mountPage()

    await wrapper.find('[aria-label^="重放死信"]').trigger('click')
    await flushPromises()

    expect(notify.notifyWarning).not.toHaveBeenCalled()
    expect(notify.notifySuccess).toHaveBeenCalledWith(expect.stringContaining('已重放'))
  })
})
