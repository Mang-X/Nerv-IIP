import type { BusinessConsoleMesProductionStatisticsDegradedReason } from '@nerv-iip/api-client'
import { createProductionStatisticsCsv } from '@/features/mes-production-report/productionStatisticsCsv'
import { mount } from '@vue/test-utils'
import { computed, nextTick, reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { presentProductionStatisticsRow } from '@/features/mes-production-report/productionStatisticsPresentation'
import MesProductionStatisticsTable from './MesProductionStatisticsTable.vue'

const directory = vi.hoisted(() => ({
  rows: [] as Array<{ code: string; displayName: string | null }>,
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    filters: {},
    resources: computed(() => directory.rows),
  }),
}))

function row(code: string, quantity: number) {
  return presentProductionStatisticsRow({
    dimension: 'shift',
    dimensionValue: code,
    shiftCode: code,
    businessDate: '2026-10-11',
    workCenterId: null,
    skuId: null,
    totalOutputQuantity: quantity,
    goodQuantity: quantity,
    scrapQuantity: 0,
    reworkQuantity: 0,
    goodRate: 1,
    scrapRate: 0,
    reworkRate: 0,
    productionReportCount: 2,
    resolutionStatus: 'resolved',
    degradedReasons: [],
  })
}
function mountTable() {
  return mount(MesProductionStatisticsTable, {
    props: {
      rows: [row('NIGHT', 120), row('CUSTOM', 85), row('UNNAMED', 7)],
      total: 3,
      page: 1,
      pageSize: '20',
      pending: false,
    },
  })
}
describe('生产日报班次名称', () => {
  beforeEach(() => {
    directory.rows = reactive([
      { code: 'NIGHT', displayName: '夜班' },
      { code: 'CUSTOM', displayName: '中班' },
      { code: 'UNNAMED', displayName: null },
    ])
  })
  it('统计对象与班次列都显示目录名称，数量和分组不变', () => {
    const wrapper = mountTable()
    expect(wrapper.text().match(/夜班/g)).toHaveLength(2)
    expect(wrapper.text().match(/中班/g)).toHaveLength(2)
    for (const code of ['NIGHT', 'CUSTOM', 'UNNAMED']) expect(wrapper.text()).not.toContain(code)
    expect(wrapper.text()).toContain('120')
    expect(wrapper.text()).toContain('85')
    expect(wrapper.text()).toContain('7')
    expect(wrapper.findAll('tbody tr')).toHaveLength(3)
  })
  it('当前快照不完整、历史无快照及原诊断在表格和 CSV 中一致，数量不变', () => {
    const reasons: BusinessConsoleMesProductionStatisticsDegradedReason[] = [
      'historicalDimensionSnapshotDegraded',
      'historicalDimensionLegacyUnresolved',
      'deviceMissing',
      'historicalTimezoneMissing',
    ]
    const rows = reasons.map((reason) => ({
      ...row('NIGHT', 2),
      resolutionStatus: 'degraded' as const,
      degradedReasons: [reason],
    }))
    const wrapper = mount(MesProductionStatisticsTable, {
      props: { rows, total: 4, page: 1, pageSize: '20', pending: false },
    })
    const csv = createProductionStatisticsCsv(rows)
    const renderedRows = wrapper.findAll('tbody tr')
    expect(renderedRows).toHaveLength(4)
    for (const [index, label] of [
      '报工维度快照不完整',
      '历史维度无法解析',
      '报工缺设备，实绩维度快照未取得',
      '历史站点时区缺失',
    ].entries()) {
      expect(renderedRows[index]!.text()).toContain(label)
      expect(renderedRows[index]!.text()).toContain('数据不完整')
      expect(csv).toContain(`,2,2,1,0,0,0,0,2,数据不完整,${label}`)
    }
    expect(renderedRows[0]!.text()).not.toContain('历史')
    expect(rows.reduce((sum, item) => sum + item.totalOutputQuantity, 0)).toBe(8)
  })
  it('目录名称变化立即更新展示，不使用硬编码翻译', async () => {
    const wrapper = mountTable()
    directory.rows[0]!.displayName = '晚间生产班'
    await nextTick()
    expect(wrapper.text().match(/晚间生产班/g)).toHaveLength(2)
    expect(wrapper.text()).not.toContain('夜班')
    expect(wrapper.text()).toContain('120')
    expect(wrapper.findAll('tbody tr')).toHaveLength(3)
  })
})
