import { mount } from '@vue/test-utils'
import { shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BusinessConsoleMesFinishedGoodsInventoryLinkResponse } from '@nerv-iip/api-client'
import ReceiptInventorySource from './ReceiptInventorySource.vue'

const state = vi.hoisted(() => ({
  source: undefined as unknown,
  error: undefined as unknown,
  pending: false,
}))
vi.mock('@/composables/useBusinessMes', () => ({
  useMesReceiptInventorySource: () => ({
    source: shallowRef(state.source),
    sourceError: shallowRef(state.error),
    sourcePending: shallowRef(state.pending),
  }),
}))
function mountSource() {
  return mount(ReceiptInventorySource, {
    props: { receipt: { requestNo: 'FGR-001', workOrderId: 'WO-001', producedLotNo: 'LOT-001' } },
    global: {
      stubs: {
        NvButton: { template: '<button><slot /></button>' },
        NvSpinner: true,
        NvDialog: { template: '<div><slot /></div>' },
        NvDialogContent: { template: '<div><slot /></div>' },
        NvDialogHeader: { template: '<div><slot /></div>' },
        NvDialogTitle: { template: '<h2><slot /></h2>' },
        NvDialogDescription: { template: '<p><slot /></p>' },
        NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
        NvDataTable: {
          props: ['columns', 'rows', 'emptyMessage'],
          template:
            '<section><p v-if="!rows.length">{{ emptyMessage }}</p><table><thead><tr><th v-for="column in columns" :key="column.key">{{ column.header }}</th></tr></thead><tbody><tr v-for="(row, index) in rows" :key="index"><td v-for="column in columns" :key="column.key">{{ column.accessor ? column.accessor(row) : row[column.key] }}</td></tr></tbody></table></section>',
        },
      },
    },
  })
}
describe('FGR 库存来源事实', () => {
  beforeEach(() => {
    state.error = undefined
    state.pending = false
    state.source = {
      requestNo: 'FGR-001',
      workOrderId: 'WO-001',
      producedLotNo: 'LOT-001',
      receiptStatus: 'Posted',
      linkStatus: 'posted',
      isInventoryLinkEstablished: true,
      requestedQuantity: 8,
      postedQuantity: 8,
      remainingQuantity: 0,
      movements: [],
      balances: [],
    } satisfies Partial<BusinessConsoleMesFinishedGoodsInventoryLinkResponse>
  })
  it('展示同一工单批次的移动与余额，不把库存可用量当入库数量', () => {
    state.source = {
      ...(state.source as object),
      movements: [
        {
          movementId: 'movement-1',
          sourceDocumentId: 'FGR-001',
          skuCode: 'FG-001',
          lotNo: 'LOT-001',
          quantity: 8,
          uomCode: 'pcs',
          locationCode: 'FG-A',
        },
      ],
      balances: [
        {
          skuCode: 'FG-001',
          lotNo: 'LOT-001',
          onHandQuantity: 8,
          reservedQuantity: 3,
          availableQuantity: 5,
          locationCode: 'FG-A',
        },
      ],
    }
    const wrapper = mountSource()
    const text = wrapper.text()
    for (const fact of [
      'WO-001',
      'LOT-001',
      'FGR-001',
      'FG-001',
      'FG-A',
      '登记数量 8',
      '已过账 8',
      '剩余 0',
    ])
      expect(text).toContain(fact)
    const tables = wrapper.findAll('table')
    const movementTable = tables.find((table) =>
      table.findAll('th').some((header) => header.text() === '移动数量'),
    )!
    const movementHeaders = movementTable.findAll('th').map((header) => header.text())
    const movementRow = movementTable
      .findAll('tbody tr')
      .find((row) =>
        ['FGR-001', 'FG-001', 'LOT-001', 'FG-A'].every((identity) =>
          row.findAll('td').some((cell) => cell.text() === identity),
        ),
      )!
    expect(movementRow).toBeDefined()
    expect(movementRow.findAll('td')[movementHeaders.indexOf('移动数量')].text()).toBe('8')

    const balanceTable = tables.find((table) =>
      table.findAll('th').some((header) => header.text() === '在库数量'),
    )!
    const balanceHeaders = balanceTable.findAll('th').map((header) => header.text())
    const balanceRow = balanceTable
      .findAll('tbody tr')
      .find((row) =>
        ['FG-001', 'LOT-001', 'FG-A'].every((identity) =>
          row.findAll('td').some((cell) => cell.text() === identity),
        ),
      )!
    expect(balanceRow).toBeDefined()
    expect(balanceRow.findAll('td')[balanceHeaders.indexOf('在库数量')].text()).toBe('8')
    expect(balanceRow.findAll('td')[balanceHeaders.indexOf('预留数量')].text()).toBe('3')
    expect(balanceRow.findAll('td')[balanceHeaders.indexOf('可用数量')].text()).toBe('5')
  })
  it.each([
    ['notPosted', '未过账', false],
    ['postingFailed', '过账失败', false],
    ['partiallyPosted', '部分过账', true],
    ['qualityRestricted', '质量受限', true],
    ['posted', '已过账', true],
  ])('按公开来源状态展示 %s', (linkStatus, label, established) => {
    state.source = {
      ...(state.source as object),
      linkStatus,
      isInventoryLinkEstablished: established,
      inventoryPostingFailureMessage: linkStatus === 'postingFailed' ? '库位不可用' : null,
    }
    const text = mountSource().text()
    expect(text).toContain(`库存状态：${label}`)
    expect(text.includes('尚未建立库存来源关联')).toBe(!established)
    expect(text).toContain('暂无该入库单的库存移动')
    expect(text).toContain('暂无对应库存余额')
    if (linkStatus === 'postingFailed') expect(text).toContain('库位不可用')
  })
  it.each(['requestNo', 'workOrderId', 'producedLotNo'])(
    '公开读回 %s 不一致时不显示库存事实',
    (key) => {
      state.source = {
        ...(state.source as object),
        [key]: 'OTHER',
        movements: [{ skuCode: 'WRONG-SKU' }],
      }
      const wrapper = mountSource()
      expect(wrapper.get('[role="alert"]').text()).toContain('不一致')
      expect(wrapper.text()).not.toContain('WRONG-SKU')
    },
  )
  it('读取失败不显示为空库存或过账成功', () => {
    state.source = undefined
    state.error = new Error('库存来源不可用')
    const wrapper = mountSource()
    expect(wrapper.get('[role="alert"]').text()).toContain('库存来源不可用')
    expect(wrapper.text()).not.toContain('暂无')
    expect(wrapper.text()).not.toContain('已过账')
  })
})
