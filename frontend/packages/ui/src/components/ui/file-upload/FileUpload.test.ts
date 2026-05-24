import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import type { FileUploadExpose } from '.'
import { FileUpload } from '.'

describe('FileUpload', () => {
  it('uploads accepted files through FileStorage session callbacks', async () => {
    const createUploadSession = vi.fn().mockResolvedValue({
      uploadSessionId: 'ups_1',
      fileId: 'file_1',
      uploadMode: 'tus',
      provider: 'tus',
      expiresAtUtc: '2026-05-24T00:00:00Z',
      upload: {
        url: '/api/files/v1/tus/ups_1',
        headers: { 'x-nerv-upload-mode': 'tus' },
      },
    })
    const completeUploadSession = vi.fn().mockResolvedValue({ fileId: 'file_1' })
    const transport = vi.fn().mockImplementation(({ onProgress }) => {
      onProgress(100)
      return Promise.resolve()
    })

    const wrapper = mount(FileUpload, {
      props: {
        purpose: 'quality-evidence',
        ownerService: 'Quality',
        ownerType: 'InspectionRecord',
        ownerId: 'inspection_1',
        organizationId: 'org_1',
        environmentId: 'env_1',
        acceptedContentTypes: ['text/plain'],
        createUploadSession,
        completeUploadSession,
        transport,
      },
    })

    const input = wrapper.get('input[type="file"]')
    const file = new File(['hello'], 'evidence.txt', { type: 'text/plain' })
    Object.defineProperty(input.element, 'files', {
      configurable: true,
      value: [file],
    })

    await input.trigger('change')
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledWith({
      organizationId: 'org_1',
      environmentId: 'env_1',
      owner: {
        ownerService: 'Quality',
        ownerType: 'InspectionRecord',
        ownerId: 'inspection_1',
      },
      filePurpose: 'quality-evidence',
      fileName: 'evidence.txt',
      contentType: 'text/plain',
      expectedSizeBytes: 5,
      checksum: null,
    })
    expect(completeUploadSession).toHaveBeenCalledWith('ups_1', {
      organizationId: 'org_1',
      environmentId: 'env_1',
      filePurpose: 'quality-evidence',
      checksum: null,
      sizeBytes: 5,
    })
    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: 'file_1', fileName: 'evidence.txt' }],
    ])
  })

  it('keeps the completed file id even when the API returns an empty string', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        createUploadSession: vi.fn().mockResolvedValue({
          uploadSessionId: 'ups_1',
          fileId: 'session_file_1',
          uploadMode: 'tus',
          provider: 'tus',
          expiresAtUtc: '2026-05-24T00:00:00Z',
          upload: {
            url: '/api/files/v1/tus/ups_1',
            headers: { 'x-nerv-upload-mode': 'tus' },
          },
        }),
        completeUploadSession: vi.fn().mockResolvedValue({ fileId: '' }),
        transport: vi.fn().mockImplementation(({ onProgress }) => {
          onProgress(100)
          return Promise.resolve()
        }),
      }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'empty-id.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: '', fileName: 'empty-id.txt' }],
    ])
  })

  it('rejects files outside accepted content types before creating a session', async () => {
    const createUploadSession = vi.fn()

    const wrapper = mount(FileUpload, {
      props: {
        purpose: 'quality-evidence',
        ownerService: 'Quality',
        ownerType: 'InspectionRecord',
        ownerId: 'inspection_1',
        organizationId: 'org_1',
        environmentId: 'env_1',
        acceptedContentTypes: ['application/pdf'],
        createUploadSession,
        completeUploadSession: vi.fn(),
      },
    })

    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', {
      configurable: true,
      value: [new File(['hello'], 'evidence.txt', { type: 'text/plain' })],
    })

    await input.trigger('change')

    expect(createUploadSession).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('File type is not accepted.')
    expect(wrapper.emitted('rejected')?.[0]).toEqual([
      [{ fileName: 'evidence.txt', reason: 'File type is not accepted.' }],
    ])
  })

  it('accepts dropped files and shows the active drop state', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    const dropzone = wrapper.get('[data-slot="file-upload-dropzone"]')
    await dropzone.trigger('dragenter')

    expect(dropzone.attributes('data-dragging')).toBe('true')

    await dropzone.trigger('drop', {
      dataTransfer: {
        files: [new File(['pdf'], 'inspection.pdf', { type: 'application/pdf' })],
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('inspection.pdf')
    expect(wrapper.text()).toContain('PDF')
    expect(dropzone.attributes('data-dragging')).toBe('false')
  })

  it('matches common file families with readable type labels', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        maxFiles: 7,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [
      new File(['doc'], 'work-instruction.docx', { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' }),
      new File(['sheet'], 'mrp.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
      new File(['deck'], 'review.pptx', { type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation' }),
      new File(['pdf'], 'inspection.pdf', { type: 'application/pdf' }),
      new File(['image'], 'evidence.png', { type: 'image/png' }),
      new File(['audio'], 'alarm.mp3', { type: 'audio/mpeg' }),
      new File(['video'], 'runoff.mp4', { type: 'video/mp4' }),
    ])

    expect(wrapper.text()).toContain('Word')
    expect(wrapper.text()).toContain('Excel')
    expect(wrapper.text()).toContain('PowerPoint')
    expect(wrapper.text()).toContain('PDF')
    expect(wrapper.text()).toContain('Image')
    expect(wrapper.text()).toContain('Audio')
    expect(wrapper.text()).toContain('Video')
  })

  it('can pause and resume an uploading file without completing the session while paused', async () => {
    const deferred = createDeferredTransport()
    const completeUploadSession = vi.fn().mockResolvedValue({ fileId: 'file_1' })
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        completeUploadSession,
        transport: deferred.transport,
      }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'evidence.txt', { type: 'text/plain' })])
    await flushPromises()
    await wrapper.get('button[aria-label="Pause evidence.txt"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('paused')
    expect(completeUploadSession).not.toHaveBeenCalled()

    await wrapper.get('button[aria-label="Resume evidence.txt"]').trigger('click')
    deferred.resolveAll()
    await flushPromises()

    expect(completeUploadSession).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: 'file_1', fileName: 'evidence.txt' }],
    ])
  })

  it('passes an abort signal to transport and aborts it when pausing a row', async () => {
    let capturedSignal: AbortSignal | undefined
    const transport = vi.fn().mockImplementation(({ signal, onProgress }) => {
      capturedSignal = signal
      onProgress(35)

      return new Promise<void>((_resolve, reject) => {
        signal?.addEventListener('abort', () => {
          reject(new DOMException('Upload paused.', 'AbortError'))
        }, { once: true })
      })
    })
    const wrapper = mount(FileUpload, {
      props: createBaseProps({ transport }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'abortable.txt', { type: 'text/plain' })])
    await waitForAssertion(() => {
      expect(capturedSignal).toBeDefined()
      expect(capturedSignal?.aborted).toBe(false)
    })

    await wrapper.get('button[aria-label="Pause abortable.txt"]').trigger('click')
    await flushPromises()

    expect(capturedSignal?.aborted).toBe(true)
    expect(wrapper.text()).toContain('paused')
  })

  it('can retry a failed upload row', async () => {
    let attempt = 0
    const transport = vi.fn().mockImplementation(({ onProgress }) => {
      attempt += 1

      if (attempt === 1) {
        return Promise.reject(new Error('Temporary network interruption.'))
      }

      onProgress(100)
      return Promise.resolve()
    })

    const wrapper = mount(FileUpload, {
      props: createBaseProps({ transport }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'evidence.txt', { type: 'text/plain' })])

    await waitForAssertion(() => {
      expect(transport).toHaveBeenCalledTimes(1)
    })
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('Temporary network interruption.')
    })

    await wrapper.get('button[aria-label="Retry evidence.txt"]').trigger('click')
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('completed')
    })

    expect(transport).toHaveBeenCalledTimes(2)
  })

  it('queues files without creating sessions when automatic upload is disabled', async () => {
    const createUploadSession = vi.fn().mockResolvedValue({
      uploadSessionId: 'ups_1',
      fileId: 'file_1',
      uploadMode: 'tus',
      provider: 'tus',
      expiresAtUtc: '2026-05-24T00:00:00Z',
      upload: {
        url: '/api/files/v1/tus/ups_1',
        headers: { 'x-nerv-upload-mode': 'tus' },
      },
    })
    const transport = vi.fn().mockResolvedValue(undefined)
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        createUploadSession,
        transport,
      }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'queued-evidence.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.text()).toContain('queued-evidence.txt')
    expect(wrapper.text()).toContain('queued')
    expect(createUploadSession).not.toHaveBeenCalled()
    expect(transport).not.toHaveBeenCalled()
  })

  it('can add files and upload queued rows through the exposed component API', async () => {
    const createUploadSession = vi.fn().mockResolvedValue({
      uploadSessionId: 'ups_1',
      fileId: 'file_1',
      uploadMode: 'tus',
      provider: 'tus',
      expiresAtUtc: '2026-05-24T00:00:00Z',
      upload: {
        url: '/api/files/v1/tus/ups_1',
        headers: { 'x-nerv-upload-mode': 'tus' },
      },
    })
    const transport = vi.fn().mockImplementation(({ onProgress }) => {
      onProgress(100)
      return Promise.resolve()
    })
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        createUploadSession,
        transport,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([
      new File(['hello'], 'external-evidence.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    expect(wrapper.text()).toContain('external-evidence.txt')
    expect(createUploadSession).not.toHaveBeenCalled()

    await upload.uploadQueued()
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledTimes(1)
    expect(transport).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('completed')
    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: 'file_1', fileName: 'external-evidence.txt' }],
    ])
  })

  it('does not count rejected or failed rows against available slots', async () => {
    const transport = vi.fn().mockRejectedValue(new Error('Network failed.'))
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
        maxFiles: 2,
        transport,
      }),
    })

    await selectFiles(wrapper, [
      new File(['hello'], 'wrong-type.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    expect(wrapper.text()).toContain('2 slots available')

    await selectFiles(wrapper, [
      new File(['pdf'], 'failed-upload.pdf', { type: 'application/pdf' }),
    ])
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('Network failed.')
    })

    expect(wrapper.text()).toContain('2 slots available')
  })

  it('uses a virtualized scroll container for large file queues', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        maxFiles: 80,
        virtualizeThreshold: 20,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose
    const files = Array.from({ length: 60 }, (_, index) =>
      new File(['hello'], `bulk-${index}.txt`, { type: 'text/plain' }))

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-virtual-list"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-slot="file-upload-row"]').length).toBeLessThan(files.length)
  })
})

async function waitForAssertion(assertion: () => void) {
  let failure: unknown

  for (let attempt = 0; attempt < 25; attempt += 1) {
    try {
      assertion()
      return
    }
    catch (error) {
      failure = error
      await flushPromises()
    }
  }

  throw failure
}

function createBaseProps(overrides: Record<string, unknown> = {}) {
  return {
    purpose: 'quality-evidence',
    ownerService: 'Quality',
    ownerType: 'InspectionRecord',
    ownerId: 'inspection_1',
    organizationId: 'org_1',
    environmentId: 'env_1',
    acceptedContentTypes: ['text/plain'],
    createUploadSession: vi.fn().mockResolvedValue({
      uploadSessionId: 'ups_1',
      fileId: 'file_1',
      uploadMode: 'tus',
      provider: 'tus',
      expiresAtUtc: '2026-05-24T00:00:00Z',
      upload: {
        url: '/api/files/v1/tus/ups_1',
        headers: { 'x-nerv-upload-mode': 'tus' },
      },
    }),
    completeUploadSession: vi.fn().mockResolvedValue({ fileId: 'file_1' }),
    ...overrides,
  }
}

async function selectFiles(wrapper: ReturnType<typeof mount>, files: File[]) {
  const input = wrapper.get('input[type="file"]')
  Object.defineProperty(input.element, 'files', {
    configurable: true,
    value: files,
  })

  await input.trigger('change')
}

function createDeferredTransport() {
  const pending: Array<() => void> = []
  const transport = vi.fn().mockImplementation(({ signal, onProgress }) => {
    onProgress(35)

    return new Promise<void>((resolve, reject) => {
      const abort = () => reject(new DOMException('Upload paused.', 'AbortError'))
      signal?.addEventListener('abort', abort, { once: true })
      pending.push(() => {
        signal?.removeEventListener('abort', abort)
        onProgress(100)
        resolve()
      })
    })
  })

  return {
    transport,
    resolveAll: () => {
      for (const resolve of pending.splice(0)) {
        resolve()
      }
    },
  }
}
