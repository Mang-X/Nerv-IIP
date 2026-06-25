import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

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

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', itemCode: undefined as string | undefined, documentType: undefined as string | undefined, skip: 0, take: 10 })

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
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  DialogRoot: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  Sheet: { template: '<div><slot /></div>' },
  SheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  SheetDescription: { template: '<p><slot /></p>' },
}

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs }

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

  it('fileId 字段标注文件上传待接入（不假装能上传）', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '登记文档')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('文件上传待接入')
  })

  it('登记向导：填完字段提交，register 收到正确 body', async () => {
    const wrapper = mount(DocumentsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '登记文档')!.trigger('click')
    await flushPromises()

    await wrapper.find('#doc-number').setValue('DOC-9')
    await wrapper.find('#doc-rev').setValue('A')
    await wrapper.find('#doc-type').setValue('规格书')
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
    expect(body.documentType).toBe('规格书')
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
