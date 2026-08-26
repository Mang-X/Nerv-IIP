import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import LineSideInventoryBalancesPanel from './LineSideInventoryBalancesPanel.vue'

describe('LineSideInventoryBalancesPanel', () => {
  it('桌面表格完整渲染服务端当前页的 200 行且不产生第二套分页', () => {
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
        hasNextPage: true,
        hasPreviousPage: false,
        page: 1,
        pageCount: 2,
        pending: false,
        ready: true,
        total: 201,
      },
    })

    expect(wrapper.findAll('tbody tr')).toHaveLength(200)
    expect(wrapper.text()).toContain('SKU-200')
    expect(wrapper.find('[data-slot="pagination"]').exists()).toBe(false)
  })
})
