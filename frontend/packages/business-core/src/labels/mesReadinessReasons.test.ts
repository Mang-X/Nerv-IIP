import { describe, expect, it } from 'vitest'
import { describeMesReadinessReason, describeMesReadinessReasons } from './mesReadinessReasons'

describe('MES readiness reason presentation', () => {
  it.each([
    ['PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成', '前序工序', '前序工序未完工'],
    ['MATERIAL_SHORTAGE: 物料 MAT-OIL 缺口 2', '物料齐套', '物料缺料'],
    ['QUALITY_HOLD_ACTIVE: 工单存在有效质量保留', '质量', '质量冻结中'],
    [
      'WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。',
      '其他门禁',
      '工单未下达',
    ],
    [
      'equipment.activeAlarm: 工业遥测存在未解除报警，设备不可用于当前工序。',
      '设备',
      '设备报警未解除',
    ],
  ])('classifies %s for an operator-readable category', (raw, category, label) => {
    expect(describeMesReadinessReason(raw)).toMatchObject({ category, label })
  })

  it('preserves the server Chinese detail separately from the reason label', () => {
    expect(
      describeMesReadinessReason('MATERIAL_SHORTAGE: 物料 MAT-OIL，批次 LOT-A 缺口 2'),
    ).toMatchObject({
      code: 'MATERIAL_SHORTAGE',
      label: '物料缺料',
      category: '物料齐套',
      detail: '物料 MAT-OIL，批次 LOT-A 缺口 2',
    })
  })

  it('keeps an unknown code actionable without exposing it as the operator label', () => {
    expect(describeMesReadinessReason('NEW_GATE_CODE: 请联系班组长确认放行')).toEqual({
      code: 'NEW_GATE_CODE',
      label: '请联系班组长确认放行',
      category: '其他门禁',
      detail: '',
      nextStep: '查看阻塞详情并按来源业务页面处理',
    })
  })

  // 服务端那串由 MesReadinessReasonCodes.WorkOrderNotReleasedReason 一处产出（#3119）。
  // 未登记的码会走兜底分支：那时 label 变成整句中文、nextStep 变成通用兜底句，
  // 也就没有「先下达」这个下一步——本用例钉住的正是这条动作接力。
  it('points a not-yet-released work order at the release action', () => {
    expect(
      describeMesReadinessReason(
        'WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。',
      ),
    ).toEqual({
      code: 'WORK_ORDER_NOT_RELEASED',
      label: '工单未下达',
      category: '其他门禁',
      detail: '工单尚未下达，请先下达工单后再开工或报工。',
      nextStep: '先在工单列表下达该工单，再开工或报工',
    })
  })

  it('merges duplicate codes and retains every distinct server detail', () => {
    expect(
      describeMesReadinessReasons([
        'MATERIAL_SHORTAGE: 物料 MAT-OIL 缺口 2',
        'MATERIAL_SHORTAGE: 物料 MAT-SEAL 缺口 5',
        'MATERIAL_SHORTAGE: 物料 MAT-OIL 缺口 2',
      ]),
    ).toEqual([
      {
        code: 'MATERIAL_SHORTAGE',
        label: '物料缺料',
        category: '物料齐套',
        detail: '物料 MAT-OIL 缺口 2、物料 MAT-SEAL 缺口 5',
        nextStep: '在工单详情「用料齐套」发起领料；物料到线边后确认收料',
      },
    ])
  })
})
