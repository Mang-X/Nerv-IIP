import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import LineSideInventoryBalancesPanel from './LineSideInventoryBalancesPanel.vue'

describe('LineSideInventoryBalancesPanel', () => {
  it('桌面表格完整渲染 200 行并由独立分页发出目标页', async () => {
    const items = Array.from({ length: 200 }, (_, index) => ({
      siteCode: 'SITE-A',
      locationCode: `LINE-${index + 1}`,
      skuCode: `SKU-${String(index + 1).padStart(3, '0')}`,
      uomCode: 'pcs',
      onHandQuantity: index + 1,
      reservedQuantity: 0,
      availableQuantity: index + 1,
      lotCount: 1,
      ageDays: 1,
      ageCompleteness: 'complete' as const,
    }))
    const wrapper = mount(LineSideInventoryBalancesPanel, {
      props: {
        error: null,
        items,
        page: 1,
        pageCount: 2,
        pageSize: 200,
        pending: false,
        ready: true,
        total: 201,
      },
    })

    expect(wrapper.findAll('tbody tr')).toHaveLength(200)
    expect(wrapper.text()).toContain('SKU-200')
    expect(wrapper.findAll('nav[aria-label="分页"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="line-side-inventory-pagination"]').exists()).toBe(true)
    expect(wrapper.get('.line-side-inventory-table').classes()).toEqual(
      expect.arrayContaining(['hidden', 'md:block']),
    )
    expect(wrapper.find('.line-side-inventory-table nav[aria-label="分页"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('显示 1–200 / 201 条')

    await wrapper.get('button[aria-label="第 2 页"]').trigger('click')
    expect(wrapper.emitted('updatePage')).toEqual([[2]])
  })

  it.each(['第 2 页加载失败', '线边库存响应页码与请求不一致，请重试。'])(
    '第 2 页错误“%s”清空数据行时仍保留下方分页恢复入口',
    async (message) => {
      const wrapper = mount(LineSideInventoryBalancesPanel, {
        props: {
          error: new Error(message),
          items: [],
          page: 2,
          pageCount: 2,
          pageSize: 200,
          pending: false,
          ready: false,
          total: 201,
        },
      })

      expect(wrapper.get('[role="alert"]').text()).toContain(message)
      expect(wrapper.findAll('nav[aria-label="分页"]')).toHaveLength(1)
      expect(wrapper.find('[data-testid="line-side-inventory-pagination"]').exists()).toBe(true)
      expect(wrapper.find('[data-slot="table-container"]').exists()).toBe(false)
      expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeUndefined()

      await wrapper.get('button[aria-label="上一页"]').trigger('click')
      expect(wrapper.emitted('updatePage')).toEqual([[1]])
    },
  )

  it('分页窗口完全由传入的 composable pageSize 驱动', () => {
    const items = Array.from({ length: 73 }, (_, index) => ({
      siteCode: 'SITE-A',
      locationCode: `LINE-${index + 1}`,
      skuCode: `SKU-${index + 1}`,
      uomCode: 'pcs',
    }))
    const wrapper = mount(LineSideInventoryBalancesPanel, {
      props: {
        error: null,
        items,
        page: 1,
        pageCount: 2,
        pageSize: 73,
        pending: false,
        ready: true,
        total: 146,
      },
    })

    expect(wrapper.findAll('tbody tr')).toHaveLength(73)
    expect(wrapper.text()).toContain('显示 1–73 / 146 条')
    expect(wrapper.findAll('nav[aria-label="分页"]')).toHaveLength(1)
  })
})
