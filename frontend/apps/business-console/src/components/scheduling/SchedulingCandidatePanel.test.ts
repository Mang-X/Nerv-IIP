import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { SchedulingCandidateSet } from '@nerv-iip/api-client'
import SchedulingCandidatePanel from './SchedulingCandidatePanel.vue'

export const candidates: SchedulingCandidateSet = {
  baselinePlanId: 'plan-production',
  asOfUtc: '2026-10-08T00:00:00Z',
  inputFingerprint: 'current-input',
  candidates: [
    {
      strategy: 'rightShift',
      inputFingerprint: 'current-input',
      plan: {
        status: 'preview',
        unscheduledOperations: [
          {
            orderId: 'WO-260108-004',
            operationId: 'OP-30',
            reasonCode: 'outsideHorizon',
            message: '排程窗口不足',
          },
        ],
      },
      kpis: {
        baselineOnTimeRate: 0.8,
        candidateOnTimeRate: 0.6,
        onTimeRateChange: -0.2,
        baselineOnTimeDenominator: 10,
        candidateOnTimeDenominator: 10,
        baselineLateOrderCount: 1,
        candidateLateOrderCount: 2,
        lateOrderCountChange: 1,
        movedOperationCount: 2,
        baselineResourceUtilization: 0.7,
        candidateResourceUtilization: 0.75,
        resourceUtilizationChange: 0.05,
        baselineUnscheduledCount: 0,
        candidateUnscheduledCount: 1,
        unscheduledCountChange: 1,
        preservedLockedCount: 1,
        totalLockedCount: 1,
        lockedAssignments: [],
      },
      movements: [
        {
          original: {
            orderId: 'WO-260108-001',
            operationId: 'OP-10',
            resourceId: 'CNC-01',
            startUtc: '2026-10-08T00:00:00Z',
            endUtc: '2026-10-08T01:00:00Z',
          },
          candidate: {
            orderId: 'WO-260108-001',
            operationId: 'OP-10',
            resourceId: 'CNC-01',
            startUtc: '2026-10-08T01:00:00Z',
            endUtc: '2026-10-08T02:00:00Z',
          },
          reasons: [
            {
              code: 'resourceUnavailable',
              source: {
                sourceReference: 'CMMS-260108-017',
                reasonCode: 'equipment.downtime',
                occurredAtUtc: '2026-10-08T00:00:00Z',
              },
            },
          ],
          paths: [
            {
              root: { orderId: 'WO-260108-001', operationId: 'OP-10' },
              steps: [
                {
                  code: 'predecessorDependency',
                  from: { orderId: 'WO-260108-001', operationId: 'OP-10' },
                  to: { orderId: 'WO-260108-001', operationId: 'OP-20' },
                },
              ],
            },
          ],
        },
      ],
      explanations: [
        {
          orderId: 'WO-260108-002',
          operationId: 'OP-10',
          code: 'frozen-conflict',
          reasons: [],
          paths: [],
        },
      ],
      transfers: [],
    },
  ],
}

describe('局部候选页内比较', () => {
  it('展示六项后端KPI、分母、移动原因与传播路径，选择和重预览保持独立', async () => {
    const wrapper = mount(SchedulingCandidatePanel, {
      props: { candidates, canManage: true, baselinePlanId: 'plan-production' },
    })
    expect(wrapper.text()).toContain('可优化已排工序准交率')
    expect(wrapper.text()).toContain('分母 10 → 10')
    expect(wrapper.text()).toContain('延期订单')
    expect(wrapper.text()).toContain('移动工序')
    expect(wrapper.text()).toContain('资源利用率')
    expect(wrapper.text()).toContain('未排工序')
    expect(wrapper.text()).toContain('锁定保持')
    const metrics = wrapper.findAll('dl > div')
    expect(metrics[0]!.text()).toContain('80.0% → 60.0%')
    expect(metrics[0]!.text()).toContain('-20.0 个百分点')
    expect(metrics[1]!.text()).toContain('1 → 2')
    expect(metrics[1]!.text()).toContain('变化 +1 单')
    expect(metrics[2]!.text()).toContain('2 道')
    expect(metrics[3]!.text()).toContain('70.0% → 75.0%')
    expect(metrics[3]!.text()).toContain('+5.0 个百分点')
    expect(metrics[4]!.text()).toContain('0 → 1')
    expect(metrics[4]!.text()).toContain('变化 +1 道')
    expect(metrics[5]!.text()).toContain('1 / 1 道保持')
    expect(wrapper.text()).toContain('设备停机或维护')
    expect(wrapper.text()).toContain('CMMS-260108-017')
    expect(wrapper.text()).toContain('前序工序影响后续工序')
    expect(wrapper.text()).toContain('冻结工序与当前约束冲突')
    expect(wrapper.text()).toContain('排程窗口不足')
    await wrapper.get('[data-testid="select-candidate"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual([candidates.candidates![0]])
    await wrapper.get('[data-testid="preview-candidates"]').trigger('click')
    expect(wrapper.emitted('preview')).toHaveLength(1)
  })
  it('无管理权限或请求进行中时禁止生成和选定', () => {
    const wrapper = mount(SchedulingCandidatePanel, {
      props: { candidates, canManage: false, baselinePlanId: 'plan-production' },
    })
    expect(wrapper.get('[data-testid="select-candidate"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="preview-candidates"]').attributes('disabled')).toBeDefined()
  })
})
