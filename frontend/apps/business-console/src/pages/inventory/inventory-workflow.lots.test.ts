import {
  inventoryState,
  mountInventoryPage,
  resetFixture,
  siteStockState,
} from './inventory-workflow.fixture'
import { beforeEach, describe, expect, it } from 'vitest'
import AvailabilityPage from './availability.vue'
import LotsPage from './lots.vue'

describe('inventory workflow pages', () => {
  beforeEach(resetFixture)

  it('全厂扫描完成且无批次时收起继续扫描入口', async () => {
    inventoryState.initialSkuCode = ''
    siteStockState.noTrackedLines = true
    const wrapper = mountInventoryPage(LotsPage)
    const table = wrapper.get('[data-ui-table]')
    expect(table.attributes('data-empty-message')).toContain('可继续扫描其余物料')
    expect(wrapper.findAll('button').some((button) => button.text() === '继续扫描其余物料')).toBe(
      true,
    )

    siteStockState.hasMore!.value = false
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('已扫描 48/48 个物料')
    expect(table.attributes('data-empty-message')).toBe('已扫描全部物料，暂无批次记录。')
    expect(wrapper.findAll('button').some((button) => button.text() === '继续扫描其余物料')).toBe(
      false,
    )
  })

  it('links inventory lot context to barcode scan records', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)

    const link = wrapper
      .findAll('[data-router-link]')
      .find((item) => item.text().includes('扫码记录'))
    expect(link?.attributes('data-to')).toContain('/barcode/scans')
    expect(link?.attributes('data-to')).toContain('inventory.count')
    expect(link?.attributes('data-to')).toContain('LOT-001')
    // 生产日期 / 保质期 / 效期来源已收进效期单元格的第二行（主要列收敛）。
    const expiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(expiryCell).toContain('2026-07-18')
    expect(expiryCell).toContain('2026-04-20')
    expect(expiryCell).toContain('89 天')
    expect(expiryCell).toContain('系统推导')
    expect(wrapper.text()).toContain('已过期，常规移动需授权放行。')
    expect(wrapper.text()).toContain('同一盘点定位存在多个生产日期或效期，请先缩小到唯一库存台账。')
    expect(wrapper.text()).toContain('该库存行暂不能发起移动，请稍后重试或联系管理员。')
    expect(
      wrapper
        .findAll('button')
        .find((button) => button.text().includes('发起移动'))
        ?.attributes(),
    ).toHaveProperty('disabled')
    expect(wrapper.text()).not.toContain('facade 未提供 total')
    // 台账首屏有三四千行（11 列 × 每行数个 RouterLink），关掉分页会把主线程钉死。
    // 非效期视图走前端切页，因此绝不能再是 manual。
    expect(wrapper.get('[data-ui-table]').attributes('data-pagination')).not.toBe('false')
    expect(wrapper.get('[data-ui-table]').attributes('data-manual')).toBe('false')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    const nearExpiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(nearExpiryCell).toContain('2026-07-25')
    expect(nearExpiryCell).toContain('2026-06-15')
    // 效期预警是服务端分页：交给表格自己出页脚，页数与总数都由调用点喂。
    expect(wrapper.get('[data-ui-table]').attributes('data-manual')).toBe('true')
    expect(wrapper.get('[data-ui-table]').attributes('data-total-items')).toBe('51')
    await wrapper.get('[data-next-page]').trigger('click')
    expect(inventoryState.expiryPage?.value).toBe(2)
    await wrapper.get('[data-page-size]').trigger('click')
    expect(inventoryState.expiryPageSize?.value).toBe(100)
    expect(inventoryState.expiryPage?.value).toBe(1)
  })

  it('renders a facade-backed lot and reservation page with traceability links', async () => {
    const wrapper = mountInventoryPage(LotsPage)

    expect(wrapper.text()).toContain('批次与预留')
    expect(wrapper.text()).toContain('LOT-001')
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.get('[data-cell="reservedQuantity"]').text()).toBe('2')
    // 生产日期 / 保质期 / 效期来源已收进效期单元格的第二行（主要列收敛）。
    const expiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(expiryCell).toContain('2026-07-18')
    expect(expiryCell).toContain('2026-04-20')
    expect(expiryCell).toContain('89 天')
    expect(expiryCell).toContain('系统推导')
    expect(inventoryState.availabilityFilters?.qualityStatus).toBeUndefined()

    const links = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(
      links.some(
        (to) =>
          to.includes('/mes/traceability') && to.includes('batchOrSerial') && to.includes('SN-001'),
      ),
    ).toBe(true)
    const disabledWms = wrapper.findAll('button').find((button) => button.text().trim() === 'WMS')
    expect(disabledWms?.attributes()).toHaveProperty('disabled')
    const blockReason = '已过期，常规移动需授权放行。'
    expect(wrapper.get('[data-cell="expiryStatus"]').text()).toBe('已过期')
    expect(wrapper.get('[data-operation-block-reason]').text()).toContain(blockReason)
    expect(wrapper.text().split(blockReason)).toHaveLength(2)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    const nearExpiryCell = wrapper.get('[data-cell="expiryDate"]').text()
    expect(nearExpiryCell).toContain('2026-07-25')
    expect(nearExpiryCell).toContain('2026-06-15')
    // 效期预警走服务端分页，总数由调用点喂给表格，页脚不再是页面自己那条独立的分页器。
    expect(wrapper.get('[data-ui-table]').attributes('data-manual')).toBe('true')
    expect(wrapper.get('[data-ui-table]').attributes('data-total-items')).toBe('51')
    const nearExpiryLinks = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(nearExpiryLinks.some((to) => to.includes('/barcode/scans'))).toBe(true)
    expect(
      nearExpiryLinks.some(
        (to) => to.includes('/wms/picking') && to.includes('locationCode') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(
      nearExpiryLinks.some(
        (to) =>
          to.includes('/quality/inspections') &&
          to.includes('batchNo') &&
          to.includes('materialLotId') &&
          to.includes('LOT-001'),
      ),
    ).toBe(true)
  })
})
