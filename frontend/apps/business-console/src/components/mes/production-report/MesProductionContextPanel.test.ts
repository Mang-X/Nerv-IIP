import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import MesProductionContextPanel from './MesProductionContextPanel.vue'

// 主数据名录：编码 → 名称；名录里没有的编码原样显示。
vi.mock('@/composables/useMasterDataDisplayNames', () => {
  const names: Record<string, string> = {
    'WC-CNC-01': '数控车削中心',
    'SHIFT-NIGHT': '夜班',
    'SITE-001': '一号工厂',
  }
  const resolve = (code?: string | null) => (code ? names[code] : undefined)
  return {
    useMasterDataDisplayNames: () => ({
      resolveSite: resolve,
      resolveShift: resolve,
      resolveWorkCenter: resolve,
    }),
  }
})

function mountPanel(overrides: Record<string, unknown> = {}) {
  return mount(MesProductionContextPanel, {
    props: {
      canReadWip: true,
      wipState: 'ready',
      wipTotal: 4,
      wipRows: [
        {
          workOrderNo: 'WO-20260831-0042',
          operationTaskNo: 'WO-20260831-0042-OP-20',
          workCenterCode: 'WC-CNC-01',
        },
      ],
      canReadOee: true,
      isSkuDimension: false,
      oeePending: false,
      oeeError: undefined,
      oeeBuckets: [
        {
          dimension: 'workCenter',
          dimensionValue: 'WC-CNC-01',
          performanceRate: 0.812,
          isDegraded: false,
        },
      ],
      ...overrides,
    },
  })
}

describe('MES production report context panel', () => {
  it('shows current WIP and producer-owned performance rate without recomputing it', () => {
    const wrapper = mountPanel()

    expect(wrapper.text()).toContain('当前在制')
    expect(wrapper.text()).toContain('4 个在制工序')
    expect(wrapper.text()).toContain('WO-20260831-0042-OP-20')
    expect(wrapper.text()).toContain('81.2%')
    expect(wrapper.text()).toContain('数控车削中心')
    expect(wrapper.text()).not.toContain('WC-CNC-01')
  })

  it('never shows system ids when the work order or task has no readable number', () => {
    const wrapper = mountPanel({
      wipRows: [
        {
          workOrderId: '01a0e1f2-f2ad-7544-8788-425020c82e15',
          operationTaskId: '01a0e1f2-f2ad-7544-8788-425020c82e16',
          workCenterId: '01a0e1f2-f2ad-7544-8788-425020c82e17',
        },
      ],
    })

    expect(wrapper.text()).toContain('未编号工序')
    expect(wrapper.text()).toContain('未编号工单')
    expect(wrapper.text()).toContain('未标工作中心')
    expect(wrapper.text()).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-/)
  })

  it('labels day and shift rows with the business date and master-data names', () => {
    const wrapper = mountPanel({
      oeeBuckets: [
        {
          dimension: 'day',
          dimensionValue: 'SITE-001',
          businessDate: '2026-09-26',
          performanceRate: 0.8,
          isDegraded: false,
        },
        {
          dimension: 'shift',
          dimensionValue: 'SHIFT-NIGHT',
          businessDate: '2026-09-26',
          performanceRate: 0.7,
          isDegraded: false,
        },
        {
          dimension: 'shift',
          dimensionValue: 'SHIFT-MIDDLE',
          businessDate: '2026-09-26',
          performanceRate: 0.6,
          isDegraded: false,
        },
      ],
    })

    expect(wrapper.text()).toContain('2026-09-26 · 一号工厂')
    expect(wrapper.text()).toContain('夜班 · 2026-09-26')
    expect(wrapper.text()).toContain('SHIFT-MIDDLE · 2026-09-26')
    expect(wrapper.text()).not.toContain('SITE-001')
  })

  it('keeps WIP error distinct from a real empty snapshot', () => {
    const error = mountPanel({ wipState: 'error', wipTotal: 0, wipRows: [] })
    expect(error.text()).toContain('当前在制读取失败，无法判断现场状态')
    expect(error.text()).not.toContain('当前没有在制工序')

    const empty = mountPanel({ wipState: 'ready', wipTotal: 0, wipRows: [] })
    expect(empty.text()).toContain('当前没有在制工序')
  })

  it('states the OEE permission boundary and the SKU authority boundary explicitly', () => {
    const forbidden = mountPanel({ canReadOee: false, oeeBuckets: [] })
    expect(forbidden.text()).toContain('没有查看设备 OEE 的权限')

    const sku = mountPanel({ isSkuDimension: true, oeeBuckets: [] })
    expect(sku.text()).toContain('按物料统计时不提供设备性能率')
  })

  it('preserves missing and degraded OEE values instead of presenting zero or complete data', () => {
    const wrapper = mountPanel({
      oeeBuckets: [
        {
          dimension: 'shift',
          dimensionValue: 'SHIFT-NIGHT',
          performanceRate: null,
          isDegraded: true,
          degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
        },
      ],
    })

    expect(wrapper.text()).toContain('夜班')
    expect(wrapper.text()).toContain('—')
    expect(wrapper.text()).toContain('数据不完整')
    expect(wrapper.text()).toContain('缺少或存在冲突的工序标准速率')
    expect(wrapper.text()).not.toContain('0.0%')
  })
})
