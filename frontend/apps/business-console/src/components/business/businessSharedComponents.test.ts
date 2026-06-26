import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import BusinessEmptyState from './BusinessEmptyState.vue'
import BusinessRowActions from './BusinessRowActions.vue'
import BusinessStatusBadge from './BusinessStatusBadge.vue'
import BusinessTablePagination from './BusinessTablePagination.vue'

vi.mock('@nerv-iip/ui', () => {
  const passthrough = (name: string) =>
    defineComponent({
      name,
      inheritAttrs: false,
      props: {
        class: {
          default: '',
          type: [String, Array, Object],
        },
      },
      setup(props, { attrs, slots }) {
        return () => h('div', { ...attrs, class: props.class }, slots.default?.())
      },
    })

  return {
    BadgePro: defineComponent({
      name: 'BadgePro',
      props: {
        variant: {
          default: 'neutral',
          type: String,
        },
      },
      setup(props, { attrs, slots }) {
        return () => h('span', { ...attrs, 'data-variant': props.variant }, slots.default?.())
      },
    }),
    ButtonPro: defineComponent({
      name: 'ButtonPro',
      props: {
        disabled: Boolean,
        type: String,
      },
      setup(props, { attrs, slots }) {
        return () =>
          h('button', { ...attrs, disabled: props.disabled, type: props.type }, slots.default?.())
      },
    }),
    // BusinessRowActions 已迁到 Pro 下拉，整模块 mock 按 Pro 导出名打桩。
    DropdownMenuPro: passthrough('DropdownMenuPro'),
    DropdownMenuProContent: passthrough('DropdownMenuProContent'),
    DropdownMenuProTrigger: passthrough('DropdownMenuProTrigger'),
    Pagination: defineComponent({
      name: 'Pagination',
      props: {
        page: Number,
      },
      emits: ['update:page'],
      setup(_props, { emit, slots }) {
        return () =>
          h('nav', { 'data-test': 'pagination', onClick: () => emit('update:page', 3) }, slots.default?.())
      },
    }),
    PaginationContent: passthrough('PaginationContent'),
    PaginationNext: passthrough('PaginationNext'),
    PaginationPrevious: passthrough('PaginationPrevious'),
    SelectPro: defineComponent({
      name: 'SelectPro',
      emits: ['update:modelValue'],
      setup(_props, { emit, slots }) {
        return () =>
          h('div', { 'data-test': 'page-size-select', onClick: () => emit('update:modelValue', '50') }, slots.default?.())
      },
    }),
    SelectProContent: passthrough('SelectProContent'),
    SelectProItem: defineComponent({
      name: 'SelectProItem',
      props: {
        value: String,
      },
      setup(props, { slots }) {
        return () => h('div', { 'data-value': props.value }, slots.default?.())
      },
    }),
    SelectProTrigger: passthrough('SelectProTrigger'),
    SelectProValue: passthrough('SelectProValue'),
  }
})

describe('business shared components', () => {
  it('resets to the first page when page size changes and clamps page display', async () => {
    const wrapper = mount(BusinessTablePagination, {
      props: {
        page: 9,
        pageSize: '20',
        totalItems: 45,
      },
    })

    expect(wrapper.text()).toContain('41-45 / 45 条')
    expect(wrapper.text()).toContain('3 / 3')

    await wrapper.get('[data-test="page-size-select"]').trigger('click')

    expect(wrapper.emitted('update:pageSize')).toEqual([['50']])
    expect(wrapper.emitted('update:page')).toEqual([[1]])
  })

  it('emits clamped page changes from pagination controls', async () => {
    const wrapper = mount(BusinessTablePagination, {
      props: {
        page: 1,
        pageSize: '10',
        totalItems: 12,
      },
    })

    await wrapper.get('[data-test="pagination"]').trigger('click')

    expect(wrapper.emitted('update:page')).toEqual([[2]])
  })

  it('renders business empty state copy with an optional action slot', () => {
    const wrapper = mount(BusinessEmptyState, {
      props: {
        action: '先确认生产计划并下达到车间。',
        description: '当前筛选条件下没有待派工工单。',
        title: '暂无待派工工单',
      },
      slots: {
        action: '<button type="button">查看生产计划</button>',
      },
    })

    expect(wrapper.text()).toContain('暂无待派工工单')
    expect(wrapper.text()).toContain('先确认生产计划并下达到车间。')
    expect(wrapper.get('button').text()).toBe('查看生产计划')
  })

  it('maps operational status values to shared badge variants and labels', () => {
    const ready = mount(BusinessStatusBadge, { props: { value: 'Ready' } })
    const running = mount(BusinessStatusBadge, { props: { value: 'InProgress' } })
    const warning = mount(BusinessStatusBadge, { props: { value: 'ConditionalRelease' } })
    const blocked = mount(BusinessStatusBadge, { props: { value: 'Unavailable' } })
    const unknown = mount(BusinessStatusBadge, { props: { value: 'NeedsReview' } })

    expect(ready.get('[data-variant="success"]').text()).toBe('可开工')
    expect(running.get('[data-variant="neutral"]').text()).toBe('执行中')
    expect(warning.get('[data-variant="warning"]').text()).toBe('条件放行')
    expect(blocked.get('[data-variant="danger"]').text()).toBe('不可用')
    expect(unknown.get('[data-variant="neutral"]').text()).toBe('NeedsReview')
  })

  it('keeps row actions accessible and supports disabled action menus', () => {
    const wrapper = mount(BusinessRowActions, {
      props: {
        disabled: true,
        label: '工单操作 WO-1001',
      },
      slots: {
        default: '<button type="button">打开详情</button>',
      },
    })

    const trigger = wrapper.get('button[aria-label="工单操作 WO-1001"]')
    expect(trigger.attributes('title')).toBe('工单操作 WO-1001')
    expect(trigger.attributes()).toHaveProperty('disabled')
    expect(wrapper.text()).toContain('打开详情')
  })
})
