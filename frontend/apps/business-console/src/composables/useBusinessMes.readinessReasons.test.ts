import { setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it } from 'vitest'
import { describeMesReadinessReason, describeMesReadinessReasons } from './useBusinessMes'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('maps backend MES readiness reason codes to shared labels and next steps', () => {
    expect(describeMesReadinessReason('QUALITY_PLAN_MISSING')).toMatchObject({
      code: 'QUALITY_PLAN_MISSING',
      label: '检验方案缺失',
      nextStep: '维护并启用 SKU 与工序检验方案后重新检查',
    })
    expect(describeMesReadinessReason('QUALITY_HOLD_ACTIVE')).toMatchObject({
      label: '质量冻结中',
      nextStep: '处理质量冻结、NCR 或放行状态后再执行',
    })
    expect(describeMesReadinessReason('EQUIPMENT_UNAVAILABLE')).toMatchObject({
      label: '设备不可用',
      nextStep: '处理报警/停机或改派可用设备',
    })
    expect(describeMesReadinessReason('EQUIPMENT_MAINTENANCE_CONFLICT')).toMatchObject({
      label: '维修占用冲突',
      nextStep: '调整维修窗口、等待释放或选择替代设备',
    })
  })

  // MAN-698 台账 #35：缺料阻塞原来整条塞进徽标（且后端还是英文生码
  // 「物料编码 + shortage + 数量」），用户既读不懂又只看得见前半截。
  // 现在码进短标签、中文事实进 detail。
  it('缺料阻塞：码取短标签，中文事实单独给 detail，不把整条塞进徽标', () => {
    expect(
      describeMesReadinessReason('MATERIAL_SHORTAGE: 物料 MAT-OIL，批次 LOT-OIL-A 缺口 2'),
    ).toEqual({
      code: 'MATERIAL_SHORTAGE',
      label: '物料缺料',
      category: '物料齐套',
      detail: '物料 MAT-OIL，批次 LOT-OIL-A 缺口 2',
      nextStep: '在工单详情「用料齐套」发起领料；物料到线边后确认收料',
    })
    expect(
      describeMesReadinessReason('MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照。'),
    ).toMatchObject({ label: '齐套快照缺失', detail: '工单缺少齐套需求快照。' })
    expect(
      describeMesReadinessReason('PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成'),
    ).toMatchObject({ label: '前序工序未完工', detail: '前序工序尚未完成' })
  })

  it('未登记的码退回中文说明当标签；中文里的冒号不会被当成码分隔符', () => {
    expect(describeMesReadinessReason('SOME_NEW_CODE: 这条还没登记')).toMatchObject({
      code: 'SOME_NEW_CODE',
      label: '这条还没登记',
      detail: '',
      nextStep: '查看阻塞详情并按来源业务页面处理',
    })
    expect(describeMesReadinessReason('物料齐套未满足：还差 3 件')).toMatchObject({
      label: '物料齐套未满足：还差 3 件',
      detail: '',
    })
  })

  // #1418：服务端按「每种缺料各一条」下发，逐条渲染会在同一工序上堆出两条一模一样的
  // 「物料缺料」——看不出是两种料，也不知道缺哪两项。同码必须合并成一条并点名。
  it('merges same-code readiness reasons and names every shortage', () => {
    const merged = describeMesReadinessReasons([
      'MATERIAL_SHORTAGE: 物料 MAT-OIL 缺口 2',
      'MATERIAL_SHORTAGE: 物料 MAT-SEAL 缺口 5',
      'QUALITY_HOLD_ACTIVE: 存在未放行的质量冻结',
    ])

    expect(merged).toHaveLength(2)
    expect(merged[0]).toMatchObject({
      code: 'MATERIAL_SHORTAGE',
      label: '物料缺料',
      detail: '物料 MAT-OIL 缺口 2、物料 MAT-SEAL 缺口 5',
    })
    // 不同码是不同处理路径，必须各占一行，不能被一起合掉。
    expect(merged[1]).toMatchObject({ code: 'QUALITY_HOLD_ACTIVE', label: '质量冻结中' })
  })

  it('collapses byte-identical duplicates without leaving an empty detail separator', () => {
    const merged = describeMesReadinessReasons(['MATERIAL_SHORTAGE', 'MATERIAL_SHORTAGE'])

    expect(merged).toHaveLength(1)
    expect(merged[0]).toMatchObject({ code: 'MATERIAL_SHORTAGE', label: '物料缺料', detail: '' })
  })

  it('returns an empty list for missing reasons', () => {
    expect(describeMesReadinessReasons(undefined)).toEqual([])
    expect(describeMesReadinessReasons(null)).toEqual([])
    expect(describeMesReadinessReasons([])).toEqual([])
  })
})
