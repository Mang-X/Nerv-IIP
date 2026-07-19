import { describe, expect, it } from 'vitest'
import {
  EQUIPMENT_DEVICE_NAME_SIZE_PX,
  EQUIPMENT_ROW_GAP_PX,
  EQUIPMENT_ROW_HEIGHT_PX,
  EQUIPMENT_ROW_ITEM_HEIGHT_PX,
  EQUIPMENT_ROW_OVERSCAN,
} from './device-wall-layout'

describe('equipment wall virtual layout', () => {
  it('keeps every device name readable and derives the virtual stride from rendered geometry', () => {
    expect(EQUIPMENT_DEVICE_NAME_SIZE_PX).toBe(14)
    expect(EQUIPMENT_ROW_ITEM_HEIGHT_PX).toBe(EQUIPMENT_ROW_HEIGHT_PX + EQUIPMENT_ROW_GAP_PX)
    expect(EQUIPMENT_ROW_OVERSCAN).toBeGreaterThanOrEqual(2)
  })
})
