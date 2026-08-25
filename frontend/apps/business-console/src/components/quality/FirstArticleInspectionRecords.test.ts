import { shallowRef } from 'vue'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FirstArticleInspectionRecords from './FirstArticleInspectionRecords.vue'

const state = vi.hoisted(() => ({
  filters: {
    organizationId: 'org-1',
    environmentId: 'env-1',
    skuCode: undefined as string | undefined,
    result: undefined as string | undefined,
    skip: 0,
    take: 20,
  },
}))

vi.mock('@/composables/useBusinessQuality', () => ({
  useQualityFirstArticleInspections: () => ({
    firstArticleRecords: shallowRef([
      {
        id: 'record-1',
        code: 'FA-REC-001',
        skuCode: 'SKU-FA-001',
        sourceDocumentId: 'MO-20260823-001',
        status: 'passed',
      },
    ]),
    firstArticleRecordsError: shallowRef(undefined),
    firstArticleRecordsPending: shallowRef(false),
    firstArticleRecordsTotal: shallowRef(1),
    recordFilters: state.filters,
    refreshFirstArticleRecords: vi.fn(),
  }),
}))
vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('20') }),
}))
vi.mock('@/utils/notify', () => ({ inlineErrorMessage: () => '' }))

const stubs = {
  NvButton: {
    props: ['disabled'],
    template: '<button type="button" :disabled="disabled"><slot /></button>',
  },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvEntityPicker: {
    props: ['modelValue', 'options'],
    emits: ['update:modelValue'],
    template:
      '<select data-testid="sku-filter" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value=""></option><option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option></select>',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select data-testid="result-filter" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="all">全部结果</option><option value="passed">合格</option><option value="rejected">不合格</option></select>',
  },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<span><slot /></span>' },
  NvSelectTrigger: { template: '<div><slot /></div>' },
  NvSelectValue: { template: '<span />' },
  NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
  NvDataTable: {
    props: ['rows'],
    template:
      '<div><div v-for="row in rows" :key="row.id"><slot name="cell-code" :row="row" /><slot name="cell-status" :row="row" /><slot name="cell-batchNo" :row="row" /></div></div>',
  },
}

describe('首件检验记录列表', () => {
  beforeEach(() => {
    state.filters.skuCode = undefined
    state.filters.result = undefined
  })

  it('按 SKU 与结果更新查询条件，并从记录号定位详情', async () => {
    const wrapper = mount(FirstArticleInspectionRecords, {
      props: {
        skuOptions: [{ value: 'SKU-FA-001', label: '精密泵体' }],
        skusPending: false,
      },
      global: { stubs },
    })

    await wrapper.get('[data-testid="sku-filter"]').setValue('SKU-FA-001')
    await wrapper.get('[data-testid="result-filter"]').setValue('passed')
    expect(state.filters.skuCode).toBe('SKU-FA-001')
    expect(state.filters.result).toBe('passed')

    const recordButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('FA-REC-001'))
    expect(recordButton).toBeDefined()
    await recordButton?.trigger('click')
    expect(wrapper.emitted('open-record')).toEqual([['record-1']])

    await wrapper.get('[data-testid="result-filter"]').setValue('rejected')
    expect(state.filters.result).toBe('rejected')
  })
})
