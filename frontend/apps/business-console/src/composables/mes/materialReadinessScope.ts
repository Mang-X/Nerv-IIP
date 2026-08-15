// 齐套口径与缺口环节的业务语言。
//
// 背景（#1291）：齐套只认「线边可用 + 已备料 + 已收料」，MRP 读的是全厂库存，
// 两个口径都成立但互相矛盾；界面必须显式标注口径，并说清「缺什么、缺在哪个环节、下一步动作」，
// 否则用户会以为系统自相矛盾。齐套仍是开工硬门（MES 侧不放松），排产侧才是软约束。

/** 齐套核算口径说明（读面必须显式呈现，不许让用户自己猜）。 */
export const MATERIAL_READINESS_SCOPE_NOTE =
  '齐套按「线边可用 + 已备料 + 已收料」口径核算，不含原料仓等其他库存；MRP 用的是全厂库存口径，两者数字不同属正常。'

export type MaterialShortageStage = 'none' | 'awaitingPreparation' | 'awaitingDelivery'

export interface MaterialShortageStageDescriptor {
  /** 缺口卡在哪个环节。 */
  label: string
  tone: 'success' | 'warning' | 'danger' | 'neutral'
  /** 下一步动作提示。 */
  nextAction: string
}

const STAGES: Record<MaterialShortageStage, MaterialShortageStageDescriptor> = {
  none: { label: '已齐套', tone: 'success', nextAction: '可按计划开工。' },
  awaitingDelivery: {
    label: '仓库配送中',
    tone: 'warning',
    nextAction: '领料已发起、仓库尚未发齐——去出库跟催收料。',
  },
  awaitingPreparation: {
    label: '尚未备料',
    tone: 'danger',
    nextAction: '还没发起领料——先发起领料；若线边无货，去库存可用量核对原料仓是否有货。',
  },
}

/**
 * 缺口环节描述。后端未回 shortageStage 时（旧快照/降级），按缺口与领料进度就地推断，
 * 保证读面永远能说清环节，而不是空着让用户猜。
 */
export function describeMaterialShortageStage(row: {
  shortageQuantity?: number | null
  requestedQuantity?: number | null
  receivedQuantity?: number | null
  shortageStage?: string | null
}): MaterialShortageStageDescriptor {
  const stage = row.shortageStage as MaterialShortageStage | undefined
  if (stage && stage in STAGES) return STAGES[stage]

  const shortage = row.shortageQuantity ?? 0
  if (shortage <= 0) return STAGES.none
  return (row.requestedQuantity ?? 0) > (row.receivedQuantity ?? 0)
    ? STAGES.awaitingDelivery
    : STAGES.awaitingPreparation
}
