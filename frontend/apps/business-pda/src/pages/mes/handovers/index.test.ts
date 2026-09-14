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
const rows = ref([
  {
    handoverId: 'HO-1',
    shiftId: 'EARLY',
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
  {
    handoverId: 'HO-2',
    shiftId: 'NIGHT',
    teamId: 'TEAM-B',
    teamName: null,
    handoverStatus: 'Accepted',
    outgoingUserName: null,
    incomingUserName: '李四',
    acceptedAtUtc: '2026-09-14T01:00:00Z',
    wipItemCount: 0,
    unfinishedWorkOrderCount: 0,
    openIssueDetailCount: 0,
  },
])
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
  })

  it('renders each handover with Chinese status and never leaks the raw status code', () => {
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    expect(text).toContain('甲班组')
    expect(text).toContain('待接班')
    expect(text).toContain('已接班')
    expect(text).not.toContain('Open')
    expect(text).not.toContain('Accepted')
  })

  it('distinguishes 待接班 from 已接班但解析不出接班人姓名', () => {
    const wrapper = mount(HandoversPage)
    const text = wrapper.get('[data-testid="handover-rows"]').text()
    expect(text).toContain('交班 张三 · 接班 待接班')
    // HO-2 已接班、交班人解析不出 → 「未记录」，不是「待接班」，也不回显用户 id。
    expect(text).toContain('交班 未记录 · 接班 李四')
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
        shiftId: 'EARLY',
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
