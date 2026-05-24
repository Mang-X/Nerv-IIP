import { describe, expect, it } from 'vitest'

import * as ui from './index'

describe('@nerv-iip/ui date and chart readiness exports', () => {
  it('exports date picker primitives from the stable package boundary', () => {
    expect(ui.Calendar).toBeDefined()
    expect(ui.RangeCalendar).toBeDefined()
    expect(ui.DatePicker).toBeDefined()
    expect(ui.DateRangePicker).toBeDefined()
  })

  it('exports chart primitives from the stable package boundary', () => {
    expect(ui.ChartContainer).toBeDefined()
    expect(ui.ChartLegendContent).toBeDefined()
    expect(ui.ChartTooltipContent).toBeDefined()
  })
})
