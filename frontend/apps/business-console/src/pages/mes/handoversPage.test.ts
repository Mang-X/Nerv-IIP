import type { Ref } from 'vue'
import { computed, reactive, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import HandoversPage from './handovers.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const TECHNICAL_USER_PATTERN = /user-emp-/i

const state = vi.hoisted(() => ({
  catalogResolved: true,
  principal: {
    permissionCodes: ['business.mes.handovers.manage'] as string[],
  },
  filters: {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    status: undefined as string | undefined,
    keyword: '',
    skip: 0,
    take: 20,
  },
  resources: {
    shift: [{ code: 'EARLY', displayName: '早班', active: true }],
    team: [{ code: 'TEAM-A', displayName: '总装早班一组', active: true }],
  },
  row: {
    handoverId: 'handover-001',
    shiftId: 'EARLY',
    teamId: 'TEAM-A' as string | undefined,
    teamName: '总装早班一组' as string | undefined,
    handoverStatus: 'open',
    openIssueCount: 1,
    createdAtUtc: '2026-08-01T08:00:00Z',
    acceptedAtUtc: undefined as string | undefined,
    // 网关注入的交班人身份：id 是工程标识符、只有 displayName 允许上屏。
    outgoingUserId: 'user-emp-1042',
    outgoingUserName: '李海生' as string | undefined,
    incomingUserId: undefined as string | undefined,
    incomingUserName: undefined as string | undefined,
    wipItemCount: 3,
    unfinishedWorkOrderCount: 2,
    openIssueDetailCount: 1,
  },
  detail: {
    handoverId: 'handover-001',
    shiftId: 'EARLY',
    teamId: 'TEAM-A',
    teamName: '总装早班一组',
    handoverStatus: 'open',
    openIssueCount: 1,
    createdAtUtc: '2026-08-01T08:00:00Z',
    acceptedAtUtc: null,
    outgoingUserId: 'user-emp-1042',
    outgoingUserName: '李海生',
    incomingUserId: null,
    incomingUserName: null,
    wipItems: [
      { workOrderId: 'WO-2026-0731', operationTaskId: 'OT-2026-0731-30', quantity: 24 },
      { workOrderId: 'WO-2026-0733', operationTaskId: null, quantity: 6 },
    ],
    unfinishedWorkOrders: [
      {
        workOrderId: 'WO-2026-0728',
        plannedQuantity: 200,
        completedQuantity: 148,
        workOrderStatus: 'started',
      },
    ],
    openIssues: [
      {
        category: 'Equipment',
        severity: 'High',
        description: '总装线 3 号拧紧枪扭矩漂移，已换备枪顶班',
        referenceId: 'DT-2026-0801-02',
      },
      {
        category: 'Quality',
        severity: 'Medium',
        description: '前桥总成异响复检未闭环',
        referenceId: null,
      },
    ],
  },
}))

const detailFace = vi.hoisted(() => ({ handoverId: undefined as unknown as Ref<string> }))

const mutations = vi.hoisted(() => ({
  createShiftHandover: vi.fn(),
  acceptShiftHandover: vi.fn(),
  refreshHandovers: vi.fn(),
  makeIdempotencyKey: vi.fn((prefix: string) => `${prefix}-stable`),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

vi.mock('@/composables/useBusinessMes', () => {
  // 详情读面按 detailHandoverId 取数：写空即停取（与 useMesShiftHandovers 的 enabled 同口径）。
  // 用例要断言页面确实把选中的交接单 id 写进去了，所以这个 ref 由测试持有。
  detailFace.handoverId = ref('')
  return {
    useMesShiftHandovers: () => ({
      filters: reactive(state.filters),
      handovers: computed(() => [state.row]),
      handoversError: ref(),
      handoversPending: ref(false),
      handoversTotal: ref(1),
      detailHandoverId: detailFace.handoverId,
      handoverDetail: computed(() => (detailFace.handoverId.value ? state.detail : undefined)),
      handoverDetailError: ref(),
      handoverDetailPending: ref(false),
      createShiftHandover: mutations.createShiftHandover,
      acceptShiftHandover: mutations.acceptShiftHandover,
      refreshHandovers: mutations.refreshHandovers,
    }),
    makeIdempotencyKey: mutations.makeIdempotencyKey,
  }
})

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: string) => ({
    filters: reactive({ take: 100 }),
    resources: computed(() =>
      state.catalogResolved
        ? (state.resources[resourceType as keyof typeof state.resources] ?? [])
        : [],
    ),
    resourcesPending: ref(false),
    resourcesError: ref(),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: state.principal }),
}))

vi.mock('@/utils/notify', async () => {
  const actual = await vi.importActual<typeof import('@/utils/notify')>('@/utils/notify')
  return {
    ...actual,
    inlineErrorMessage: () => '',
    notifyError: mutations.notifyError,
    notifyOperationFailure: mutations.notifyOperationFailure,
    notifySuccess: mutations.notifySuccess,
  }
})

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref(20) }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
  // 与 NvDataTable 同口径：单元格优先用 `cell-<key>` 插槽，没有插槽才回落到
  // accessor / row[key]（真组件 valueOf 的行为）。行点击也照真组件挂在整行上——
  // 操作列的 stopPropagation 只有这样才被真正检验。
  NvDataTable: {
    props: ['columns', 'rows', 'emptyMessage'],
    emits: ['row-click'],
    template: `
      <section>
        <p v-if="!rows || rows.length === 0">{{ emptyMessage }}</p>
        <div v-for="(row, index) in rows" :key="index" @click="$emit('row-click', row)">
          <span v-for="column in columns" :key="column.key">
            <slot :name="'cell-' + column.key" :row="row">{{
              column.accessor ? column.accessor(row) : (row[column.key] ?? '')
            }}</slot>
          </span>
        </div>
      </section>
    `,
  },
  // NvDialog 的 barrel 别名带 name（frontend/packages/ui/src/components/pc/dialog/index.ts），
  // stub 按 Nv 名匹配即可命中。此 stub 必须用 v-if 承载开合状态，否则真实 DialogRoot
  // 无条件渲染 slot，dialog 的开合状态在测试里恒为"已渲染"。
  NvDialog: {
    props: ['open'],
    emits: ['update:open'],
    template: '<div v-if="open"><slot /></div>',
  },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvMetricCard: { props: ['label', 'value'], template: '<div>{{ label }} {{ value }}</div>' },
  NvPageHeader: {
    props: ['title'],
    template: '<header>{{ title }}<slot name="actions" /></header>',
  },
  NvRowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  NvDropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
  DropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  // 与 NvDialog 同理：stub 必须用 v-if 承载开合，否则抽屉在测试里恒为「已渲染」。
  NvSheet: {
    props: ['open'],
    emits: ['update:open'],
    template:
      '<div v-if="open"><button data-testid="close-detail" @click="$emit(\'update:open\', false)" /><slot /></div>',
  },
  NvSheetContent: { template: '<section><slot /></section>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  Spinner: { template: '<span />' },
}

function acceptedResponse() {
  return {
    data: {
      accepted: true,
    },
  }
}

function mountPage() {
  return mount(HandoversPage, { global: { stubs } })
}

describe('MES handovers read-face guard', () => {
  beforeEach(() => {
    state.catalogResolved = true
    state.principal.permissionCodes = ['business.mes.handovers.manage']
    state.filters.organizationId = 'org-001'
    state.filters.environmentId = 'env-dev'
    state.row.handoverId = 'handover-001'
    state.row.shiftId = 'EARLY'
    state.row.teamId = 'TEAM-A'
    state.row.teamName = '总装早班一组'
    state.row.handoverStatus = 'open'
    state.row.acceptedAtUtc = undefined
    state.row.outgoingUserName = '李海生'
    state.row.incomingUserName = undefined
    state.row.wipItemCount = 3
    state.row.unfinishedWorkOrderCount = 2
    state.row.openIssueDetailCount = 1
    detailFace.handoverId.value = ''
    mutations.createShiftHandover.mockReset()
    mutations.acceptShiftHandover.mockReset()
    mutations.refreshHandovers.mockReset().mockResolvedValue(undefined)
    mutations.makeIdempotencyKey.mockClear()
    mutations.notifySuccess.mockReset()
    mutations.notifyError.mockReset()
    mutations.notifyOperationFailure.mockReset()
  })

  it('shows the DTO team name and never exposes technical identifiers', () => {
    const wrapper = mountPage()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('总装早班一组')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('shows neutral placeholders instead of raw identifiers when the directory cannot resolve them', () => {
    state.catalogResolved = false
    state.row.teamName = undefined
    // teamId 留着业务码（如 'TEAM-A'）时，resolveTeamLabel 在目录未解出时会兜底显示码本身
    // （人可读，非原始标识符，属预期行为，见 handovers.vue 的 resolveTeamLabel）；只有真的没有
    // teamId 时才会落到 '未指派'，这里显式清空 teamId 来触发这条兜底分支。
    state.row.teamId = undefined
    const wrapper = mountPage()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('—')
    expect(visibleText).toContain('未指派')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('disables the create button and explains why when the user lacks manage permission', () => {
    state.principal.permissionCodes = []
    const wrapper = mountPage()

    const button = wrapper.get('[aria-label="新建班次交接"]')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('title')).toBe('没有交接单管理权限')
  })

  it('does not open the create dialog when the entry guard trips between render and click (isolated stale-DOM interleave)', async () => {
    // 以确定性交错模拟 stale-DOM 时序：按钮渲染时守卫未拦截（未禁用），业务上下文在同一 tick 内
    // 失效——Vue 的 DOM patch 要到下一个 microtask 才落地，而 vue-test-utils 的 trigger() 派发前
    // 会同步读取当前 DOM 的 disabled 属性；不等 nextTick 就直接改状态再点击，能在 DOM 还没来得及
    // 重渲染成禁用之前触发点击，从而真正跑进 openCreateDialog 内部的
    // `if (createEntryBlocker.value) return`。不能证明项：本用例只证明「blocker 为真时该行会
    // 早返回」，不证明真实浏览器点击与 Vue microtask flush 之间确有这个时序窗口——那属于
    // ProviderBehavior，本 lane（jsdom + vue-test-utils）证不到。
    // `reactive(state.filters)` 拿到的是 Vue 按目标对象缓存的同一个响应式代理（组件内部
    // useMesShiftHandovers() 拿到的也是它），必须经这个代理写，直接改 state.filters 的裸对象
    // 不会触发依赖更新。这是本票要补的鉴别力：删掉那一行，此用例必须变红。
    const wrapper = mountPage()
    const button = wrapper.get('[aria-label="新建班次交接"]')
    expect(button.attributes('disabled')).toBeUndefined()

    reactive(state.filters).environmentId = ''
    await button.trigger('click')

    expect(mutations.makeIdempotencyKey).not.toHaveBeenCalled()
    expect(mutations.createShiftHandover).not.toHaveBeenCalled()
  })

  it('validates the create form before issuing a mutation', async () => {
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')

    expect(mutations.createShiftHandover).not.toHaveBeenCalled()
    expect(wrapper.get('[role="alert"]').text()).toContain('请选择班次和班组')
    expect(wrapper.get('[data-testid="handover-create-shift"]').attributes('data-invalid')).toBe('')
    expect(wrapper.get('[data-testid="handover-create-team"]').attributes('data-invalid')).toBe('')
  })

  it('creates from visible directory values, shows the real receipt outcome, and refreshes', async () => {
    mutations.createShiftHandover.mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="handover-create-shift"]').setValue('EARLY')
    await wrapper.get('[data-testid="handover-create-team"]').setValue('TEAM-A')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.createShiftHandover).toHaveBeenCalledWith({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      shiftId: 'EARLY',
      teamId: 'TEAM-A',
      teamName: '总装早班一组',
      idempotencyKey: 'mes-handover-create-stable',
    })
    expect(mutations.createShiftHandover.mock.calls[0]?.[0]).not.toHaveProperty('openIssueIds')
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifySuccess).toHaveBeenCalledWith('班次交接创建成功，服务端已受理。')
  })

  it('keeps the create idempotency key for a retry after an operation failure', async () => {
    mutations.createShiftHandover
      .mockRejectedValueOnce(new Error('network unavailable'))
      .mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="handover-create-shift"]').setValue('EARLY')
    await wrapper.get('[data-testid="handover-create-team"]').setValue('TEAM-A')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.notifyOperationFailure).toHaveBeenCalledWith(
      '创建班次交接失败',
      expect.any(Error),
      '创建班次交接失败，请稍后重试。',
    )
    expect(wrapper.find('[data-testid="create-handover-form"]').exists()).toBe(true)

    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.createShiftHandover).toHaveBeenCalledTimes(2)
    expect(mutations.createShiftHandover.mock.calls[0]?.[0].idempotencyKey).toBe(
      'mes-handover-create-stable',
    )
    expect(mutations.createShiftHandover.mock.calls[1]?.[0].idempotencyKey).toBe(
      'mes-handover-create-stable',
    )
  })

  it('accepts an open handover with current context and refreshes after the receipt', async () => {
    mutations.acceptShiftHandover.mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[data-testid="accept-handover"]').trigger('click')
    await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.acceptShiftHandover).toHaveBeenCalledWith('handover-001', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      idempotencyKey: 'mes-handover-accept-stable',
    })
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifySuccess).toHaveBeenCalledWith('接班已受理，服务端已受理。')
  })

  it('closes the accept dialog and disables the row entry once the refreshed list confirms an uncertain accept', async () => {
    mutations.acceptShiftHandover.mockRejectedValueOnce(new Error('request timed out'))
    mutations.refreshHandovers.mockImplementationOnce(async () => {
      state.row.handoverStatus = 'accepted'
    })
    const wrapper = mountPage()

    await wrapper.get('[data-testid="accept-handover"]').trigger('click')
    await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.acceptShiftHandover).toHaveBeenCalledTimes(1)
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifySuccess).toHaveBeenCalledWith('接班已受理，列表已确认。')
    // 刷新确认已接班后，submitAccept 会主动关闭弹层（handovers.vue:361）；行内接班按钮也随之
    // 因 canAcceptRow() 不再满足 isOpenHandover() 而禁用——不留重复提交的入口，不是"表单还开着
    // 但静默吞掉第二次提交"。
    expect(wrapper.find('[data-testid="accept-handover-form"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="accept-handover"]').attributes('disabled')).toBeDefined()
    expect(mutations.acceptShiftHandover).toHaveBeenCalledTimes(1)
  })

  it('blocks a second accept when the refreshed list still cannot confirm the outcome', async () => {
    mutations.acceptShiftHandover.mockRejectedValueOnce(new Error('request timed out'))
    const wrapper = mountPage()

    await wrapper.get('[data-testid="accept-handover"]').trigger('click')
    await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.acceptShiftHandover).toHaveBeenCalledTimes(1)
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifyOperationFailure).toHaveBeenCalledWith(
      '接班结果待确认',
      expect.any(Error),
      '接班结果尚未确认，请刷新页面核实；本页已阻止重复提交。',
    )
    expect(wrapper.text()).toContain('接班结果尚未确认，请刷新列表核实；本页已阻止重复提交。')
    expect(
      wrapper
        .get('[data-testid="accept-handover-form"]')
        .get('button[type="submit"]')
        .attributes('disabled'),
    ).toBe('')

    await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
    await flushPromises()
    expect(mutations.acceptShiftHandover).toHaveBeenCalledTimes(1)
  })

  it.each([403, 404, 409, 422])(
    'keeps the stable accept key retryable after deterministic HTTP %s rejection',
    async (status) => {
      const error = { response: { status } }
      mutations.acceptShiftHandover
        .mockRejectedValueOnce(error)
        .mockResolvedValueOnce(acceptedResponse())
      const wrapper = mountPage()

      await wrapper.get('[data-testid="accept-handover"]').trigger('click')
      await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
      await flushPromises()

      expect(mutations.notifyOperationFailure).toHaveBeenCalledWith(
        '接班失败',
        error,
        '接班失败，请检查权限或交接单状态后重试。',
      )
      expect(mutations.refreshHandovers).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="accept-handover-form"]').exists()).toBe(true)

      await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
      await flushPromises()

      expect(mutations.acceptShiftHandover).toHaveBeenCalledTimes(2)
      expect(mutations.acceptShiftHandover.mock.calls[0]?.[1].idempotencyKey).toBe(
        'mes-handover-accept-stable',
      )
      expect(mutations.acceptShiftHandover.mock.calls[1]?.[1].idempotencyKey).toBe(
        'mes-handover-accept-stable',
      )
    },
  )

  it('在列表行给出交班人、接班人与三类明细计数', () => {
    const wrapper = mountPage()
    const visibleText = wrapper.text().replace(/\s+/g, ' ')

    expect(visibleText).toContain('李海生')
    expect(visibleText).toContain('在制 3')
    expect(visibleText).toContain('未完工单 2')
    expect(visibleText).toContain('遗留 1')
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it.each([
    ['已接班且目录解出显示名', '周敏', '周敏'],
    ['已接班但目录解不出显示名', undefined, '未记录'],
  ])('接班人列在%s时给出对应说法', (_label, incomingUserName, expected) => {
    state.row.handoverStatus = 'accepted'
    state.row.acceptedAtUtc = '2026-08-01T16:05:00Z'
    state.row.incomingUserName = incomingUserName
    const wrapper = mountPage()

    expect(wrapper.text()).toContain(expected)
    expect(wrapper.text()).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('点开交接单按 id 取详情，并把三类明细全量摆出来', async () => {
    const wrapper = mountPage()
    expect(wrapper.find('[data-testid="handover-detail"]').exists()).toBe(false)

    await wrapper.get('[data-testid="handovers-table"] > div').trigger('click')
    await flushPromises()

    expect(detailFace.handoverId.value).toBe('handover-001')
    const detailText = wrapper.get('[data-testid="handover-detail"]').text().replace(/\s+/g, ' ')

    // 在制清点：工序任务缺省时说清是按工单登记，不留空格子。
    expect(detailText).toContain('WO-2026-0731')
    expect(detailText).toContain('OT-2026-0731-30')
    expect(detailText).toContain('24')
    expect(detailText).toContain('按工单登记')
    // 未完工单：计划/完成进度与工单状态中文化。
    expect(detailText).toContain('WO-2026-0728')
    expect(detailText).toContain('148')
    expect(detailText).toContain('已开工')
    // 设备与质量遗留问题：类别、严重度与关联单据都译成业务说法。
    expect(detailText).toContain('设备')
    expect(detailText).toContain('高')
    expect(detailText).toContain('总装线 3 号拧紧枪扭矩漂移，已换备枪顶班')
    expect(detailText).toContain('DT-2026-0801-02')
    expect(detailText).toContain('质量')
    expect(detailText).toContain('中')
    expect(detailText).toContain('前桥总成异响复检未闭环')
    expect(detailText).toContain('无')

    expect(detailText).not.toMatch(UUID_PATTERN)
    expect(detailText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('关掉详情抽屉后停止持有详情请求', async () => {
    const wrapper = mountPage()
    await wrapper.get('[data-testid="handovers-table"] > div').trigger('click')
    await flushPromises()
    expect(detailFace.handoverId.value).toBe('handover-001')

    await wrapper.get('[data-testid="close-detail"]').trigger('click')
    await flushPromises()

    expect(detailFace.handoverId.value).toBe('')
    expect(wrapper.find('[data-testid="handover-detail"]').exists()).toBe(false)
  })

  it('点操作列不顺带打开详情抽屉', async () => {
    const wrapper = mountPage()

    await wrapper.get('[data-testid="accept-handover"]').trigger('click')
    await flushPromises()

    expect(detailFace.handoverId.value).toBe('')
    expect(wrapper.find('[data-testid="handover-detail"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="accept-handover-form"]').exists()).toBe(true)
  })

  it.each([
    ['已接班状态', () => (state.row.handoverStatus = 'accepted')],
    ['缺少业务上下文', () => (state.filters.environmentId = '')],
    ['没有接班权限', () => (state.principal.permissionCodes = [])],
  ])('在%s时不发起接班请求', async (_label, arrange) => {
    arrange()
    const wrapper = mountPage()
    const action = wrapper.find('[data-testid="accept-handover"]')

    if (action.exists()) await action.trigger('click')
    await flushPromises()

    expect(mutations.acceptShiftHandover).not.toHaveBeenCalled()
  })
})
