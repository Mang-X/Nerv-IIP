import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import NotificationsPage from './index.vue'

const notificationState = vi.hoisted(() => ({
  batchError: { value: undefined as Error | undefined },
  batchPending: { __v_isRef: true, value: false },
  markReadPending: { __v_isRef: true, value: false },
  messagesPending: { __v_isRef: true, value: false },
  markAllUnreadRead: vi.fn(),
  markRead: vi.fn(),
  markReadError: { value: undefined as Error | undefined },
  refreshNotifications: vi.fn(),
  tasksPending: { __v_isRef: true, value: false },
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

vi.mock('@/composables/useNotifications', () => ({
  useNotifications: () => ({
    allError: computed(() => notificationState.batchError.value ?? notificationState.markReadError.value),
    batchPending: notificationState.batchPending,
    markAllUnreadRead: notificationState.markAllUnreadRead,
    markRead: notificationState.markRead,
    markReadError: notificationState.markReadError,
    markReadPending: notificationState.markReadPending,
    messagesError: shallowRef(),
    messagesPending: notificationState.messagesPending,
    openTasks: computed(() => [
      {
        taskId: 'task-1',
        messageId: 'msg-1',
        taskType: 'acknowledge',
        status: 'open',
        actionRef: 'ops://task/1',
        createdAtUtc: '2026-05-21T00:10:00Z',
      },
    ]),
    readMessages: computed(() => [
      {
        messageId: 'msg-2',
        status: 'read',
        severity: 'info',
        title: 'Deployment complete',
        createdAtUtc: '2026-05-20T23:00:00Z',
        readAtUtc: '2026-05-21T00:05:00Z',
      },
    ]),
    refreshNotifications: notificationState.refreshNotifications,
    tasksError: shallowRef(),
    tasksPending: notificationState.tasksPending,
    unreadMessages: computed(() => [
      {
        messageId: 'msg-1',
        status: 'unread',
        severity: 'warning',
        title: 'Disk pressure',
        summary: 'Node A is above threshold',
        resource: {
          resourceType: 'node',
          resourceId: 'node-a',
        },
        createdAtUtc: '2026-05-21T00:00:00Z',
      },
    ]),
  }),
}))

describe('Notifications page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notificationState.batchError.value = undefined
    notificationState.batchPending.value = false
    notificationState.markReadError.value = undefined
    notificationState.markReadPending.value = false
    notificationState.messagesPending.value = false
    notificationState.tasksPending.value = false
    notificationState.markAllUnreadRead.mockResolvedValue(undefined)
    notificationState.markRead.mockResolvedValue(undefined)
    notificationState.refreshNotifications.mockResolvedValue(undefined)
  })

  function mountPage() {
    return mount(NotificationsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })
  }

  it('renders unread/read messages and open tasks', () => {
    const wrapper = mountPage()

    expect(wrapper.text()).toContain('通知')
    expect(wrapper.text()).toContain('Disk pressure')
    expect(wrapper.text()).toContain('Deployment complete')
    expect(wrapper.text()).toContain('acknowledge')
    expect(wrapper.text()).toContain('node: node-a')
    // 两个消息分区必须有稳定且唯一的标题 id（中文标题不能塌成同一个 id）。
    expect(wrapper.find('#notification-unread-title').exists()).toBe(true)
    expect(wrapper.find('#notification-read-title').exists()).toBe(true)
  })

  it('calls mark read and batch read actions', async () => {
    const wrapper = mountPage()

    await wrapper.find('button[aria-label="标记已读：Disk pressure"]').trigger('click')
    await flushPromises()

    expect(notificationState.markRead).toHaveBeenCalledWith('msg-1')

    await wrapper.find('button[aria-label="全部标记已读"]').trigger('click')
    await flushPromises()

    expect(notificationState.markAllUnreadRead).toHaveBeenCalled()
  })

  it('does not show success toast when mark read rejects', async () => {
    notificationState.markRead.mockRejectedValue(new Error('Forbidden'))
    const wrapper = mountPage()

    await wrapper.find('button[aria-label="标记已读：Disk pressure"]').trigger('click')
    await flushPromises()

    expect(notificationState.markRead).toHaveBeenCalledWith('msg-1')
    expect(toastState.success).not.toHaveBeenCalledWith('通知已标记为已读')
  })

  it('disables bulk write while another notification operation is pending', () => {
    notificationState.markReadPending.value = true
    const wrapper = mountPage()

    expect(
      wrapper.find('button[aria-label="全部标记已读"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('renders notification errors', () => {
    notificationState.markReadError.value = new Error('Forbidden')
    const wrapper = mountPage()

    expect(wrapper.text()).toContain('无法更新通知')
    expect(wrapper.text()).toContain('Forbidden')
  })
})
