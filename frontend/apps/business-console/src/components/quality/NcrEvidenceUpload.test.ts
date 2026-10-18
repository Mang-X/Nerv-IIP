import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { FileUpload } from '@nerv-iip/ui'
import NcrEvidenceUpload from './NcrEvidenceUpload.vue'

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
vi.mock('@/utils/notify', () => ({ notifyOperationFailure: vi.fn(), notifyWarning: vi.fn() }))

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

beforeEach(() => {
  vi.clearAllMocks()
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
  expect(api.patch.mock.calls[1]![0].headers['Upload-Offset']).toBe('4')
  expect(wrapper.emitted('completed')?.at(-1)?.[0]).toEqual([
    { fileId: 'completed-file', fileName: '尺寸复检.txt' },
  ])
  wrapper.unmount()
})

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
  wrapper.unmount()
})

it('does not count a late complete after cancellation', async () => {
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
