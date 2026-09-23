import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref, shallowRef } from 'vue'

import DeadLettersPage from './dead-letters.vue'
import type {
  DeadLetterBatchRowResult,
  DeadLetterReplayOutcome,
  DeadLetterReplayResult,
  DeadLetterTarget,
} from '@/composables/useBusinessDeadLetters'
import { deadLetterRowKey } from '@/composables/useBusinessDeadLetters'

/**
 * 截获层在 `@nerv-iip/ui` 的 `toast`，**不在** `@/utils/notify`：真实的 notify 要跑完共享文案映射。
 * 断言的是「映射之后、交给 toast 组件的最终字符串」——经过文案映射，但不是渲染后的 DOM。
 * 在 notify 层截获只能证明「传了什么参数」，而 `notifyOperationFailure` 的兜底句在映射非空时
 * 根本不上屏（`utils/notify.ts:270`），那样会再一次证明一句用户看不到的话。
 */
const toastSpy = vi.hoisted(() => ({
  success: vi.fn(),
  warning: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/ui')>()),
  toast: toastSpy,
}))

const state = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  unavailableSources: [] as Array<{ service: string; reason?: string }>,
  replayResults: new Map<string, DeadLetterReplayResult>(),
  /** 下一次重放直接抛出的错误；设了它，`nextOutcome` 就不生效。 */
  nextReplayError: undefined as unknown,
  /** 下一次重放的返回值——`noHandler` 是「按了也不会有任何变化」那一档。 */
  nextOutcome: { status: 'replayed', succeeded: true } as DeadLetterReplayOutcome,
  /** 下一次「重放选中」返回的逐行结果（由 composable 的单测另证其分类）。 */
  nextBatchResults: [] as DeadLetterBatchRowResult[],
  filters: { service: 'all', eventType: '', status: 'all' },
  permissionCodes: ['business.dlq.read', 'business.dlq.manage'] as string[],
  /** 与真实契约同宽：四张卡各取一个字段，mock 比真类型窄就等于那两张卡零覆盖。 */
  metrics: {
    pendingCount: 2,
    failedCount: 1,
    replayedCount: 7,
    ignoredCount: 4,
  } as Record<string, number> | undefined,
  metricsError: undefined as unknown,
  selectedRowKeys: [] as string[],
  selectedDeadLetter: undefined as Record<string, unknown> | undefined,
  selectedTarget: undefined as { service: string; deadLetterId: string } | undefined,
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
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
        metrics: computed(() => state.metrics),
        metricsError: computed(() => state.metricsError),
        metricsPending: ref(false),
        refresh: vi.fn(async () => undefined),
        replayOne: vi.fn(async (target: DeadLetterTarget) => {
          const rowKey = deadLetterRowKey(target.service, target.deadLetterId)
          if (state.nextReplayError) {
            // 行上记录由 composable 负责（其单测另证）；这里照同一分类复刻，只为给页面喂状态。
            if (actual.isReplayUnanswered(state.nextReplayError)) {
              state.replayResults.set(rowKey, {
                answered: false,
                statusAtAttempt: target.statusAtAttempt,
              })
            }
            throw state.nextReplayError
          }
          state.replayResults.set(rowKey, { answered: true, outcome: state.nextOutcome })
          return state.nextOutcome
        }),
        replayResults: state.replayResults,
        replayPending: ref(false),
        replaySelected: vi.fn(async () => state.nextBatchResults),
        selectedDeadLetter: computed(() => state.selectedDeadLetter),
        detailError: shallowRef(),
        detailPending: ref(false),
        selectedRowKey: ref(''),
        selectedRowKeys: ref<string[]>(state.selectedRowKeys),
        selectedTarget: computed(() => state.selectedTarget),
        serviceMetrics: computed(() => []),
        unavailableSources,
      }
    },
  }
})

const stubs = { BusinessLayout: { template: '<main><slot /></main>' } }

function seedRow(service = 'Erp', id = 'dl-1', status = 'pending') {
  state.items = [
    {
      service,
      deadLetter: {
        id,
        eventType: 'erp.OperationActualTimeLaborCost',
        consumerName: 'business-erp.operation-actual-time-labor-cost',
        failureCode: 'missing-work-center-cost-rate',
        status,
        deadLetteredAtUtc: '2026-09-20T02:00:00Z',
      },
    },
  ]
}

/** 抽屉走 reka 的真实弹层：要 flushPromises 而不是 nextTick，否则框还没开。 */
async function mountDrawerWithReason(permissionCodes: string[]) {
  seedRow()
  state.permissionCodes = permissionCodes
  state.selectedTarget = { service: 'Erp', deadLetterId: 'dl-1' }
  state.selectedDeadLetter = {
    id: 'dl-1',
    status: 'pending',
    eventType: 'erp.OperationActualTimeLaborCost',
  }
  const wrapper = await mountPage()

  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('详情'))
    ?.trigger('click')
  await flushPromises()
  const reason = document.querySelector('#dead-letter-ignore-reason') as HTMLTextAreaElement | null
  if (!reason) throw new Error('忽略原因输入框未渲染——抽屉没打开')
  reason.value = '上游已下线，不再重放'
  reason.dispatchEvent(new Event('input'))
  await flushPromises()
  return wrapper
}

/** 抽屉内容 teleport 到 body，wrapper 查不到，按文案在 document 里找。 */
function drawerIgnoreButton(_wrapper: unknown) {
  const button = [...document.querySelectorAll('button')].find((b) =>
    (b.textContent ?? '').includes('忽略'),
  )
  return button
    ? { attributes: (name: string) => button.getAttribute(name) ?? undefined }
    : undefined
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
    state.replayResults = reactive(new Map())
    state.nextReplayError = undefined
    state.nextBatchResults = []
    state.nextOutcome = { status: 'replayed', succeeded: true }
    state.filters = { service: 'all', eventType: '', status: 'all' }
    state.permissionCodes = ['business.dlq.read', 'business.dlq.manage']
    state.metrics = { pendingCount: 2, failedCount: 1, replayedCount: 7, ignoredCount: 4 }
    state.metricsError = undefined
    state.selectedRowKeys = []
    state.selectedDeadLetter = undefined
    state.selectedTarget = undefined
    toastSpy.success.mockClear()
    toastSpy.warning.mockClear()
    toastSpy.error.mockClear()
    toastSpy.info.mockClear()
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

    expect(toastSpy.success).not.toHaveBeenCalled()
    expect(toastSpy.warning).toHaveBeenCalledWith(expect.stringContaining('该服务无重放能力'))
    expect(wrapper.text()).toContain('该服务无重放能力')
  })

  it('只读角色：行内重放按钮不可点——按了必然 403', async () => {
    seedRow()
    state.permissionCodes = ['business.dlq.read']
    const wrapper = await mountPage()

    expect(wrapper.find('[aria-label^="重放死信"]').attributes('disabled')).toBeDefined()
  })

  it('只读角色：动作栏「重放选中」不可点', async () => {
    seedRow()
    state.selectedRowKeys = [deadLetterRowKey('Erp', 'dl-1')]
    state.permissionCodes = ['business.dlq.read']
    const wrapper = await mountPage()

    const bulk = wrapper.findAll('button').find((b) => b.text().includes('重放选中'))
    expect(bulk, '动作栏按钮应已渲染（选中了 1 行）').toBeTruthy()
    expect(bulk?.attributes('disabled')).toBeDefined()
  })

  it('带 manage 码：动作栏「重放选中」可点', async () => {
    seedRow()
    state.selectedRowKeys = [deadLetterRowKey('Erp', 'dl-1')]
    state.permissionCodes = ['business.dlq.read', 'business.dlq.manage']
    const wrapper = await mountPage()

    const bulk = wrapper.findAll('button').find((b) => b.text().includes('重放选中'))
    expect(bulk?.attributes('disabled')).toBeUndefined()
  })

  it('只读角色：抽屉内「忽略」不可点——即使已填写理由', async () => {
    const wrapper = await mountDrawerWithReason(['business.dlq.read'])

    const ignore = drawerIgnoreButton(wrapper)
    expect(ignore, '忽略按钮应已渲染').toBeTruthy()
    expect(ignore?.attributes('disabled')).toBeDefined()
  })

  it('带 manage 码：抽屉内「忽略」在填了理由后可点', async () => {
    const wrapper = await mountDrawerWithReason(['business.dlq.read', 'business.dlq.manage'])

    expect(drawerIgnoreButton(wrapper)?.attributes('disabled')).toBeUndefined()
  })

  it('持有 manage 码时重放按钮可点', async () => {
    seedRow()
    state.permissionCodes = ['business.dlq.read', 'business.dlq.manage']
    const wrapper = await mountPage()

    expect(wrapper.find('[aria-label^="重放死信"]').attributes('disabled')).toBeUndefined()
  })

  it('概览取数失败时明确降级，不画 0 冒充「正常且为零」', async () => {
    state.metricsError = new Error('boom')
    const text = (await mountPage()).text()

    expect(text).toContain('未能读取死信概览')
    expect(text).not.toContain('待处理2')
  })

  it('概览失败时点名服务清单也受影响，且下拉停用——不能被读成「只接入了一个来源」', async () => {
    state.metricsError = new Error('boom')
    const wrapper = await mountPage()
    const text = wrapper.text()

    // 降级条要点全受影响面：服务清单与概览同源，一起失效。
    expect(text).toContain('服务清单')
    expect(text).toContain('这不代表平台只接入了一个来源')
    // 只剩「全部服务」一项的可用下拉本身就是那个错误结论的来源，必须停用。
    // 只剩「全部服务」一项的**可用**下拉本身就是那个错误结论的来源，必须停用。
    const serviceTrigger = wrapper.findAll('button').find((b) => b.text() === '全部服务')
    expect(serviceTrigger, '服务下拉应已渲染').toBeTruthy()
    expect(serviceTrigger?.attributes('disabled')).toBeDefined()
  })

  it('四张概览卡各自取自己的字段，不是同一个数', async () => {
    const text = (await mountPage()).text().replace(/\s/g, '')

    expect(text).toContain('待处理2')
    expect(text).toContain('重放失败1')
    expect(text).toContain('已重放7')
    expect(text).toContain('已忽略4')
  })

  it('「未知」与「重放失败」在屏上是两回事：文案不同、也不是红色', async () => {
    seedRow()
    state.replayResults.set(deadLetterRowKey('Erp', 'dl-1'), {
      answered: false,
      statusAtAttempt: 'pending',
    })
    const wrapper = await mountPage()

    const badge = wrapper.find('[aria-label="状态：未收到答复，待核实"]')
    expect(badge.exists(), '行上应显示「未知」档').toBe(true)
    // 只看这一行：页面上的概览卡本身就叫「重放失败」，拿整页文本当 oracle 会误报。
    const row = wrapper.findAll('tr').find((tr) => tr.text().includes('Erp'))
    expect(row?.text()).not.toContain('重放失败')
    // 色调不是 danger：它不是失败，是不确定。
    expect(badge.classes().join(' ')).not.toContain('destructive')
  })

  it('T2 待处理 × 未答复：刷新后状态转成已重放，「未知」让位——不与「状态」列并存矛盾', async () => {
    // 网关返 502 而下游其实已重放：发起时是 pending，刷新后行状态已转成 replayed。
    seedRow('Erp', 'dl-1', 'replayed')
    state.replayResults.set(deadLetterRowKey('Erp', 'dl-1'), {
      answered: false,
      statusAtAttempt: 'pending',
    })
    const wrapper = await mountPage()
    // 只看这一行：概览卡也叫「已重放」，整页 toContain 恒真、没有鉴别力。
    const row = wrapper.findAll('tr').find((tr) => tr.text().includes('Erp'))

    expect(row?.find('[aria-label="状态：已重放"]').exists()).toBe(true)
    expect(row?.text()).not.toContain('待核实')
  })

  it('单条重放没收到答复：toast 说「未确认、先核实」，不说失败', async () => {
    seedRow()
    state.nextReplayError = new Error('Failed to fetch')
    const wrapper = await mountPage()

    await wrapper.find('[aria-label^="重放死信"]').trigger('click')
    await flushPromises()

    // 交给 toast 的最终串：断网映射非空，屏上是「重放未确认：网络异常…请刷新列表核实…」。
    // 兜底句（含「勿直接重试」）在这条分支上**不上屏**——单条路径按裁定不在本轮范围。
    const shown = toastSpy.error.mock.calls.at(-1)?.[0] as string
    expect(shown.startsWith('重放未确认：')).toBe(true)
    expect(shown).toContain('请刷新列表核实')
    expect(shown).not.toContain('重放失败')
    expect(wrapper.find('[aria-label="状态：未收到答复，待核实"]').exists()).toBe(true)
  })

  it('单条重放被网关明确拒绝（4xx）：说失败，行上不挂「未知」', async () => {
    seedRow()
    state.nextReplayError = Object.assign(new Error('HTTP 403'), { response: { status: 403 } })
    const wrapper = await mountPage()

    await wrapper.find('[aria-label^="重放死信"]').trigger('click')
    await flushPromises()

    const shown = toastSpy.error.mock.calls.at(-1)?.[0] as string
    expect(shown.startsWith('重放失败')).toBe(true)
    expect(wrapper.text()).not.toContain('待核实')
  })

  it('重放真的成功时才报成功', async () => {
    seedRow()
    state.nextOutcome = { status: 'replayed', succeeded: true }
    const wrapper = await mountPage()

    await wrapper.find('[aria-label^="重放死信"]').trigger('click')
    await flushPromises()

    expect(toastSpy.warning).not.toHaveBeenCalled()
    expect(toastSpy.success).toHaveBeenCalledWith(expect.stringContaining('已重放'))
  })

  it('T1 重放失败 × 未答复：刷新后仍是重放失败，「未知」继续显示——状态没有转移', async () => {
    seedRow('Erp', 'dl-1', 'failed')
    state.replayResults.set(deadLetterRowKey('Erp', 'dl-1'), {
      answered: false,
      statusAtAttempt: 'failed',
    })
    const wrapper = await mountPage()
    const row = wrapper.findAll('tr').find((tr) => tr.text().includes('Erp'))

    // 点击前就是「重放失败」：这不是服务端对这次尝试的答复，不能让位。
    expect(row?.find('[aria-label="状态：未收到答复，待核实"]').exists()).toBe(true)
  })

  it('T3 混合批次（成功 + 5xx + 429）：交给 toast 的串里三类计数都在', async () => {
    seedRow()
    state.selectedRowKeys.splice(0, state.selectedRowKeys.length, deadLetterRowKey('Erp', 'dl-1'))
    state.nextBatchResults = [
      { rowKey: 'Erp/a', kind: 'answered', outcome: { status: 'replayed', succeeded: true } },
      { rowKey: 'Mes/b', kind: 'unanswered', error: new Error('Failed to fetch') },
      { rowKey: 'Wms/c', kind: 'rejected', error: rateLimited() },
    ]
    const wrapper = await mountPage()

    await clickReplaySelected(wrapper)

    const shown = toastSpy.error.mock.calls.at(-1)?.[0] as string
    expect(shown).toContain('已答复 1 条')
    expect(shown).toContain('未确认 1 条')
    expect(shown).toContain('被拒绝 1 条')
  })

  it('T4/T6 仅被拒绝（429 无正文、映射为空）：串里有被拒绝计数、没有「未确认」', async () => {
    // T4（区分 C5：未确认段只在计数 > 0 时出现）与 T6（区分 C7：汇总串也作兜底句）
    // 的可达输入是同一个——网关限流不写正文，429 必然映射为空——故合为一条，不按格重复。
    seedRow()
    state.selectedRowKeys.splice(0, state.selectedRowKeys.length, deadLetterRowKey('Erp', 'dl-1'))
    state.nextBatchResults = [{ rowKey: 'Erp/dl-1', kind: 'rejected', error: rateLimited() }]
    const wrapper = await mountPage()

    await clickReplaySelected(wrapper)

    const shown = toastSpy.error.mock.calls.at(-1)?.[0] as string
    expect(shown).toContain('被拒绝 1 条')
    expect(shown).not.toContain('未确认')
  })

  it('T5 仅未答复：串里有未确认计数、没有「被拒绝」', async () => {
    seedRow()
    state.selectedRowKeys.splice(0, state.selectedRowKeys.length, deadLetterRowKey('Erp', 'dl-1'))
    state.nextBatchResults = [
      { rowKey: 'Erp/dl-1', kind: 'unanswered', error: new Error('Failed to fetch') },
    ]
    const wrapper = await mountPage()

    await clickReplaySelected(wrapper)

    const shown = toastSpy.error.mock.calls.at(-1)?.[0] as string
    expect(shown).toContain('未确认 1 条')
    expect(shown).not.toContain('被拒绝')
  })
})

/** 网关限流的 429：只有状态码、没有可解析正文——与 `BusinessGateway/Program.cs` 的限流一致。 */
function rateLimited() {
  return { response: { status: 429 } }
}

async function clickReplaySelected(wrapper: ReturnType<typeof mount>) {
  const bulk = wrapper.findAll('button').find((b) => b.text().includes('重放选中'))
  if (!bulk) throw new Error('动作栏「重放选中」未渲染——没有选中行')
  await bulk.trigger('click')
  await flushPromises()
}
