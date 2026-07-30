import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import FulfillmentTimelineSheet from './FulfillmentTimelineSheet.vue'
import { useAuthStore } from '@/stores/auth'

// 时间线本体有自己的用例；这里只验销售订单详情上的「对该单排产」入口（MAN-694 / #1262）。
vi.mock('./FulfillmentTimelineBody.vue', () => ({
  default: defineComponent({ template: '<div data-testid="timeline-body" />' }),
}))
vi.mock('@/components/scheduling/SingleOrderSchedulingDialog.vue', () => ({
  default: defineComponent({
    props: { open: Boolean, contextLabel: String, initialKeyword: String, readOnly: Boolean },
    template:
      '<div data-testid="schedule-dialog" :data-context="contextLabel" :data-keyword="initialKeyword" />',
  }),
}))
vi.mock('@nerv-iip/ui', async () => {
  const { defineComponent: define } = await vi.importActual<typeof import('vue')>('vue')
  const Shell = define({ template: '<div><slot /></div>' })
  return {
    NvButton: define({
      props: { disabled: Boolean },
      emits: ['click'],
      template:
        '<button type="button" :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
    }),
    NvSheet: Shell,
    NvSheetContent: Shell,
    NvSheetDescription: Shell,
    NvSheetHeader: Shell,
    NvSheetTitle: Shell,
  }
})

function mountSheet() {
  return mount(FulfillmentTimelineSheet, {
    props: { open: true, order: { salesOrderNo: 'SO-2026-0001', status: 'released' } },
  })
}

describe('履约追踪 Sheet 上的单单排产入口', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('无排产管理权限时入口禁用并说明原因', () => {
    const wrapper = mountSheet()

    const entry = wrapper.get('[data-testid="sales-order-schedule-single"]')
    expect(entry.attributes('disabled')).toBeDefined()
    expect(entry.attributes('title')).toContain('没有排产管理权限')
    expect(wrapper.find('[data-testid="schedule-dialog"]').exists()).toBe(false)
  })

  it('有权限时点击打开弹窗，并把销售单号作为检索起点带过去', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.scheduling.plans.manage'] },
    } as never)
    const wrapper = mountSheet()

    await wrapper.get('[data-testid="sales-order-schedule-single"]').trigger('click')

    const dialog = wrapper.get('[data-testid="schedule-dialog"]')
    expect(dialog.attributes('data-context')).toBe('销售订单 SO-2026-0001')
    // 检索起点 ≠ 关联关系：契约里没有 销售订单→工单 的稳定键，工单最终由排产员确认。
    expect(dialog.attributes('data-keyword')).toBe('SO-2026-0001')
  })

  it('没有销售单号时不渲染入口（空 scope 不给可点的动作）', () => {
    const wrapper = mount(FulfillmentTimelineSheet, { props: { open: true, order: null } })

    expect(wrapper.find('[data-testid="sales-order-schedule-single"]').exists()).toBe(false)
  })
})
