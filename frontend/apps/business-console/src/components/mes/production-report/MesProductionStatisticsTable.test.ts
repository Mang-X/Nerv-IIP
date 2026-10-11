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
