import { describe, expect, it } from 'vitest'
import {
  INVENTORY_EXTERNAL_MOVEMENT_TYPES,
  INVENTORY_MANUAL_MOVEMENT_DEFAULT_TYPE,
  INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS,
  INVENTORY_MOVEMENT_TYPE_LABELS,
  inventoryMovementTypeLabel,
} from './inventoryReference'

/**
 * 走查台账 #49 回归锁：库存移动类型在契约层是自由 string，只有测试能拦住
 * 「界面提供后端必拒的类型」这类漂移。
 *
 * 台账 #68（transfer 单腿凭空增减库存）由 #1359 在后端治本（调拨强制两腿配平），
 * 因此 `transfer` 是合法且必要的选项——这里同时锁住它**不许被再次砍掉**。
 */
describe('库存移动受控值', () => {
  it('读面标签与后端 StockMovement.SupportedMovementTypes 逐字一致', () => {
    expect(Object.keys(INVENTORY_MOVEMENT_TYPE_LABELS)).toEqual([
      'inbound',
      'outbound',
      'transfer',
      'adjustment',
      'count-adjustment',
      'status-transfer-out',
      'status-transfer-in',
    ])
  })

  it('外部命令接受的类型与后端 ExternalMovementTypes 逐字一致', () => {
    expect(INVENTORY_EXTERNAL_MOVEMENT_TYPES).toEqual([
      'inbound',
      'outbound',
      'transfer',
      'adjustment',
    ])
  })

  it('后端从不接受的 receipt / issue 不再出现在任何一层', () => {
    for (const ghost of ['receipt', 'issue']) {
      expect(Object.keys(INVENTORY_MOVEMENT_TYPE_LABELS)).not.toContain(ghost)
      expect(INVENTORY_EXTERNAL_MOVEMENT_TYPES).not.toContain(ghost)
      expect(INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS.map((o) => o.value)).not.toContain(ghost)
    }
  })

  it('人工补录面提供的类型与外部命令接受的类型一一对应，不多不少', () => {
    // 多一个 = 必失败的幽灵值；少一个 = 白白砍掉一项后端支持的能力。
    expect(INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS.map((option) => option.value)).toEqual(
      INVENTORY_EXTERNAL_MOVEMENT_TYPES,
    )
  })

  // #1359 已在后端把调拨改成强制两腿配平，移库因此是合法且必要的选项；
  // 别再按"表达不出对侧腿"的旧前提把它砍掉（前提已经变了）。
  it('移库仍在选项里：后端已强制两腿配平，不是能砍掉的能力', () => {
    expect(INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS.map((o) => o.value)).toContain('transfer')
    expect(inventoryMovementTypeLabel('transfer')).toBe('移库')
  })

  it('弹框默认类型是人工补录面真能过账的类型', () => {
    expect(INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS.map((o) => o.value)).toContain(
      INVENTORY_MANUAL_MOVEMENT_DEFAULT_TYPE,
    )
  })

  it('每个提供的选项都有中文标签，未知码值如实回显', () => {
    for (const option of INVENTORY_MANUAL_MOVEMENT_TYPE_OPTIONS) {
      expect(inventoryMovementTypeLabel(option.value)).toBe(option.label)
    }
    expect(inventoryMovementTypeLabel('unknown-code')).toBe('unknown-code')
    expect(inventoryMovementTypeLabel(undefined)).toBe('—')
  })
})
