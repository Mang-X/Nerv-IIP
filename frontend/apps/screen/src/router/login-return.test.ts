import { expect, it, vi } from 'vitest'
import { screenLoginTarget } from './index'

vi.mock('vue-router/auto-routes', async (original) => ({
  ...(await original<typeof import('vue-router/auto-routes')>()),
  handleHotUpdate: vi.fn(),
}))

it('接班后返回当前岗位有权查看的原屏；没有该读权时回门厅', () => {
  const target = '/equipment?from=wall'
  expect(screenLoginTarget(target, ['business.wms.shipments.read'])).toBe('/')
  expect(screenLoginTarget(target, ['business.iiot.alarms.read'])).toBe(target)
  expect(screenLoginTarget('/andon', ['business.wms.shipments.read'])).toBe('/')
  expect(screenLoginTarget('/andon', ['business.mes.operations.read'])).toBe('/andon')
  expect(screenLoginTarget('/line/WC-A', [])).toBe('/line/WC-A')
})
