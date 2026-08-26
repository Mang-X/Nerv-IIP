import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import LineSideInventoryBalancesPanel from './LineSideInventoryBalancesPanel.vue'

describe('LineSideInventoryBalancesPanel', () => {
  it('桌面表格完整渲染 200 行并由内建 manual 分页发出目标页', async () => {
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
        pending: false,
        ready: true,
        total: 201,
      },
    })

    expect(wrapper.findAll('tbody tr')).toHaveLength(200)
    expect(wrapper.text()).toContain('SKU-200')
    expect(wrapper.findAll('nav[aria-label="分页"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('显示 1–200 / 201 条')

    await wrapper.get('button[aria-label="第 2 页"]').trigger('click')
    expect(wrapper.emitted('updatePage')).toEqual([[2]])
  })
})
