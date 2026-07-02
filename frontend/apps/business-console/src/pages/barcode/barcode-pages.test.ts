import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import RulesPage from './rules.vue'
import TemplatesPage from './templates.vue'

const barcode = vi.hoisted(() => ({
  saveRule: vi.fn(),
  saveTemplate: vi.fn(),
  route: { query: {} as Record<string, unknown> },
  ruleFilters: undefined as undefined | { keyword?: string, skip: number, take: number },
  templateFilters: undefined as undefined | { skip: number, take: number },
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('vue-router', () => ({
  useRoute: () => barcode.route,
}))

vi.mock('@/composables/useBusinessBarcode', () => ({
  useBarcodeRules: () => {
    const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100, keyword: '', status: undefined })
    barcode.ruleFilters = filters
    return {
      filters,
      rules: computed(() => [
        {
          barcodeRuleId: 'rule-1',
          ruleCode: 'GS1-CASE',
          barcodeType: 'gs1-128',
          prefix: '0691234',
          length: 18,
          checksumRule: 'gs1-mod10',
          gs1CompanyPrefixLength: 7,
          allowedSourceDocumentTypes: ['inventory.receipt', 'production.report'],
          status: 'active',
        },
      ]),
      rulesError: shallowRef(undefined),
      rulesPending: shallowRef(false),
      rulesTotal: computed(() => 1),
      refreshRules: vi.fn(),
      saveRule: barcode.saveRule,
      saveRulePending: shallowRef(false),
      saveRuleError: shallowRef(undefined),
    }
  },
  useBarcodeTemplates: () => {
    const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100, status: undefined })
    barcode.templateFilters = filters
    return {
      filters,
      templates: computed(() => [
        {
          templateId: 'tpl-1',
          templateCode: 'SKU_BOX',
          templateName: '外箱标签',
          templateFileId: 'file-label-box',
          variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
          status: 'active',
        },
      ]),
      templatesError: shallowRef(undefined),
      templatesPending: shallowRef(false),
      templatesTotal: computed(() => 1),
      refreshTemplates: vi.fn(),
      saveTemplate: barcode.saveTemplate,
      saveTemplatePending: shallowRef(false),
      saveTemplateError: shallowRef(undefined),
    }
  },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  DialogPro: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
const selectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectProValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function setInput(wrapper: ReturnType<typeof mount>, selector: string, value: string) {
  return wrapper.find(selector).setValue(value)
}

describe('barcode pages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    barcode.route.query = {}
    barcode.ruleFilters = undefined
    barcode.templateFilters = undefined
    barcode.saveRule.mockResolvedValue(undefined)
    barcode.saveTemplate.mockResolvedValue(undefined)
  })

  it('renders rule maintenance with source usage, explicit SKU gap, and route-seeded keyword', async () => {
    barcode.route.query = { ruleCode: 'GS1-CASE' }
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    expect(wrapper.text()).toContain('条码规则')
    expect(wrapper.text()).toContain('GS1-CASE')
    expect(wrapper.text()).toContain('GS1 公司前缀 7 位')
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('生产报工')
    expect(wrapper.text()).toContain('按默认条码规则反查待 SKU facade 支持')
    expect(barcode.ruleFilters?.keyword).toBe('GS1-CASE')
    expect(barcode.ruleFilters?.take).toBe(10)
  })

  it('blocks GS1 rule submission without company prefix length', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建规则'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-rule-code', 'GS1-PALLET')
    await setInput(wrapper, '#barcode-rule-prefix', '0691234')
    await setInput(wrapper, '#barcode-rule-length', '18')
    await setInput(wrapper, '#barcode-rule-checksum', 'gs1-mod10')
    await wrapper.find('select[aria-label="条码类型"]').setValue('gs1-128')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('GS1 规则必须填写公司前缀长度。')
  })

  it('submits a valid GS1 rule with source document usage', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建规则'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-rule-code', 'GS1-PALLET')
    await setInput(wrapper, '#barcode-rule-prefix', '0691234')
    await setInput(wrapper, '#barcode-rule-length', '18')
    await setInput(wrapper, '#barcode-rule-checksum', 'gs1-mod10')
    await setInput(wrapper, '#barcode-rule-gs1-prefix', '7')
    await wrapper.find('select[aria-label="条码类型"]').setValue('gs1-128')
    await wrapper.find('input[aria-label="适用场景：收货入库"]').setValue(true)
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(expect.objectContaining({
      ruleCode: 'GS1-PALLET',
      barcodeType: 'gs1-128',
      gs1CompanyPrefixLength: 7,
      allowedSourceDocumentTypes: ['inventory.receipt'],
      status: 'active',
    }))
  })

  it('prefills an existing barcode rule for update', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('编辑'))!.trigger('click')
    await flushPromises()

    expect((wrapper.find('#barcode-rule-code').element as HTMLInputElement).value).toBe('GS1-CASE')
    expect(wrapper.find('#barcode-rule-code').attributes('readonly')).toBeDefined()
    expect((wrapper.find('#barcode-rule-prefix').element as HTMLInputElement).value).toBe('0691234')

    await setInput(wrapper, '#barcode-rule-prefix', '0699999')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(expect.objectContaining({
      ruleCode: 'GS1-CASE',
      prefix: '0699999',
      gs1CompanyPrefixLength: 7,
    }))
  })

  it('renders and saves a label template with field schema text', async () => {
    const wrapper = mount(TemplatesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.text()).toContain('标签模板')
    expect(wrapper.text()).toContain('SKU_BOX')
    expect(wrapper.text()).toContain('skuCode')
    expect(wrapper.text()).toContain('适用对象')

    await wrapper.findAll('button').find((b) => b.text().includes('新建模板'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await setInput(wrapper, '#barcode-template-file', 'file-pallet')
    await setInput(wrapper, '#barcode-template-schema', '{"fields":["sscc","lotNo"]}')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(expect.objectContaining({
      templateCode: 'PALLET_LABEL',
      templateName: '托盘标签',
      templateFileId: 'file-pallet',
      variableSchemaJson: '{"fields":["sscc","lotNo"]}',
      status: 'active',
    }))
  })

  it('prefills an existing label template for update', async () => {
    const wrapper = mount(TemplatesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('编辑'))!.trigger('click')
    await flushPromises()

    expect((wrapper.find('#barcode-template-code').element as HTMLInputElement).value).toBe('SKU_BOX')
    expect(wrapper.find('#barcode-template-code').attributes('readonly')).toBeDefined()
    expect((wrapper.find('#barcode-template-file').element as HTMLInputElement).value).toBe('file-label-box')

    await setInput(wrapper, '#barcode-template-name', '外箱标签 V2')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(expect.objectContaining({
      templateCode: 'SKU_BOX',
      templateName: '外箱标签 V2',
      templateFileId: 'file-label-box',
    }))
  })
})
