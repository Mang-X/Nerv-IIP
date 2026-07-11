import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import RetryableListError from './RetryableListError.vue'

describe('RetryableListError', () => {
  it('surfaces classified copy for timeout / offline / business errors', () => {
    expect(
      mount(RetryableListError, { props: { error: new RequestTimeoutError() } }).text(),
    ).toContain('网络超时，请检查连接后重试')
    expect(mount(RetryableListError, { props: { error: new OfflineError() } }).text()).toContain(
      '当前离线，请检查网络连接后重试',
    )
    expect(
      mount(RetryableListError, { props: { error: { message: '服务器繁忙' } } }).text(),
    ).toContain('服务器繁忙')
  })

  it('uses the page fallback for a business error without a usable message', () => {
    expect(
      mount(RetryableListError, { props: { error: {}, fallback: '加载失败，请重试。' } }).text(),
    ).toContain('加载失败，请重试。')
  })

  it('always offers a retry (GET is idempotent) and emits on click', async () => {
    const wrapper = mount(RetryableListError, { props: { error: new RequestTimeoutError() } })
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('disables the retry while a refetch is pending', () => {
    const wrapper = mount(RetryableListError, {
      props: { error: new RequestTimeoutError(), pending: true },
    })
    expect(wrapper.get('[data-testid="retry-list"]').attributes('disabled')).toBeDefined()
  })

  it('lets a page keep its own root testId anchor', () => {
    const wrapper = mount(RetryableListError, {
      props: { error: new RequestTimeoutError(), testId: 'error-banner' },
    })
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(true)
  })
})
