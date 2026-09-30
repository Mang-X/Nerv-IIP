import { describe, expect, it } from 'vitest'
import { toModel } from './aps-mapper'
import { samplePlan } from './fixtures'
import { taskFactRows } from './task-facts'

describe('排产任务决策事实（#4107 票面）', () => {
  const task = toModel({
    ...samplePlan,
    assignments: [
      {
        ...samplePlan.assignments![0],
        currentExecution: {
          workOrderProgress: { completedQuantity: 25, plannedQuantity: 100 },
          isMaterialReady: false,
          materialReadyUtc: null,
          equipmentState: 'Running',
          isEquipmentSourceFresh: false,
        },
      },
    ],
    validationContext: {
      operations: [
        {
          orderId: 'WO-001',
          operationId: 'op-10',
          dueUtc: samplePlan.assignments![0].endUtc,
          predecessorOperationIds: ['op-05'],
        },
      ],
    },
  }).tasks.find((item) => item.id === 'a1')!
  it('保留工单完成量口径、缺料未知日期、陈旧设备状态和真实前序', () => {
    expect(taskFactRows(task)).toEqual([
      ['工单进度', '25 / 100'],
      ['交期差', '按期'],
      ['当前到料', '缺料 · 未知到料日'],
      ['当前设备', '未知 · 开工前请人工确认设备可用'],
      ['前序', 'WO-001 · op-05'],
      ['后序', '无'],
    ])
  })
  it('修改结束时间后交期差随同一任务变化，改派设备后旧状态不冒充当前设备', () => {
    const changed = {
      ...task,
      endUtc: new Date(Date.parse(task.endUtc) + 60 * 60_000).toISOString(),
      resourceId: 'DEV-NEW',
      currentExecution: {
        ...task.currentExecution,
        equipmentState: 'Running',
        isEquipmentSourceFresh: true,
      },
    }
    expect(taskFactRows(changed)).toContainEqual(['交期差', '延期 60 分钟'])
    expect(taskFactRows(changed)).toContainEqual(['当前设备', '未知 · 开工前请人工确认设备可用'])
  })
  it('缺少执行信息不能呈现设备正常或制造到料日期', () => {
    expect(taskFactRows({ ...task, currentExecution: null })).toContainEqual([
      '当前设备',
      '未知 · 开工前请人工确认设备可用',
    ])
    expect(taskFactRows({ ...task, currentExecution: null })).toContainEqual([
      '当前到料',
      '未知到料日',
    ])
  })
})

it('商业来源只消费 MES 给出的真实关联并保留无来源空值', async () => {
  const { withWorkOrderFacts } = await import('./task-facts')
  const commercialSourceFacts = {
    status: 'available' as const,
    salesOrders: [{ salesOrderNo: 'SO-261001-018', customerCode: 'CUST-018' }],
  }
  const model = withWorkOrderFacts(toModel(samplePlan), [
    { workOrderId: 'WO-001', commercialSourceFacts },
  ])
  const operation = model.tasks.find((task) => task.id === 'a1')!
  expect(taskFactRows(operation)).toContainEqual(['销售订单', 'SO-261001-018'])
  expect(taskFactRows(operation)).toContainEqual(['客户', 'CUST-018'])
  expect(
    withWorkOrderFacts(model, []).tasks.find((task) => task.id === 'a1')?.commercialSourceFacts,
  ).toBeUndefined()
})
