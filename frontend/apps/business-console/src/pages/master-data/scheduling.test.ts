import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SchedulingPage from './scheduling.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'SHIFT-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({ name: '白班' }),
}))

function stubResource(resourceType: string) {
  const labelByType: Record<string, { code: string, name: string }> = {
    'shift': { code: 'SHIFT-A', name: '白班' },
    'work-calendar': { code: 'CAL-A', name: '标准日历' },
  }
  const entry = labelByType[resourceType]
  const rows = entry ? [{ resourceType, code: entry.code, displayName: entry.name, active: true, snapshotVersion: '1' }] : []
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
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}

async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

describe('master-data scheduling page', () => {
  it('renders title, two tabs and a disabled 月历视图 entry marked 建设中', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('排班与日历')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('班次'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作日历'))).toBe(true)
    expect(wrapper.text()).toContain('白班')
  })

  it('work-calendar tab shows a disabled 月历视图 button labelled 建设中', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    const monthBtn = wrapper.findAll('button').find((b) => b.text().includes('月历视图'))
    expect(monthBtn).toBeTruthy()
    expect(monthBtn!.text()).toContain('建设中')
    expect(monthBtn!.attributes('disabled')).toBeDefined()
  })

  it('shift edit opens edit mode with code read-only', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('SHIFT-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑班次')
    const codeInput = document.getElementById('shift-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks shift create on empty required fields with summary alert and no create call', async () => {
    stub.create.mockClear()
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建班次'))!.trigger('click')
    await flushPromises()
    expect(wrapper.find('#shift-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('creating a shift posts code/name/paidMinutes and fires success toast', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建班次'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#shift-code').setValue('SHIFT-NEW')
    await wrapper.find('#shift-name').setValue('夜班')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as { code: string, name: string, paidMinutes: number }
    expect(body.code).toBe('SHIFT-NEW')
    expect(body.name).toBe('夜班')
    expect(body.paidMinutes).toBe(480)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })
})
