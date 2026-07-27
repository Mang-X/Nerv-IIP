import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, inject, provide, reactive, shallowRef } from 'vue'

import DocumentsPage from './documents.vue'

const stub = vi.hoisted(() => ({
  registerDocument: vi.fn().mockResolvedValue({ data: {} }),
  fetchDocumentDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const docRow = {
  documentNumber: 'DOC-1',
  revision: 'A',
  documentType: '图纸',
  fileId: 'file-abc',
  fileName: 'drawing.pdf',
  contentType: 'application/pdf',
  itemCode: 'ITEM-1',
  registeredAtUtc: '2026-01-02T00:00:00Z',
}

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  itemCode: undefined as string | undefined,
  documentType: undefined as string | undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringDocuments: () => ({
    documents: computed(() => [docRow]),
    documentsError: shallowRef(undefined),
    documentsPending: shallowRef(false),
    documentsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    registerDocument: stub.registerDocument,
    registerPending: shallowRef(false),
    registerError: shallowRef(undefined),
    fetchDocumentDetail: stub.fetchDocumentDetail,
  }),
  // 关联物料改成从工程物料目录里选，页面新引入了这个读面。
  useEngineeringItems: () => ({
    filters: reactive({ skip: 0, take: 200 }),
    items: computed(() => [
      { itemCode: 'ITEM-1', revision: 'A', name: '控制主板', status: 'Published' },
    ]),
    itemsError: shallowRef(undefined),
    itemsPending: shallowRef(false),
    itemsTotal: computed(() => 1),
    refresh: vi.fn(),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  DialogRoot: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  // NvSheet 根 = reka DialogRoot（与对话框共用 DialogRoot stub），内容/标头为真 .vue 按 Pro 名打桩。
  NvSheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}

// 关联物料是只选的实体选择器（内部自带 reka Dialog，会撞上这里的 DialogRoot 桩），
// 桩成带同名 id 的输入位，用例继续用 `#doc-item-code` 表达「选中了某个物料」。
const pickerStubs = {
  NvEntityPicker: {
    props: ['modelValue', 'options', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}

/**
 * 文档类型已从自由文本改成受控下拉：id 挂在 NvSelectTrigger（真实组件是 button）上，
 * 桩件把它上提到 `<select>`，`#doc-type` 选择器与 `setValue` 语义都保持不变。
 */
const selectTriggerIdKey = Symbol('nv-select-stub-trigger-id')
const selectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    setup() {
      const triggerId = shallowRef<string | undefined>(undefined)
      provide(selectTriggerIdKey, (id?: string) => {
        triggerId.value = id
      })
      return { triggerId }
    },
    template:
      '<select v-bind="$attrs" :id="triggerId ?? $attrs.id" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: {
    props: ['id'],
    setup(props: { id?: string }) {
      inject<((id?: string) => void) | undefined>(selectTriggerIdKey, undefined)?.(props.id)
    },
    template: '<slot />',
  },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
}

const allStubs = {
  ...layoutStub,
  ...dialogStubs,
  ...sheetStubs,
  ...pickerStubs,
  ...selectStubs,
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.registerDocument.mockClear()
  stub.fetchDocumentDetail.mockReset()
  stub.fetchDocumentDetail.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.itemCode = undefined
  filters.documentType = undefined
})

describe('engineering documents page', () => {
  it('渲染标题与文档行（文档号/类型/文件名）', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('工程文档')
    expect(wrapper.text()).toContain('DOC-1')
    expect(wrapper.text()).toContain('图纸')
    expect(wrapper.text()).toContain('drawing.pdf')
  })

  it('只登记文件引用，不假装能上传（无上传控件、只有文件引用 ID）', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '登记文档')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('#doc-file-id').exists()).toBe(true)
    expect(wrapper.find('input[type="file"]').exists()).toBe(false)
    expect(wrapper.findAll('button').some((b) => b.text().includes('上传'))).toBe(false)
  })

  it('登记向导：填完字段提交，register 收到正确 body', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '登记文档')!.trigger('click')
    await flushPromises()

    await wrapper.find('#doc-number').setValue('DOC-9')
    await wrapper.find('#doc-rev').setValue('A')
    // 文档类型改成受控下拉后，提交体带的是受控值（label 只用于显示）。
    await wrapper.find('#doc-type').setValue('specification')
    await wrapper.find('#doc-file-id').setValue('file-xyz')
    await wrapper.find('#doc-file-name').setValue('spec.pdf')
    await wrapper.find('#doc-content-type').setValue('application/pdf')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.registerDocument).toHaveBeenCalledTimes(1)
    const body = stub.registerDocument.mock.calls[0]![0] as Record<string, unknown>
    expect(body.documentNumber).toBe('DOC-9')
    expect(body.revision).toBe('A')
    expect(body.documentType).toBe('specification')
    expect(body.fileId).toBe('file-xyz')
    expect(body.fileName).toBe('spec.pdf')
    expect(body.contentType).toBe('application/pdf')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('校验拦截：必填未填点登记出现汇总提示且不发请求', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '登记文档')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.registerDocument).not.toHaveBeenCalled()
  })

  it('查看：行「查看」拉 get-by-id 渲染真实文档明细', async () => {
    stub.fetchDocumentDetail.mockResolvedValue({
      documentNumber: 'DOC-1',
      revision: 'A',
      documentType: '图纸',
      fileId: 'file-detail',
      fileName: 'detail.pdf',
      contentType: 'application/pdf',
    })
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    expect(stub.fetchDocumentDetail).toHaveBeenCalledWith('DOC-1', 'A')
    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('detail.pdf')
    expect(sheet.text()).toContain('file-detail')
  })
})
