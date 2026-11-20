import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

import { useBusinessContextStore } from '@/stores/businessContext'

import CountsPage from './counts.vue'
import InboundPage from './inbound.vue'
import OutboundPage from './outbound.vue'

const pages = [
  { name: '收货入库', route: 'inbound', page: InboundPage, list: 'inbound-orders' },
  { name: '出库发货', route: 'outbound', page: OutboundPage, list: 'outbound-orders' },
  { name: '盘点执行', route: 'counts', page: CountsPage, list: 'count-executions' },
]

afterEach(() => configureApiClient())

describe('WMS PC 关键字筛选契约', () => {
  it.each(pages)('$name：搜索与清除关键字均从服务端第一页取数', async ({ route, page, list }) => {
    const requests: URLSearchParams[] = []
    configureApiClient({
      baseUrl: 'http://gateway.test',
      fetch: async (input) => {
        const url = new URL(new Request(input).url)
        if (url.pathname === `/api/business-console/v1/wms/${list}`) {
          requests.push(url.searchParams)
          return Response.json({ success: true, data: { items: [], total: 30 } })
        }
        if (url.pathname.startsWith('/api/business-console/v1/wms/work-scopes/')) {
          return Response.json({
            success: true,
            data: {
              items: [{ scopeKind: 'site', scopeId: 'SITE-001', displayName: '一号仓库' }],
            },
          })
        }
        return Response.json({ success: true, data: { items: [], total: 0 } })
      },
    })

    const pinia = createPinia()
    useBusinessContextStore(pinia).patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: page }],
    })
    await router.push(`/wms/${route}`)
    const wrapper = mount(RouterView, {
      global: {
        plugins: [pinia, router, [PiniaColada, { queryOptions: { gcTime: 0, staleTime: 0 } }]],
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          WmsInventoryContextPanel: true,
          WmsOperationalCandidateFilters: true,
        },
      },
    })
    await flushPromises()

    function expectListRequest(keyword: string | null, skip: string) {
      expect(requests.at(-1)?.getAll('keyword')).toEqual(keyword === null ? [] : [keyword])
      expect(requests.at(-1)?.get('skip')).toBe(skip)
    }

    const nextPage = () => wrapper.get('button[aria-label="下一页"]').trigger('click')
    const search = wrapper.get('input[aria-label="关键字搜索"]')

    await nextPage()
    await flushPromises()
    expectListRequest(null, '10')

    await search.setValue('SKU-2407')
    await flushPromises()
    expectListRequest('SKU-2407', '0')

    await nextPage()
    await flushPromises()
    expectListRequest('SKU-2407', '10')

    await search.setValue('')
    await flushPromises()
    expectListRequest(null, '0')
    expect(wrapper.get('button[aria-current="page"]').attributes('aria-label')).toBe('第 1 页')
  })
})
