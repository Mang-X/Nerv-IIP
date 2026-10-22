import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { FileUpload } from '@nerv-iip/ui'
import NcrEvidenceUpload from './NcrEvidenceUpload.vue'
import { notifyOperationFailure } from '@/utils/notify'

const api = vi.hoisted(() => ({
  create: vi.fn(),
  head: vi.fn(),
  patch: vi.fn(),
  complete: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  createBusinessConsoleQualityEvidenceUploadSession: api.create,
  getBusinessConsoleQualityEvidenceTusOffset: api.head,
  patchBusinessConsoleQualityEvidenceTusUpload: api.patch,
  completeBusinessConsoleQualityEvidenceUpload: api.complete,
}))
vi.mock('@/utils/notify', async (original) => ({
  ...(await original<typeof import('@/utils/notify')>()),
  notifyOperationFailure: vi.fn(),
  notifyWarning: vi.fn(),
}))

const scope = { organizationId: 'org-001', environmentId: 'env-dev', ncrId: 'ncr-1' }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const file = () => new File(['inspection'], '尺寸复检.txt', { type: 'text/plain' })
const completed = { data: { success: true, data: { fileId: 'completed-file' } } }

function blobText(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error)
    reader.readAsText(blob)
  })
}

beforeEach(() => {
  vi.resetAllMocks()
  api.create.mockResolvedValue({
    data: {
      success: true,
      data: {
        uploadSessionId: 'session-1',
        fileId: 'reserved-file',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        uploadUrl: '/api/business-console/v1/files/quality-evidence/tus/session-1',
        uploadHeaders: {},
      },
    },
  })
  api.head.mockResolvedValue({
    response: new Response(null, { headers: { 'Upload-Offset': '0' } }),
  })
  api.patch.mockResolvedValue({ response: new Response(null, { status: 204 }) })
  api.complete.mockResolvedValue(completed)
})

it('counts only completed evidence and sends raw bytes with the current NCR scope', async () => {
  const finish = deferred<typeof completed>()
  api.complete.mockReturnValue(finish.promise)
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const upload = wrapper.findComponent(FileUpload)
  const pending = upload.vm.addFiles([file()])
  await flushPromises()
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(wrapper.text()).toContain('上传中')
  expect(api.create.mock.calls[0]![0].body).toEqual({
    ...scope,
    fileName: '尺寸复检.txt',
    contentType: 'text/plain',
    expectedSizeBytes: 10,
    checksum: null,
  })
  const patch = api.patch.mock.calls[0]![0]
  expect(patch.body.size).toBe(10)
  expect(await blobText(patch.body)).toBe('inspection')
  expect(patch.headers).toMatchObject({
    'X-Organization-Id': scope.organizationId,
    'X-Environment-Id': scope.environmentId,
    'Upload-Offset': '0',
    'Tus-Resumable': '1.0.0',
  })
  expect(api.complete.mock.calls[0]![0].body).toEqual({ ...scope, checksum: null, sizeBytes: 10 })
  finish.resolve(completed)
  await pending
  await flushPromises()
  expect(wrapper.emitted('completed')?.at(-1)).toEqual([
    [{ fileId: 'completed-file', fileName: '尺寸复检.txt' }],
  ])
  wrapper.unmount()
})

it('keeps failed evidence out of attachments and retries the same session from its offset', async () => {
  api.patch.mockRejectedValueOnce(new Error('上传中断'))
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  await wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  expect(wrapper.text()).toContain('上传中断')
  expect(wrapper.emitted('completed')).toBeUndefined()
  api.head.mockResolvedValue({
    response: new Response(null, { headers: { 'Upload-Offset': '4' } }),
  })
  await wrapper.findComponent(FileUpload).vm.retryFailed()
  await flushPromises()
  expect(api.create).toHaveBeenCalledTimes(1)
  expect(api.patch.mock.calls[1]![0].body.size).toBe(6)
  expect(await blobText(api.patch.mock.calls[1]![0].body)).toBe('ection')
  expect(api.patch.mock.calls[1]![0].headers['Upload-Offset']).toBe('4')
  expect(wrapper.emitted('completed')?.at(-1)?.[0]).toEqual([
    { fileId: 'completed-file', fileName: '尺寸复检.txt' },
  ])
  wrapper.unmount()
})

// Regression: #4505. A final PATCH closes the real tus session before its response reaches the client.
it('recovers a lost final PATCH response through HEAD 404 and same-session completion', async () => {
  api.patch.mockRejectedValueOnce(new TypeError('Failed to fetch'))
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  await wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(api.complete).not.toHaveBeenCalled()

  const actual =
    await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
  api.head.mockImplementationOnce((options) =>
    actual.getBusinessConsoleQualityEvidenceTusOffset({
      ...options,
      baseUrl: 'http://quality.invalid',
      fetch: async () => new Response(null, { status: 404 }),
    }),
  )
  const finish = deferred<typeof completed>()
  api.complete.mockReturnValueOnce(finish.promise)
  await wrapper.get('button[aria-label="重试 尺寸复检.txt"]').trigger('click')
  await flushPromises()
  expect(api.complete).toHaveBeenCalledWith(
    expect.objectContaining({
      path: { uploadSessionId: 'session-1' },
      body: { ...scope, checksum: null, sizeBytes: 10 },
    }),
  )
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(wrapper.text()).toContain('上传中')
  finish.resolve(completed)
  await flushPromises()
  expect(api.create).toHaveBeenCalledTimes(1)
  expect(api.patch).toHaveBeenCalledTimes(1)
  expect(wrapper.emitted('completed')).toEqual([
    [[{ fileId: 'completed-file', fileName: '尺寸复检.txt' }]],
  ])
  wrapper.unmount()
})

it.each([400, 403, 404])(
  'keeps HEAD 404 evidence failed when completion rejects with %s',
  async (status) => {
    const actual =
      await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
    api.head.mockImplementationOnce((options) =>
      actual.getBusinessConsoleQualityEvidenceTusOffset({
        ...options,
        baseUrl: 'http://quality.invalid',
        fetch: async () => new Response(null, { status: 404 }),
      }),
    )
    const reason = '当前会话不能作为本 NCR 的处置证据。'
    api.complete.mockRejectedValueOnce({ status, detail: reason })
    const wrapper = mount(NcrEvidenceUpload, { props: scope })
    await wrapper.findComponent(FileUpload).vm.addFiles([file()])
    await flushPromises()
    expect(api.complete).toHaveBeenCalledTimes(1)
    expect(api.patch).not.toHaveBeenCalled()
    expect(wrapper.emitted('completed')).toBeUndefined()
    expect(wrapper.text()).toContain(reason)
    wrapper.unmount()
  },
)

it.each([403, 500])('does not try completion after HEAD %s', async (status) => {
  const actual =
    await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
  const reason = '无法读取当前上传会话。'
  api.head.mockImplementationOnce((options) =>
    actual.getBusinessConsoleQualityEvidenceTusOffset({
      ...options,
      baseUrl: 'http://quality.invalid',
      fetch: async () => Response.json({ status, detail: reason }, { status }),
    }),
  )
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  await wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  expect(api.patch).not.toHaveBeenCalled()
  expect(api.complete).not.toHaveBeenCalled()
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(wrapper.text()).toContain(reason)
  wrapper.unmount()
})

it('does not try completion when HEAD fails without an HTTP response', async () => {
  const actual =
    await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
  api.head.mockImplementationOnce((options) =>
    actual.getBusinessConsoleQualityEvidenceTusOffset({
      ...options,
      baseUrl: 'http://quality.invalid',
      fetch: async () => {
        throw new TypeError('Failed to fetch')
      },
    }),
  )
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  await wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  expect(api.patch).not.toHaveBeenCalled()
  expect(api.complete).not.toHaveBeenCalled()
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(wrapper.text()).toContain('网络异常')
  wrapper.unmount()
})

it.each(['create', 'head', 'patch', 'complete'] as const)(
  'preserves the domain rejection from a generated-client response object at %s',
  async (operation) => {
    const reason = '上传内容与声明的文件类型不匹配。'
    api[operation].mockRejectedValueOnce({ detail: reason, status: 400 })
    const wrapper = mount(NcrEvidenceUpload, { props: scope })
    await wrapper
      .findComponent(FileUpload)
      .vm.addFiles([new File(['inspection'], '尺寸复检.pdf', { type: 'application/pdf' })])
    await flushPromises()
    expect(wrapper.text()).toContain(reason)
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '证据上传失败',
      expect.objectContaining({ message: reason }),
      '请重试或移除后重新选择。',
    )
    expect(wrapper.emitted('completed')).toBeUndefined()
    wrapper.unmount()
  },
)

it('cancels a pending session before any bytes or completion can be sent', async () => {
  const session = deferred<unknown>()
  const response = await api.create()
  api.create.mockReturnValue(session.promise)
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const upload = wrapper.findComponent(FileUpload)
  const pending = upload.vm.addFiles([file()])
  await flushPromises()
  upload.vm.clear()
  session.resolve(response)
  await pending
  await flushPromises()
  expect(api.patch).not.toHaveBeenCalled()
  expect(api.complete).not.toHaveBeenCalled()
  expect(wrapper.emitted('completed')?.flat(2) ?? []).toEqual([])
  expect(notifyOperationFailure).not.toHaveBeenCalled()
  wrapper.unmount()
})

it('maps a network Error through the canonical feedback utility', async () => {
  api.patch.mockRejectedValueOnce(new Error('Failed to fetch'))
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  await wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  const message = '网络异常，操作结果可能尚未确认；请刷新列表核实后再重试。'
  expect(wrapper.text()).toContain(message)
  expect(wrapper.text()).not.toContain('Failed to fetch')
  expect(notifyOperationFailure).toHaveBeenCalledWith(
    '证据上传失败',
    expect.objectContaining({ message }),
    '请重试或移除后重新选择。',
  )
  expect(api.complete).not.toHaveBeenCalled()
  wrapper.unmount()
})

it('keeps an aborted transfer paused without failure feedback and can resume it', async () => {
  api.patch.mockImplementationOnce(
    ({ signal }: { signal: AbortSignal }) =>
      new Promise((_, reject) => {
        signal.addEventListener('abort', () =>
          reject(new DOMException('The operation was aborted.', 'AbortError')),
        )
      }),
  )
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const upload = wrapper.findComponent(FileUpload)
  const pending = upload.vm.addFiles([file()])
  await flushPromises()
  upload.vm.pauseAll()
  await pending
  await flushPromises()
  expect(wrapper.text()).toContain('已暂停')
  expect(wrapper.text()).not.toContain('失败')
  expect(notifyOperationFailure).not.toHaveBeenCalled()
  expect(api.complete).not.toHaveBeenCalled()
  await upload.vm.resumeAll()
  await flushPromises()
  expect(api.create).toHaveBeenCalledTimes(1)
  expect(wrapper.emitted('completed')?.at(-1)?.[0]).toEqual([
    { fileId: 'completed-file', fileName: '尺寸复检.txt' },
  ])
  wrapper.unmount()
})

it('does not count a late HEAD 404 recovery after cancellation', async () => {
  const actual =
    await vi.importActual<typeof import('@nerv-iip/api-client')>('@nerv-iip/api-client')
  api.head.mockImplementationOnce((options) =>
    actual.getBusinessConsoleQualityEvidenceTusOffset({
      ...options,
      baseUrl: 'http://quality.invalid',
      fetch: async () => new Response(null, { status: 404 }),
    }),
  )
  const finish = deferred<typeof completed>()
  api.complete.mockReturnValue(finish.promise)
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const upload = wrapper.findComponent(FileUpload)
  const pending = upload.vm.addFiles([file()])
  await flushPromises()
  upload.vm.clear()
  finish.resolve(completed)
  await pending
  await flushPromises()
  expect(wrapper.emitted('completed')?.flat(2) ?? []).toEqual([])
  wrapper.unmount()
})

it('aborts the old NCR upload when its dialog closes or its keyed scope is replaced', async () => {
  const finish = deferred<typeof completed>()
  api.complete.mockReturnValueOnce(finish.promise)
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const pending = wrapper.findComponent(FileUpload).vm.addFiles([file()])
  await flushPromises()
  const signal = api.patch.mock.calls[0]![0].signal as AbortSignal
  wrapper.unmount()
  expect(signal.aborted).toBe(true)
  const next = mount(NcrEvidenceUpload, { props: { ...scope, ncrId: 'ncr-2' } })
  finish.resolve(completed)
  await pending
  await flushPromises()
  expect(next.emitted('completed')).toBeUndefined()
  expect(api.complete.mock.calls[0]![0].body.ncrId).toBe('ncr-1')
  next.unmount()
})

it('does not count reserved file ids when completion fails and can retry completion without resending bytes', async () => {
  api.complete.mockRejectedValueOnce(new Error('完成确认失败'))
  const wrapper = mount(NcrEvidenceUpload, { props: scope })
  const upload = wrapper.findComponent(FileUpload)
  await upload.vm.addFiles([file()])
  await flushPromises()
  expect(wrapper.emitted('completed')).toBeUndefined()
  expect(wrapper.text()).toContain('完成确认失败')
  api.head.mockResolvedValue({
    response: new Response(null, { headers: { 'Upload-Offset': '10' } }),
  })
  await upload.vm.retryFailed()
  await flushPromises()
  expect(api.create).toHaveBeenCalledTimes(1)
  expect(api.patch).toHaveBeenCalledTimes(1)
  expect(api.complete).toHaveBeenCalledTimes(2)
  expect(wrapper.emitted('completed')?.at(-1)?.[0]).toEqual([
    { fileId: 'completed-file', fileName: '尺寸复检.txt' },
  ])
  wrapper.unmount()
})
