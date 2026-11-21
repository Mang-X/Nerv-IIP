import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import type { FileUploadExpose } from '.'
import { FileUpload } from '.'

import {
  stubFileUploadObjectUrls,
  createBaseProps,
  createDeferredTransport,
  selectFiles,
  qualityEvidencePurpose,
} from './FileUpload.test.helpers'

vi.mock('motion-v', async () =>
  (await import('./FileUpload.test.helpers')).createFileUploadMotionMock(),
)

stubFileUploadObjectUrls()

describe('FileUpload', () => {
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
    await wrapper.get('button[aria-label="暂停 evidence.txt"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('已暂停')
    expect(completeUploadSession).not.toHaveBeenCalled()

    await wrapper.get('button[aria-label="继续 evidence.txt"]').trigger('click')
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
        signal?.addEventListener(
          'abort',
          () => {
            reject(new DOMException('Upload paused.', 'AbortError'))
          },
          { once: true },
        )
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

    await wrapper.get('button[aria-label="暂停 abortable.txt"]').trigger('click')
    await flushPromises()

    expect(capturedSignal?.aborted).toBe(true)
    expect(wrapper.text()).toContain('已暂停')
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

    await wrapper.get('button[aria-label="重试 evidence.txt"]').trigger('click')
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('已完成')
    })

    expect(transport).toHaveBeenCalledTimes(2)
  })

  it('creates a new upload session when retrying an expired failed session', async () => {
    let attempt = 0
    const createUploadSession = vi
      .fn()
      .mockResolvedValueOnce({
        uploadSessionId: 'ups_expired',
        fileId: 'file_expired',
        uploadMode: 'tus',
        provider: 'tus',
        expiresAtUtc: '2000-01-01T00:00:00Z',
        upload: {
          url: '/api/files/v1/tus/ups_expired',
          headers: { 'x-nerv-upload-mode': 'tus' },
        },
      })
      .mockResolvedValueOnce({
        uploadSessionId: 'ups_fresh',
        fileId: 'file_fresh',
        uploadMode: 'tus',
        provider: 'tus',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        upload: {
          url: '/api/files/v1/tus/ups_fresh',
          headers: { 'x-nerv-upload-mode': 'tus' },
        },
      })
    const completeUploadSession = vi.fn().mockResolvedValue({ fileId: 'file_fresh' })
    const transport = vi.fn().mockImplementation(({ onProgress }) => {
      attempt += 1

      if (attempt === 1) {
        return Promise.reject(new Error('Upload session expired.'))
      }

      onProgress(100)
      return Promise.resolve()
    })
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        createUploadSession,
        completeUploadSession,
        transport,
      }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'expired-session.txt', { type: 'text/plain' })])
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('Upload session expired.')
    })

    await wrapper.get('button[aria-label="重试 expired-session.txt"]').trigger('click')
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('已完成')
    })

    expect(createUploadSession).toHaveBeenCalledTimes(2)
    expect(completeUploadSession).toHaveBeenCalledWith('ups_fresh', expect.any(Object))
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
    expect(wrapper.text()).toContain('待上传')
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

    await upload.addFiles([new File(['hello'], 'external-evidence.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.text()).toContain('external-evidence.txt')
    expect(createUploadSession).not.toHaveBeenCalled()

    await upload.uploadQueued()
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledTimes(1)
    expect(transport).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('已完成')
    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: 'file_1', fileName: 'external-evidence.txt' }],
    ])
  })

  it('exposes queue state for parent action buttons', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        transport: vi.fn().mockImplementation(({ onProgress }) => {
          onProgress(100)
          return Promise.resolve()
        }),
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    expect(upload.hasRows).toBe(false)
    expect(upload.hasQueuedRows).toBe(false)

    await upload.addFiles([new File(['hello'], 'queued-state.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(upload.hasRows).toBe(true)
    expect(upload.hasQueuedRows).toBe(true)

    await upload.uploadQueued()
    await flushPromises()

    expect(upload.hasRows).toBe(true)
    expect(upload.hasQueuedRows).toBe(false)

    upload.clear()
    await flushPromises()

    expect(upload.hasRows).toBe(false)
    expect(upload.hasQueuedRows).toBe(false)
  })

  it('uses the latest owner and scope props when uploading queued rows', async () => {
    const createUploadSession = vi.fn().mockResolvedValue({
      uploadSessionId: 'ups_1',
      fileId: 'file_1',
      uploadMode: 'tus',
      provider: 'tus',
      expiresAtUtc: '2026-05-24T00:00:00Z',
      upload: {
        url: '/api/console/v1/files/tus/ups_1',
        headers: { 'x-nerv-upload-mode': 'tus' },
      },
    })
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        ownerId: 'inspection_old',
        organizationId: 'org_old',
        environmentId: 'env_old',
        purpose: 'old-purpose',
        createUploadSession,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'late-scope.txt', { type: 'text/plain' })])
    await wrapper.setProps({
      ownerId: 'inspection_new',
      organizationId: 'org_new',
      environmentId: 'env_new',
      purpose: qualityEvidencePurpose,
    })

    await upload.uploadQueued()
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledWith(
      expect.objectContaining({
        organizationId: 'org_new',
        environmentId: 'env_new',
        filePurpose: qualityEvidencePurpose,
        owner: expect.objectContaining({
          ownerId: 'inspection_new',
        }),
      }),
    )
  })

  it('clears rows without emitting a synthetic empty completed event', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'queued-evidence.txt', { type: 'text/plain' })])
    upload.clear()
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
    expect(wrapper.emitted('completed')).toBeUndefined()
  })

  it('does not emit completed when removing a non-completed row', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'queued-evidence.txt', { type: 'text/plain' })])
    await wrapper.get('button[aria-label="移除 queued-evidence.txt"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
    expect(wrapper.emitted('completed')).toBeUndefined()
  })

  it('does not count rejected or failed rows against available slots', async () => {
    const transport = vi.fn().mockRejectedValue(new Error('Network failed.'))
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        variant: 'queue',
        acceptedContentTypes: ['application/pdf'],
        maxFiles: 2,
        transport,
      }),
    })

    await selectFiles(wrapper, [new File(['hello'], 'wrong-type.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.text()).toContain('还可上传 2 个文件')

    await selectFiles(wrapper, [
      new File(['pdf'], 'failed-upload.pdf', { type: 'application/pdf' }),
    ])
    await waitForAssertion(() => {
      expect(wrapper.text()).toContain('Network failed.')
    })

    expect(wrapper.text()).toContain('还可上传 2 个文件')
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
    const files = Array.from(
      { length: 60 },
      (_, index) => new File(['hello'], `bulk-${index}.txt`, { type: 'text/plain' }),
    )

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-virtual-list"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-slot="file-upload-row"]').length).toBeLessThan(files.length)
    expect(
      wrapper
        .findAll('[data-motion-row="true"]')
        .every((row) => row.attributes('data-has-layout') !== 'true'),
    ).toBe(true)
  })

  it('does not render compact rows twice when the queue exceeds the virtualization threshold', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        maxFiles: 60,
        variant: 'compact',
        virtualizeThreshold: 20,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose
    const files = Array.from(
      { length: 45 },
      (_, index) => new File(['hello'], `compact-${index}.txt`, { type: 'text/plain' }),
    )

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-virtual-list"]').exists()).toBe(false)
    expect(wrapper.findAll('[data-slot="file-upload-row"]')).toHaveLength(files.length)
  })

  it('accounts for row spacing in virtualized queue height', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        maxFiles: 60,
        virtualizeThreshold: 10,
        virtualRowHeight: 92,
        virtualListHeight: 10000,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose
    const files = Array.from(
      { length: 12 },
      (_, index) => new File(['hello'], `spaced-${index}.txt`, { type: 'text/plain' }),
    )

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-virtual-list"]').attributes('style')).toContain(
      'height: 1200px',
    )
  })
})

async function waitForAssertion(assertion: () => void) {
  let failure: unknown

  for (let attempt = 0; attempt < 25; attempt += 1) {
    try {
      assertion()
      return
    } catch (error) {
      failure = error
      await flushPromises()
    }
  }

  throw failure
}
