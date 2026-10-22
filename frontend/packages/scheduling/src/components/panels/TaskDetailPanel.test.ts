import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { ScheduleTask } from '../../model/types'
import TaskDetailPanel from './TaskDetailPanel.vue'

function task(materialReadyUtc?: string | null): ScheduleTask {
  return {
    id: 'assignment-001',
    orderId: 'WO-001',
    operationId: 'OP-10',
    operationSequence: 10,
    type: 'operation',
    text: '精加工',
    startUtc: '2026-09-29T08:00:00.000Z',
    endUtc: '2026-09-29T10:00:00.000Z',
    locked: false,
    hasConflict: false,
    materialRisk: {
      orderId: 'WO-001',
      operationId: 'OP-10',
      reasonCodes: ['material-shortage'],
      shortages: [],
      message: '物料未齐套。已按计划排入，需在开工前完成备料。',
      materialReadyUtc,
    },
  }
}

describe('TaskDetailPanel 物料预计到料', () => {
  it('有 ETA 时显示预计到料 MM-DD', () => {
    const wrapper = mount(TaskDetailPanel, {
      props: { task: task('2026-09-28T08:00:00.000Z'), readOnly: true },
    })

    expect(wrapper.find('[data-testid="task-material-risk"]').text()).toContain('预计到料 09-28')
  })

  it('无 ETA 时保留原风险说明且不伪造日期', () => {
    const wrapper = mount(TaskDetailPanel, { props: { task: task(null), readOnly: true } })
    const risk = wrapper.find('[data-testid="task-material-risk"]')

    expect(risk.text()).toContain('需在开工前完成备料')
    expect(risk.text()).not.toContain('预计到料')
  })
})

it('shows two real segments and excludes the overnight gap from work hours (#4004)', () => {
  const operation = task()
  const wrapper = mount(TaskDetailPanel, {
    props: {
      task: {
        ...operation,
        endUtc: '2026-09-30T10:00:00.000Z',
        segments: [
          { startUtc: operation.startUtc, endUtc: operation.endUtc },
          { startUtc: '2026-09-30T08:00:00.000Z', endUtc: '2026-09-30T10:00:00.000Z' },
        ],
      },
      readOnly: true,
    },
  })
  expect(wrapper.text()).toContain('4 小时')
  expect(wrapper.text()).toContain('第 1 段')
  expect(wrapper.text()).toContain('第 2 段')
})

it('explains downtime risk without replacing conflict or moving the operation', () => {
  const operation = { ...task(), downtimeRisk: '设备停机影响此工序；选定候选前保持原排程。' }
  const wrapper = mount(TaskDetailPanel, { props: { task: operation, readOnly: true } })
  expect(wrapper.text()).toContain('设备停机影响此工序')
  expect(wrapper.text()).toContain('选定候选前保持原排程')
})

// #4405 r1 / #4449：详情与表格共享对应工厂时间；只读不能出现锁定编辑。
it.each([
  ['Asia/Shanghai', '16:00', '18:00'],
  ['UTC', '08:00', '10:00'],
])('renders factory timezone %s in readonly operation details', (timezone, start, end) => {
  const wrapper = mount(TaskDetailPanel, {
    props: {
      task: { ...task(), timezone, factoryTimeLabel: `宁波工厂 · ${timezone}` },
      readOnly: true,
    },
  })
  expect(wrapper.text()).toContain(`宁波工厂 · ${timezone}`)
  expect(wrapper.text()).toContain(start)
  expect(wrapper.text()).toContain(end)
  expect(wrapper.find('button').exists()).toBe(false)
  wrapper.unmount()
})
