import { describe, expect, it } from 'vitest'
import {
  mesWorkOrderReleaseBlocker,
  mesWorkOrderRetroactiveReleaseNotice,
  type MesWorkOrderReleaseCandidate,
} from './workOrderRelease'

/**
 * 语义来源：后端 `ReleaseWorkOrderCommandHandler`
 * （`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`）
 * 与 `WorkOrder.ThrowIfCannotRelease`。下达守卫是：工单状态 ∈ {created, started, hold}、
 * 有生产版本、至少一道工序任务、无有效质量保留、设备/物料就绪。**工序状态不在守卫内。**
 *
 * 工序 blocker 的可用性来源：`MesOperationTaskActionReadinessEvaluator.Evaluate`
 * 对 InProgress / Paused / 其它非 Queued 工序一律返回空 BlockReasons，
 * 所以空 blocker 在非 queued 工序上不构成就绪证据。
 *
 * 回归来源：#3118（母票 #3113）——前端曾要求全部工序 queued，比后端更严，
 * 把「工序已开工的工单事后补下达」这条自愈路径从界面上藏掉。
 */

const queuedReadyTask = {
  status: 'Queued',
  blockReasons: [] as string[],
  evaluatedAtUtc: '2026-09-04T02:00:00Z',
}

function candidate(
  overrides: Partial<MesWorkOrderReleaseCandidate> = {},
): MesWorkOrderReleaseCandidate {
  return {
    workOrderId: 'WO-20260904-000001',
    status: 'created',
    productionVersionId: 'PV-FG-100-A',
    operationTasks: [queuedReadyTask],
    ...overrides,
  }
}

describe('mesWorkOrderReleaseBlocker', () => {
  it('全部工序排队且就绪的工单可以下达', () => {
    expect(mesWorkOrderReleaseBlocker(candidate())).toBeNull()
  })

  // #3118 的核心回归：工单 created、唯一工序已开工，后端守卫会受理，界面必须让点。
  it('工序已在制的 created 工单可以下达（后端守卫不要求工序排队）', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [
            { status: 'InProgress', blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' },
          ],
        }),
      ),
    ).toBeNull()
  })

  it.each(['Paused', 'Completed', 'Cancelled', 'ScheduleInvalidated'])(
    '工序处于 %s 时不阻断下达',
    (status) => {
      expect(
        mesWorkOrderReleaseBlocker(
          candidate({
            operationTasks: [{ status, blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' }],
          }),
        ),
      ).toBeNull()
    },
  )

  // 非 queued 工序的 readiness 根本没被求值，缺 evaluatedAtUtc / blockReasons 是常态，
  // 不能借「就绪状态尚未取得」把这条路径重新堵回去。
  it('非排队工序缺少 readiness 读数时也不阻断下达', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [{ status: 'InProgress', blockReasons: null, evaluatedAtUtc: null }],
        }),
      ),
    ).toBeNull()
  })

  it('混合工序：在制工序旁边的排队工序仍缺料时照旧阻断', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [
            { status: 'InProgress', blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' },
            {
              status: 'Queued',
              blockReasons: ['MATERIAL_SHORTAGE: 物料 MAT-STEEL-01 缺 12.5 KG'],
              evaluatedAtUtc: '2026-09-04T02:00:00Z',
            },
          ],
        }),
      ),
    ).toBe('物料缺料，不能下达：物料 MAT-STEEL-01 缺 12.5 KG')
  })

  it('混合工序：在制工序旁边的排队工序已就绪时可以下达', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [
            { status: 'InProgress', blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' },
            queuedReadyTask,
          ],
        }),
      ),
    ).toBeNull()
  })

  it('排队工序的 readiness 读数缺失时失败关闭', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [{ status: 'Queued', blockReasons: [], evaluatedAtUtc: null }],
        }),
      ),
    ).toBe('工序就绪状态尚未取得，不能下达')
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [
            { status: 'Queued', blockReasons: null, evaluatedAtUtc: '2026-09-04T02:00:00Z' },
          ],
        }),
      ),
    ).toBe('工序就绪状态尚未取得，不能下达')
  })

  // 前序工序未完工是「本工序还不能开工」，不是「工单不能下达」；后端下达守卫不看它。
  it('排队工序只因前序工序未完工被挡时仍可下达', () => {
    expect(
      mesWorkOrderReleaseBlocker(
        candidate({
          operationTasks: [
            { status: 'InProgress', blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' },
            {
              status: 'Queued',
              blockReasons: ['PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成（工序 10）'],
              evaluatedAtUtc: '2026-09-04T02:00:00Z',
            },
          ],
        }),
      ),
    ).toBeNull()
  })

  it.each(['created', 'started', 'hold'])('工单状态 %s 落在后端允许的下达状态内', (status) => {
    expect(mesWorkOrderReleaseBlocker(candidate({ status }))).toBeNull()
  })

  it.each(['released', 'completed', 'closed', 'cancelled', 'scrapped', 'split', 'merged'])(
    '工单状态 %s 不在后端允许的下达状态内',
    (status) => {
      expect(mesWorkOrderReleaseBlocker(candidate({ status }))).toBe('当前状态不能下达')
    },
  )

  it('缺工单标识、缺生产版本、无工序任务分别给出各自的原因', () => {
    expect(mesWorkOrderReleaseBlocker(candidate({ workOrderId: undefined }))).toBe(
      '工单标识缺失，不能下达',
    )
    expect(mesWorkOrderReleaseBlocker(candidate({ productionVersionId: '  ' }))).toBe(
      '缺少生产版本，不能下达',
    )
    expect(mesWorkOrderReleaseBlocker(candidate({ operationTasks: [] }))).toBe(
      '尚未生成工序任务，不能下达',
    )
  })

  it('有效质量保留阻断下达，无论来自工单标志还是保留明细', () => {
    expect(mesWorkOrderReleaseBlocker(candidate({ hasActiveQualityHold: true }))).toBe(
      '存在有效质量保留，不能下达',
    )
    expect(mesWorkOrderReleaseBlocker(candidate({ qualityHolds: [{ isActive: true }] }))).toBe(
      '存在有效质量保留，不能下达',
    )
    expect(
      mesWorkOrderReleaseBlocker(candidate({ qualityHolds: [{ isActive: false }] })),
    ).toBeNull()
  })
})

describe('mesWorkOrderRetroactiveReleaseNotice', () => {
  // 文案只能说谓词支持的那件事。之前写成「这是对已开工工单的补充下达」是读面不支持的推断：
  // `ScheduleInvalidated`（`MarkScheduleInvalidated` 对 Queued 不豁免）与 `Cancelled`
  // （`Cancel` 只豁免 Completed/Cancelled）都能从 Queued 直达，工单一道工序都没开过。
  // 正向夹具此前只有 InProgress 一种，正是这个覆盖缺口把那句推断藏住了。
  it.each(['InProgress', 'Paused', 'Completed', 'Cancelled', 'ScheduleInvalidated'])(
    '工序为 %s 时提示只陈述「已有工序不在排队中」，不推断是否开过工',
    (status) => {
      expect(
        mesWorkOrderRetroactiveReleaseNotice(
          candidate({
            operationTasks: [{ status, blockReasons: [], evaluatedAtUtc: '2026-09-04T02:00:00Z' }],
          }),
        ),
      ).toBe('该工单已有工序不在排队中。')
    },
  )

  it('全部工序仍在排队时不给该提示', () => {
    expect(mesWorkOrderRetroactiveReleaseNotice(candidate())).toBeNull()
  })

  it('没有工序任务时不给该提示', () => {
    expect(mesWorkOrderRetroactiveReleaseNotice(candidate({ operationTasks: [] }))).toBeNull()
  })
})
