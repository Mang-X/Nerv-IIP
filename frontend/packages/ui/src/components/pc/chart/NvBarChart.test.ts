import { flushPromises, mount } from '@vue/test-utils'
import { VisAxis, VisCrosshair, VisGroupedBar, VisXYContainer } from '@unovis/vue'
import type { Axis } from '@unovis/ts/components/axis'
import type { Crosshair } from '@unovis/ts/components/crosshair'
import { expect, it, vi } from 'vitest'
import NvBarChart from './NvBarChart.vue'

it('同步周月切换的横轴和提示，并让 Crosshair 读取柱形系列数值', async () => {
  const warn = vi.spyOn(console, 'warn')
  const week = [{ period: '26-11-30周', demand: 12, supply: 7 }]
  type Row = (typeof week)[number]
  const month = [{ period: '26-12月', demand: 36, supply: 21 }]
  const wrapper = mount(NvBarChart, {
    props: {
      data: week,
      xKey: 'period',
      series: [
        { key: 'demand', label: '毛需求' },
        { key: 'supply', label: '补充' },
      ],
    },
  })
  try {
    for (const [data, period, demand, supply] of [
      [week, '26-11-30周', 12, 7],
      [month, '26-12月', 36, 21],
      [week, '26-11-30周', 12, 7],
    ] as const) {
      await wrapper.setProps({ data })
      await flushPromises()
      const { data: renderedData } = wrapper.getComponent(VisXYContainer).props() as {
        data: Row[]
      }
      const row = renderedData[0]!
      const { component: axis } = wrapper.findAllComponents(VisAxis)[0]!.vm as unknown as {
        component: Axis<Row>
      }
      const { component: crosshair } = wrapper.getComponent(VisCrosshair).vm as unknown as {
        component: Crosshair<Row>
      }
      const bars = wrapper.getComponent(VisGroupedBar).props() as {
        y: ((item: Row) => number)[]
      }
      const points = crosshair.config.y as ((item: Row) => number)[]
      expect(axis.config.tickFormat?.(0, 0, [0])).toBe(period)
      expect(bars.y.map((value) => value(row))).toEqual([demand, supply])
      expect(points.map((value) => value(row))).toEqual([demand, supply])
      expect(crosshair.config.template?.(row, 0, renderedData, 0)).toContain(period)
    }
    expect(warn).not.toHaveBeenCalledWith(
      expect.stringContaining('Y accessors have not been configured'),
    )
  } finally {
    wrapper.unmount()
    warn.mockRestore()
  }
})
