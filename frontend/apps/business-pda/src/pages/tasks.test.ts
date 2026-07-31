import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

vi.mock('@/composables/useWorkbenchHome', () => ({
  HOME_PERMISSIONS: {
    mesOperations: 'business.mes.operations.read',
    quality: 'business.quality.inspection-records.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    wmsCounts: 'business.wms.counts.read',
  },
  usePdaIdentity: () => ({
    can: (permission: string) => permission.includes('quality') || permission.includes('mes'),
  }),
  useMyDispatchTasks: () => {
    throw new Error('the task hub must not consume client-filtered personal dispatch facts')
  },
}))

import TasksPage from './tasks.vue'

describe('PDA tasks page', () => {
  it('uses truthful scoped task entrances without claiming client-filtered MES rows are personal', async () => {
    const wrapper = mount(TasksPage)

    expect(wrapper.text()).toContain('生产作业')
    expect(wrapper.text()).toContain('我的质检任务')
    expect(wrapper.text()).not.toContain('我的生产任务')
    expect(wrapper.text()).not.toContain('暂无派给我的生产任务')
    expect(wrapper.text()).not.toContain('仓储任务')

    const production = wrapper
      .findAll('[role="button"]')
      .find((cell) => cell.text().includes('生产作业'))!
    await production.trigger('keydown', { key: 'Enter' })
    expect(push).toHaveBeenCalledWith('/mes/operation')

    await wrapper.get('[data-testid="quality-self-tasks"]').trigger('click')
    expect(push).toHaveBeenCalledWith('/quality/tasks')
  })
})
