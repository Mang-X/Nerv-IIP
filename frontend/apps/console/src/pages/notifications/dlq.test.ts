import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

import DeadLetterPage from './dlq.vue'

const deadLetterState = vi.hoisted(() => ({
  allError: { value: undefined as Error | undefined },
  consumerNameFilter: { __v_isRef: true, value: '' },
  detailPending: { __v_isRef: true, value: false },
  eventTypeFilter: { __v_isRef: true, value: '' },
  ignore: vi.fn(),
  ignorePending: { __v_isRef: true, value: false },
  listPending: { __v_isRef: true, value: false },
  refreshDeadLetters: vi.fn(),
  replay: vi.fn(),
  replayBatchPending: { __v_isRef: true, value: false },
  replayFiltered: vi.fn(),
  replayPending: { __v_isRef: true, value: false },
  selectedDeadLetterId: { __v_isRef: true, value: undefined as string | undefined },
  statusFilter: { __v_isRef: true, value: 'Pending' },
}))

const toastState = vi.hoisted(() => ({
  success: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async () => {
  const actual = await vi.importActual<typeof import('@nerv-iip/ui')>('@nerv-iip/ui')

  return {
    ...actual,
    toast: {
      success: toastState.success,
    },
  }
})

const rows = [
  {
    id: '018f8b65-32d1-7111-9cde-0242ac120002',
    consumerName: 'notification.operation-task-failed',
    eventId: 'event-001',
    eventType: 'ops.OperationTaskFailed',
    eventVersion: 1,
    sourceService: 'ops',
    idempotencyKey: 'operation-task-failed:task-001',
    failureCode: 'handler-retry-exhausted',
    failureMessage: 'Handler failed.',
    status: 'Pending',
    deadLetteredAtUtc: '2026-05-21T00:00:00Z',
    replayedAtUtc: null,
  },
]

vi.mock('@/composables/useNotificationDeadLetters', () => ({
  useNotificationDeadLetters: () => ({
    allError: deadLetterState.allError,
    consumerNameFilter: deadLetterState.consumerNameFilter,
    deadLetters: computed(() => rows),
    detailPending: deadLetterState.detailPending,
    eventTypeFilter: deadLetterState.eventTypeFilter,
    failedCount: computed(() => 0),
    ignore: deadLetterState.ignore,
    ignorePending: deadLetterState.ignorePending,
    listPending: deadLetterState.listPending,
    pendingCount: computed(() => 1),
    refreshDeadLetters: deadLetterState.refreshDeadLetters,
    replay: deadLetterState.replay,
    replayBatchPending: deadLetterState.replayBatchPending,
    replayFiltered: deadLetterState.replayFiltered,
    replayPending: deadLetterState.replayPending,
    selectedDeadLetter: computed(() => ({
      ...rows[0],
      eventClrType: 'Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent',
      eventJson: '{"eventId":"event-001"}',
    })),
    selectedDeadLetterId: deadLetterState.selectedDeadLetterId,
    statusFilter: deadLetterState.statusFilter,
  }),
}))

describe('Notification DLQ page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    deadLetterState.allError.value = undefined
    deadLetterState.consumerNameFilter.value = ''
    deadLetterState.eventTypeFilter.value = ''
    deadLetterState.ignorePending.value = false
    deadLetterState.listPending.value = false
    deadLetterState.replayBatchPending.value = false
    deadLetterState.replayPending.value = false
    deadLetterState.selectedDeadLetterId.value = rows[0].id
    deadLetterState.statusFilter.value = 'Pending'
    deadLetterState.ignore.mockResolvedValue(undefined)
    deadLetterState.refreshDeadLetters.mockResolvedValue(undefined)
    deadLetterState.replay.mockResolvedValue({ succeeded: true, status: 'Replayed' })
    deadLetterState.replayFiltered.mockResolvedValue({ items: [{ succeeded: true }] })
  })

  function mountPage() {
    return mount(DeadLetterPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })
  }

  it('renders filters, dead-letter rows and selected payload', () => {
    const wrapper = mountPage()

    expect(wrapper.text()).toContain('死信队列')
    expect(wrapper.text()).toContain('ops.OperationTaskFailed')
    expect(wrapper.text()).toContain('notification.operation-task-failed')
    expect(wrapper.text()).toContain('{"eventId":"event-001"}')
  })

  it('runs single and filtered replay actions', async () => {
    const wrapper = mountPage()

    await wrapper.find('button[aria-label="重放死信：event-001"]').trigger('click')
    await flushPromises()

    expect(deadLetterState.replay).toHaveBeenCalledWith(rows[0].id)

    await wrapper.find('button[aria-label="重放当前筛选死信"]').trigger('click')
    await flushPromises()

    expect(deadLetterState.replayFiltered).toHaveBeenCalled()
    expect(toastState.success).toHaveBeenCalledWith('已提交 1 条匹配死信重放')
  })

  it('requires an ignore reason before ignoring the selected dead letter', async () => {
    const wrapper = mountPage()

    expect(wrapper.find('button[aria-label="忽略选中死信"]').attributes('disabled')).toBeDefined()

    await wrapper.find('textarea').setValue('manual replacement processed')
    await wrapper.find('button[aria-label="忽略选中死信"]').trigger('click')
    await flushPromises()

    expect(deadLetterState.ignore).toHaveBeenCalledWith(rows[0].id, 'manual replacement processed')
    expect(toastState.success).toHaveBeenCalledWith('死信消息已忽略')
  })
})
