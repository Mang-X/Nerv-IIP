import type {
  BusinessConsoleOrderUrgency,
  BusinessConsoleOrderUrgencyDetail,
} from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UrgencyDisplayMode } from '@/composables/useUrgencyDisplayMode'
import OrderUrgencyBadge from './OrderUrgencyBadge.vue'

const state = vi.hoisted(() => ({
  permissionCodes: ['business.scheduling.plans.manage'] as string[],
  detail: undefined as BusinessConsoleOrderUrgencyDetail | undefined,
  setBusinessPriority: vi.fn(),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

vi.mock('@/composables/useOrderUrgencyDetail', async () => {
  const { shallowRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useOrderUrgencyDetail: () => ({
      detail: shallowRef(state.detail),
      pending: shallowRef(false),
    }),
    useSetOrderUrgencyBusinessPriority: () => ({
      error: shallowRef(undefined),
      pending: shallowRef(false),
      setBusinessPriority: state.setBusinessPriority,
    }),
  }
})

const urgency: BusinessConsoleOrderUrgency = {
  orderId: 'WO-001',
  businessReference: 'SO-001',
  level: 'urgent',
  modelVersion: 'order-urgency-v1',
  calculatedAtUtc: '2026-07-22T08:00:00Z',
  businessPriority: {
    level: 'p1',
    source: 'manual',
    reason: '重点客户',
    revision: 2,
    setAtUtc: '2026-07-22T07:00:00Z',
    reasonCodes: ['business.priority.p1'],
  },
  timeCriticality: {
    level: 'highrisk',
    criticalRatio: 0.8,
    slackHours: -2,
    expectedDelayHours: 5,
    reasonCodes: ['time.cr.belowOne'],
  },
  executionRisk: {
    level: 'attention',
    isSourceStale: true,
    reasonCodes: ['material.shortage'],
    facts: [],
  },
}

const routerLinkStub = {
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
}

const stubs = {
  ...routerLinkStub,
  NvTooltipProvider: { template: '<div><slot /></div>' },
  NvTooltip: { template: '<div><slot /></div>' },
  NvTooltipTrigger: { template: '<div><slot /></div>' },
  NvTooltipContent: { template: '<div><slot /></div>' },
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<aside><slot /></aside>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvButton: {
    props: ['disabled', 'type'],
    emits: ['click'],
    template: '<button :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvStatusBadge: {
    props: ['label', 'tone', 'value'],
    template: '<span>{{ label ?? value }}</span>',
  },
  NvField: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvFieldError: { props: ['errors'], template: '<p class="field-error">{{ errors?.[0] }}</p>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<span><slot /></span>' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function mountBadge(props: {
  orderReference: string
  urgency?: BusinessConsoleOrderUrgency
  mode?: UrgencyDisplayMode
}) {
  return mount(OrderUrgencyBadge, { props, global: { stubs } })
}

beforeEach(() => {
  state.permissionCodes = ['business.scheduling.plans.manage']
  state.detail = undefined
  state.setBusinessPriority = vi.fn().mockResolvedValue({ success: true })
})

describe('OrderUrgencyBadge display modes', () => {
  const cases: Array<[UrgencyDisplayMode, string]> = [
    ['level', '紧急'],
    ['businessPriority', 'P1'],
    ['dynamicUrgency', '高风险'],
    ['executionRisk', '关注'],
    ['criticalRatio', 'CR 0.8'],
    ['slack', 'Slack -2h'],
    ['expectedDelay', '延误 5h'],
  ]

  it.each(cases)('renders the %s mode on the trigger badge', (mode, label) => {
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode })
    expect(wrapper.get('[aria-label="查看 SO-001 紧急度解释"]').text()).toBe(label)
  })

  it('defaults to the unified level when no mode is provided', () => {
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency })
    expect(wrapper.get('[aria-label="查看 SO-001 紧急度解释"]').text()).toBe('紧急')
  })

  it('routes to the scheduling order id without a page reload', () => {
    const wrapper = mountBadge({
      orderReference: 'SO-001',
      urgency: { orderId: 'WO-001', businessReference: 'SO-001', level: 'critical' },
    })
    const link = wrapper.get('[data-router-link]')
    expect(link.attributes('data-to')).toContain('WO-001')
  })
})

describe('OrderUrgencyBadge priority editing', () => {
  it('submits a governed priority payload with the required reason and optional expiry', async () => {
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode: 'level' })

    await wrapper.get('#urgency-priority-reason').setValue('重点客户插单')
    await wrapper.get('#urgency-priority-expiry').setValue('2026-08-01T00:00')
    await wrapper.get('[data-testid="priority-editor"]').trigger('submit')
    await flushPromises()

    expect(state.setBusinessPriority).toHaveBeenCalledTimes(1)
    const payload = state.setBusinessPriority.mock.calls[0]![0]
    expect(payload).toMatchObject({
      orderReference: 'WO-001',
      level: 'p1',
      reason: '重点客户插单',
    })
    expect(payload.expiresAtUtc).toBe(new Date('2026-08-01T00:00').toISOString())
    // The list/detail refresh is signalled so the new revision propagates.
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  it('blocks the write and shows an error when the reason is missing', async () => {
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode: 'level' })

    await wrapper.get('[data-testid="priority-editor"]').trigger('submit')
    await flushPromises()

    expect(state.setBusinessPriority).not.toHaveBeenCalled()
    expect(wrapper.get('.field-error').text()).toContain('请填写调整原因')
    expect(wrapper.emitted('refresh')).toBeUndefined()
  })

  it('hides the write action from host-domain read-only users', () => {
    state.permissionCodes = ['business.erp.sales.read']
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode: 'level' })

    expect(wrapper.find('[data-testid="priority-editor"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('仅排产管理权限可调整人工优先级')
    // Read-only users still inspect the urgency explanation.
    expect(wrapper.text()).toContain('CR / Slack')
    expect(wrapper.text()).toContain('执行风险')
  })
})

describe('OrderUrgencyBadge audit history', () => {
  it('renders every append-only priority change with actor, timestamps, levels, reason and expiry', () => {
    state.detail = {
      current: urgency,
      history: [],
      businessPriorityChanges: [
        {
          revision: 2,
          previousLevel: 'p2',
          newLevel: 'p1',
          changedBy: 'planner@nerv',
          reason: '重点客户',
          changedAtUtc: '2026-07-22T07:00:00Z',
          expiresAtUtc: '2026-08-01T00:00:00Z',
        },
        {
          revision: 1,
          previousLevel: null,
          newLevel: 'p2',
          changedBy: 'system',
          reason: '初始设置',
          changedAtUtc: '2026-07-20T07:00:00Z',
          expiresAtUtc: null,
        },
      ],
    }
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode: 'level' })

    const rows = wrapper.findAll('[data-testid="priority-audit-row"]')
    expect(rows).toHaveLength(2)
    // Newest revision first (append-only, sorted by revision desc).
    expect(rows[0]!.text()).toContain('planner@nerv')
    expect(rows[0]!.text()).toContain('P2 → P1')
    expect(rows[0]!.text()).toContain('重点客户')
    expect(rows[1]!.text()).toContain('system')
    expect(rows[1]!.text()).toContain('— → P2')
    expect(rows[1]!.text()).toContain('长期有效')
    // The current setter is surfaced from the latest change.
    expect(wrapper.text()).toContain('planner@nerv')
  })

  it('shows an empty state when there is no manual priority history', () => {
    const wrapper = mountBadge({ orderReference: 'SO-001', urgency, mode: 'level' })
    expect(wrapper.text()).toContain('暂无人工优先级调整记录')
  })
})
