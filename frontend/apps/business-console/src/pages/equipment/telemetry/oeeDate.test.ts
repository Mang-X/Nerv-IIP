import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, expect, it, vi } from 'vitest'
import { createBusinessConsoleI18n } from '@/i18n'
import { useBusinessContextStore } from '@/stores/businessContext'
import OeePage from './oee.vue'

const requests = vi.hoisted(() => ({ table: vi.fn(), trend: vi.fn() }))
vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  const response = { success: true, data: { buckets: [], totalCount: 0 } }
  return {
    ...actual,
    queryBusinessConsoleTelemetryOeeAggregatesQueryOptions: (options: { query: unknown }) => ({
      key: ['oee-date-table', JSON.stringify(options.query)],
      query: async () => {
        requests.table(options.query)
        return response
      },
    }),
    queryBusinessConsoleTelemetryOeeAggregates: async (options: { query: unknown }) => {
      requests.trend(options.query)
      return { data: response }
    },
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveSite: () => undefined,
    resolveWorkshop: () => undefined,
    resolveLine: () => undefined,
    resolveWorkCenter: () => undefined,
    resolveShift: () => undefined,
  }),
}))
vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ query: { businessDate: '2026-06-18' } }),
  RouterLink: { template: '<a><slot /></a>' },
}))
afterEach(() => {
  vi.clearAllMocks()
})

// PublicContract: #3905 spec r1 / #4389，真实日期交互保持最终查询的 ISO / 空值语义。
it('本地化业务日期选择与清除后，刷新明细和趋势使用原有查询条件', async () => {
  const pinia = createPinia()
  useBusinessContextStore(pinia).patchContext({ organizationId: 'org-1', environmentId: 'env-1' })
  const i18n = createBusinessConsoleI18n({ locale: 'zh-CN' })
  const wrapper = mount(OeePage, {
    attachTo: document.body,
    global: {
      plugins: [pinia, i18n, [PiniaColada, { queryOptions: { gcTime: 0 } }]],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        DirectoryPicker: { template: '<button>全部</button>' },
      },
    },
  })
  await flushPromises()
  const picker = wrapper.get('[data-slot="nv-date-picker"]')
  expect(picker.get('button').text()).toBe('2026/6/18')
  const refresh = wrapper.findAll('button').find((button) => button.text() === '刷新')!
  const expectDate = (businessDate: string | undefined) => {
    for (const request of [requests.table, requests.trend]) {
      expect(request).toHaveBeenLastCalledWith(expect.objectContaining({ businessDate }))
    }
  }
  expectDate('2026-06-18')
  i18n.global.locale.value = 'en-US'
  await flushPromises()
  expect(picker.get('button').text()).toBe('6/18/2026')
  await refresh.trigger('click')
  await flushPromises()
  expectDate('2026-06-18')

  await picker.get('button').trigger('click')
  await flushPromises()
  const day = Array.from(document.querySelectorAll<HTMLButtonElement>('.nv-dp-cell')).find(
    (cell) => cell.textContent?.trim() === '19',
  )!
  day.click()
  await flushPromises()
  expect(picker.get('button').text()).toBe('6/19/2026')
  await refresh.trigger('click')
  await flushPromises()
  expectDate('2026-06-19')

  await picker.get('[aria-label="清除所选日期"]').trigger('click')
  await flushPromises()
  await refresh.trigger('click')
  await flushPromises()
  expectDate(undefined)
  expect(picker.get('button').text()).toBe('选择日期')
  wrapper.unmount()
})
