import { mount, flushPromises } from '@vue/test-utils'
import { it, expect, vi } from 'vitest'
import { shallowRef } from 'vue'
import Page from './finance/accounting-period-open.vue'
const state = vi.hoisted(() => ({
  submit: vi.fn().mockResolvedValue({ data: { accountingPeriodId: 'PERIOD-1' } }),
  success: vi.fn(),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({
    query: { date: '2026-10-12', fromFoundation: '1', foundationWorkCenterCode: 'WC-A' },
  }),
}))
vi.mock('@pinia/colada', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@pinia/colada')>()),
  useMutation: () => ({ mutateAsync: state.submit, isLoading: shallowRef(false) }),
}))
vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => ({ organizationId: 'org-001', environmentId: 'env-dev' }),
}))
vi.mock('@/utils/notify', () => ({ notifySuccess: state.success, notifyOperationFailure: vi.fn() }))
it('从缺项日期开立实际期间并保留返回原范围入口', async () => {
  const wrapper = mount(Page, {
    global: { stubs: { BusinessLayout: { template: '<main><slot /></main>' } } },
  })
  expect((wrapper.get('#period-start').element as HTMLInputElement).value).toBe('2026-10-12')
  await wrapper.get('#period-code').setValue('FY26-P10')
  await wrapper.get('#period-start').setValue('2026-10-01')
  await wrapper.get('#period-end').setValue('2026-10-31')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(state.submit).toHaveBeenCalledWith({
    body: {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      periodCode: 'FY26-P10',
      startDate: '2026-10-01',
      endDate: '2026-10-31',
    },
  })
  expect(state.success).toHaveBeenCalled()
  expect(wrapper.get('a').attributes('href')).toContain('workCenterCode=WC-A')
})
