import { describe, expect, it } from 'vitest'
import { MES_READINESS_REASON_DISPLAYS, describeMesReadinessReason } from '@nerv-iip/business-core'
import { RELEASE_IGNORED_TASK_BLOCKERS, mesWorkOrderReleaseBlocker } from './workOrderRelease'

/**
 * #3119 的回归来源：后端新增阻断码 `WORK_ORDER_NOT_RELEASED` 之后，它经工单列表读面穿到
 * `mesWorkOrderReleaseBlocker`，而豁免集里没有它——于是**每一张 `created` 工单的「下达」按钮**
 * 都被这条「你还没下达」的理由禁掉，形成自指死锁：文案叫用户去下达，控件却被这条文案关掉。
 *
 * 当时没被抓住的原因是夹具漂移：既有 18 条下达用例都写死
 * `status:'created'` + `blockReasons: []`，**而这个组合在守卫上线之后后端再也回不出来**。
 *
 * 本文件钉两件事：
 * ① 具体那一格（下面第一条用例，夹具用后端真正会回的组合）；
 * ② **归类的完备性**——共享词表里的每个码都必须被显式决定「阻不阻断下达」。
 *    ② 才是防复发的那条：它把「新增码默认阻断下达」这个静默默认改成编译期之外的红。
 */

/** 后端 `MesReadinessReasonCodes.WorkOrderNotReleasedReason` 的逐字形态。 */
const WORK_ORDER_NOT_RELEASED_REASON =
  'WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。'

/**
 * 共享词表里每个码对下达的影响，**必须逐个列全**。
 * `true` = 阻断下达；`false` = 不阻断（即在 `RELEASE_IGNORED_TASK_BLOCKERS` 里）。
 */
const RELEASE_IMPACT_BY_CODE: Readonly<Record<string, boolean>> = {
  MATERIAL_SHORTAGE: true,
  MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: true,
  // 前序工序未完工是开工的先后顺序问题，与工单维度的下达无关。
  PREVIOUS_OPERATION_INCOMPLETE: false,
  QUALITY_PLAN_MISSING: true,
  QUALITY_HOLD_ACTIVE: true,
  EQUIPMENT_UNAVAILABLE: true,
  EQUIPMENT_MAINTENANCE_CONFLICT: true,
  'equipment.activeAlarm': true,
  'equipment.stateUnavailable': true,
  'equipment.downtime': true,
  'equipment.maintenanceWindow': true,
  'equipment.inspectionRequired': true,
  'equipment.sourceStale': true,
  'equipment.tagMappingMissing': true,
  'equipment.noEligibleSubstitute': true,
  'equipment.sourceUnavailable': true,
  SOURCE_SERVICE_UNAVAILABLE: true,
  // #3119：补救动作就是下达本身，绝不能阻断下达。
  WORK_ORDER_NOT_RELEASED: false,
}

describe('工单下达的工序阻断码归类', () => {
  it('created 工单带 WORK_ORDER_NOT_RELEASED 时仍可下达（#3119 自指死锁的那一格）', () => {
    expect(
      mesWorkOrderReleaseBlocker({
        workOrderId: 'WO-20260905-000001',
        status: 'created',
        productionVersionId: 'PV-FG-100-A',
        operationTasks: [
          {
            status: 'Queued',
            blockReasons: [WORK_ORDER_NOT_RELEASED_REASON],
            evaluatedAtUtc: '2026-09-05T00:00:00Z',
          },
        ],
      }),
    ).toBeNull()
  })

  it('同一夹具再叠一条真阻断码时仍然拦住——放行的是那个码，不是那张工单', () => {
    expect(
      mesWorkOrderReleaseBlocker({
        workOrderId: 'WO-20260905-000001',
        status: 'created',
        productionVersionId: 'PV-FG-100-A',
        operationTasks: [
          {
            status: 'Queued',
            blockReasons: [
              WORK_ORDER_NOT_RELEASED_REASON,
              'MATERIAL_SHORTAGE: 物料 MAT-OIL 缺口 2',
            ],
            evaluatedAtUtc: '2026-09-05T00:00:00Z',
          },
        ],
      }),
    ).toBe('物料缺料，不能下达：物料 MAT-OIL 缺口 2')
  })

  it('共享词表里的每个码都被显式归类，没有落进静默默认', () => {
    expect(Object.keys(RELEASE_IMPACT_BY_CODE).sort()).toEqual(
      Object.keys(MES_READINESS_REASON_DISPLAYS).sort(),
    )
  })

  it('归类表与豁免集逐字一致', () => {
    const ignoredByPolicy = Object.entries(RELEASE_IMPACT_BY_CODE)
      .filter(([, blocks]) => !blocks)
      .map(([code]) => code)
      .sort()
    expect(ignoredByPolicy).toEqual([...RELEASE_IGNORED_TASK_BLOCKERS].sort())
  })

  it.each(Object.entries(RELEASE_IMPACT_BY_CODE))(
    '%s 的下达影响与归类一致',
    (code, blocksRelease) => {
      const display = MES_READINESS_REASON_DISPLAYS[code]
      expect(display).toBeDefined()
      const reason = `${code}: ${display!.label}`
      // 前提自检：这一行确实被解析成该码，否则下面的断言测的是兜底分支。
      expect(describeMesReadinessReason(reason).code).toBe(code)

      const blocker = mesWorkOrderReleaseBlocker({
        workOrderId: 'WO-20260905-000002',
        status: 'created',
        productionVersionId: 'PV-FG-100-A',
        operationTasks: [
          {
            status: 'Queued',
            blockReasons: [reason],
            evaluatedAtUtc: '2026-09-05T00:00:00Z',
          },
        ],
      })
      expect(blocker === null).toBe(!blocksRelease)
    },
  )
})
