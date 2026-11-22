import { mesState, routerState, mountReports } from './production-reports.fixture'
import { flushPromises } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

describe('production reports page — 遥测报工候选转正', () => {
  const candidate = {
    candidateId: 'candidate-1',
    deviceAssetId: 'DEV-001',
    tagKey: '产量计数',
    goodQuantity: 8,
    bucketEndUtc: '2026-08-01T08:00:00Z',
    status: 'pending-confirmation',
    workOrderId: 'WO-1',
    operationTaskId: '019fbb41-0010-7000-8000-000000000010',
  }
  const workOrders = [
    {
      workOrderId: 'WO-1',
      workOrderNo: 'WO-1',
      operationTasks: [
        {
          operationTaskId: '019fbb41-0020-7000-8000-000000000020',
          operationTaskNo: 'WO-1-OP-20',
          operationSequence: 20,
        },
        {
          operationTaskId: '019fbb41-0010-7000-8000-000000000010',
          operationTaskNo: 'WO-1-OP-10',
          operationSequence: 10,
        },
      ],
    },
    {
      workOrderId: 'WO-2',
      workOrderNo: 'WO-2',
      operationTasks: [
        {
          operationTaskId: '019fbb41-0210-7000-8000-000000000210',
          operationTaskNo: 'WO-2-OP-10',
          operationSequence: 10,
        },
      ],
    },
  ]

  async function openCandidate() {
    mesState.candidateRows = [candidate]
    mesState.workOrderRows.value = workOrders
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '处理')!
      .trigger('click')
    return wrapper
  }
  const promoteButton = (wrapper: Awaited<ReturnType<typeof openCandidate>>) =>
    wrapper.findAll('button').find((b) => b.text() === '确认并转正')!

  it('默认选中候选自带的工单与工序任务，转正提交的仍是这两个标识', async () => {
    const wrapper = await openCandidate()
    const taskPicker = wrapper.get('select[aria-label="工序任务"]')

    expect((wrapper.get('select[aria-label="工单"]').element as HTMLSelectElement).value).toBe(
      'WO-1',
    )
    expect(taskPicker.findAll('option').map((o) => o.text())).toEqual([
      '',
      'WO-1-OP-10',
      'WO-1-OP-20',
    ])
    await promoteButton(wrapper).trigger('click')

    expect(mesState.promoteCandidate).toHaveBeenCalledWith(
      'candidate-1',
      'WO-1',
      '019fbb41-0010-7000-8000-000000000010',
    )
  })

  it('工单搜索结果里没有已选工单时，仍保留它的工序候选与已选工序', async () => {
    const wrapper = await openCandidate()

    // 模拟在工单搜索框改搜别的单号：服务端结果里不再有 WO-1。
    mesState.workOrderRows.value = [workOrders[1]!]
    await flushPromises()
    const taskPicker = wrapper.get('select[aria-label="工序任务"]')
    expect((taskPicker.element as HTMLSelectElement).value).toBe(
      '019fbb41-0010-7000-8000-000000000010',
    )
    expect(taskPicker.findAll('option').map((o) => o.text())).toEqual([
      '',
      'WO-1-OP-10',
      'WO-1-OP-20',
    ])
  })

  it('改选工单后清掉上一张工单的工序，只能从新工单的工序里挑', async () => {
    const wrapper = await openCandidate()

    await wrapper.get('select[aria-label="工单"]').setValue('WO-2')
    const taskPicker = wrapper.get('select[aria-label="工序任务"]')
    expect((taskPicker.element as HTMLSelectElement).value).toBe('')
    expect(taskPicker.findAll('option').map((o) => o.text())).toEqual(['', 'WO-2-OP-10'])
    expect(promoteButton(wrapper).attributes('disabled')).toBeDefined()

    await taskPicker.setValue('019fbb41-0210-7000-8000-000000000210')
    await promoteButton(wrapper).trigger('click')

    expect(mesState.promoteCandidate).toHaveBeenCalledWith(
      'candidate-1',
      'WO-2',
      '019fbb41-0210-7000-8000-000000000210',
    )
  })
})

describe('追溯报工深链', () => {
  it.each(['单值', '数组'] as const)(
    '仅读用户按分页外 ReportNo 自动打开详情并保留返回查询（%s）',
    async (queryKind) => {
      const returnTo = '/mes/traceability?mode=batch&batchOrSerial=LOT-009999&workOrderId=WO-9'
      routerState.query = {
        reportNo: 'PRPT-009999',
        sourceNodeType: 'ProductionReport',
        sourceNodeId: 'PRPT-009999',
        traceabilityReturn: queryKind === '数组' ? [returnTo, '/mes/work-orders'] : returnTo,
      }
      const wrapper = mountReports(['business.mes.reporting.read'])
      await flushPromises()
      expect(wrapper.get('[role="dialog"]').text()).toContain('报工详情 · PRPT-009999')
      expect(wrapper.get('[role="dialog"]').text()).toContain('返回追溯')
      expect(wrapper.get('[role="dialog"] a').attributes('href')).toBe(returnTo)
      expect(wrapper.get('[role="dialog"]').text()).toContain('12')
      expect(
        wrapper.findAll('[data-testid="row"]').some((row) => row.text().includes('PRPT-009999')),
      ).toBe(false)
      wrapper.unmount()
    },
  )

  it('来源产出批次与报工详情不一致时页面解释并停止呈现事实', async () => {
    routerState.query = {
      reportNo: 'PRPT-009999',
      sourceNodeType: 'ProducedLot',
      sourceNodeId: 'LOT-20261014-000001',
      traceabilityReturn: '/mes/traceability?mode=batch&batchOrSerial=LOT-20261014-000001',
    }
    // 公开详情 fixture 返回同一报工单号和良品 12，但没有来源节点所指的产出批次。
    const wrapper = mountReports(['business.mes.reporting.read'])
    try {
      await flushPromises()
      const dialog = wrapper.get('[role="dialog"]')
      expect(dialog.text()).toContain('报工详情 · PRPT-009999')
      expect(dialog.text()).toContain('来源节点与报工产出事实不匹配')
      expect(dialog.text()).not.toContain('良品')
      expect(dialog.find('[aria-label="产出事实"]').exists()).toBe(false)
    } finally {
      wrapper.unmount()
    }
  })
})
