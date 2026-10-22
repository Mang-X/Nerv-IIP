import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'
import WmsAssignWorkPoolDialog from './WmsAssignWorkPoolDialog.vue'

const state = vi.hoisted(() => ({
  assign: vi.fn(() => Promise.resolve({})),
  pools: [] as Array<{
    poolCode: string
    displayName: string
    siteCode: string
    members: Array<{ principalId: string }>
  }>,
}))

vi.mock('@/composables/useWmsWorkPools', () => ({
  useWmsWorkPools: () => ({
    pools: computed(() => state.pools),
    poolsPending: shallowRef(false),
    assign: state.assign,
    assignPending: shallowRef(false),
    newIdempotencyKey: () => 'assign-intent-1',
  }),
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessWorkers: () => ({
    workers: computed(() => [
      { userId: 'user-emp-049', displayName: '李仓管', employeeNo: 'EMP-049' },
    ]),
  }),
}))
vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const pickerStub = {
  props: ['modelValue', 'options', 'id', 'showCode'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :data-options="(options ?? []).map((o) => o.value).join(\',\')" :data-show-code="String(showCode ?? true)" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

function mountDialog() {
  return mount(WmsAssignWorkPoolDialog, {
    attachTo: document.body,
    props: {
      open: true,
      target: 'inbound',
      resourceId: 'ib-1',
      resourceLabel: '入库单 IB-20260928-000001',
      siteCode: 'SITE-001',
      version: 3,
    },
    global: { stubs: { NvEntityPicker: pickerStub, RouterLink: true } },
  })
}

describe('WmsAssignWorkPoolDialog（#3849）', () => {
  beforeEach(() => {
    document.body.innerHTML = ''
    state.assign.mockClear()
    state.pools = [
      {
        poolCode: 'WP-0001',
        displayName: '收货组',
        siteCode: 'SITE-001',
        members: [{ principalId: 'user-emp-049' }],
      },
      { poolCode: 'WP-0002', displayName: '外厂收货组', siteCode: 'SITE-002', members: [] },
    ]
  })

  it('只列本工厂的作业池，唯一一个时直接选中并按单据版本提交分配', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    const pool = document.body.querySelector<HTMLInputElement>('#wms-assign-pool')!
    expect(pool.dataset.options).toBe('WP-0001')
    expect(pool.value).toBe('WP-0001')

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.assign).toHaveBeenCalledWith('inbound', 'ib-1', {
      poolCode: 'WP-0001',
      idempotencyKey: 'assign-intent-1',
      expectedVersion: 3,
    })
    wrapper.unmount()
  })

  it('从关闭态打开（行与开关同一轮变化）时仍默认选中本工厂唯一的作业池', async () => {
    const wrapper = mount(WmsAssignWorkPoolDialog, {
      attachTo: document.body,
      props: { open: false, target: 'inbound', resourceLabel: '入库单' },
      global: { stubs: { NvEntityPicker: pickerStub, RouterLink: true } },
    })
    await flushPromises()
    await wrapper.setProps({
      open: true,
      resourceId: 'ib-1',
      resourceLabel: '入库单 IB-20260928-000001',
      siteCode: 'SITE-001',
      version: 3,
    })
    await flushPromises()

    expect(document.body.querySelector<HTMLInputElement>('#wms-assign-pool')!.value).toBe('WP-0001')
    wrapper.unmount()
  })

  it('指定作业人员时只能从所选池的成员里挑，并随分配一起下发', async () => {
    const wrapper = mountDialog()
    await flushPromises()

    const operator = document.body.querySelector<HTMLInputElement>('#wms-assign-operator')!
    expect(operator.dataset.options).toBe('user-emp-049')
    // 选项值是人员身份（可能是登录账号 ID），不能当编码印在候选里（#3924）。
    expect(operator.dataset.showCode).toBe('false')
    operator.value = 'user-emp-049'
    operator.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.assign).toHaveBeenCalledWith(
      'inbound',
      'ib-1',
      expect.objectContaining({ poolCode: 'WP-0001', operatorPrincipalId: 'user-emp-049' }),
    )
    wrapper.unmount()
  })
})
