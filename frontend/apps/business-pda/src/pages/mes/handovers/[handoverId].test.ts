import { NvMobileDialog } from '@nerv-iip/ui-mobile'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

const push = vi.fn(async () => {})
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ params: { handoverId: 'HO-1' } }),
}))

const canRead = ref(true)
const canManage = ref(true)
const hasScope = ref(true)
const detail = ref<Record<string, unknown> | undefined>({
  handoverId: 'HO-1',
  shiftId: 'EARLY',
  teamId: 'TEAM-A',
  teamName: '甲班组',
  handoverStatus: 'Open',
  createdAtUtc: '2026-09-14T13:53:47.000Z',
  outgoingUserId: 'user-zhang',
  outgoingUserName: '张三',
  incomingUserId: null,
  incomingUserName: null,
  acceptedAtUtc: null,
  wipItems: [{ workOrderId: 'WO-1', operationTaskId: null, quantity: 5 }],
  unfinishedWorkOrders: [
    { workOrderId: 'WO-2', plannedQuantity: 10, completedQuantity: 4, workOrderStatus: 'Released' },
  ],
  openIssues: [
    {
      category: 'Equipment',
      severity: 'High',
      description: '2 号机导轨异响',
      referenceId: 'DT-9',
    },
  ],
  attachments: [
    { fileId: 'file-1', fileName: 'photo.jpg', contentType: 'image/jpeg', sizeBytes: 2048 },
  ],
})
const detailError = ref<unknown>(null)
const hasFailedResponse = ref(false)
const refresh = vi.fn(async () => {})
const acceptHandover = vi.fn(async () => ({ data: { accepted: true } }))
const openAttachment = vi.fn(async () => {})
const viewerError = ref('')

vi.mock('@/composables/useBusinessShiftHandover', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    useMesShiftHandoverDetail: () => ({
      enabled: computed(() => canRead.value),
      canRead,
      canManage,
      hasScope,
      detail: computed(() => detail.value),
      wipItems: computed(() => (detail.value?.wipItems as unknown[]) ?? []),
      unfinishedWorkOrders: computed(() => (detail.value?.unfinishedWorkOrders as unknown[]) ?? []),
      openIssues: computed(() => (detail.value?.openIssues as unknown[]) ?? []),
      attachments: computed(() => (detail.value?.attachments as unknown[]) ?? []),
      pending: ref(false),
      error: detailError,
      hasFailedResponse,
      acceptPending: ref(false),
      refresh,
      acceptHandover,
    }),
    useShiftHandoverDirectoryLabels: () => ({
      directoryEnabled: ref(true),
      resolveShiftLabel: (value?: string | null) =>
        ({ EARLY: '早班' })[(value ?? '').trim()] ?? (value?.trim() || '未排班'),
      resolveTeamLabel: (value?: string | null) =>
        ({ 'TEAM-A': '甲班组' })[(value ?? '').trim()] ?? (value?.trim() || '未指派班组'),
    }),
    useShiftHandoverAttachmentViewer: () => ({
      openingFileId: ref(''),
      error: viewerError,
      openAttachment,
    }),
  }
})

const DetailPage = (await import('./[handoverId].vue')).default

/** 确认框的确认键按下即无条件关框；用例走的就是那条路径。 */
async function confirmAccept(wrapper: ReturnType<typeof mount>) {
  await wrapper.get('[data-testid="open-accept-confirm"]').trigger('click')
  wrapper.findComponent(NvMobileDialog).vm.$emit('confirm')
  await flushPromises()
}

const OPEN_DETAIL = { ...detail.value }

describe('PDA 接班确认页', () => {
  beforeEach(() => {
    push.mockClear()
    refresh.mockClear()
    acceptHandover.mockClear()
    acceptHandover.mockResolvedValue({ data: { accepted: true } })
    openAttachment.mockClear()
    canRead.value = true
    canManage.value = true
    hasScope.value = true
    detailError.value = null
    hasFailedResponse.value = false
    viewerError.value = ''
    detail.value = { ...OPEN_DETAIL }
  })

  it('renders the three detail sections and the attachment list in Chinese', () => {
    const wrapper = mount(DetailPage)
    expect(wrapper.get('[data-testid="detail-wip"]').text()).toContain('按工单登记')
    expect(wrapper.get('[data-testid="detail-unfinished"]').text()).toContain('已下达')
    expect(wrapper.get('[data-testid="detail-unfinished"]').text()).not.toContain('Released')
    const issues = wrapper.get('[data-testid="detail-issues"]').text()
    expect(issues).toContain('设备')
    expect(issues).toContain('高')
    expect(issues).not.toContain('Equipment')
    expect(wrapper.get('[data-testid="detail-attachments"]').text()).toContain('photo.jpg')
    expect(wrapper.get('[data-testid="detail-attachments"]').text()).toContain('2.0 KB')
  })

  it('shows the shift display name instead of the raw master-data code', () => {
    const wrapper = mount(DetailPage)
    expect(wrapper.get('[data-testid="handover-summary"]').text()).toContain('早班')
    expect(wrapper.get('[data-testid="handover-summary"]').text()).not.toContain('EARLY')
  })

  it('headlines the handover number and prints the timestamps', () => {
    const wrapper = mount(DetailPage)
    const summary = wrapper.get('[data-testid="handover-summary"]').text()
    expect(summary).toContain('HO-1')
    expect(summary).toContain('交班时间 2026/9/14 21:53')
    // 还没接班 → 不印一个空的「接班时间」行。
    expect(summary).not.toContain('接班时间')
  })

  it('never writes 接班 未记录 when the incoming id IS on record (A 类缺陷的详情侧防线)', () => {
    detail.value = {
      ...OPEN_DETAIL,
      handoverStatus: 'Accepted',
      acceptedAtUtc: '2026-09-14T14:20:00.000Z',
      outgoingUserId: 'user-admin',
      outgoingUserName: null,
      incomingUserId: 'user-admin',
      incomingUserName: null,
    }
    const wrapper = mount(DetailPage)
    const summary = wrapper.get('[data-testid="handover-summary"]').text()
    expect(summary).toContain('已接班')
    expect(summary).toContain('交班 姓名未知 · 接班 姓名未知')
    expect(summary).not.toContain('未记录')
    expect(summary).not.toContain('user-admin')
    expect(summary).toContain('接班时间 2026/9/14 22:20')
  })

  it('says 交班时点没有登记 only when the detail really loaded', async () => {
    detail.value = {
      ...OPEN_DETAIL,
      wipItems: [],
      unfinishedWorkOrders: [],
      openIssues: [],
      attachments: [],
    }
    const loaded = mount(DetailPage)
    expect(loaded.get('[data-testid="detail-wip"]').text()).toContain('交班时点没有登记')

    // 取数失败时三段必须闭嘴：说「没登记」等于把「没取到」谎报成「没有」。
    detail.value = undefined
    detailError.value = new Error('详情服务异常')
    hasFailedResponse.value = true
    const failed = mount(DetailPage)
    await flushPromises()
    expect(failed.find('[data-testid="handover-detail-error"]').exists()).toBe(true)
    expect(failed.get('[data-testid="detail-wip"]').text()).not.toContain('交班时点没有登记')
  })

  it('accepts the handover and shows the success result', async () => {
    const wrapper = mount(DetailPage)
    await confirmAccept(wrapper)

    expect(acceptHandover).toHaveBeenCalledTimes(1)
    // 请求体为空：接班人由网关按认证 principal 注入（#3328）。
    expect(acceptHandover.mock.calls[0]).toHaveLength(0)
    expect(wrapper.text()).toContain('接班已确认')
  })

  it('blocks accepting an already-accepted handover and says so', async () => {
    detail.value = {
      ...OPEN_DETAIL,
      handoverStatus: 'Accepted',
      incomingUserName: '李四',
      acceptedAtUtc: '2026-09-14T01:00:00Z',
    }
    const wrapper = mount(DetailPage)

    expect(wrapper.get('[data-testid="accept-blocker"]').text()).toContain('已被接班')
    // trigger() 对 disabled 元素直接跳过，所以这里不靠点击证明守卫——直接查 disabled 属性
    // 再证明即使绕过 UI 调到确认回调也不会发请求。
    expect(wrapper.get('[data-testid="open-accept-confirm"]').attributes('disabled')).toBeDefined()
    wrapper.findComponent(NvMobileDialog).vm.$emit('confirm')
    await flushPromises()
    expect(acceptHandover).not.toHaveBeenCalled()
  })

  it('blocks a reader without handovers.manage and names the permission', () => {
    canManage.value = false
    const wrapper = mount(DetailPage)
    expect(wrapper.get('[data-testid="accept-blocker"]').text()).toContain(
      'business.mes.handovers.manage',
    )
  })

  it('keeps the failure reason on the PAGE (弹框按确认即关，原因写框里走不到)', async () => {
    acceptHandover.mockRejectedValueOnce(
      Object.assign(new Error('rejected'), { status: 409, message: '状态已变化' }),
    )
    const wrapper = mount(DetailPage)
    await confirmAccept(wrapper)

    expect(wrapper.text()).toContain('接班失败')
    await wrapper.get('[data-testid="retry-accept"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="open-accept-confirm"]').exists()).toBe(true)
  })

  it('offers a refresh-to-verify path when the outcome is indeterminate', async () => {
    acceptHandover.mockRejectedValueOnce(Object.assign(new Error('boom'), { status: 502 }))
    const wrapper = mount(DetailPage)
    await confirmAccept(wrapper)

    expect(wrapper.text()).toContain('提交结果未知')
    await wrapper.get('[data-testid="verify-accept"]').trigger('click')
    expect(refresh).toHaveBeenCalled()
  })

  it('opens an attachment through the read-face viewer and surfaces its error', async () => {
    const wrapper = mount(DetailPage)
    await wrapper.get('[data-testid="open-attachment-0"]').trigger('click')
    expect(openAttachment).toHaveBeenCalledWith(
      expect.objectContaining({ fileId: 'file-1', fileName: 'photo.jpg' }),
    )

    viewerError.value = '照片打开失败，请重试。'
    await flushPromises()
    expect(wrapper.get('[data-testid="detail-attachment-error"]').text()).toContain('照片打开失败')
  })

  it('names the missing read permission when the account cannot read handovers', () => {
    canRead.value = false
    const wrapper = mount(DetailPage)
    expect(wrapper.get('[data-testid="detail-blocker"]').text()).toContain(
      'business.mes.handovers.read',
    )
  })
})
