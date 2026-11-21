import { state, mountOptions } from './maintenance-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import WorkOrdersPage from './work-orders.vue'

describe('维修工单预计恢复', () => {
  async function openPrediction() {
    document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
    await flushPromises()
    ;[...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
      .find((el) => el.textContent?.includes('预计恢复'))!
      .click()
    await flushPromises()
  }

  it('预填本地时间、提交 UTC 和版本，清空可删除预测', async () => {
    state.query = {}
    state.workOrders[0]!.assignedTechnicianUserId = 'user-admin'
    state.workOrders[0]!.expectedRestoreAtUtc = '2026-10-09T08:45:00Z'
    state.workOrders[0]!.version = 4
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await openPrediction()
    const input = document.body.querySelector<HTMLInputElement>('#mwo-expected-restore')!
    expect(new Date(input.value).toISOString()).toBe('2026-10-09T08:45:00.000Z')
    input.value = '2026-10-10T13:30'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    input.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(state.updateExpectedRestore).toHaveBeenCalledWith(
      state.workOrders[0],
      new Date('2026-10-10T13:30').toISOString(),
      { kind: 'self', id: 'user-admin' },
    )
    expect(state.toastSuccess).toHaveBeenCalled()
    await openPrediction()
    const clear = document.body.querySelector<HTMLInputElement>('#mwo-expected-restore')!
    clear.value = ''
    clear.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    clear.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(state.updateExpectedRestore).toHaveBeenLastCalledWith(state.workOrders[0], null, {
      kind: 'self',
      id: 'user-admin',
    })
  })

  it('其他技师不可编辑预测', async () => {
    state.query = {}
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
    await flushPromises()
    const action = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (el) => el.textContent?.includes('预计恢复'),
    )!
    expect(action.getAttribute('data-disabled')).not.toBeNull()
  })

  it('更新失败保留抽屉并显示错误反馈', async () => {
    state.query = {}
    state.workOrders[0]!.assignedTechnicianUserId = 'user-admin'
    state.updateExpectedRestore.mockRejectedValueOnce(new Error('工单版本已变化，请刷新后重试。'))
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await openPrediction()
    const input = document.body.querySelector<HTMLInputElement>('#mwo-expected-restore')!
    input.value = '2026-10-10T13:30'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    input.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(state.toastError).toHaveBeenCalled()
    expect(input.value).toBe('2026-10-10T13:30')
    expect(document.body.querySelector('#mwo-expected-restore')).not.toBeNull()
  })
})

it('多授权范围维修人员可按工单团队更新，并只选择 Maintenance 支持的范围', async () => {
  state.query = {}
  state.workOrders[0]!.assignedTechnicianUserId = 'user-admin'
  state.workOrders[0]!.assignedTeamId = 'TEAM-A'
  state.expectedRestoreScopes = [
    { kind: 'work-center', id: 'WC-B', displayName: '冲压中心' },
    { kind: 'self', id: 'user-admin', displayName: '本人' },
    { kind: 'team', id: 'TEAM-A', displayName: '维修一班' },
  ]
  mount(WorkOrdersPage, mountOptions())
  await flushPromises()
  document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
  await flushPromises()
  ;[...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
    .find((el) => el.textContent?.includes('预计恢复'))!
    .click()
  await flushPromises()
  const picker = document.body.querySelector<HTMLButtonElement>('#mwo-expected-restore-scope')!
  expect(picker.textContent).toContain('维修一班')
  picker.click()
  await flushPromises()
  const options = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')]
  expect(options.some((el) => el.textContent?.includes('冲压中心'))).toBe(false)
  options.find((el) => el.textContent?.includes('本人'))!.click()
  await flushPromises()
  document.body
    .querySelector('#mwo-expected-restore')!
    .closest('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
  await flushPromises()
  expect(state.updateExpectedRestore).toHaveBeenCalledWith(state.workOrders[0], null, {
    kind: 'self',
    id: 'user-admin',
    displayName: '本人',
  })
})
