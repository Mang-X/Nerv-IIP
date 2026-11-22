import {
  routeGuardState,
  completeTask,
  claimTask,
  filters,
  defaultTasks,
  operationTasksRef,
  tasksRefreshingRef,
  dispatchBeforeUnload,
  resetFixture,
} from './operation.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(resetFixture)

  it('把分页器的真实刷新生命周期绑定给任务列表壳', async () => {
    const wrapper = mount(OperationPage)

    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(false)
    tasksRefreshingRef.value = true
    await wrapper.vm.$nextTick()
    expect(wrapper.getComponent({ name: 'TaskListShell' }).props('refreshing')).toBe(true)
  })

  it('renders the scan bar and an operation ListRow per task', () => {
    const wrapper = mount(OperationPage)
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    // 夹具里 workOrderNo（MO-…）与 workOrderId（WO-…）取值不同：列表必须展示工单号即 workOrderId。
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).toContain('WO-2026-0002 · 工序 20')
    expect(wrapper.text()).not.toContain('MO-2026-0001')
    expect(wrapper.text()).not.toContain('工单信息未提供')
  })

  it('uses the resolved work-order strong id as an exact task filter', async () => {
    const wrapper = mount(OperationPage)
    await wrapper.getComponent({ name: 'MesScanPrevalidation' }).vm.$emit('accepted', {
      kind: 'work-order',
      candidate: {},
      workOrderId: 'WO-2026-0002',
    })
    await flushPromises()
    expect(filters.workOrderId).toBe('WO-2026-0002')
    expect(filters.operationTaskId).toBeUndefined()
    expect(filters.keyword).toBeUndefined()
  })

  it('opens the action BottomSheet when a row is tapped', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()
    // BottomSheet 内容 teleport 到 body
    expect(document.body.textContent).toContain('完成')
    wrapper.unmount()
  })

  it('lets the current operator claim an unassigned queued task in the selected work center', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[1],
        workCenterId: 'WC-A',
        assignedUserId: null,
        assignedUserName: null,
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-claim"]')!.click()
    await flushPromises()

    expect(claimTask).toHaveBeenCalledTimes(1)
    expect(claimTask).toHaveBeenCalledWith(
      'WO-2026-0002',
      'OP-2',
      expect.objectContaining({
        idempotencyKey: expect.any(String),
        context: expect.objectContaining({
          action: 'claim',
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        }),
      }),
    )
    expect(document.body.textContent).toContain('任务领取请求已受理')
    wrapper.unmount()
  })

  it('does not carry an abandoned list scan failure into a manually selected task', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const listScanner = wrapper.getComponent({ name: 'MesScanPrevalidation' })
    await listScanner.vm.$emit('statusChange', 'unknown')

    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const contextScanner = wrapper
      .findAllComponents({ name: 'MesScanPrevalidation' })
      .find((scanner) => (scanner.props('acceptedKinds') as string[]).includes('device'))!
    await contextScanner.vm.$emit('statusChange', 'resolved')
    await nextTick()

    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="action-pause"]')!.disabled,
    ).toBe(false)
    wrapper.unmount()
  })

  it('keeps concurrent scanners aggregated and records validated device and personnel context', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const scanners = wrapper.findAllComponents({ name: 'MesScanPrevalidation' })
    const contextScanner = scanners.find((scanner) =>
      (scanner.props('acceptedKinds') as string[]).includes('device'),
    )!
    const listScanner = scanners.find(
      (scanner) => !(scanner.props('acceptedKinds') as string[]).includes('device'),
    )!

    await contextScanner.vm.$emit('accepted', {
      kind: 'device',
      candidate: {},
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      scannedObjectId: 'DEVICE-1',
    })
    await contextScanner.vm.$emit('accepted', {
      kind: 'personnel',
      candidate: {},
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      scannedObjectId: 'USER-1',
    })
    await flushPromises()
    expect(document.body.querySelector('[data-testid="operation-validated-device"]')).not.toBeNull()
    expect(
      document.body.querySelector('[data-testid="operation-validated-personnel"]'),
    ).not.toBeNull()

    await listScanner.vm.$emit('statusChange', 'pending')
    await contextScanner.vm.$emit('statusChange', 'pending')
    await contextScanner.vm.$emit('statusChange', 'resolved')
    await nextTick()
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="action-pause"]')!.disabled,
    ).toBe(true)

    await listScanner.vm.$emit('statusChange', 'resolved')
    await nextTick()
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="action-pause"]')!.disabled,
    ).toBe(false)
    wrapper.unmount()
  })

  it('shows the work-order number and operation sequence with server-evaluated blocker details', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[1],
        workOrderId: 'WO-2026-0042',
        operationTaskId: 'WO-2026-0042-OP-20',
        workOrderNo: 'MO-2026-0042',
        operationTaskNo: 'OP-TASK-0020',
        operationCode: 'OP-CUT',
        deviceAssetId: 'device-asset-lathe-07',
        deviceAssetCode: 'DEV-LATHE-07',
        deviceAssetName: '七号数控车床',
        allowedActions: [],
        blockReasons: [
          'PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成（工序 10）',
          'MATERIAL_SHORTAGE: 物料 MAT-STEEL 缺口 2',
          'equipment.activeAlarm: 工业遥测存在未解除报警，设备不可用于当前工序。',
          'QUALITY_HOLD_ACTIVE: 工单存在有效质量保留，无法开工',
        ],
        evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    const taskDefinition = [...document.body.querySelectorAll('dt')].find(
      (term) => term.textContent === '工序',
    )?.nextElementSibling
    expect(taskDefinition?.textContent).toBe('工序 20')
    expect(document.body.textContent).not.toContain('WO-2026-0042-OP-20')
    expect(document.body.textContent).toContain('WO-2026-0042')
    expect(document.body.textContent).not.toContain('MO-2026-0042')
    expect(document.body.textContent).not.toContain('OP-TASK-0020')
    expect(document.body.textContent).not.toContain('工单信息未提供')
    expect(document.body.textContent).toContain('七号数控车床（DEV-LATHE-07）')
    expect(document.body.textContent).not.toContain('device-asset-lathe-07')
    expect(document.body.textContent).toContain('2026')
    expect(document.body.textContent).toContain('前序工序')
    expect(document.body.textContent).toContain('物料齐套')
    expect(document.body.textContent).toContain('设备')
    expect(document.body.textContent).toContain('质量')
    expect(document.body.querySelector('[data-testid="action-start"]')).toBeNull()
  })

  it('never shows a rework operation task number (OPT-…-<GUID>) in the list, detail or result', async () => {
    const reworkTaskId = 'OPT-0000-0190a7c2d4e84b6f9a1b2c3d4e5f6a7b'
    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        operationTaskId: reworkTaskId,
        workOrderId: 'WO-2026-0001-R01',
        workOrderNo: 'MO-2026-0001-R01',
        operationTaskNo: reworkTaskId,
        operationSequence: 10,
        workOrderType: 'rework',
        sourceWorkOrderId: 'WO-2026-0001',
        sourceNcrId: 'ncr-001',
        sourceNcrCode: 'NCR-2026-0001',
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })
    expect(wrapper.text()).toContain('返工 · WO-2026-0001-R01 · 工序 10')

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wrapper.text()).toContain('WO-2026-0001-R01 · 工序 10')
    for (const text of [wrapper.text(), document.body.textContent ?? '']) {
      expect(text).not.toContain('0190a7c2d4e84b6f9a1b2c3d4e5f6a7b')
      expect(text).not.toContain('OPT-')
    }
  })

  it('shows 设备信息未提供 instead of a raw device id when the device has no readable name or code', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[1],
        operationCode: 'OP-STANDARD-20',
        deviceAssetId: 'device-asset-internal-missing',
        allowedActions: [],
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('OP-STANDARD-20')
    expect(document.body.textContent).toContain('设备信息未提供')
    expect(document.body.textContent).not.toContain('device-asset-internal-missing')
  })

  it('renders lifecycle buttons only from server allowedActions instead of local status guesses', async () => {
    operationTasksRef.value = [
      {
        ...defaultTasks[0],
        status: 'InProgress',
        allowedActions: ['resume'],
      },
    ]
    const wrapper = mount(OperationPage, { attachTo: document.body })

    await wrapper.get('[data-row]').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[data-testid="action-resume"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="action-pause"]')).toBeNull()
    expect(document.body.querySelector('[data-testid="action-complete"]')).toBeNull()
  })

  it('completes a task only after explicit confirmation, calling completeTask with the exact pair', async () => {
    const wrapper = mount(OperationPage, { attachTo: document.body })
    const rows = wrapper.findAll('[data-row]')
    await rows[0].trigger('click')
    await flushPromises()

    // 点"完成"——先进入二次确认，不立即调用
    const completeBtn = document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!
    completeBtn.click()
    await flushPromises()
    expect(completeTask).not.toHaveBeenCalled()

    // 确认后才调用 —— 携带稳定逐操作幂等键
    const confirmBtn = document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!
    confirmBtn.click()
    await flushPromises()
    expect(completeTask).toHaveBeenCalledWith(
      'WO-2026-0001',
      'OP-1',
      expect.objectContaining({ idempotencyKey: expect.any(String) }),
    )
    expect(completeTask.mock.calls[0][2].idempotencyKey).toBeTruthy()

    // 成功后显示 Result 成功文案
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('工序已完成')
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).not.toContain('MO-2026-0001')
    expect(routeGuardState.guard?.()).toBe(true)
    expect(dispatchBeforeUnload().defaultPrevented).toBe(false)
    wrapper.unmount()
  })

  it('presents readable predecessor sequences from a rejected complete command without raw task IDs', async () => {
    completeTask.mockRejectedValueOnce(new Error('前序工序尚未完成：工序 10、工序 20 等 4 道。'))
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
    await flushPromises()

    expect(wrapper.text()).toContain('前序工序尚未完成：工序 10、工序 20 等 4 道。')
    expect(wrapper.text()).not.toContain('OP-10')
    expect(wrapper.text()).not.toContain('OP-20')
  })
})
