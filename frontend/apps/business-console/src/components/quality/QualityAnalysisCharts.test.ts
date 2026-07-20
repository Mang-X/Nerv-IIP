import type { BusinessConsoleQualitySpcControlChartResponse } from '@nerv-iip/api-client'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import QualitySpcCharts from './QualitySpcCharts.vue'
import QualityParetoPanel from './QualityParetoPanel.vue'
import {
  buildParetoChartRows,
  buildSpcChartPresentation,
  formatQualityQuantity,
  type QualityAnalysisBucket,
} from '@/composables/useBusinessQualityAnalysis'

const chart: BusinessConsoleQualitySpcControlChartResponse = {
  subgroups: [
    { index: 1, xbar: 10.1, range: 0.4 },
    { index: 2, xbar: 10.7, range: 0.6 },
    { index: 3, xbar: 11.3, range: 0.8 },
    { index: 4, xbar: 11.5, range: 0.7 },
    { index: 5, xbar: 11.8, range: 0.9 },
  ],
  controlLimits: {
    centerLine: 10.6,
    averageRange: 0.58,
    xbarUpperControlLimit: 11.4,
    xbarLowerControlLimit: 9.8,
    rangeUpperControlLimit: 1.2,
    rangeLowerControlLimit: 0,
  },
  ruleViolations: [
    {
      rule: 'trend-increasing',
      startSubgroupIndex: 2,
      endSubgroupIndex: 5,
      message: '连续上升趋势',
    },
  ],
}

describe('quality analysis chart presentation', () => {
  it('maps the same subgroup values and control limits into Xbar and R chart rows', () => {
    const presentation = buildSpcChartPresentation(chart)

    expect(presentation.xbarRows).toEqual([
      { subgroup: '子组 1', xbar: 10.1, centerLine: 10.6, ucl: 11.4, lcl: 9.8 },
      { subgroup: '子组 2', xbar: 10.7, centerLine: 10.6, ucl: 11.4, lcl: 9.8 },
      { subgroup: '子组 3', xbar: 11.3, centerLine: 10.6, ucl: 11.4, lcl: 9.8 },
      { subgroup: '子组 4', xbar: 11.5, centerLine: 10.6, ucl: 11.4, lcl: 9.8 },
      { subgroup: '子组 5', xbar: 11.8, centerLine: 10.6, ucl: 11.4, lcl: 9.8 },
    ])
    expect(presentation.rangeRows[4]).toEqual({
      subgroup: '子组 5',
      range: 0.9,
      centerLine: 0.58,
      ucl: 1.2,
      lcl: 0,
    })
    expect(presentation.violationMarkers).toEqual([
      {
        key: 'spc-violation-trend-increasing-2-5-1',
        label: '子组 2–5',
        message: '连续上升趋势',
        targetId: 'spc-violation-trend-increasing-2-5-1',
        startSubgroupIndex: 2,
        endSubgroupIndex: 5,
      },
    ])
  })

  it('keeps Pareto rows in defect-quantity order without changing current-window totals', () => {
    const buckets: QualityAnalysisBucket[] = [
      { label: '尺寸超差', count: 2, defectQuantity: 8, sharePercent: 67 },
      { label: '表面划伤', count: 1, defectQuantity: 4, sharePercent: 33 },
    ]

    expect(buildParetoChartRows(buckets)).toEqual([
      { reason: '尺寸超差', defectQuantity: 8 },
      { reason: '表面划伤', defectQuantity: 4 },
    ])
    expect(formatQualityQuantity(4.25)).toBe('4.25')
  })

  it('creates unique safe anchors for duplicate rule intervals', () => {
    const presentation = buildSpcChartPresentation({
      ...chart,
      ruleViolations: [
        { rule: 'trend: increasing', startSubgroupIndex: 2, endSubgroupIndex: 5 },
        { rule: 'trend: increasing', startSubgroupIndex: 2, endSubgroupIndex: 5 },
      ],
    })

    expect(presentation.violationMarkers.map((marker) => marker.targetId)).toEqual([
      'spc-violation-trend-increasing-2-5-1',
      'spc-violation-trend-increasing-2-5-2',
    ])
    expect(new Set(presentation.violationMarkers.map((marker) => marker.key)).size).toBe(2)
  })
})

describe('QualitySpcCharts', () => {
  const stubs = {
    NvLineChart: {
      name: 'NvLineChart',
      props: ['data', 'xKey', 'series', 'height'],
      template: '<div data-testid="line-chart" />',
    },
  }

  it('renders two charts and a color-independent violation locator', () => {
    const wrapper = mount(QualitySpcCharts, {
      props: { chart, pending: false, warmup: false, errorMessage: '' },
      global: { stubs },
    })

    expect(wrapper.findAll('[data-testid="line-chart"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('Xbar 控制图')
    expect(wrapper.text()).toContain('R 控制图')
    expect(wrapper.text()).toContain('判异子组 2–5')
    expect(wrapper.find('[data-testid="spc-violation-band"]').text()).toContain('判异子组 2–5')
    expect(wrapper.find('[data-testid="spc-violation-marker"]').attributes('href')).toBe(
      '#spc-violation-trend-increasing-2-5-1',
    )
  })

  it.each([
    [{ pending: true, warmup: false, errorMessage: '' }, '正在加载 SPC 控制图'],
    [{ pending: false, warmup: true, errorMessage: '' }, '至少形成一个完整子组后显示控制图'],
    [
      { pending: false, warmup: false, errorMessage: '没有权限查看质量分析。' },
      '没有权限查看质量分析。',
    ],
  ])('renders an explicit non-success state', (state, message) => {
    const wrapper = mount(QualitySpcCharts, {
      props: { chart: null, ...state },
      global: { stubs },
    })

    expect(wrapper.text()).toContain(message)
    expect(wrapper.findAll('[data-testid="line-chart"]')).toHaveLength(0)
  })

  it('distinguishes missing control limits from missing subgroups', () => {
    const withoutLimits = mount(QualitySpcCharts, {
      props: {
        chart: { subgroups: chart.subgroups },
        pending: false,
        warmup: false,
        errorMessage: '',
      },
      global: { stubs },
    })
    expect(withoutLimits.text()).toContain('当前范围尚无完整控制限')

    const withoutSubgroups = mount(QualitySpcCharts, {
      props: {
        chart: { controlLimits: chart.controlLimits },
        pending: false,
        warmup: false,
        errorMessage: '',
      },
      global: { stubs },
    })
    expect(withoutSubgroups.text()).toContain('当前范围没有完整子组')
  })

  it('distinguishes complete limits from subgroup values that cannot be plotted', () => {
    const withoutRangeValues = mount(QualitySpcCharts, {
      props: {
        chart: {
          controlLimits: chart.controlLimits,
          subgroups: [{ index: 1, xbar: 10.1 }],
        },
        pending: false,
        warmup: false,
        errorMessage: '',
      },
      global: { stubs },
    })

    expect(withoutRangeValues.text()).toContain('完整子组缺少可绘制的 Range 值')
    expect(withoutRangeValues.text()).not.toContain('尚无完整控制限')
    expect(withoutRangeValues.findAll('[data-testid="line-chart"]')).toHaveLength(1)
  })
})

describe('QualityParetoPanel', () => {
  const stubs = {
    NvBarChart: {
      name: 'NvBarChart',
      props: ['data', 'xKey', 'series', 'height'],
      template: '<div data-testid="bar-chart" />',
    },
    NvDataTable: {
      name: 'NvDataTable',
      props: ['rows'],
      template: '<div data-testid="pareto-table"><slot /></div>',
    },
  }

  it('labels the Pareto chart as a current-return-window view and keeps its audit table', () => {
    const rows: QualityAnalysisBucket[] = [
      { label: '尺寸超差', count: 2, defectQuantity: 8, sharePercent: 67 },
      { label: '表面划伤', count: 1, defectQuantity: 4, sharePercent: 33 },
    ]
    const wrapper = mount(QualityParetoPanel, {
      props: { rows, pending: false },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('当前返回窗口缺陷 Pareto')
    expect(wrapper.text()).toContain('不是全量历史趋势')
    expect(wrapper.find('[data-testid="bar-chart"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="pareto-table"]').exists()).toBe(true)
  })

  it('renders a truthful no-NCR state without a chart', () => {
    const wrapper = mount(QualityParetoPanel, {
      props: { rows: [], pending: false },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('当前返回窗口没有 NCR')
    expect(wrapper.find('[data-testid="bar-chart"]').exists()).toBe(false)
  })

  it('does not render a no-data table while the request failed', () => {
    const wrapper = mount(QualityParetoPanel, {
      props: { rows: [], pending: false, errorMessage: '网络异常，请检查连接后重试。' },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('网络异常，请检查连接后重试。')
    expect(wrapper.text()).not.toContain('当前返回窗口没有 NCR')
    expect(wrapper.find('[data-testid="pareto-table"]').exists()).toBe(false)
  })

  it('shows an explicit loading placeholder instead of a blank chart card', () => {
    const wrapper = mount(QualityParetoPanel, {
      props: { rows: [], pending: true },
      global: { stubs },
    })

    expect(wrapper.text()).toContain('正在加载当前返回窗口缺陷数据')
  })
})
