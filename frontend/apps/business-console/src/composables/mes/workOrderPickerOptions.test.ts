import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import { buildWorkOrderPickerOptions } from './workOrderPickerOptions'

const page: BusinessConsoleMesWorkOrderItem[] = [
  {
    workOrderId: 'WO-20260731-000001',
    workOrderNo: 'WO-20260731-000001',
    skuCode: 'SKU-FG-100',
    status: 'released',
  },
  { workOrderId: 'WO-20260731-000002', workOrderNo: 'WO-20260731-000002', skuCode: 'SKU-FG-200' },
  // 没有工单标识的行不该变成一个选不动的空选项。
  { skuCode: 'SKU-FG-300' },
]

describe('buildWorkOrderPickerOptions', () => {
  it('用真实工单号作显示文案，物料与状态作辅助识别', () => {
    const options = buildWorkOrderPickerOptions(page, '')
    expect(options.map((option) => option.value)).toEqual([
      'WO-20260731-000001',
      'WO-20260731-000002',
    ])
    expect(options[0]!.label).toBe('WO-20260731-000001')
    expect(options[0]!.hint).toContain('SKU-FG-100')
  })

  it('已选工单就在当前结果页时不重复补项', () => {
    const options = buildWorkOrderPickerOptions(page, 'WO-20260731-000002')
    expect(options.filter((option) => option.value === 'WO-20260731-000002')).toHaveLength(1)
  })

  // 服务端搜索一页装不下整个目录：已选工单可能来自地址栏或上一次搜索，
  // 不补占位就会显示成「未选择」，让人以为选中丢了。
  it('已选工单不在当前结果页时补占位项，且排在最前', () => {
    const options = buildWorkOrderPickerOptions(page, 'WO-20260615-000042')
    expect(options[0]).toEqual({
      value: 'WO-20260615-000042',
      label: 'WO-20260615-000042',
      hint: '当前所选',
    })
  })

  it('占位项显示人读工单号，而不是内部标识', () => {
    const withInternalId: BusinessConsoleMesWorkOrderItem[] = [
      { workOrderId: 'a3f1c2d4-0000-4000-8000-000000000001', workOrderNo: 'WO-20260731-000007' },
    ]
    // 目录里能查到这张工单时，占位项跟着显示真实工单号。
    const known = buildWorkOrderPickerOptions(
      withInternalId,
      'a3f1c2d4-0000-4000-8000-000000000001',
    )
    expect(known.every((option) => option.label === 'WO-20260731-000007')).toBe(true)
  })

  it('空白已选值不产生占位项', () => {
    expect(buildWorkOrderPickerOptions(page, '   ')).toHaveLength(2)
    expect(buildWorkOrderPickerOptions(page, null)).toHaveLength(2)
  })
})
