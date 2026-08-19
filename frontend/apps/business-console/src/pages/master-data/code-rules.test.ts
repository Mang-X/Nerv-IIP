import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import CodeRulesPage from './code-rules.vue'

const stub = vi.hoisted(() => ({
  createRuleVersion: vi.fn().mockResolvedValue({}),
  notifyFailure: vi.fn(),
  notifySuccess: vi.fn(),
}))

const rules = [
  {
    ruleKey: 'sku',
    displayName: 'SKU 编码规则',
    appliesTo: '物料',
    scope: 'organization' as const,
    version: 3,
    isActive: true,
    segments: [
      {
        type: 'sequence' as const,
        width: 4,
        start: 1,
        padChar: '0',
        reset: 'none' as const,
        required: true,
      },
    ],
  },
]

vi.mock('@/composables/useCodeRules', () => ({
  useCodeRules: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    rules: computed(() => rules),
    rulesError: shallowRef(undefined),
    rulesPending: shallowRef(false),
    refresh: vi.fn(),
    fetchRuleDetail: vi.fn(),
    previewCode: vi.fn(),
    createRuleVersion: stub.createRuleVersion,
    createPending: shallowRef(false),
  }),
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: vi.fn(() => ''),
  notifyOperationFailure: stub.notifyFailure,
  notifySuccess: stub.notifySuccess,
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  CarriedContextSummary: { template: '<div><slot /></div>' },
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvDataTable: {
    props: ['rows'],
    template:
      '<div><div v-for="row in rows" :key="row.ruleKey"><slot name="cell-actions" :row="row" /></div></div>',
  },
  NvStatusBadge: { template: '<span />' },
  DialogRoot: { props: ['open'], template: '<section v-if="open"><slot /></section>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDatePicker: { template: '<input type="date" />' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<div><slot /></div>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<span><slot /></span>' },
  NvCheckbox: { template: '<input type="checkbox" />' },
  Spinner: { template: '<span />' },
}

let wrapper: ReturnType<typeof mount> | null = null

function button(label: string) {
  return wrapper!.findAll('button').find((candidate) => candidate.text().trim() === label)
}

async function openVersionForm() {
  wrapper = mount(CodeRulesPage, { global: { stubs }, attachTo: document.body })
  await button('新建版本')!.trigger('click')
  await flushPromises()
  await wrapper.get('#cr-by').setValue('张工')
  return wrapper
}

async function submitVersionForm() {
  ;(button('发布版本')!.element as HTMLButtonElement).click()
  await flushPromises()
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

beforeEach(() => {
  stub.createRuleVersion.mockReset().mockResolvedValue({})
  stub.notifyFailure.mockClear()
  stub.notifySuccess.mockClear()
})

describe('编码规则版本的变更原因', () => {
  it('提交后内联拒绝空值与 Unicode 空白，且不发请求', async () => {
    await openVersionForm()

    const reasonInput = wrapper!.get('#cr-reason')
    const submitButton = button('发布版本')!
    expect(wrapper!.get('form').attributes('novalidate')).toBeDefined()
    expect(submitButton.attributes('type')).toBe('submit')
    expect((submitButton.element as HTMLButtonElement).form).toBe(wrapper!.get('form').element)
    expect(reasonInput.attributes('required')).toBeDefined()
    expect(reasonInput.attributes('maxlength')).toBe('500')
    expect(reasonInput.attributes('aria-describedby')).toBe('cr-reason-help cr-reason-count')
    expect(wrapper!.get('#cr-reason-count').attributes('aria-live')).toBe('polite')
    expect(wrapper!.text()).toContain('0 / 500')
    await submitVersionForm()
    expect(wrapper!.text()).toContain('请输入变更原因。')
    expect(reasonInput.attributes('aria-invalid')).toBe('true')
    expect(reasonInput.attributes('aria-describedby')).toBe('cr-reason-error cr-reason-count')
    expect(stub.createRuleVersion).not.toHaveBeenCalled()

    await wrapper!.get('#cr-reason').setValue('\u3000\t\n')
    await submitVersionForm()
    expect(wrapper!.text()).toContain('变更原因不能只包含空白字符。')
    expect(stub.createRuleVersion).not.toHaveBeenCalled()
  })

  it('允许 500 字边界，并对 501 字异常输入失败关闭', async () => {
    await openVersionForm()

    await wrapper!.get('#cr-reason').setValue('甲'.repeat(501))
    await submitVersionForm()
    expect(wrapper!.text()).toContain('501 / 500')
    expect(wrapper!.text()).toContain('变更原因不能超过 500 个字符。')
    expect(stub.createRuleVersion).not.toHaveBeenCalled()

    await wrapper!.get('#cr-reason').setValue('甲'.repeat(500))
    await submitVersionForm()
    expect(stub.createRuleVersion).toHaveBeenCalledTimes(1)
    expect(stub.createRuleVersion.mock.calls[0]?.[1].changeReason).toBe('甲'.repeat(500))
  })

  it('提交 trim 后的原因，成功后清理表单状态', async () => {
    await openVersionForm()
    await wrapper!.get('#cr-reason').setValue('  调整物料标签编号规则  ')

    await submitVersionForm()

    expect(stub.createRuleVersion).toHaveBeenCalledWith(
      'sku',
      expect.objectContaining({ changeReason: '调整物料标签编号规则' }),
    )
    expect(stub.notifySuccess).toHaveBeenCalledTimes(1)
    expect(wrapper!.find('#cr-reason').exists()).toBe(false)

    await button('新建版本')!.trigger('click')
    await flushPromises()
    expect(wrapper!.get<HTMLInputElement>('#cr-reason').element.value).toBe('')
    expect(wrapper!.text()).toContain('0 / 500')
  })

  it('服务失败时保留原因与弹窗，成功重试后再清理', async () => {
    stub.createRuleVersion.mockRejectedValueOnce(new Error('服务不可用'))
    await openVersionForm()
    await wrapper!.get('#cr-reason').setValue('  现场标签格式调整  ')

    await submitVersionForm()

    expect(stub.notifyFailure).toHaveBeenCalledTimes(1)
    expect(wrapper!.get<HTMLInputElement>('#cr-reason').element.value).toBe('  现场标签格式调整  ')

    await submitVersionForm()
    expect(stub.createRuleVersion).toHaveBeenCalledTimes(2)
    expect(wrapper!.find('#cr-reason').exists()).toBe(false)
  })

  it('取消不发请求，重新打开时不残留原因', async () => {
    await openVersionForm()
    await wrapper!.get('#cr-reason').setValue('临时调整')

    await button('取消')!.trigger('click')
    await flushPromises()
    expect(stub.createRuleVersion).not.toHaveBeenCalled()
    expect(wrapper!.find('#cr-reason').exists()).toBe(false)

    await button('新建版本')!.trigger('click')
    await flushPromises()
    expect(wrapper!.get<HTMLInputElement>('#cr-reason').element.value).toBe('')
  })
})
