import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { BusinessConsoleSchedulingConflict } from '@nerv-iip/api-client'
import SchedulingPlanConflicts from './SchedulingPlanConflicts.vue'
import { formatDateTime } from '@/utils/format'

// Regression / #4463, PR #4488 review 5557416603：MES 已 Ready 后仍须完整保留、折叠并标明冻结预警。
const warnings: BusinessConsoleSchedulingConflict[] = Array.from({ length: 8 }, (_, index) => [
  {
    conflictId: `material-${index}`,
    severity: 'warning' as const,
    reasonCode: 'material' as const,
    orderId: 'WO-MAIN',
    operationId: `OP-${index + 1}`,
    message: '物料未齐套：11 项缺口',
  },
  {
    conflictId: `equipment-${index}`,
    severity: 'warning' as const,
    reasonCode: 'equipment' as const,
    orderId: 'WO-MAIN',
    operationId: `OP-${index + 1}`,
    resourceId: `WC-${index + 1}`,
    message: '设备尚未接入采集',
  },
]).flat()

describe('方案冻结冲突与不可排原因', () => {
  it('labels the snapshot and groups every repeated warning without dropping operation identities', async () => {
    const generatedAtUtc = '2026-10-17T02:00:00Z'
    const wrapper = mount(SchedulingPlanConflicts, {
      props: { conflicts: warnings, unscheduledOperations: [], generatedAtUtc },
    })
    expect(wrapper.text()).toContain('方案冻结冲突与不可排原因')
    expect(wrapper.text()).toContain('不代表当前 MES 齐套或设备状态')
    expect(wrapper.text()).toContain(formatDateTime(generatedAtUtc))
    const groups = wrapper.findAll('details')
    expect(groups).toHaveLength(2)
    expect(groups[0]!.get('summary').text()).toContain('预警 · 物料约束 · 8 条')
    expect(groups[1]!.get('summary').text()).toContain('预警 · 设备约束 · 8 条')
    for (const group of groups) {
      expect(group.attributes('open')).toBeUndefined()
      await group.get('summary').trigger('click')
      expect(group.findAll('li')).toHaveLength(8)
    }
    for (const warning of warnings) {
      expect(wrapper.text()).toContain(`${warning.orderId} · ${warning.operationId}`)
      expect(wrapper.text()).toContain(warning.message)
    }
    expect(wrapper.text()).toContain('WC-8')
  })

  it('keeps blocking conflicts and unscheduled operations visible beside collapsed non-blocking notices', () => {
    const wrapper = mount(SchedulingPlanConflicts, {
      props: {
        conflicts: [
          ...warnings,
          {
            conflictId: 'blocked',
            severity: 'error',
            reasonCode: 'material',
            message: '硬约束缺料阻断',
            orderId: 'WO-BLOCKED',
            operationId: 'OP-B',
          },
          { severity: 'info', reasonCode: 'material', message: '物料提示' },
        ],
        unscheduledOperations: [
          {
            orderId: 'WO-PENDING',
            operationId: 'OP-P',
            reasonCode: 'predecessorUnscheduled',
            message: '等待前序',
          },
        ],
      },
    })
    const blocking = wrapper.findAll('p').find((p) => p.text().includes('硬约束缺料阻断'))!
    expect(blocking.element.closest('details')).toBeNull()
    expect(blocking.text()).toContain('阻断 · 物料约束')
    const unscheduled = wrapper.findAll('p').find((p) => p.text().includes('等待前序'))!
    expect(unscheduled.element.closest('details')).toBeNull()
    expect(unscheduled.text()).toContain('WO-PENDING · OP-P · 前序未排')
    expect(wrapper.findAll('summary').map((summary) => summary.text())).toContain(
      '提示 · 物料约束 · 1 条（展开明细）',
    )
  })

  it('describes an empty snapshot without asserting current readiness', () => {
    const wrapper = mount(SchedulingPlanConflicts, {
      props: { conflicts: [], unscheduledOperations: [] },
    })
    expect(wrapper.text()).toContain('方案快照未记录冲突或不可排原因')
    expect(wrapper.text()).not.toContain('当前已齐套')
    expect(wrapper.find('details').exists()).toBe(false)
  })
})
