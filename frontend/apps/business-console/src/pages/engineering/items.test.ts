import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ItemsPage from './items.vue'

const stub = vi.hoisted(() => ({
  createItemRevision: vi.fn().mockResolvedValue({ data: {} }),
  fetchItemDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const itemRow = {
  itemCode: 'ITEM-1',
  revision: 'A',
  name: '主控板',
  status: 'Published',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-02T00:00:00Z',
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', itemCode: undefined as string | undefined, status: undefined as string | undefined, skip: 0, take: 10 })

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringItems: () => ({
    items: computed(() => [itemRow]),
    itemsError: shallowRef(undefined),
    itemsPending: shallowRef(false),
    itemsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    createItemRevision: stub.createItemRevision,
    createPending: shallowRef(false),
    createError: shallowRef(undefined),
    fetchItemDetail: stub.fetchItemDetail,
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  Sheet: { template: '<div><slot /></div>' },
  SheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  SheetDescription: { template: '<p><slot /></p>' },
}
const formSelectStubs = {
  Select: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectContent: { template: '<slot />' },
  SelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  Checkbox: {
    props: ['checked'],
    emits: ['update:checked'],
    template: '<input type="checkbox" :checked="checked" @change="$emit(\'update:checked\', $event.target.checked)" />',
  },
}

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs, ...formSelectStubs }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.createItemRevision.mockClear()
  stub.fetchItemDetail.mockReset()
  stub.fetchItemDetail.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.itemCode = undefined
  filters.status = undefined
})

describe('engineering items page', () => {
  it('渲染标题与物料修订行（编码/名称/状态用 Published）', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('工程物料')
    expect(wrapper.text()).toContain('ITEM-1')
    expect(wrapper.text()).toContain('主控板')
    expect(wrapper.text()).toContain('已发布')
  })

  it('新建修订向导：新建物料模式下不传 itemCode，create 收到正确 body', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '新建修订')!.trigger('click')
    await flushPromises()

    await wrapper.find('#item-rev').setValue('B')
    await wrapper.find('#item-name').setValue('新主控板')
    // 勾选立即发布
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createItemRevision).toHaveBeenCalledTimes(1)
    const body = stub.createItemRevision.mock.calls[0]![0] as Record<string, unknown>
    expect(body.itemCode).toBeUndefined()
    expect(body.revision).toBe('B')
    expect(body.name).toBe('新主控板')
    expect(body.release).toBe(true)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('校验拦截：未填修订/名称点提交出现汇总提示且不发请求', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '新建修订')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createItemRevision).not.toHaveBeenCalled()
  })

  it('查看：行「查看」拉 get-by-id 渲染真实修订明细', async () => {
    stub.fetchItemDetail.mockResolvedValue({
      itemCode: 'ITEM-1',
      revision: 'A',
      name: '主控板详情',
      status: 'Published',
    })
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    expect(stub.fetchItemDetail).toHaveBeenCalledWith('ITEM-1', 'A')
    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('主控板详情')
  })
})
