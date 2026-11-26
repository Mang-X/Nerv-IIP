import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessWorkbenchSummary } from './useBusinessWorkbench'

const { requestSummary } = vi.hoisted(() => ({ requestSummary: vi.fn() }))

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleWorkbenchSummaryQueryOptions: ({ query }: { query: object }) => ({
    key: ['workbench-summary', query],
    query: requestSummary,
  }),
}))

function summaryResponse(referenceId: string, messageId: string) {
  return {
    success: true,
    data: {
      todos: { status: 'available', items: [{ referenceId }] },
      messages: { status: 'available', items: [{ messageId }] },
    },
  }
}

it('isolates workbench data on a principal change and refreshes the current principal', async () => {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.accessToken = 'synthetic-session'
  auth.principal = { principalType: 'user', principalId: 'operator-a' }
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-workbench',
    environmentId: 'env-workbench',
  })
  const incoming = Promise.withResolvers<ReturnType<typeof summaryResponse>>()
  const outgoing = Promise.withResolvers<ReturnType<typeof summaryResponse>>()
  requestSummary
    .mockResolvedValueOnce(summaryResponse('PO-A', 'MSG-A'))
    .mockReturnValueOnce(outgoing.promise)
    .mockReturnValueOnce(incoming.promise)
    .mockResolvedValueOnce(summaryResponse('PO-B-UPDATED', 'MSG-B-UPDATED'))

  const wrapper = mount(
    defineComponent({
      setup() {
        const workbench = useBusinessWorkbenchSummary()
        return () =>
          h('div', [
            h('button', { onClick: workbench.refreshWorkbenchSummary }, '刷新'),
            h('p', workbench.todoItems.value.map((item) => item.referenceId).join()),
            h('p', workbench.messageItems.value.map((item) => item.messageId).join()),
          ])
      },
    }),
    { global: { plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 300_000 } }]] } },
  )
  const visibleItems = () => wrapper.findAll('p').map((item) => item.text())

  await flushPromises()
  expect(visibleItems()).toEqual(['PO-A', 'MSG-A'])

  await wrapper.get('button').trigger('click')
  expect(requestSummary).toHaveBeenCalledTimes(2)

  auth.principal = { principalType: 'user', principalId: 'operator-b' }
  await flushPromises()
  expect(visibleItems()).toEqual(['', ''])
  expect(requestSummary).toHaveBeenCalledTimes(3)

  incoming.resolve(summaryResponse('PO-B', 'MSG-B'))
  await flushPromises()
  expect(visibleItems()).toEqual(['PO-B', 'MSG-B'])

  outgoing.resolve(summaryResponse('PO-A-LATE', 'MSG-A-LATE'))
  await flushPromises()
  expect(visibleItems()).toEqual(['PO-B', 'MSG-B'])

  await wrapper.get('button').trigger('click')
  await flushPromises()
  expect(visibleItems()).toEqual(['PO-B-UPDATED', 'MSG-B-UPDATED'])

  auth.clearSession()
  await wrapper.get('button').trigger('click')
  await flushPromises()
  expect(visibleItems()).toEqual(['', ''])
  expect(requestSummary).toHaveBeenCalledTimes(4)
})
