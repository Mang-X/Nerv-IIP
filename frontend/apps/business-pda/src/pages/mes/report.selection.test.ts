import {
  route,
  replace,
  recordReport,
  refreshWorkOrders,
  refreshExactTask,
  workOrdersErrorRef,
  tasksErrorRef,
  tasksPendingRef,
  reportScopeMessageRef,
  reportScopeReadyRef,
  contextGenerationRef,
  reportableTasksPendingRef,
  reportableTasksErrorRef,
  reportableTasksReadyRef,
  reportScopeRef,
  reportContextRef,
  operationTaskDiscoveryCalls,
  workOrderFilters,
  defaultWorkOrders,
  workOrdersRef,
  defaultOperationTasks,
  operationTasksRef,
  reportableTasksOverrideRef,
  workOrderDetailRef,
  workOrderDetailPendingRef,
  workOrderDetailErrorRef,
  workOrderDetailHasSuccessfulResponseRef,
  workOrderDetailHasFailedResponseRef,
  refreshWorkOrderDetail,
  exactTaskRef,
  exactTaskScopeReadyRef,
  exactTaskScopeMessageRef,
  materialLotsRef,
  workOrderListScopeReadyRef,
  workOrderListScopeUnavailableRef,
  workOrderListScopeMessageRef,
  selectWorkOrder,
  resetFixture,
} from './report.fixture'
import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ReportPage from './report.vue'

describe('PDA MES production reporting page', () => {
  beforeEach(resetFixture)

  it('starts on the select-work-order step listing work orders', () => {
    const wrapper = mount(ReportPage)
    // ScanBar 可见用于扫工单
    expect(wrapper.find('input[placeholder^="扫"]').exists()).toBe(true)
    // 工单号可读呈现
    expect(wrapper.text()).toContain('WO-2026-0001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    // 尚未到选工序，列表里不应出现工序序号
    expect(wrapper.text()).not.toContain('工序 10')
  })

  it('marks rework work orders and tasks from server authority while standard rows stay unchanged', async () => {
    const reworkAuthority = {
      workOrderType: 'rework',
      sourceWorkOrderId: 'WO-SOURCE-001',
      sourceNcrId: 'ncr-001',
      sourceNcrCode: 'NCR-2026-0001',
    }
    workOrdersRef.value = [{ ...defaultWorkOrders[0], ...reworkAuthority }, defaultWorkOrders[1]]
    operationTasksRef.value = defaultOperationTasks.map((task) =>
      task.workOrderId === defaultWorkOrders[0].workOrderId
        ? { ...task, ...reworkAuthority }
        : task,
    )
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      ...reworkAuthority,
      operationTasks: operationTasksRef.value.filter(
        (task) => task.workOrderId === defaultWorkOrders[0].workOrderId,
      ),
    }
    const wrapper = mount(ReportPage)

    expect(wrapper.text()).toContain('返工 · WO-2026-0001')
    expect(wrapper.text()).toContain('来源 NCR NCR-2026-0001 · 源工单 WO-SOURCE-001')
    expect(wrapper.text()).toContain('WO-2026-0002')
    expect(wrapper.text()).not.toContain('返工 · WO-2026-0002')

    await selectWorkOrder(wrapper, 0)
    expect(wrapper.text()).toContain('返工 · WO-2026-0001 · 工序 10')
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    await flushPromises()
    expect(
      document.body.querySelector('[data-testid="report-rework-source"]')?.textContent?.trim(),
    ).toBe('来源 NCR NCR-2026-0001 · 源工单 WO-SOURCE-001')
  })

  it('fails closed when a deep-linked rework work order omits source authority', async () => {
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      workOrderType: 'rework',
      sourceWorkOrderId: null,
      sourceNcrId: null,
      sourceNcrCode: null,
      operationTasks: [{ ...defaultOperationTasks[0], workOrderType: 'rework' }],
    }
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('返工来源信息不完整')
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('shows the missing-scope reason and keeps production reporting disabled', async () => {
    reportScopeReadyRef.value = false
    reportScopeMessageRef.value = '尚未选择已授权作业范围，当前操作已禁用。'
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-scope-message"]').text()).toContain(
      '尚未选择已授权作业范围',
    )
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    const submit = document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!
    expect(submit.disabled).toBe(true)
    submit.click()
    await flushPromises()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it.each([
    ['未选择', '尚未选择已授权作业范围，当前操作已禁用。'],
    ['核验失败', '作业范围核验失败，当前操作已禁用。请刷新后重试。'],
  ])(
    'shows the reporting-read scope %s reason instead of claiming the exact task is absent',
    async (_state, scopeMessage) => {
      workOrderDetailRef.value = {
        ...defaultWorkOrders[0],
        operationTasks: [],
      }
      exactTaskRef.value = null
      exactTaskScopeReadyRef.value = false
      exactTaskScopeMessageRef.value = scopeMessage
      route.query = {
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-READ-SCOPE',
      }

      const wrapper = mount(ReportPage)
      await flushPromises()

      const issue = wrapper.get('[data-testid="report-route-issue"]').text()
      expect(issue).toContain(scopeMessage)
      expect(issue).not.toContain('未找到工单')
      expect(issue).not.toContain('未找到工单 WO-2026-0001 下的工序任务')
      expect(recordReport).not.toHaveBeenCalled()
      expect(refreshExactTask).not.toHaveBeenCalled()
    },
  )

  it('tells the operator in business terms when the account has no work-order scope', () => {
    workOrdersRef.value = []
    workOrderListScopeReadyRef.value = false
    workOrderListScopeUnavailableRef.value = true
    workOrderListScopeMessageRef.value = '请联系管理员在 IAM 为该账号配置数据范围'

    const wrapper = mount(ReportPage)

    const notice = wrapper.get('[data-testid="work-order-scope-notice"]').text()
    expect(notice).toBe('当前账号没有可查看的工单范围，请联系管理员开通。')
    expect(wrapper.text()).not.toContain('IAM')
  })

  it('shows the business empty state for a work order without operations', async () => {
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: [],
    }
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0)

    expect(wrapper.text()).toContain('该工单暂无工序')
  })

  it('detail failure blocks route identity and shows a retry action', async () => {
    route.query = { workOrderId: 'WO-2026-0001' }
    workOrderDetailRef.value = null
    workOrderDetailErrorRef.value = new Error('工单详情查询失败')
    workOrderDetailHasSuccessfulResponseRef.value = false
    workOrderDetailHasFailedResponseRef.value = true

    const wrapper = mount(ReportPage)
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('详情加载失败')
    expect(wrapper.get('[data-testid="work-order-detail-error"]').text()).toContain(
      '工单详情查询失败',
    )
    expect(wrapper.text()).not.toContain('未找到工单 WO-2026-0001')
    expect(wrapper.text()).not.toContain('该工单暂无工序')

    await wrapper.get('[data-testid="work-order-detail-error"] button').trigger('click')
    expect(refreshWorkOrderDetail).toHaveBeenCalledTimes(1)
  })

  it('uses the resolved work-order strong id as the exact report route', async () => {
    const wrapper = mount(ReportPage)
    await wrapper.getComponent({ name: 'MesScanPrevalidation' }).vm.$emit('accepted', {
      kind: 'work-order',
      candidate: {},
      workOrderId: 'WO-2026-0002',
    })
    await flushPromises()
    expect(workOrderFilters.workOrderId).toBe('WO-2026-0002')
    expect(workOrderFilters.keyword).toBeUndefined()
    expect(replace).toHaveBeenCalledWith({
      path: '/mes/report',
      query: { workOrderId: 'WO-2026-0002' },
    })
  })

  it('applies accepted material, device, and personnel scans to the selected report context', async () => {
    materialLotsRef.value = [
      {
        requestId: 'MIR-PDA-001',
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-1',
        materialId: 'MAT-OIL',
        uomCode: 'L',
        materialLotId: 'LOT-OIL-001',
        requestedQuantity: 5,
        receivedQuantity: 5,
        consumedQuantity: 1,
        status: 'received',
      },
    ]
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const scanner = wrapper
      .findAllComponents({ name: 'MesScanPrevalidation' })
      .find((item) => (item.props('acceptedKinds') as string[]).includes('material'))!

    await scanner.vm.$emit('accepted', {
      kind: 'material',
      candidate: {},
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      materialIssueRequestId: 'MIR-PDA-001',
    })
    await scanner.vm.$emit('accepted', {
      kind: 'device',
      candidate: {},
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      scannedObjectId: 'DEVICE-1',
    })
    await scanner.vm.$emit('accepted', {
      kind: 'personnel',
      candidate: {},
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      scannedObjectId: 'USER-1',
    })
    await flushPromises()

    expect(
      document.body.querySelector<HTMLInputElement>('[data-testid="material-lot-MIR-PDA-001"]')!
        .checked,
    ).toBe(true)
    expect(document.body.querySelector('[data-testid="report-validated-device"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="report-validated-personnel"]')).not.toBeNull()
    wrapper.unmount()
  })

  it('shows detail operations after a work order is selected without list discovery', async () => {
    const wrapper = mount(ReportPage)
    await selectWorkOrder(wrapper, 0)
    // 选工序步出现工序序号
    expect(wrapper.text()).toContain('工序 10')
    expect(wrapper.text()).toContain('工序 20')
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it('detail 已成功时立即展示任务且 report 页面不创建旧 operation-task list discovery', async () => {
    tasksPendingRef.value = true
    tasksErrorRef.value = new Error('non-authority list failed')
    route.query = { workOrderId: 'WO-2026-0001' }
    const wrapper = mount(ReportPage)
    await flushPromises()

    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).not.toContain('加载工序失败')
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it('切换工单后旧工序行不能打开或提交', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
    const staleTaskRow = wrapper
      .findAll('[data-row]')
      .find((row) => row.text().includes('WO-2026-0001 · 工序 10'))
    expect(staleTaskRow, '切换前必须真实存在旧工单任务行').toBeDefined()

    await wrapper.get('[data-testid="change-work-order"]').trigger('click')
    await selectWorkOrder(wrapper, 1)
    expect(wrapper.text()).toContain('当前工单')
    expect(wrapper.text()).toContain('WO-2026-0002')

    // 模拟 reactive query 已切 key、但旧 key 的任务行在新请求期间仍留在 data 中。
    await staleTaskRow!.trigger('click')
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')
    expect(goodInput).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('可从同时包含工单和工序任务 ID 的 URL 直达同一报工实体', async () => {
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.text()).toContain('当前工单')
    expect(wrapper.text()).toContain('WO-2026-0002')
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('快速切换时隐藏 pending/乱序任务，只绑定最新完成的 query pair', async () => {
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()

    workOrderDetailPendingRef.value = true
    reportableTasksPendingRef.value = true
    reportableTasksReadyRef.value = false
    reportableTasksOverrideRef.value = []
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()

    expect(wrapper.text()).not.toContain('WO-2026-0001 · 工序 10')
    expect(wrapper.text()).not.toContain('该工单暂无工序')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 旧请求迟到时仍处于新 key 的 pending 阶段，不得重新启用任何实体。
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: [defaultOperationTasks[0]],
    }
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    // 只有新 key 的响应完成后，才绑定 URL 请求的精确 pair。
    workOrderDetailRef.value = {
      ...defaultWorkOrders[1],
      operationTasks: [defaultOperationTasks[2]],
    }
    workOrderDetailPendingRef.value = false
    reportableTasksPendingRef.value = false
    reportableTasksReadyRef.value = true
    reportableTasksOverrideRef.value = [defaultOperationTasks[2]]
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('第 101+ 工单及其第 101+ 工序任务可从 URL 直达，不依赖首屏 bounded list', async () => {
    const targetWorkOrder = {
      workOrderId: 'WO-OUTSIDE-101',
      skuId: 'SKU-OUTSIDE',
      quantity: 1,
      status: 'Released',
    }
    const targetTask = {
      operationTaskId: 'OP-OUTSIDE-101',
      workOrderId: targetWorkOrder.workOrderId,
      status: 'Ready',
      allowedActions: ['report'],
      operationSequence: 1010,
      workCenterId: 'WC-OUTSIDE',
    }
    workOrdersRef.value = Array.from({ length: 100 }, (_, index) => ({
      workOrderId: `WO-FIRST-${index + 1}`,
      skuId: `SKU-${index + 1}`,
      quantity: 1,
      status: 'Released',
    }))
    operationTasksRef.value = Array.from({ length: 100 }, (_, index) => ({
      operationTaskId: `OP-FIRST-${index + 1}`,
      workOrderId: targetWorkOrder.workOrderId,
      status: 'Ready',
      operationSequence: index + 1,
      workCenterId: 'WC-FIRST',
    }))
    workOrderDetailRef.value = {
      ...targetWorkOrder,
      operationTasks: [...operationTasksRef.value, targetTask],
    }
    reportableTasksOverrideRef.value = [targetTask]
    route.query = {
      workOrderId: targetWorkOrder.workOrderId,
      operationTaskId: targetTask.operationTaskId,
    }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(wrapper.text()).toContain(targetWorkOrder.workOrderId)
    expect(document.body.textContent).toContain('工序 1010')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('完整 pair 的第 501+ 工序任务通过窄 exact resolver 直达', async () => {
    const workOrderId = 'WO-MANY-TASKS'
    const operationTaskId = 'OP-501'
    workOrderDetailRef.value = {
      workOrderId,
      skuId: 'SKU-MANY',
      quantity: 1,
      status: 'Released',
      operationTasks: Array.from({ length: 500 }, (_, index) => ({
        operationTaskId: `OP-${index + 1}`,
        workOrderId,
        status: 'Ready',
        operationSequence: index + 1,
        workCenterId: 'WC-MANY',
      })),
    }
    exactTaskRef.value = {
      operationTaskId,
      workOrderId,
      status: 'Ready',
      allowedActions: ['report'],
      operationSequence: 501,
      workCenterId: 'WC-MANY',
    }
    reportableTasksOverrideRef.value = [exactTaskRef.value]
    route.query = { workOrderId, operationTaskId }

    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.textContent).toContain('工序 501')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
  })

  it('工单 detail 网络或授权失败时显示加载失败并 fail closed，不误报不存在', async () => {
    workOrderDetailErrorRef.value = new Error('403')
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('详情加载失败')
    expect(wrapper.text()).not.toContain('未找到工单')
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('同一路由 query 变化及浏览器前进后退会重新绑定 pair 并清空数量', async () => {
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const firstGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    firstGood.value = '7'
    firstGood.dispatchEvent(new Event('input'))
    await flushPromises()

    // 模拟同 route 的 query push / 浏览器 forward。
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0002 · 工序 10')
    expect(
      document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!.value,
    ).toBe('0')

    // 模拟浏览器 back。
    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    await flushPromises()
    expect(document.body.textContent).toContain('WO-2026-0001 · 工序 10')
    expect(document.body.textContent).not.toContain('WO-2026-0002 · 工序 10')
    expect(
      document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!.value,
    ).toBe('0')
    expect(recordReport).not.toHaveBeenCalled()
    expect(operationTaskDiscoveryCalls).toBe(0)
  })

  it.each([
    ['缺少工单 ID', { operationTaskId: 'OP-1' }, '报工链接缺少工单'],
    [
      '工单不存在',
      { workOrderId: 'WO-MISSING', operationTaskId: 'OP-404' },
      '未找到工单 WO-MISSING',
    ],
    [
      '工序任务不存在',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-MISSING' },
      '未在工单 WO-2026-0002 下找到链接中的工序',
    ],
    [
      'pair 不匹配',
      { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-1' },
      '未在工单 WO-2026-0002 下找到链接中的工序',
    ],
  ])('%s 时显示明确安全状态且不写入', async (_name, query, message) => {
    route.query = query
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain(message)
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('rejects a deep-linked completed task when server allowedActions omits report', async () => {
    operationTasksRef.value = [
      {
        operationTaskId: 'OP-DONE',
        workOrderId: 'WO-2026-0001',
        status: 'Completed',
        operationSequence: 10,
        allowedActions: [],
      },
    ]
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: operationTasksRef.value,
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-DONE' }
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('当前不可报工')
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('names a non-reportable rework task by sequence and never shows its OPT-…-<GUID> number', async () => {
    const reworkTaskId = 'OPT-0000-0190a7c2d4e84b6f9a1b2c3d4e5f6a7b'
    operationTasksRef.value = [
      {
        operationTaskId: reworkTaskId,
        operationTaskNo: reworkTaskId,
        workOrderId: 'WO-2026-0001',
        status: 'Completed',
        operationSequence: 10,
        allowedActions: [],
      },
    ]
    workOrderDetailRef.value = {
      ...defaultWorkOrders[0],
      operationTasks: operationTasksRef.value,
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: reworkTaskId }
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain(
      '工序 10 当前不可报工',
    )
    expect(document.body.textContent).not.toContain('0190a7c2d4e84b6f9a1b2c3d4e5f6a7b')
    expect(document.body.textContent).not.toContain('OPT-')
    expect(recordReport).not.toHaveBeenCalled()
  })

  it('hides a task outside the reporting-write work-center scope', async () => {
    reportScopeRef.value = { kind: 'work-center', id: 'WC-A' }
    reportContextRef.value = {
      ...reportContextRef.value,
      scopeKind: 'work-center',
      scopeId: 'WC-A',
    }
    reportableTasksOverrideRef.value = [defaultOperationTasks[0]]
    route.query = { workOrderId: 'WO-2026-0001' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.text()).toContain('工序 10')
    expect(wrapper.text()).not.toContain('工序 20')
    expect(wrapper.findAll('[data-row]')).toHaveLength(1)
    wrapper.unmount()
  })

  it('workshop scope only exposes tasks returned by the server reportable authority set', async () => {
    reportScopeRef.value = { kind: 'workshop', id: 'WS-1' }
    reportContextRef.value = {
      ...reportContextRef.value,
      scopeKind: 'workshop',
      scopeId: 'WS-1',
    }
    reportableTasksOverrideRef.value = [defaultOperationTasks[0]]
    route.query = { workOrderId: 'WO-2026-0001' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.text()).toContain('工序 10')
    expect(wrapper.text()).not.toContain('工序 20')
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()

    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-2' }
    await flushPromises()
    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('当前不可报工')
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()
    wrapper.unmount()
  })

  it('scope authority pending/error/empty are fail-closed and distinguishable', async () => {
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    reportableTasksPendingRef.value = true
    reportableTasksReadyRef.value = false
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()

    reportableTasksPendingRef.value = false
    reportableTasksErrorRef.value = new Error('authority unavailable')
    await flushPromises()
    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('可报工工序读取失败')

    reportableTasksErrorRef.value = null
    reportableTasksReadyRef.value = true
    reportableTasksOverrideRef.value = []
    await flushPromises()
    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain('当前不可报工')
    wrapper.unmount()
  })

  it('scope switch keeps a late old authority set disabled until the new set is ready', async () => {
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()

    contextGenerationRef.value = 2
    reportContextRef.value = {
      ...reportContextRef.value,
      scopeKind: 'work-center',
      scopeId: 'WC-B',
      generation: 2,
    }
    reportableTasksPendingRef.value = true
    reportableTasksReadyRef.value = false
    reportableTasksOverrideRef.value = [defaultOperationTasks[0]]
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).toBeNull()

    reportableTasksOverrideRef.value = [defaultOperationTasks[1]]
    reportableTasksPendingRef.value = false
    reportableTasksReadyRef.value = true
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-2' }
    await flushPromises()
    expect(document.body.querySelector('[data-testid="good-quantity"]')).not.toBeNull()
    wrapper.unmount()
  })

  it('fails closed when task rework authority does not match its parent work order', async () => {
    const parent = {
      ...defaultWorkOrders[0],
      workOrderType: 'rework',
      sourceWorkOrderId: 'WO-SOURCE-A',
      sourceNcrId: 'NCR-A',
      sourceNcrCode: 'NCR-A-CODE',
    }
    const mismatchedTask = {
      ...defaultOperationTasks[0],
      workOrderType: 'rework',
      sourceWorkOrderId: 'WO-SOURCE-B',
      sourceNcrId: 'NCR-B',
      sourceNcrCode: 'NCR-B-CODE',
    }
    workOrderDetailRef.value = { ...parent, operationTasks: [mismatchedTask] }
    operationTasksRef.value = [mismatchedTask]
    route.query = {
      workOrderId: parent.workOrderId,
      operationTaskId: mismatchedTask.operationTaskId,
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    expect(wrapper.get('[data-testid="report-route-issue"]').text()).toContain(
      '返工来源与工单不一致',
    )
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()
    wrapper.unmount()
  })

  it('工单列表超时：显示可操作错误文案 + 重试调 refresh（GET 安全）', async () => {
    workOrdersErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(ReportPage)
    const banner = wrapper.get('[data-testid="work-orders-error"]')
    expect(banner.text()).toContain('网络超时，请检查连接后重试')
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(refreshWorkOrders).toHaveBeenCalledTimes(1)
  })

  // P2：加载失败时空态与错误态互斥，不把网络错误误报成"暂无"。
  it('工单加载失败时不显示"暂无可报工的工单"空态', () => {
    workOrdersRef.value = []
    workOrdersErrorRef.value = new RequestTimeoutError()
    const wrapper = mount(ReportPage)
    expect(wrapper.find('[data-testid="work-orders-error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('暂无可报工的工单')
  })
})
