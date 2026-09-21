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
