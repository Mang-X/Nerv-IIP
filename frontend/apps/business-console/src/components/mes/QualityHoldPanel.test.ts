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
      holdReason: '终检判定不合格',
      heldAtUtc: '2026-07-14T02:00:00Z',
      heldBy: 'qa-01',
      canManage: true,
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
    const link = wrapper.get('[data-router-link]')
    expect(link.attributes('data-to')).toContain('/quality/inspections')
    expect(link.attributes('data-to')).toContain('INSP-000001')
  })

  it('hides the force-release control without manage permission', () => {
    const wrapper = mountPanel({ canManage: false })
    expect(wrapper.findAll('button').some((b) => b.text().includes('强制释放'))).toBe(false)
  })

  it('force-releases with a required reason and notifies success', async () => {
    const wrapper = mountPanel({ canManage: true })
    await wrapper.findAll('button').find((b) => b.text().includes('强制释放'))!.trigger('click')
    await wrapper.get('#force-release-reason').setValue('设备已复检，人工放行')
    await wrapper.findAll('button').find((b) => b.text().includes('确认强制释放'))!.trigger('click')
    await flushPromises()
    expect(holdState.forceRelease).toHaveBeenCalledWith('设备已复检，人工放行')
    expect(notifySpies.success).toHaveBeenCalled()
    expect(wrapper.emitted('released')).toBeTruthy()
  })
})
