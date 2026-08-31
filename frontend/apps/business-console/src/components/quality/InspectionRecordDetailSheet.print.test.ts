import { shallowRef } from 'vue'
import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import InspectionRecordDetailSheet from './InspectionRecordDetailSheet.vue'

vi.mock('@/composables/useBusinessQuality', () => ({
  useQualityInspectionRecordDetail: () => ({
    record: shallowRef({
      inspectionRecordId: 'FA-REC-001',
      result: 'passed',
      skuCode: 'SKU-FA-001',
      sourceDocumentId: 'MO-20260823-001',
      inspectedQuantity: 1,
      resultLines: [],
    }),
    recordPending: shallowRef(false),
    recordError: shallowRef(undefined),
    refreshRecord: vi.fn(),
  }),
}))
vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: () => '精密泵体' }),
}))
vi.mock('@/utils/notify', () => ({ notifyError: vi.fn() }))

const stubs = {
  NvSheet: { template: '<section><slot /></section>' },
  NvSheetContent: { template: '<article><slot /></article>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvButton: { template: '<button type="button"><slot /></button>' },
  NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
  Spinner: { template: '<span />' },
}

describe('检验记录详情打印', () => {
  afterEach(() => vi.restoreAllMocks())

  it('用户点击打印后调用浏览器打印，并保留当前详情内容', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {
      expect(document.body.classList.contains('printing-inspection-record')).toBe(true)
    })
    const wrapper = mount(InspectionRecordDetailSheet, {
      props: {
        open: true,
        recordId: 'FA-REC-001',
        organizationId: 'org-1',
        environmentId: 'env-1',
      },
      global: { stubs },
    })

    const printButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('打印检验记录'))
    expect(printButton).toBeDefined()
    await printButton?.trigger('click')

    expect(print).toHaveBeenCalledOnce()
    expect(document.body.classList.contains('printing-inspection-record')).toBe(false)
    expect(wrapper.find('[data-printable-inspection-record]').text()).toContain('FA-REC-001')
    expect(wrapper.text()).toContain('精密泵体')
  })
})
