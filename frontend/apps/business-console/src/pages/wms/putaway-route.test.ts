import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PutawayPage from './putaway.vue'

const state = vi.hoisted(() => ({
  routeQuery: {
    inboundOrderNo: 'IB-1',
    inboundOrderId: 'ib-1',
    create: '1',
  } as Record<string, unknown>,
  createPutaway: vi.fn(),
  permissionCodes: ['business.wms.receipts.read', 'business.wms.receipts.manage'] as string[],
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: state.routeQuery }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

vi.mock('@/composables/useBusinessWms', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  return {
    useWmsPutawayTasks: () => ({
      filters: reactive({ skip: 0, take: 100 }),
      putawayTasks: computed(() => []),
      putawayTasksError: shallowRef(undefined),
      putawayTasksPending: shallowRef(false),
      putawayTasksTotal: computed(() => 0),
      refreshPutawayTasks: vi.fn(),
      createPutaway: state.createPutaway,
      createPutawayPending: shallowRef(false),
      createPutawayError: shallowRef(undefined),
    }),
  }
})

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')
  return {
    usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('100') }),
  }
})

describe('WMS putaway route handoff', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    state.createPutaway.mockResolvedValue(undefined)
    state.permissionCodes = ['business.wms.receipts.read', 'business.wms.receipts.manage']
  })

  it('opens the create flow with the real inbound order id and submits it unchanged', async () => {
    const wrapper = mount(PutawayPage, {
      attachTo: document.body,
      global: {
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          WmsInventoryContextPanel: true,
        },
      },
    })
    await flushPromises()

    expect(document.body.textContent).toContain('新建上架任务')
    expect(document.body.querySelector<HTMLInputElement>('#wms-putaway-inbound')?.value).toBe(
      'ib-1',
    )

    await setInput('#wms-putaway-no', 'PUT-IB-1-01')
    await setInput('#wms-putaway-line', '1')
    await setInput('#wms-putaway-from', 'QA-STAGE-01')
    await setInput('#wms-putaway-to', 'RACK-A-01')
    await setInput('#wms-putaway-qty', '5')
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.createPutaway).toHaveBeenCalledWith('ib-1', {
      taskNo: 'PUT-IB-1-01',
      lineNo: '1',
      fromLocationCode: 'QA-STAGE-01',
      toLocationCode: 'RACK-A-01',
      quantity: 5,
    })

    wrapper.unmount()
  })

  it('requires a positive quantity before calling the create endpoint', async () => {
    const wrapper = mountPutaway()
    await flushPromises()

    await setInput('#wms-putaway-no', 'PUT-IB-1-01')
    await setInput('#wms-putaway-line', '1')
    await setInput('#wms-putaway-from', 'QA-STAGE-01')
    await setInput('#wms-putaway-to', 'RACK-A-01')
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.createPutaway).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('上架数量需为正数')
    wrapper.unmount()
  })

  it('does not auto-open the create form for a read-only WMS principal', async () => {
    state.permissionCodes = ['business.wms.receipts.read']
    const wrapper = mountPutaway()
    await flushPromises()

    expect(document.body.querySelector('#wms-putaway-inbound')).toBeNull()
    expect(wrapper.text()).not.toContain('新建上架任务')
    wrapper.unmount()
  })
})

function mountPutaway() {
  return mount(PutawayPage, {
    attachTo: document.body,
    global: {
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        WmsInventoryContextPanel: true,
      },
    },
  })
}

async function setInput(selector: string, value: string) {
  const input = document.body.querySelector<HTMLInputElement>(selector)
  expect(input).not.toBeNull()
  input!.value = value
  input!.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
}
