import { describe, expect, it } from 'vitest'
import {
  describeMaterialShortageStage,
  MATERIAL_READINESS_SCOPE_NOTE,
} from './materialReadinessScope'

describe('齐套口径自解释', () => {
  it('口径说明显式点出线边/备料范围，并解释与 MRP 全厂口径的差异', () => {
    expect(MATERIAL_READINESS_SCOPE_NOTE).toContain('线边')
    expect(MATERIAL_READINESS_SCOPE_NOTE).toContain('备料')
    expect(MATERIAL_READINESS_SCOPE_NOTE).toContain('全厂')
  })

  it('已发起领料但没收齐 → 仓库配送中，动作是跟催收料', () => {
    const stage = describeMaterialShortageStage({
      shortageQuantity: 145.86,
      requestedQuantity: 145.86,
      receivedQuantity: 0,
    })

    expect(stage.label).toBe('仓库配送中')
    expect(stage.nextAction).toContain('跟催')
  })

  it('一张领料都没发 → 尚未备料，动作里指向库存可用量核对', () => {
    const stage = describeMaterialShortageStage({
      shortageQuantity: 145.86,
      requestedQuantity: 0,
      receivedQuantity: 0,
    })

    expect(stage.label).toBe('尚未备料')
    expect(stage.nextAction).toContain('库存可用量')
  })

  it('不缺料 → 已齐套', () => {
    expect(describeMaterialShortageStage({ shortageQuantity: 0 }).label).toBe('已齐套')
  })

  it('后端回了 shortageStage 就以后端为准，不再就地推断', () => {
    const stage = describeMaterialShortageStage({
      shortageQuantity: 5,
      requestedQuantity: 0,
      receivedQuantity: 0,
      shortageStage: 'awaitingDelivery',
    })

    expect(stage.label).toBe('仓库配送中')
  })
})
