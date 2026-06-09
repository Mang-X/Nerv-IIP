import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DevicesPage from './devices.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({
    model: 'KR-210',
    manufacturer: 'KUKA',
    serialNo: 'SN-9001',
    assetClassCode: 'ROBOT',
    lineCode: 'LINE-A',
    workCenterCode: 'WC-A',
    criticality: 'high',
    maintainable: true,
  }),
}))

function stubResource(resourceType: string) {
  const rows = resourceType === 'device-asset'
    ? [{ resourceType: 'device-asset', code: 'EQ-01', displayName: '焊接机器人', active: true, snapshotVersion: '1' }]
    : []
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    items: computed(() => rows),
    total: computed(() => rows.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: stub.create,
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
  }
}

function stubActions() {
  return {
    update: actionStub.update,
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: actionStub.fetchDetail,
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉（reka-ui，懒挂载到 body）换成同步渲染插槽的轻量桩，
// 让「编辑」菜单项可直接点击，从而断言行操作触发 @edit 后对话框进入编辑态。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}

describe('master-data devices page', () => {
  it('renders the title, sample row and create button', async () => {
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('设备台账')
    expect(wrapper.text()).toContain('焊接机器人')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建设备'))).toBe(true)
  })

  it('exposes per-row actions (detail / rename / disable)', async () => {
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('opens the device dialog in edit mode (full-field) when a row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(DevicesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('EQ-01')
    // 对话框进入编辑态：标题含「编辑设备」，编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑设备')
    const codeInput = document.getElementById('dev-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks create on empty required fields with a summary alert and no create call', async () => {
    stub.create.mockClear()
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 打开「新建设备」对话框（重置为默认，必填项留空 → 非法）。
    const createBtn = wrapper.findAll('button').find((b) => b.text().includes('新建设备'))
    await createBtn!.trigger('click')
    await flushPromises()

    // 对话框 teleport 到 body，从 body 取就地表单触发提交。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })
})
