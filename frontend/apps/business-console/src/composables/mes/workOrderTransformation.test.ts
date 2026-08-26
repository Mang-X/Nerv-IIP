import { describe, expect, it } from 'vitest'
import {
  isTransformationConflict,
  parsePositiveQuantity,
  sumQuantities,
  validateMergeInput,
  validateSplitInput,
} from './workOrderTransformation'

describe('工单拆分与合并前端校验', () => {
  it('只接受正数，并保留小数数量的可提交值', () => {
    expect(parsePositiveQuantity(' 12.500 ')).toBe(12.5)
    expect(parsePositiveQuantity('0')).toBeUndefined()
    expect(parsePositiveQuantity('-1')).toBeUndefined()
    expect(parsePositiveQuantity('not-a-number')).toBeUndefined()
  })

  it('用十进制口径比较拆分数量，避免 0.1 + 0.2 的浮点误差', () => {
    expect(sumQuantities([0.1, 0.2])).toBe(0.3)
    expect(
      validateSplitInput({
        sourceWorkOrderId: 'WO-SOURCE',
        sourceQuantity: 0.3,
        targets: [
          { workOrderId: 'WO-CHILD-1', quantity: '0.1' },
          { workOrderId: 'WO-CHILD-2', quantity: '0.2' },
        ],
        reason: '按客户批次拆分',
      }),
    ).toEqual([])
  })

  it('拒绝拆分目标不足、目标重复、数量不守恒和空原因', () => {
    expect(
      validateSplitInput({
        sourceWorkOrderId: 'WO-SOURCE',
        sourceQuantity: 10,
        targets: [{ workOrderId: 'WO-CHILD-1', quantity: '10' }],
        reason: '',
      }),
    ).toEqual(['至少填写两个子工单。', '请填写拆分原因。'])

    expect(
      validateSplitInput({
        sourceWorkOrderId: 'WO-SOURCE',
        sourceQuantity: 10,
        targets: [
          { workOrderId: 'WO-CHILD-1', quantity: '4' },
          { workOrderId: 'WO-CHILD-1', quantity: '7' },
        ],
        reason: '调整',
      }),
    ).toContain('子工单标识不能重复。')
    expect(
      validateSplitInput({
        sourceWorkOrderId: 'WO-SOURCE',
        sourceQuantity: 10,
        targets: [
          { workOrderId: 'WO-CHILD-1', quantity: '4' },
          { workOrderId: 'WO-CHILD-2', quantity: '7' },
        ],
        reason: '调整',
      }),
    ).toContain('拆分后数量必须等于源工单数量 10。')
  })

  it('只允许同一 SKU/生产版本/UOM 的源工单合并，并要求新目标标识', () => {
    const sources = [
      {
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        productionVersionId: 'PV-1',
        uomCode: 'PCS',
        quantity: 2,
        status: 'created',
      },
      {
        workOrderId: 'WO-2',
        skuId: 'SKU-1',
        productionVersionId: 'PV-1',
        uomCode: 'PCS',
        quantity: 3,
        status: 'released',
      },
    ]
    expect(
      validateMergeInput({
        sources,
        targetWorkOrderId: 'WO-NEW',
        reason: '同 SKU 小单合并',
      }),
    ).toEqual([])
    expect(
      validateMergeInput({
        sources,
        targetWorkOrderId: 'WO-1',
        reason: '同 SKU 小单合并',
      }),
    ).toContain('合并目标必须是新的工单标识。')
    expect(
      validateMergeInput({
        sources: [...sources, { ...sources[1], workOrderId: 'WO-3', skuId: 'SKU-2' }],
        targetWorkOrderId: 'WO-NEW',
        reason: '同 SKU 小单合并',
      }),
    ).toContain('只能合并 SKU、生产版本和单位都相同的工单。')
  })

  it('单位缺失时 fail-closed，不把两个未知单位当作相同单位', () => {
    expect(
      validateMergeInput({
        sources: [
          {
            workOrderId: 'WO-1',
            skuId: 'SKU-1',
            productionVersionId: 'PV-1',
            uomCode: 'PCS',
            quantity: 2,
            status: 'created',
          },
          {
            workOrderId: 'WO-2',
            skuId: 'SKU-1',
            productionVersionId: 'PV-1',
            uomCode: undefined,
            quantity: 3,
            status: 'released',
          },
        ],
        targetWorkOrderId: 'WO-NEW',
        reason: '同 SKU 小单合并',
      }),
    ).toContain('合并源工单的单位信息未取得，无法确认数量单位；请刷新列表后重试。')
  })

  it('将 HTTP 409 或冲突文案归入冲突态', () => {
    expect(isTransformationConflict({ response: { status: 409 } })).toBe(true)
    expect(isTransformationConflict(new Error('work-order transformation conflict'))).toBe(true)
    expect(isTransformationConflict(new Error('network down'))).toBe(false)
  })
})
