import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPlanGantt from './SchedulingPlanGantt.vue'

vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: () => undefined }),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveWorkCenter: (id: string) => id,
    resolveWorkCenterCategory: () => undefined,
  }),
}))

/**
 * #1399 M7 —— 「切个页签风险 chip 就消失」。
 *
 * 走查报告把根因记在「读面丢弃 risks」（根因 C）上，实机复核已推翻：网关 facade 的
 * SchedulePlanContract 带 materialRisks / equipmentRisks，codegen 也生成了这两个字段，
 * aps-mapper 会按 orderId+operationId 把风险挂回工序。链路是通的。
 *
 * 但这条链上任何一环断掉都不会报错，只会安静地少两项图例——正是最难发现的那种回归。
 * 所以在这里钉死：**方案带风险时，甘特页签必须把两项风险图例点亮**。
 *
 * 注意判定要成对：只验「有风险时点亮」会漏掉「无风险时也硬列」的反向错误
 * （图例列了但图上一条都没有，同样是假事实）。
 */

const BASE_ASSIGNMENTS = [
  {
    assignmentId: 'a1',
    orderId: 'WO-2026-03008',
    operationId: 'OP-10',
    operationSequence: 10,
    resourceId: 'WC-ROD-01',
    workCenterId: 'WC-ROD-01',
    startUtc: '2026-08-01T00:00:00.000Z',
    endUtc: '2026-08-01T03:00:00.000Z',
    isLocked: false,
  },
  {
    assignmentId: 'a2',
    orderId: 'WO-2026-03008',
    operationId: 'OP-20',
    operationSequence: 20,
    resourceId: 'WC-CNC-02',
    workCenterId: 'WC-CNC-02',
    startUtc: '2026-08-01T03:00:00.000Z',
    endUtc: '2026-08-01T06:00:00.000Z',
    isLocked: false,
  },
]

function plan(patch: Record<string, unknown> = {}) {
  return {
    planId: 'plan-risk-1',
    status: 'generated',
    algorithmVersion: 'heuristic-1',
    generatedAtUtc: '2026-08-01T00:00:00.000Z',
    assignments: BASE_ASSIGNMENTS,
    ...patch,
  }
}

function mountGantt(props: Record<string, unknown>) {
  return mount(SchedulingPlanGantt, {
    props: { workOrders: [], ...props },
    global: { plugins: [createPinia()] },
  })
}

describe('SchedulingPlanGantt 风险图例 (#1399 M7)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('方案带物料/设备风险时，两项风险图例都点亮', () => {
    const wrapper = mountGantt({
      plan: plan({
        materialRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-10',
            reasonCodes: ['material.shortage'],
            shortages: [],
            message: '缺 2 项物料',
          },
        ],
        equipmentRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-20',
            resourceId: 'WC-CNC-02',
            reasonCodes: ['equipment.snapshot.missing'],
            message: '设备无状态快照',
          },
        ],
      }),
    })

    const text = wrapper.text()
    expect(text).toContain('缺料待备')
    expect(text).toContain('设备状态未知')
  })

  it('方案没有风险时，这两项图例不出现（不硬列出图上根本没有的语义）', () => {
    const wrapper = mountGantt({ plan: plan({ materialRisks: [], equipmentRisks: [] }) })

    const text = wrapper.text()
    expect(text).not.toContain('缺料待备')
    expect(text).not.toContain('设备状态未知')
  })

  it('风险按 orderId + operationId 挂到具体工序上，挂不上的不点亮', () => {
    // 工序号对不上（OP-99 不在本方案里）：图例不该因为"数组非空"就点亮。
    const wrapper = mountGantt({
      plan: plan({
        materialRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-99',
            reasonCodes: [],
            shortages: [],
            message: '对不上的风险',
          },
        ],
        equipmentRisks: [],
      }),
    })

    expect(wrapper.text()).not.toContain('缺料待备')
  })
})
