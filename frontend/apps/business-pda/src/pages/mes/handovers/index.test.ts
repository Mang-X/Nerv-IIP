import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const push = vi.fn(async () => {})
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const filters = reactive<{ status: string | undefined }>({ status: 'open' })
const canRead = ref(true)
const canManage = ref(true)
const hasScope = ref(true)
const rows = ref<Array<Record<string, unknown>>>([
  {
    handoverId: 'SHO-20260914-000001',
    shiftId: 'DAY',
    teamId: 'TEAM-A',
    teamName: '甲班组',
    handoverStatus: 'Open',
    createdAtUtc: '2026-09-14T13:53:47.000Z',
    acceptedAtUtc: null,
    outgoingUserId: 'user-zhang',
    outgoingUserName: '张三',
    incomingUserId: null,
    incomingUserName: null,
    wipItemCount: 2,
    unfinishedWorkOrderCount: 1,
    openIssueDetailCount: 3,
  },
  {
    handoverId: 'SHO-20260914-000002',
    shiftId: 'NIGHT',
    teamId: 'TEAM-B',
    teamName: null,
    handoverStatus: 'Accepted',
    createdAtUtc: '2026-09-14T14:10:00.000Z',
    acceptedAtUtc: '2026-09-14T14:20:00.000Z',
    outgoingUserId: null,
    outgoingUserName: null,
    incomingUserId: 'user-li',
    incomingUserName: '李四',
    wipItemCount: 0,
    unfinishedWorkOrderCount: 0,
    openIssueDetailCount: 0,
  },
])

/** 同班组同班次、明细计数相同的两条「已接班」——B 类缺陷的真实数据形状。 */
const TWIN_ACCEPTED_ROWS = [
  {
    handoverId: 'SHO-20260914-000010',
    shiftId: 'DAY',
    teamId: 'TEAM-A',
    teamName: '甲班组',
    handoverStatus: 'Accepted',
    createdAtUtc: '2026-09-14T13:00:00.000Z',
    acceptedAtUtc: '2026-09-14T13:53:00.000Z',
    outgoingUserId: 'user-admin',
    outgoingUserName: null,
    incomingUserId: 'user-admin',
    incomingUserName: null,
    wipItemCount: 1,
    unfinishedWorkOrderCount: 1,
    openIssueDetailCount: 1,
  },
  {
    handoverId: 'SHO-20260914-000011',
    shiftId: 'DAY',
    teamId: 'TEAM-A',
    teamName: '甲班组',
    handoverStatus: 'Accepted',
    createdAtUtc: '2026-09-14T14:00:00.000Z',
    acceptedAtUtc: '2026-09-14T14:41:00.000Z',
    outgoingUserId: 'user-admin',
    outgoingUserName: null,
    incomingUserId: 'user-admin',
    incomingUserName: null,
    wipItemCount: 1,
    unfinishedWorkOrderCount: 1,
    openIssueDetailCount: 1,
  },
]

const DEFAULT_ROWS = [...rows.value]

const pending = ref(false)
const error = ref<unknown>(null)
const hasSuccessfulResponse = ref(true)
const hasFailedResponse = ref(false)
const refresh = vi.fn(async () => {})

vi.mock('@/composables/useBusinessShiftHandover', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    useMesShiftHandovers: () => ({
      filters,
      enabled: canRead,
      canRead,
      canManage,
      hasScope,
      handovers: computed(() => rows.value),
      total: computed(() => rows.value.length),
      pending,
      error,
      lastUpdatedAt: ref('2026-09-14T02:03:04.000Z'),
      hasSuccessfulResponse,
      hasFailedResponse,
      refresh,
    }),
    useShiftHandoverDirectoryLabels: () => ({
      directoryEnabled: ref(true),
      resolveShiftLabel: (value?: string | null) =>
        ({ DAY: '早班', NIGHT: '晚班' })[(value ?? '').trim()] ?? (value?.trim() || '未排班'),
      resolveTeamLabel: (value?: string | null) =>
        ({ 'TEAM-A': '甲班组' })[(value ?? '').trim()] ?? (value?.trim() || '未指派班组'),
    }),
  }
})

const HandoversPage = (await import('./index.vue')).default

describe('PDA 接班列表页', () => {
  beforeEach(() => {
    push.mockClear()
    refresh.mockClear()
    filters.status = 'open'
    canRead.value = true
    canManage.value = true
    hasScope.value = true
    pending.value = false
    error.value = null
    hasSuccessfulResponse.value = true
    hasFailedResponse.value = false
    rows.value = [...DEFAULT_ROWS]
  })

  it('renders each handover with Chinese status and never leaks the raw status code', () => {
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    expect(text).toContain('甲班组')
    // 标题是交接单号：同班组一天交多次班时，它是这批数据里唯一天然互异的业务字段。
    expect(text).toContain('SHO-20260914-000001')
    expect(text).toContain('待接班')
    expect(text).toContain('已接班')
    expect(text).not.toContain('Open')
    expect(text).not.toContain('Accepted')
  })

  it('distinguishes the three identity states instead of collapsing them into 未记录', () => {
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    // 待接班：incoming id 确实为空 → 「待接班」，不是「未记录」。
    expect(text).toContain('交班 张三 · 接班 待接班')
    // 已接班：交班人 id 为空 → 「未记录」；接班人有名字 → 显示名字。
    expect(text).toContain('交班 未记录 · 接班 李四')
  })

  it('never writes 接班 未记录 on a row whose incoming id IS on record', () => {
    // A 类缺陷的列表侧防线：一张标着「已接班」的单子同屏写「接班未记录」是与数据相反的读数。
    rows.value = [...TWIN_ACCEPTED_ROWS]
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    expect(text).toContain('已接班')
    expect(text).toContain('交班 姓名未知 · 接班 姓名未知')
    expect(text).not.toContain('接班 未记录')
    expect(text).not.toContain('user-admin')
  })

  it('keeps two same-team/same-shift accepted rows distinguishable on screen', () => {
    // B：同班组、同班次、明细计数全同的两条已接班记录，点进去之前必须能分辨。
    rows.value = [...TWIN_ACCEPTED_ROWS]
    const wrapper = mount(HandoversPage)
    const texts = wrapper.findAll('[data-testid="handover-rows"] [data-row]').map((r) => r.text())
    expect(texts).toHaveLength(2)
    expect(texts[0]).not.toBe(texts[1])
    // 单号与状态时点两者都要真的出现在屏上，而不是靠不可见属性「理论上不同」。
    expect(texts[0]).toContain('SHO-20260914-000010')
    expect(texts[1]).toContain('SHO-20260914-000011')
    expect(texts[0]).toContain('09/14 21:53')
    expect(texts[1]).toContain('09/14 22:41')
  })

  it('pairs the timestamp with the status: 待接班看交班时间、已接班看接班时间', () => {
    const wrapper = mount(HandoversPage)
    const texts = wrapper.findAll('[data-testid="handover-rows"] [data-row]').map((r) => r.text())
    expect(texts[0]).toContain('09/14 21:53') // createdAtUtc，此单还没被接
    expect(texts[1]).toContain('09/14 22:20') // acceptedAtUtc，不是 createdAtUtc 的 22:10
    expect(texts[1]).not.toContain('09/14 22:10')
  })

  it('shows the shift/team directory display names, not the raw master-data codes', () => {
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    expect(text).toContain('早班')
    expect(text).not.toContain('DAY')
    // 第二行的 teamName 是 null，班组名回落到目录解析；目录里没有 TEAM-B 就原样回显业务码。
    expect(text).toContain('TEAM-B')
  })

  it('shows the per-handover detail counts the operator needs before tapping in', () => {
    const wrapper = mount(HandoversPage)
    expect(wrapper.get('[data-testid="handover-rows"]').text()).toContain(
      '在制 2 · 未完工单 1 · 遗留 3',
    )
  })

  it('navigates to the detail route with the handover id percent-encoded', async () => {
    rows.value = [{ ...rows.value[0], handoverId: 'HO/1 A' }]
    const wrapper = mount(HandoversPage)
    await wrapper.get('[data-testid="handover-rows"] [data-row]').trigger('click')
    expect(push).toHaveBeenCalledWith('/mes/handovers/HO%2F1%20A')
    rows.value = [{ ...rows.value[0], handoverId: 'HO-1' }]
  })

  it('switches the server-side status filter when the tab changes', async () => {
    const wrapper = mount(HandoversPage)
    const tabs = wrapper.findAllComponents({ name: 'MobileTabs' })
    tabs[0].vm.$emit('update:modelValue', 'accepted')
    await flushPromises()
    expect(filters.status).toBe('accepted')
  })

  it('names the missing read permission instead of showing an empty list', () => {
    canRead.value = false
    rows.value = []
    const wrapper = mount(HandoversPage)
    expect(wrapper.get('[data-testid="handovers-blocker"]').text()).toContain(
      'business.mes.handovers.read',
    )
    rows.value = [
      {
        handoverId: 'HO-1',
        shiftId: 'DAY',
        teamId: 'TEAM-A',
        teamName: '甲班组',
        handoverStatus: 'Open',
        outgoingUserName: '张三',
        incomingUserName: null,
        acceptedAtUtc: null,
        wipItemCount: 2,
        unfinishedWorkOrderCount: 1,
        openIssueDetailCount: 3,
      },
    ]
  })

  it('shows the retryable error — NOT the empty state — when the list query failed', async () => {
    rows.value = []
    error.value = new Error('交接单服务异常')
    hasSuccessfulResponse.value = false
    const wrapper = mount(HandoversPage)
    await flushPromises()

    expect(wrapper.find('[data-testid="handovers-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="handovers-empty"]').exists()).toBe(false)

    await wrapper.get('[data-testid="handovers-error"] button').trigger('click')
    expect(refresh).toHaveBeenCalled()
  })

  it('shows the empty state only for a successful empty response', () => {
    rows.value = []
    error.value = null
    hasSuccessfulResponse.value = true
    const wrapper = mount(HandoversPage)
    expect(wrapper.find('[data-testid="handovers-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="handovers-error"]').exists()).toBe(false)
  })

  it('hides the 去交班 shortcut from readers without handovers.manage', async () => {
    const wrapper = mount(HandoversPage)
    expect(wrapper.find('[data-testid="go-handover-entry"]').exists()).toBe(true)

    canManage.value = false
    await flushPromises()
    expect(wrapper.find('[data-testid="go-handover-entry"]').exists()).toBe(false)
  })
})
