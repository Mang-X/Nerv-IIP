import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import MesProductionContextPanel from './MesProductionContextPanel.vue'

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
    expect(wrapper.text()).toContain('WC-CNC-01')
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
    expect(forbidden.text()).toContain('无设备效率读取权限，未请求 OEE 数据')

    const sku = mountPanel({ isSkuDimension: true, oeeBuckets: [] })
    expect(sku.text()).toContain('当前没有 SKU 维度的效率权威')
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

    expect(wrapper.text()).toContain('SHIFT-NIGHT')
    expect(wrapper.text()).toContain('—')
    expect(wrapper.text()).toContain('数据不完整')
    expect(wrapper.text()).toContain('缺少或存在冲突的工序标准速率')
    expect(wrapper.text()).not.toContain('0.0%')
  })
})
