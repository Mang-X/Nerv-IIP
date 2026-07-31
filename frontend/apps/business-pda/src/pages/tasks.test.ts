import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

vi.mock('@/composables/useWorkbenchHome', () => ({
  HOME_PERMISSIONS: {
    quality: 'business.quality.inspection-records.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    wmsCounts: 'business.wms.counts.read',
  },
  usePdaIdentity: () => ({
    can: (permission: string) => permission.includes('quality'),
  }),
  useMyDispatchTasks: () => ({
    enabled: ref(true),
    pending: ref(false),
    openTasks: ref([
      {
        operationTaskId: 'task-1',
        workOrderNo: 'WO-2026-00001',
        assignedUserId: 'user-1',
        status: 'Queued',
      },
    ]),
    queuedCount: ref(1),
    inProgressCount: ref(0),
  }),
}))

import TasksPage from './tasks.vue'

describe('PDA tasks page', () => {
  it('shows server-filtered personal MES work and the truthful self-scoped quality entrance', async () => {
    const wrapper = mount(TasksPage)

    expect(wrapper.text()).toContain('WO-2026-00001')
    expect(wrapper.text()).toContain('我的质检任务')
    expect(wrapper.text()).not.toContain('仓储任务')

    await wrapper.get('[data-testid="quality-self-tasks"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/quality/tasks')
  })
})
