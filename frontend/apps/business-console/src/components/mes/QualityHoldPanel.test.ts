import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import QualityHoldPanel from './QualityHoldPanel.vue'

const notifySpies = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))
vi.mock('@/utils/notify', () => ({
  notifySuccess: notifySpies.success,
  notifyError: notifySpies.error,
}))

const holdState = vi.hoisted(() => ({
  forceRelease: vi.fn(async () => undefined),
  timeline: [] as Array<Record<string, unknown>>,
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesQualityHold: () => ({
    timeline: ref(holdState.timeline),
    timelinePending: ref(false),
    timelineError: ref(undefined),
    refreshTimeline: vi.fn(async () => undefined),
    forceRelease: holdState.forceRelease,
    forceReleasePending: ref(false),
    forceReleaseError: ref(undefined),
  }),
}))

const stubs = {
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
  NvStatusBadge: {
    props: ['tone', 'label'],
    template: '<span data-testid="badge" :data-tone="tone">{{ label }}</span>',
  },
  NvButton: {
    props: ['disabled'],
    template: '<button :disabled="disabled" v-bind="$attrs"><slot /></button>',
  },
  NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogFooter: { template: '<footer><slot /></footer>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  Spinner: true,
}

function mountPanel(props: Record<string, unknown>) {
  return mount(QualityHoldPanel, {
    props: {
      organizationId: 'org',
      environmentId: 'dev',
      sourceService: 'business-mes',
      sourceDocumentId: 'WO-1',
      scope: 'work-order',
      isActive: true,
      holdReason: '终检判定不合格',
      heldAtUtc: '2026-07-14T02:00:00Z',
      heldBy: 'qa-01',
      canManage: true,
      canReadTimeline: true,
      canReadInspectionRecords: true,
      ...props,
    },
    global: { stubs },
  })
}

describe('QualityHoldPanel', () => {
  beforeEach(() => {
    notifySpies.success.mockReset()
    notifySpies.error.mockReset()
    holdState.forceRelease.mockClear()
    holdState.timeline = [
      {
        transitionId: 't1',
        eventKind: 'hold-applied',
        origin: 'automatic',
        actor: 'quality',
        occurredAtUtc: '2026-07-14T02:00:00Z',
        reason: '终检判定不合格',
        sourceInspectionRecordId: 'INSP-REC-9',
        sourceInspectionDocumentId: 'INSP-000001',
      },
      {
        transitionId: 't2',
        eventKind: 'inspection-released',
        origin: 'automatic',
        actor: 'quality',
        occurredAtUtc: '2026-07-14T05:00:00Z',
        reason: '复检合格',
      },
    ]
  })

  it('renders the hold timeline with kind, origin and source-inspection interlink', () => {
    const wrapper = mountPanel({})
    expect(wrapper.text()).toContain('质量保留中')
    expect(wrapper.text()).toContain('施加质量保留')
    expect(wrapper.text()).toContain('复检合格自动放行')
    expect(wrapper.text()).toContain('自动')
    // 来源检验互链须带 inspectionRecordId（定位记录，目标页开只读记录详情）+ inspectionPlanId（方案上下文），
    // 且不再用目标页不消费的 keyword。
    const link = wrapper.get('[data-router-link]')
    const to = JSON.parse(link.attributes('data-to') as string)
    expect(to.path).toBe('/quality/inspections')
    expect(to.query.inspectionRecordId).toBe('INSP-REC-9')
    expect(to.query.inspectionPlanId).toBe('INSP-000001')
    expect(to.query.keyword).toBeUndefined()
    expect(link.text()).toContain('来源检验记录 INSP-REC-9')
  })

  it('hides the source-inspection link without inspection-records read permission', () => {
    // 目标页需 business.quality.inspection-records.read：无该权限不显示互链，避免点后被路由守卫拒（死链）。
    const wrapper = mountPanel({ canReadInspectionRecords: false })
    expect(wrapper.find('[data-router-link]').exists()).toBe(false)
    // 时间线本身仍可见（只是不带下钻互链）。
    expect(wrapper.text()).toContain('施加质量保留')
  })

  it('links source-inspection even when only a record id exists (no plan)', () => {
    holdState.timeline = [
      {
        transitionId: 't3',
        eventKind: 'hold-applied',
        origin: 'automatic',
        actor: 'quality',
        occurredAtUtc: '2026-07-14T02:00:00Z',
        sourceInspectionRecordId: 'INSP-REC-ONLY',
      },
    ]
    const wrapper = mountPanel({})
    const to = JSON.parse(wrapper.get('[data-router-link]').attributes('data-to') as string)
    expect(to.query.inspectionRecordId).toBe('INSP-REC-ONLY')
    expect(to.query.inspectionPlanId).toBeUndefined()
  })

  it('hides the force-release control without manage permission', () => {
    const wrapper = mountPanel({ canManage: false })
    expect(wrapper.findAll('button').some((b) => b.text().includes('强制释放'))).toBe(false)
  })

  it('shows released state + timeline and no force-release for an inactive hold', () => {
    const wrapper = mountPanel({
      isActive: false,
      canManage: true,
      releasedAtUtc: '2026-07-14T05:00:00Z',
      releasedBy: 'user:user-admin',
      releaseReason: '人工放行',
      releaseSource: 'manual-force-release',
    })
    expect(wrapper.text()).toContain('已释放')
    expect(wrapper.text()).toContain('人工强制释放')
    // 释放后时间线仍完整可见（含释放事件），满足验收「hold 自动消失且时间线完整」。
    expect(wrapper.text()).toContain('人工强制释放')
    expect(wrapper.text()).toContain('复检合格自动放行')
    // 非活跃保留不提供强制释放动作。
    expect(wrapper.findAll('button').some((b) => b.text().includes('强制释放'))).toBe(false)
  })

  it('force-releases with a required reason and notifies success', async () => {
    const wrapper = mountPanel({ canManage: true })
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('强制释放'))!
      .trigger('click')
    await wrapper.get('#force-release-reason').setValue('设备已复检，人工放行')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('确认强制释放'))!
      .trigger('click')
    await flushPromises()
    expect(holdState.forceRelease).toHaveBeenCalledWith('设备已复检，人工放行')
    expect(notifySpies.success).toHaveBeenCalled()
    expect(wrapper.emitted('released')).toBeTruthy()
  })

  it('marks the reason field on empty submit without sending (no disabled button)', async () => {
    const wrapper = mountPanel({ canManage: true })
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('强制释放'))!
      .trigger('click')
    // 理由为空直接点确认：按钮未被禁用，点后标红 + 提示，且不发释放请求。
    const confirm = wrapper.findAll('button').find((b) => b.text().includes('确认强制释放'))!
    expect(confirm.attributes('disabled')).toBeUndefined()
    await confirm.trigger('click')
    expect(holdState.forceRelease).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请填写释放理由')
  })

  it('gates the timeline behind quality read permission (no request, clear note)', () => {
    // 时间线读端点需 business.mes.quality.read：无该权限时不加载时间线、给出说明，而非逐个保留 403。
    const wrapper = mountPanel({ canReadTimeline: false })
    expect(wrapper.text()).toContain('需要「质量」读取权限才能查看保留时间线')
    expect(wrapper.text()).not.toContain('复检合格自动放行')
    // 无可刷新的时间线时不显示刷新入口。
    expect(wrapper.findAll('button').some((b) => b.text().includes('刷新'))).toBe(false)
  })
})
