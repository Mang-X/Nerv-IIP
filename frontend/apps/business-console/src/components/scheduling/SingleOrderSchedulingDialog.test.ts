import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SingleOrderSchedulingDialog from './SingleOrderSchedulingDialog.vue'

const state = vi.hoisted(() => ({
  permissionCodes: ['business.scheduling.plans.manage'] as string[],
  requests: [] as Record<string, unknown>[],
  pushed: [] as unknown[],
  workOrders: [] as Record<string, unknown>[],
  hasScope: true,
  reject: null as Error | null,
}))

vi.mock('@/composables/useSingleOrderScheduling', async () => {
  const actual = await vi.importActual<typeof import('@/composables/useSingleOrderScheduling')>(
    '@/composables/useSingleOrderScheduling',
  )
  return {
    ...actual,
    useSingleOrderScheduling: () => ({
      context: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
      hasScope: computed(() => state.hasScope),
      pending: shallowRef(false),
      scheduleSingleOrder: vi.fn(async (request: Record<string, unknown>) => {
        state.requests.push(request)
        if (state.reject) throw state.reject
        return { planId: 'PLAN-SINGLE-1' }
      }),
    }),
  }
})

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkOrders: () => ({
    filters: reactive({ keyword: undefined as string | undefined, statuses: '' }),
    refreshWorkOrders: vi.fn(),
    workOrders: computed(() => state.workOrders),
    workOrdersError: computed(() => undefined),
    workOrdersPending: computed(() => false),
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: vi.fn(async (to: unknown) => {
      state.pushed.push(to)
    }),
  }),
}))

vi.mock('@/utils/notify', () => ({
  notifyError: vi.fn(),
  notifySuccess: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async () => {
  const { defineComponent } = await vi.importActual<typeof import('vue')>('vue')
  const Shell = defineComponent({ template: '<div><slot /></div>' })
  const Button = defineComponent({
    props: { disabled: Boolean, type: { type: String, default: 'button' } },
    emits: ['click'],
    template:
      '<button :type="type" :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  })
  const Input = defineComponent({
    props: { modelValue: { type: [String, Number], default: '' } },
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  })
  const Select = defineComponent({
    props: { modelValue: { type: String, default: '' } },
    emits: ['update:modelValue'],
    template: '<div class="select-stub"><slot /></div>',
  })
  const Checkbox = defineComponent({
    props: { modelValue: Boolean },
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  })
  return {
    NvButton: Button,
    NvCheckbox: Checkbox,
    NvDialog: Shell,
    NvDialogContent: Shell,
    NvDialogDescription: Shell,
    NvDialogFooter: Shell,
    NvDialogHeader: Shell,
    NvDialogTitle: Shell,
    NvField: Shell,
    NvFieldGroup: Shell,
    NvFieldLabel: Shell,
    NvInput: Input,
    NvSelect: Select,
    NvSelectContent: Shell,
    NvSelectItem: Shell,
    NvSelectTrigger: Shell,
    NvSelectValue: Shell,
    Spinner: Shell,
  }
})

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

function mountDialog(props: Record<string, unknown> = {}) {
  return mount(SingleOrderSchedulingDialog, { props: { open: true, ...props } })
}

describe('单单排产弹窗（MAN-694 / #1262）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    state.requests = []
    state.pushed = []
    state.workOrders = []
    state.hasScope = true
    state.reject = null
  })

  it('界面上写明语义：新建只含该单的方案，插入现有方案尚不可用', () => {
    const wrapper = mountDialog({ workOrderId: 'WO-77' })
    const semantics = wrapper.get('[data-testid="single-order-scheduling-semantics"]').text()

    expect(semantics).toContain('新建一个只含该单的排程方案')
    expect(semantics).toContain('现有方案保持不变')
    expect(semantics).toContain('MAN-674')
  })

  it('提交时把用户指定的窗口与固定工单送进单单排产，并跳到该方案', async () => {
    const wrapper = mountDialog({ workOrderId: 'WO-77' })

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.requests).toHaveLength(1)
    const request = state.requests[0]!
    expect(request.workOrderId).toBe('WO-77')
    // 默认窗口仍是 7 天，但已是从窗口表单解析出来的值，而不是提交时现算的死数。
    const span =
      (new Date(String(request.horizonEndUtc)).getTime() -
        new Date(String(request.horizonStartUtc)).getTime()) /
      86_400_000
    expect(span).toBe(7)
    expect(state.pushed[0]).toEqual({
      path: '/scheduling',
      query: { planId: 'PLAN-SINGLE-1', orderReference: 'WO-77' },
    })
  })

  it('没有固定工单又没选中候选时不提交，并说明还差什么', async () => {
    const wrapper = mountDialog({ initialKeyword: 'SO-2026-0001' })

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.requests).toHaveLength(0)
    expect(wrapper.text()).toContain('请先选择要排产的工单')
    // 契约里没有 销售订单→工单 的关联键，这一点必须写在界面上，不能让人以为是自动带出的。
    expect(wrapper.text()).toContain('稳定关联键')
  })

  it('只读（无排产管理权限）时不发请求，并说明缺哪个权限码', async () => {
    const wrapper = mountDialog({ workOrderId: 'WO-77', readOnly: true })

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.requests).toHaveLength(0)
    expect(wrapper.text()).toContain('business.scheduling.plans.manage')
  })

  it('失败原因留在弹窗里，用户改完窗口能重试', async () => {
    state.reject = new Error('工单没有生产版本')
    const wrapper = mountDialog({ workOrderId: 'WO-77' })

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('工单没有生产版本')
    expect(state.pushed).toHaveLength(0)
  })
})
