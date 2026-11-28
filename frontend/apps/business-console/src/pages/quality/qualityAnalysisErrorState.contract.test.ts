import { configureApiClient } from '@nerv-iip/api-client'
import { NvMetricCard } from '@nerv-iip/ui'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'

import { useBusinessContextStore } from '@/stores/businessContext'

import AnalysisPage from './analysis.vue'

afterEach(() => configureApiClient())

describe('质量分析 NCR 读取失败', () => {
  it('报告加载失败并隐藏 NCR 汇总，刷新成功后显示真实空结果', async () => {
    let ncrUnavailable = true
    configureApiClient({
      baseUrl: 'http://gateway.test',
      fetch: async (input) => {
        const { pathname } = new URL(new Request(input).url)
        if (pathname === '/api/business-console/v1/quality/ncrs' && ncrUnavailable) {
          return Response.json({ title: 'Service unavailable' }, { status: 503 })
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
      routes: [{ path: '/:pathMatch(.*)*', component: AnalysisPage }],
    })
    await router.push('/quality/analysis')
    const wrapper = mount(RouterView, {
      global: {
        plugins: [pinia, router, [PiniaColada, { queryOptions: { gcTime: 0 } }]],
        stubs: { BusinessLayout: { template: '<main><slot /></main>' } },
      },
    })
    await flushPromises()

    const ncrSummary = [
      '0 条 NCR',
      '不合格品处置构成',
      '缺陷数量',
      '尚未处置',
      '当前分析时间范围缺陷 Pareto',
      '当前分析时间范围内没有物料维度',
      '当前分析时间范围内没有来源维度',
    ]
    expect(wrapper.get('[role="alert"]').text()).toContain('NCR 数据加载失败')
    expect(wrapper.text()).toContain('控制图台账')
    const violationCard = wrapper
      .findAllComponents(NvMetricCard)
      .find((card) => card.props('label') === '判异点')
    expect(violationCard?.text()).toContain('暂无样本')
    expect(violationCard?.props('status')).toEqual({ label: '暂无样本', tone: 'neutral' })
    for (const text of ncrSummary) expect(wrapper.text()).not.toContain(text)

    ncrUnavailable = false
    const refresh = wrapper.findAll('button').find((button) => button.text() === '刷新')
    expect(refresh).toBeDefined()
    await refresh!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    for (const text of ncrSummary) expect(wrapper.text()).toContain(text)
  })
})
