import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import FoundationReturnContext from './FoundationReturnContext.vue'
vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: {
      fromFoundation: '1',
      foundationSiteCode: 'SITE-A',
      foundationLineCode: 'LINE-A',
      foundationWorkCenterCode: 'WC-A',
      foundationSkuId: 'SKU-A',
    },
  }),
}))
it('维护后返回完整原范围', () => {
  const wrapper = mount(FoundationReturnContext)
  const url = new URL(wrapper.get('a').attributes('href')!, 'http://localhost')
  expect(url.pathname).toBe('/mes/foundation')
  expect(Object.fromEntries(url.searchParams)).toEqual({
    siteCode: 'SITE-A',
    lineCode: 'LINE-A',
    workCenterCode: 'WC-A',
    skuId: 'SKU-A',
  })
})
