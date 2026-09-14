import { flushPromises, mount } from '@vue/test-utils'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, ref } from 'vue'

// jsdom 不实现 Element.prototype.scrollTo，NvPicker 打开时会调用它（见 EntryForm 用例同样注释）。
const originalScrollTo = Element.prototype.scrollTo
beforeAll(() => {
  Element.prototype.scrollTo = function scrollToStub() {}
})
afterAll(() => {
  Element.prototype.scrollTo = originalScrollTo
})

const push = vi.fn(async () => {})
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}))

const shiftOptions = ref([
  { value: 'EARLY', label: '早班' },
  { value: 'NIGHT', label: '夜班' },
])
const teamOptions = ref([{ value: 'TEAM-A', label: '甲班组' }])
const directoryError = ref<unknown>(null)
const directoryEnabled = ref(true)
const refreshDirectory = vi.fn(async () => [])

const canManage = ref(true)
const hasScope = ref(true)
const createHandover = vi.fn(async (_input: unknown) => ({ data: { accepted: true } }))
const uploadAttachment = vi.fn(async () => ({
  fileId: 'file-1',
  fileName: 'photo.jpg',
  contentType: 'image/jpeg',
  sizeBytes: 1024,
}))

vi.mock('@/composables/useBusinessShiftHandover', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>()
  return {
    ...actual,
    useShiftHandoverDirectory: () => ({
      enabled: directoryEnabled,
      shiftOptions: computed(() => shiftOptions.value),
      teamOptions: computed(() => teamOptions.value),
      pending: ref(false),
      error: directoryError,
      refresh: refreshDirectory,
    }),
    useShiftHandoverSubmission: () => ({
      canManage,
      hasScope,
      createPending: ref(false),
      uploadAttachment,
      createHandover,
    }),
  }
})

const { NvPicker } = await import('@nerv-iip/ui-mobile')
const HandoverPage = (await import('./handover.vue')).default
const ShiftHandoverEntryForm = (await import('./components/ShiftHandoverEntryForm.vue')).default
const ShiftHandoverPhotoCapture = (await import('./components/ShiftHandoverPhotoCapture.vue'))
  .default

/**
 * 页面用例默认把两个私有子组件 stub 掉：它们各自有专门的组件用例
 * （ShiftHandoverEntryForm.test.ts / ShiftHandoverPhotoCapture.test.ts），页面这一层只需要断言
 * 「哪一步该渲染哪个子组件、提交时带了什么载荷」。真实组合由本文件末尾那个不 stub 的集成用例承担。
 */
function mountPage() {
  return mount(HandoverPage, {
    global: { stubs: { ShiftHandoverEntryForm: true, ShiftHandoverPhotoCapture: true } },
  })
}

async function pickShiftAndTeam(wrapper: ReturnType<typeof mount>) {
  await wrapper.get('[data-testid="shift-cell"]').trigger('click')
  wrapper.findAllComponents(NvPicker)[0].vm.$emit('update:modelValue', 'EARLY')
  await nextTick()
  await wrapper.get('[data-testid="team-cell"]').trigger('click')
  wrapper.findAllComponents(NvPicker)[1].vm.$emit('update:modelValue', 'TEAM-A')
  await nextTick()
}

describe('PDA 交班录入页', () => {
  beforeEach(() => {
    push.mockClear()
    createHandover.mockClear()
    createHandover.mockResolvedValue({ data: { accepted: true } })
    uploadAttachment.mockClear()
    refreshDirectory.mockClear()
    canManage.value = true
    hasScope.value = true
    directoryEnabled.value = true
    directoryError.value = null
    shiftOptions.value = [
      { value: 'EARLY', label: '早班' },
      { value: 'NIGHT', label: '夜班' },
    ]
    teamOptions.value = [{ value: 'TEAM-A', label: '甲班组' }]
  })

  it('starts at step 1 and hides the detail editor until shift AND team are chosen', async () => {
    const wrapper = mountPage()
    expect(wrapper.text()).toContain('第 1/3 步')
    expect(wrapper.findComponent(ShiftHandoverEntryForm).exists()).toBe(false)
    expect(wrapper.findComponent(ShiftHandoverPhotoCapture).exists()).toBe(false)

    await wrapper.get('[data-testid="shift-cell"]').trigger('click')
    wrapper.findAllComponents(NvPicker)[0].vm.$emit('update:modelValue', 'EARLY')
    await nextTick()
    // 只选了班次还不够（域构造器 shiftId 和 teamId 都是必填）。
    expect(wrapper.findComponent(ShiftHandoverEntryForm).exists()).toBe(false)

    await wrapper.get('[data-testid="team-cell"]').trigger('click')
    wrapper.findAllComponents(NvPicker)[1].vm.$emit('update:modelValue', 'TEAM-A')
    await nextTick()
    expect(wrapper.findComponent(ShiftHandoverEntryForm).exists()).toBe(true)
    expect(wrapper.findComponent(ShiftHandoverPhotoCapture).exists()).toBe(true)
    expect(wrapper.text()).toContain('第 2/3 步')
  })

  it('names the missing permission instead of saying just 无权限', async () => {
    canManage.value = false
    const wrapper = mountPage()
    expect(wrapper.get('[data-testid="handover-blocker"]').text()).toContain(
      'business.mes.handovers.manage',
    )

    canManage.value = true
    directoryEnabled.value = false
    await nextTick()
    expect(wrapper.get('[data-testid="handover-blocker"]').text()).toContain(
      'business.masterdata.resources.read',
    )
  })

  it('submits an EMPTY handover — 空明细在写面是合法的', async () => {
    const wrapper = mountPage()
    await pickShiftAndTeam(wrapper)
    await wrapper.get('[data-testid="confirm-details"]').trigger('click')

    expect(wrapper.get('[data-testid="submit-step"]').text()).toContain('本次交班没有登记任何明细')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    expect(createHandover).toHaveBeenCalledTimes(1)
    const payload = createHandover.mock.calls[0][0] as Record<string, unknown>
    expect(payload).toMatchObject({
      shiftId: 'EARLY',
      teamId: 'TEAM-A',
      teamName: '甲班组',
      wipItems: [],
      unfinishedWorkOrders: [],
      openIssues: [],
      attachments: [],
    })
    expect(wrapper.text()).toContain('交班已提交')
  })

  it('never puts an outgoing user id into the request body (网关按 principal 注入)', async () => {
    const wrapper = mountPage()
    await pickShiftAndTeam(wrapper)
    await wrapper.get('[data-testid="confirm-details"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    const payload = createHandover.mock.calls[0][0] as Record<string, unknown>
    expect(Object.keys(payload)).not.toContain('outgoingUserId')
    expect(Object.keys(payload)).not.toContain('outgoingUserName')
    expect(Object.keys(payload)).not.toContain('organizationId')
  })

  it('reuses the SAME idempotency key across a retry and rotates it for the next handover', async () => {
    createHandover.mockRejectedValueOnce(Object.assign(new Error('boom'), { status: 503 }))
    const wrapper = mountPage()
    await pickShiftAndTeam(wrapper)
    await wrapper.get('[data-testid="confirm-details"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    // 503 = 结果未知；因为服务端按 idempotencyKey 去重，页面提供重试。
    await wrapper.get('[data-testid="retry-submit"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    const firstKey = (createHandover.mock.calls[0][0] as { idempotencyKey: string }).idempotencyKey
    const retryKey = (createHandover.mock.calls[1][0] as { idempotencyKey: string }).idempotencyKey
    expect(retryKey).toBe(firstKey)
    expect(firstKey).toBeTruthy()

    await wrapper.get('[data-testid="start-another"]').trigger('click')
    await pickShiftAndTeam(wrapper)
    await wrapper.get('[data-testid="confirm-details"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    const nextKey = (createHandover.mock.calls[2][0] as { idempotencyKey: string }).idempotencyKey
    expect(nextKey).not.toBe(firstKey)
  })

  // 这一格**不 stub**：它要证明页面与两个私有子组件的真实组合能把明细和照片带进请求体。
  it('carries the entered details and uploaded photo into the create payload', async () => {
    const wrapper = mount(HandoverPage)
    await pickShiftAndTeam(wrapper)

    await wrapper.get('[data-testid="wip-section"] input').setValue('WO-2026-0001')
    await wrapper.get('[data-testid="wip-quantity-cell"]').trigger('click')
    wrapper.findComponent({ name: 'NumberKeyboard' }).vm.$emit('update:modelValue', '7')
    await nextTick()
    await wrapper.get('[data-testid="add-wip"]').trigger('click')
    await nextTick()

    const input = wrapper.get('[data-testid="photo-input"]').element as HTMLInputElement
    Object.defineProperty(input, 'files', {
      configurable: true,
      value: [new File([new Uint8Array(3)], 'IMG.jpg', { type: 'image/jpeg' })],
    })
    await wrapper.get('[data-testid="photo-input"]').trigger('change')
    await flushPromises()

    await wrapper.get('[data-testid="confirm-details"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    const payload = createHandover.mock.calls[0][0] as Record<string, unknown>
    expect(payload.wipItems).toEqual([{ workOrderId: 'WO-2026-0001', quantity: 7 }])
    expect(payload.attachments).toEqual([
      { fileId: 'file-1', fileName: 'photo.jpg', contentType: 'image/jpeg', sizeBytes: 1024 },
    ])
  })

  it('routes an indeterminate failure to 核实 instead of a blind resubmit', async () => {
    createHandover.mockRejectedValueOnce(Object.assign(new Error('gateway down'), { status: 502 }))
    const wrapper = mountPage()
    await pickShiftAndTeam(wrapper)
    await wrapper.get('[data-testid="confirm-details"]').trigger('click')
    await wrapper.get('[data-testid="submit-handover"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('提交结果未知')
    await wrapper.get('[data-testid="verify-submit"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/mes/handovers')
  })

  it('shows the directory error with a retry instead of an empty shift picker', async () => {
    directoryError.value = new Error('目录服务异常')
    shiftOptions.value = []
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.find('[data-testid="handover-directory-error"]').exists()).toBe(true)
    await wrapper.get('[data-testid="handover-directory-error"] button').trigger('click')
    expect(refreshDirectory).toHaveBeenCalled()
  })

  it('explains an empty shift catalogue rather than leaving a dead picker', () => {
    shiftOptions.value = []
    const wrapper = mountPage()
    expect(wrapper.get('[data-testid="no-shift-options"]').text()).toContain('没有可选班次')
  })
})
