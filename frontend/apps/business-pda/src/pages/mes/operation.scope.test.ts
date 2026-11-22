import {
  routeState,
  completeTask,
  filters,
  operationScopeMessageRef,
  operationScopeReadyRef,
  operationListScopeRef,
  operationListContextIdentityRef,
  operationActionContextIdentityRef,
  defaultTasks,
  operationTasksRef,
  tasksPendingRef,
  tasksSuccessfulRef,
  resetFixture,
} from './operation.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(resetFixture)

  it('consumes the workOrderId and operationTaskId deep link and opens that exact task', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-2',
    })

    mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(filters.workOrderId).toBe('WO-2026-0002')
    expect(filters.operationTaskId).toBe('OP-2')
    expect(filters.keyword).toBeUndefined()
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 20')
    expect(document.body.textContent).not.toContain('WO-2026-0001 · 工序 10')
  })

  it('closes the old task and waits for the new pair response when the reused route query changes', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationTasksRef.value = [defaultTasks[0]]
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-2',
    })
    await nextTick()

    expect(filters.workOrderId).toBe('WO-2026-0002')
    expect(filters.operationTaskId).toBe('OP-2')
    expect(filters.keyword).toBeUndefined()
    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()

    operationTasksRef.value = [defaultTasks[1]]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 20')
    expect(document.body.textContent).not.toContain('WO-2026-0001 · 工序 10')
  })

  it('closes a fixed-pair sheet and reopens only from the new scope response', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListScopeRef.value = {
      kind: 'work-center',
      id: 'WC-B',
      displayName: '精加工二线',
    }
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()

    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()

    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        operationSequence: 30,
        workCenterId: 'WC-B',
      },
    ]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 30')
    expect(document.body.textContent).not.toContain('WO-2026-0001 · 工序 10')
  })

  it('closes a fixed-pair sheet and fails closed when the new scope omits the task', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListScopeRef.value = {
      kind: 'work-center',
      id: 'WC-B',
      displayName: '精加工二线',
    }
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()

    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()

    operationTasksRef.value = []
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '未在当前作业范围内找到指定工序任务',
    )
    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()
  })

  it('does not revive a fixed-pair sheet from a stale response after rapid scope changes', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')

    tasksPendingRef.value = true
    tasksSuccessfulRef.value = false
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
    await nextTick()
    operationListContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-C'
    await nextTick()

    operationTasksRef.value = [defaultTasks[0]]
    await flushPromises()
    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()

    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        operationSequence: 40,
        workCenterId: 'WC-C',
      },
    ]
    tasksSuccessfulRef.value = true
    tasksPendingRef.value = false
    await flushPromises()

    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 40')
    expect(document.body.textContent).not.toContain('WO-2026-0001 · 工序 10')
  })

  it('waits for the manage scope when exact task data arrives first, then opens the deep link', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })
    operationScopeReadyRef.value = false
    operationActionContextIdentityRef.value = 'principal-001\u0000org-001\u0000env-dev\u0000\u0000'
    mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()

    operationActionContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
    operationScopeReadyRef.value = true
    await flushPromises()

    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).not.toBeNull()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')
  })

  it('fails closed when a reused route changes to an incomplete task identity', async () => {
    routeState.replaceQuery?.({ operationTaskId: 'OP-1' })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '缺少工单或任务标识',
    )
    expect(wrapper.findAll('[data-row]')).toHaveLength(0)
    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()
  })

  it('fails closed when the exact pair is absent from the authorized response', async () => {
    routeState.replaceQuery?.({
      workOrderId: 'WO-NOT-AUTHORIZED',
      operationTaskId: 'OP-NOT-AUTHORIZED',
    })
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-deep-link-message"]').text()).toContain(
      '未在当前作业范围内找到指定工序任务',
    )
    expect(wrapper.findAll('[data-row]')).toHaveLength(0)
    expect(
      document.body.querySelector('[data-slot="mobile-sheet-content"][data-state="open"]'),
    ).toBeNull()
  })

  it('shows the missing-scope reason and disables lifecycle actions', async () => {
    operationScopeReadyRef.value = false
    operationScopeMessageRef.value = '尚未选择已授权作业范围，当前操作已禁用。'
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="operation-scope-message"]').text()).toContain(
      '尚未选择已授权作业范围',
    )
    const action = document.body.querySelector<HTMLButtonElement>(
      '[data-testid="action-complete"]',
    )!
    expect(action.disabled).toBe(true)
    action.click()
    await flushPromises()
    expect(completeTask).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
