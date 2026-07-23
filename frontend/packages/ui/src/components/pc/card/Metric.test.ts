import { mount } from '@vue/test-utils'
import { beforeAll, describe, expect, it } from 'vitest'
import NvMetricCard from './NvMetricCard.vue'
import NvMetricRing from './NvMetricRing.vue'
import NvMetricStrip from './NvMetricStrip.vue'

// Locks the parts a snapshot/typecheck can't: tone derivation, clamping, the
// structured footers replacing free text, and the action/facet emits that make
// a KPI card an entry point rather than a dead end.

beforeAll(() => {
  // NvAreaChart (unovis) observes size; not exercised here but keep it safe.
  if (!globalThis.ResizeObserver) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
})

describe('NvMetricCard 变体契约', () => {
  it('delta 方向派生语义 tone，可被 override 表达「涨了但是坏事」', () => {
    const up = mount(NvMetricCard, {
      props: {
        label: '一次合格率',
        value: '98.6',
        unit: '%',
        trend: { value: '0.4pt', direction: 'up' },
      },
    })
    expect(up.html()).toContain('text-success-strong')

    // 超期工单 +2：方向朝上但语义为坏，override tone=danger 应压过默认的 success
    const badUp = mount(NvMetricCard, {
      props: {
        label: '超期工单',
        value: 6,
        trend: { value: '2', direction: 'up', tone: 'danger' },
      },
    })
    expect(badUp.html()).toContain('text-destructive-strong')
    expect(badUp.html()).not.toContain('text-success-strong')
  })

  it('alert 危险态浸染卡面并把数值染红，动作按钮触发 action 事件', async () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'alert',
        tone: 'danger',
        label: '超期未完工',
        value: 6,
        unit: '单',
        status: { label: '需处理', tone: 'danger' },
        footStart: '最久已超期 3 天',
        action: { label: '去处理' },
      },
    })
    expect(wrapper.html()).toContain('bg-destructive/[0.04]')
    // 数值染红
    expect(wrapper.find('p.text-2xl').classes()).toContain('text-destructive-strong')
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('action')).toHaveLength(1)
  })

  it('breakdown 每个分段都出一条带计数的图例', () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'breakdown',
        label: '在制工单',
        value: 38,
        segments: [
          { label: '进行中', value: 24, tone: 'brand' },
          { label: '待派工', value: 9, tone: 'neutral' },
          { label: '超期', value: 2, tone: 'danger' },
        ],
      },
    })
    const legend = wrapper.findAll('li')
    expect(legend).toHaveLength(3)
    expect(legend[0].text()).toContain('进行中')
    expect(legend[0].text()).toContain('24')
    expect(wrapper.html()).toContain('bg-destructive')
  })

  it('target 进度夹在 0–100，默认目标刻度在条末，footStart/footEnd 落位', () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'target',
        label: '本月产量达成',
        value: '13,847',
        unit: '件',
        targetLabel: '目标 15,000 件',
        progress: 92.3,
        footStart: '达成 92.3%',
        footEnd: '缺口 1,153 件 · 剩 5 天',
      },
    })
    const fill = wrapper.find('.nv-metric-bar > div')
    expect(fill.attributes('style')).toContain('width: 92.3%')
    expect(wrapper.text()).toContain('目标 15,000 件')
    expect(wrapper.text()).toContain('缺口 1,153 件 · 剩 5 天')

    const over = mount(NvMetricCard, {
      props: { variant: 'target', label: '履约', value: '1', progress: 140 },
    })
    expect(over.find('.nv-metric-bar > div').attributes('style')).toContain('width: 100%')
  })

  it('bars 每个数据点一根柱，当前柱被强调', () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'bars',
        label: '日产量',
        value: '12,480',
        unit: '件',
        series: [7050, 8680, 6370, 9760, 8130, 11250, 12480],
        currentIndex: 6,
        footStart: '07-17',
        footEnd: '今日',
      },
    })
    const bars = wrapper.findAll('.nv-metric-bars > span')
    expect(bars).toHaveLength(7)
    // 当前柱实色，非当前柱轻量
    expect(bars[6].classes()).toContain('bg-brand')
    expect(bars[0].classes()).toContain('bg-brand/30')
  })

  // 回归：tone + 透明度决不能拼接得到（`bg-${tone}/70` Tailwind 扫不到 → 柱子没背景色）
  it('bars 的 tone 覆盖落在字面类名上，非当前柱也有背景色', () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'bars',
        label: '设备报警次数',
        value: 14,
        series: [5, 4, 6, 5, 7, 6, 14],
        currentIndex: 6,
        barTones: ['neutral', 'warning', 'neutral', 'neutral', 'neutral', 'neutral', 'danger'],
      },
    })
    const bars = wrapper.findAll('.nv-metric-bars > span')
    expect(bars[1].classes()).toContain('bg-warning/70')
    expect(bars[6].classes()).toContain('bg-destructive')
  })

  it('breakdown 悬浮图例项，联动淡出其余分段', async () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'breakdown',
        label: '在制工单',
        value: 38,
        segments: [
          { label: '进行中', value: 24, tone: 'brand' },
          { label: '待派工', value: 9, tone: 'neutral' },
          { label: '超期', value: 2, tone: 'danger' },
        ],
      },
    })
    const dimmed = () =>
      wrapper.findAll('.nv-metric-slice').filter((s) => s.classes().includes('nv-metric-dim'))
    expect(dimmed()).toHaveLength(0)

    await wrapper.findAll('li')[0].trigger('mousemove', { clientX: 10, clientY: 10 })
    // 分段条 3 + 图例 3 = 6 个 slice；命中下标的那两个保持高亮，其余 4 个淡出
    expect(dimmed()).toHaveLength(4)

    await wrapper.findAll('li')[0].trigger('mouseleave')
    expect(dimmed()).toHaveLength(0)
  })

  it('bars / target 给键盘与读屏用户留下文本等价物（微图本身只响应指针）', () => {
    const bars = mount(NvMetricCard, {
      props: {
        variant: 'bars',
        label: '日产量',
        value: '12,480',
        series: [7050, 8680, 12480],
        seriesLabels: ['07-21', '07-22', '07-23'],
        seriesUnit: ' 件',
      },
    })
    const viz = bars.find('[role="img"]')
    expect(viz.exists()).toBe(true)
    expect(viz.attributes('aria-label')).toContain('07-23: 12480 件')

    const target = mount(NvMetricCard, {
      props: {
        variant: 'target',
        label: '本月产量达成',
        value: '13,847',
        unit: '件',
        targetLabel: '目标 15,000 件',
        progress: 92.3,
      },
    })
    const meter = target.find('[role="progressbar"]')
    expect(meter.attributes('aria-valuenow')).toBe('92.3')
    expect(meter.attributes('aria-valuemax')).toBe('100')
    expect(meter.attributes('aria-valuetext')).toContain('92.3%')
  })

  it('facets 点击维度 chip 抛出 facet 事件，异常维度被染色', async () => {
    const wrapper = mount(NvMetricCard, {
      props: {
        variant: 'facets',
        label: '开放 NCR',
        value: 7,
        facets: [
          { label: '待处置', value: 3, tone: 'danger' },
          { label: '处置中', value: 2 },
        ],
      },
    })
    const chips = wrapper.findAll('button')
    expect(chips[0].classes().join(' ')).toContain('text-destructive-strong')
    await chips[0].trigger('click')
    expect(wrapper.emitted('facet')?.[0]?.[0]).toMatchObject({ label: '待处置', value: 3 })
  })

  it('保留已弃用的 hint（默认变体），77 个存量页面不破', () => {
    const wrapper = mount(NvMetricCard, {
      props: { label: '在制工单', value: 38, hint: '较昨日 +5' },
    })
    expect(wrapper.text()).toContain('较昨日 +5')
  })
})

describe('NvMetricRing / NvMetricStrip', () => {
  const ringSegments = [
    { label: '进行中', value: 24, tone: 'brand' as const },
    { label: '待派工', value: 9, tone: 'neutral' as const },
    { label: '超期', value: 2, tone: 'danger' as const },
  ]

  it('ring 每个分段一条弧，弧长按占比派生并首尾相接', () => {
    const wrapper = mount(NvMetricRing, {
      props: { label: '在制工单', value: 35, centerCaption: '总计', segments: ringSegments },
    })
    const circ = 2 * Math.PI * 36
    const gap = 2.5
    const arcs = wrapper.findAll('.nv-ring-seg')
    expect(arcs).toHaveLength(3)

    // 第一段：24/35 的弧长（扣掉分段间隙），且从 0 起画
    const span0 = (24 / 35) * circ
    expect(arcs[0].attributes('stroke-dasharray')).toBe(`${span0 - gap} ${circ - (span0 - gap)}`)
    expect(arcs[0].attributes('stroke-dashoffset')).toBe('0')
    // 第二段接在第一段之后（偏移 = 前一段的完整占比，不含间隙）
    expect(arcs[1].attributes('stroke-dashoffset')).toBe(String(-span0))
  })

  it('ring 图例给出每段计数与占比，中心默认显示总数', () => {
    const wrapper = mount(NvMetricRing, {
      props: { label: '在制工单', value: 35, centerCaption: '总计', segments: ringSegments },
    })
    const rows = wrapper.findAll('.nv-ring-row')
    expect(rows).toHaveLength(3)
    expect(rows[0].text()).toContain('进行中')
    expect(rows[0].text()).toContain('24')
    expect(rows[0].text()).toContain('68.6%') // 24/35
    expect(wrapper.find('.nv-ring-center').text()).toContain('35')
    expect(wrapper.find('.nv-ring-center').text()).toContain('总计')
  })

  it('ring 悬浮图例项：其余分段淡出，中心切到该段读数', async () => {
    const wrapper = mount(NvMetricRing, {
      props: { label: '在制工单', value: 35, centerCaption: '总计', segments: ringSegments },
    })
    const dimmed = () => wrapper.findAll('.nv-ring-dim').length // 弧 + 图例行 一起淡出
    expect(dimmed()).toBe(0)

    await wrapper.findAll('.nv-ring-row')[2].trigger('mouseenter')
    // 3 弧 + 3 行 = 6 个可淡出元素，命中的那两个保持高亮
    expect(dimmed()).toBe(4)
    const center = wrapper.find('.nv-ring-center').text()
    expect(center).toContain('2') // 超期 = 2
    expect(center).toContain('超期 · 5.7%')

    await wrapper.findAll('.nv-ring-row')[2].trigger('mouseleave')
    expect(dimmed()).toBe(0)
    expect(wrapper.find('.nv-ring-center').text()).toContain('总计')
  })

  it('strip 每格一指标，valueTone 染色，向上 meta 带趋势图标', () => {
    const wrapper = mount(NvMetricStrip, {
      props: {
        cells: [
          { label: '今日产量', value: '12,480', unit: '件', meta: '4.9% 环比', metaTone: 'up' },
          { label: '超期工单', value: 2, unit: '单', valueTone: 'danger', meta: '最久超期 3 天' },
        ],
      },
    })
    const cells = wrapper.findAll('.flex-1')
    expect(cells).toHaveLength(2)
    expect(cells[1].find('p.text-xl').classes()).toContain('text-destructive-strong')
    // 向上 meta 出趋势图标（svg）
    expect(cells[0].find('svg').exists()).toBe(true)
  })
})
