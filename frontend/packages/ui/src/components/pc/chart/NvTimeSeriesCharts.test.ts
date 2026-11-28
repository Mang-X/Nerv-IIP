import { mount } from '@vue/test-utils'
import { VisAxis, VisPlotline, VisXYContainer } from '@unovis/vue'
import { expect, it } from 'vitest'
import NvAreaChart from './NvAreaChart.vue'
import NvLineChart from './NvLineChart.vue'

const data = Array.from({ length: 48 }, (_, index) => ({
  label: String(index),
  value: 58 + index / 10,
}))

it('keeps dense line samples and displays the alarm threshold on the chart', () => {
  const wrapper = mount(NvLineChart, {
    props: {
      data,
      xKey: 'label',
      series: [{ key: 'value', label: '温度' }],
      referenceLines: [{ value: 72, label: '> 72 °C' }],
    },
  })
  expect((wrapper.getComponent(VisXYContainer).props() as { data: unknown[] }).data).toHaveLength(
    48,
  )
  expect(
    wrapper
      .findAllComponents(VisAxis)
      .map((axis) => axis.props() as { type: string; numTicks: number })
      .find((axis) => axis.type === 'x')?.numTicks,
  ).toBe(6)
  expect(wrapper.getComponent(VisPlotline).props()).toMatchObject({
    value: 72,
    labelText: '> 72 °C',
  })
  wrapper.unmount()
})

it('keeps dense area samples while reducing the horizontal tick count', () => {
  const wrapper = mount(NvAreaChart, { props: { data } })
  expect((wrapper.getComponent(VisXYContainer).props() as { data: unknown[] }).data).toHaveLength(
    48,
  )
  expect(
    wrapper
      .findAllComponents(VisAxis)
      .map((axis) => axis.props() as { type: string; numTicks: number })
      .find((axis) => axis.type === 'x')?.numTicks,
  ).toBe(6)
  wrapper.unmount()
})
