import { mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import SchedulingDaySchedule from './SchedulingDaySchedule.vue'

const plan = {
  planId: 'PLAN-20260930-01',
  status: 'released' as const,
  assignments: [
    {
      assignmentId: 'overnight',
      orderId: 'WO-ROD-01',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'CNC-01',
      workCenterId: 'WC-CNC',
      startUtc: new Date(2026, 8, 29, 23).toISOString(),
      endUtc: new Date(2026, 8, 30, 2).toISOString(),
    },
    {
      assignmentId: 'ends-at-midnight',
      orderId: 'WO-ROD-02',
      operationId: 'OP-20',
      resourceId: 'CNC-02',
      workCenterId: 'WC-CNC',
      startUtc: new Date(2026, 8, 29, 20).toISOString(),
      endUtc: new Date(2026, 8, 30).toISOString(),
    },
    {
      assignmentId: 'other-center',
      orderId: 'WO-ASSEMBLY-01',
      operationId: 'OP-30',
      resourceId: 'LINE-01',
      workCenterId: 'WC-ASSEMBLY',
      startUtc: new Date(2026, 8, 30, 8).toISOString(),
      endUtc: new Date(2026, 8, 30, 10).toISOString(),
    },
  ],
}
afterEach(() => {
  vi.restoreAllMocks()
  document.body.innerHTML = ''
})

it('shows overlapping assignments on the selected local day and prints only the selected work center sheet', async () => {
  // #4085: a start-date-only predicate loses the overnight operation; <= includes an ended operation.
  const print = vi.spyOn(window, 'print').mockImplementation(() => {})
  const wrapper = mount(SchedulingDaySchedule, { props: { plan }, attachTo: document.body })
  await wrapper.get('input[type="date"]').setValue('2026-09-30')
  await wrapper.get('select').setValue('WC-CNC')
  expect(wrapper.text()).toContain('WO-ROD-01')
  expect(wrapper.text()).not.toContain('WO-ROD-02')
  expect(wrapper.text()).not.toContain('WO-ASSEMBLY-01')
  await wrapper.get('[data-testid="print-day-schedule"]').trigger('click')
  const sheet = document.querySelector('.scheduling-day-print')!
  expect(sheet.textContent).toContain('2026-09-30')
  expect(sheet.textContent).toContain('WC-CNC')
  expect(sheet.textContent).toContain('已发布')
  expect(sheet.textContent).toContain('OP-10')
  expect(sheet.textContent).toContain('CNC-01')
  expect(sheet.textContent).toContain('2026-09-29 23:00')
  expect(sheet.textContent).toContain('2026-09-30 02:00')
  expect(sheet.textContent).not.toContain('WO-ASSEMBLY-01')
  expect(print).toHaveBeenCalledOnce()
  wrapper.unmount()
})
