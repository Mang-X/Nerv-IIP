import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ErrorRetry from './ErrorRetry.vue'

describe('NvMobileErrorRetry', () => {
  it('renders the already-classified message', () => {
    expect(
      mount(ErrorRetry, { props: { message: '网络超时，请检查连接后重试' } }).text(),
    ).toContain('网络超时，请检查连接后重试')
  })

  it('always offers a retry (GET is idempotent) and emits on click', async () => {
    const wrapper = mount(ErrorRetry, { props: { message: 'x' } })
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('disables retry while a refetch is pending', () => {
    const wrapper = mount(ErrorRetry, { props: { message: 'x', pending: true } })
    expect(wrapper.get('[data-testid="retry-list"]').attributes('disabled')).toBeDefined()
  })

  it('lets a page keep its own root testId anchor', () => {
    const wrapper = mount(ErrorRetry, { props: { message: 'x', testId: 'error-banner' } })
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })
})
