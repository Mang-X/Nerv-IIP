import {
  barcode,
  layoutStub,
  dialogStubs,
  selectStubs,
  setInput,
  resetFixture,
} from './barcode-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import RulesPage from './rules.vue'

describe('barcode pages', () => {
  beforeEach(resetFixture)

  it('renders rule maintenance with source usage and route-seeded keyword', async () => {
    barcode.route.query = { ruleCode: 'GS1-CASE' }
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('条码规则')
    expect(wrapper.text()).toContain('GS1-CASE')
    expect(wrapper.text()).toContain('GS1 公司前缀 7 位')
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('生产报工')
    expect(barcode.ruleFilters?.keyword).toBe('GS1-CASE')
    expect(barcode.ruleFilters?.take).toBe(10)
  })

  it('blocks GS1 rule submission without company prefix length', async () => {
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建规则'))!
      .trigger('click')
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
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建规则'))!
      .trigger('click')
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

    expect(barcode.saveRule).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleCode: 'GS1-PALLET',
        barcodeType: 'gs1-128',
        gs1CompanyPrefixLength: 7,
        allowedSourceDocumentTypes: ['inventory.receipt'],
        status: 'active',
      }),
    )
  })

  it('prefills an existing barcode rule for update', async () => {
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('编辑'))!
      .trigger('click')
    await flushPromises()

    // 规则编码由所选行带出，只读展示（不再是 readonly 输入框）。
    const carried = wrapper.find('[data-slot="carried-context"]')
    expect(carried.exists()).toBe(true)
    expect(carried.text()).toContain('GS1-CASE')
    expect(wrapper.find('#barcode-rule-code').exists()).toBe(false)
    expect((wrapper.find('#barcode-rule-prefix').element as HTMLInputElement).value).toBe('0691234')

    await setInput(wrapper, '#barcode-rule-prefix', '0699999')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleCode: 'GS1-CASE',
        prefix: '0699999',
        gs1CompanyPrefixLength: 7,
      }),
    )
  })
})
