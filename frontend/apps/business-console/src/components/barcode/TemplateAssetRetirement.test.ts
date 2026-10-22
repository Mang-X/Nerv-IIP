import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import TemplateAssetRetirement from './TemplateAssetRetirement.vue'

const api = vi.hoisted(() => ({
  read: vi.fn(),
  retire: vi.fn(),
  allowed: true,
  error: vi.fn(),
  success: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleBarcodeTemplateAssetRetirement: api.read,
  retireBusinessConsoleBarcodeTemplateAsset: api.retire,
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { permissionCodes: api.allowed ? ['business.barcodes.template-assets.retire'] : [] },
  }),
}))
vi.mock('@/utils/notify', () => ({ notifyError: api.error, notifySuccess: api.success }))

const props = {
  organizationId: 'org-001',
  environmentId: 'env-dev',
  templateId: '01992309-3049-7000-8000-000000000001',
  fileId: 'label-carton-v3',
  templateName: '成品外箱标签',
  inactive: true,
}
const checksum = `sha256:${'a'.repeat(64)}`
const ready = {
  data: { success: true, data: { fileId: props.fileId, checksum, decisionId: null, status: null } },
}
const slot = { template: '<div><slot /></div>' }
function render(inactive = true) {
  return mount(TemplateAssetRetirement, {
    props: { ...props, inactive },
    global: {
      stubs: {
        NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        NvAlertDialogContent: slot,
        NvAlertDialogHeader: slot,
        NvAlertDialogTitle: slot,
        NvAlertDialogDescription: slot,
        NvAlertDialogFooter: slot,
      },
    },
  })
}
async function click(wrapper: ReturnType<typeof render>, text: string) {
  await wrapper
    .findAll('button')
    .find((button) => button.text() === text)!
    .trigger('click')
  await flushPromises()
}

// PublicContract / DomainInvariant: #3049 approved B acceptance and A generated contract.
describe('template asset retirement interaction', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.allowed = true
    api.read.mockResolvedValue(ready)
    api.retire.mockResolvedValue({ data: { success: true, data: { decisionId: 'decision-001' } } })
  })

  it('hides the action and makes no calls without the retirement permission', () => {
    api.allowed = false
    const wrapper = render()
    expect(wrapper.findAll('button')).toHaveLength(0)
    expect(api.read).not.toHaveBeenCalled()
    expect(api.retire).not.toHaveBeenCalled()
  })

  it('requires an inactive template and a reason before sending a retirement request', async () => {
    const active = render(false)
    await click(active, '资产退役')
    expect(active.text()).toContain('请先停用模板')
    expect(active.find('form').exists()).toBe(false)
    active.unmount()
    const wrapper = render()
    await click(wrapper, '资产退役')
    expect(wrapper.text()).not.toContain('请填写退役原因')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.text()).toContain('请填写退役原因')
    expect(api.retire).not.toHaveBeenCalled()
  })

  it.each([
    ['pending', '处理中'],
    ['quota-released', '配额已释放，文件仍保留'],
    ['execution-outcome-unknown', '执行结果待核实，已暂停'],
    ['replay-window-expired', '查询窗口已过期'],
  ])('renders %s from the read contract without a re-execution action', async (status, label) => {
    api.read.mockResolvedValue({
      data: {
        success: true,
        data: { fileId: props.fileId, checksum: null, decisionId: 'decision-001', status },
      },
    })
    const wrapper = render()
    await click(wrapper, '资产退役')
    expect(wrapper.get('[role="status"]').text()).toContain(label)
    expect(wrapper.find('form').exists()).toBe(false)
    await click(wrapper, '刷新状态')
    expect(api.read).toHaveBeenCalledTimes(2)
    expect(api.retire).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toMatch(/decision-001|sha256:|purge/)
  })

  it('suppresses duplicate in-flight submits and retains accepted until a real status is read', async () => {
    let finish!: (value: unknown) => void
    api.retire.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const wrapper = render()
    await click(wrapper, '资产退役')
    await wrapper.get('textarea').setValue('包装规格更新，停用旧版标签')
    await wrapper.get('form').trigger('submit')
    await wrapper.get('form').trigger('submit')
    expect(api.retire).toHaveBeenCalledTimes(1)
    expect(api.retire.mock.calls[0]![0].body).toMatchObject({
      organizationId: props.organizationId,
      environmentId: props.environmentId,
      templateId: props.templateId,
      fileId: props.fileId,
      checksum,
      reason: '包装规格更新，停用旧版标签',
    })
    finish({ data: { success: true, data: { decisionId: 'decision-001' } } })
    await flushPromises()
    expect(wrapper.get('[role="status"]').text()).toContain('已受理')
    expect(wrapper.find('form').exists()).toBe(false)
    await click(wrapper, '刷新状态')
    expect(wrapper.get('[role="status"]').text()).toContain('已受理')
    expect(api.retire).toHaveBeenCalledTimes(1)
  })

  it('reuses the frozen request after a lost response and never displays the raw error', async () => {
    api.retire.mockRejectedValueOnce(new Error('敏感原因 proof=secret key=internal-object'))
    const wrapper = render()
    await click(wrapper, '资产退役')
    await wrapper.get('textarea').setValue('包装规格更新')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(wrapper.get('textarea').attributes('disabled')).toBeDefined()
    await click(wrapper, '刷新状态')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(api.retire.mock.calls[1]![0].body).toEqual(api.retire.mock.calls[0]![0].body)
    expect(api.error.mock.calls.flat().join(' ')).not.toMatch(/secret|internal-object|敏感原因/)
    expect(wrapper.text()).not.toMatch(/secret|internal-object|敏感原因/)
  })

  it('does not enable submission when the initial read fails', async () => {
    api.read.mockRejectedValue(new Error('签名 secret'))
    const wrapper = render()
    await click(wrapper, '资产退役')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(api.error).toHaveBeenCalledWith('读取退役状态失败，请稍后刷新核实。')
    expect(api.retire).not.toHaveBeenCalled()
  })
})
