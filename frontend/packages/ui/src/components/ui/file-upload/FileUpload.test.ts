import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import type { FileUploadExpose } from '.'
import { FileUpload, fileUploadMotion } from '.'

const originalCreateObjectURL = URL.createObjectURL
const originalRevokeObjectURL = URL.revokeObjectURL

beforeEach(() => {
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    value: vi.fn(() => 'blob:nerv-iip-preview'),
  })
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    value: vi.fn(),
  })
})

afterEach(() => {
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    value: originalCreateObjectURL,
  })
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    value: originalRevokeObjectURL,
  })
})

vi.mock('motion-v', () => {
  const passthrough = (slot: string, tag = 'div') => defineComponent({
    name: slot,
    inheritAttrs: false,
    setup(_, { attrs, slots }) {
      const hasAttr = (name: string) => attrs[name] !== undefined

      return () => h(tag, {
        ...attrs,
        'data-motion-component': slot,
        'data-has-while-hover': hasAttr('whileHover') || hasAttr('while-hover') ? 'true' : undefined,
        'data-has-while-tap': hasAttr('whileTap') || hasAttr('while-tap') ? 'true' : undefined,
        'data-has-layout': hasAttr('layout') ? 'true' : undefined,
      }, slots.default?.())
    },
  })

  return {
    AnimatePresence: passthrough('animate-presence'),
    MotionConfig: passthrough('motion-config'),
    motion: {
      button: passthrough('motion-button', 'button'),
      div: passthrough('motion-div'),
      span: passthrough('motion-span', 'span'),
    },
  }
})

describe('FileUpload', () => {
  it('exposes and renders with the shared motion-v upload curves', async () => {
    expect(fileUploadMotion.fastInvoke).toEqual({
      duration: 0.187,
      ease: [0, 0, 0, 1],
    })
    expect(fileUploadMotion.pointToPointShort).toEqual({
      duration: 0.187,
      ease: [0.55, 0.55, 0, 1],
    })

    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'motion-evidence.txt', { type: 'text/plain' })])
    await flushPromises()

    expect(wrapper.find('[data-motion-component="motion-config"]').exists()).toBe(true)
    expect(wrapper.find('[data-motion-component="motion-button"][data-dragging="false"]').exists()).toBe(true)
    expect(wrapper.find('[data-motion-component="motion-div"][data-motion-row="true"]').exists()).toBe(true)
    expect(wrapper.get('[data-slot="file-upload-dropzone"]').classes()).toContain('data-[dragging=true]:border-primary')
    expect(wrapper.get('[data-slot="file-upload-dropzone"]').classes()).not.toContain('data-[dragging=true]:border-brand')
  })

  it('applies visible motion affordances to the dropzone and row state changes', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'animated-status.txt', { type: 'text/plain' })])
    await flushPromises()

    const dropzone = wrapper.get('[data-slot="file-upload-dropzone"]')
    expect(dropzone.attributes('data-has-while-hover')).toBe('true')
    expect(dropzone.attributes('data-has-while-tap')).toBe('true')
    expect(wrapper.find('[data-motion-component="motion-span"][data-motion-status="true"]').exists()).toBe(true)
    expect(wrapper.find('[data-motion-component="motion-div"][data-motion-icon="true"]').exists()).toBe(true)
    expect(wrapper.find('[data-motion-component="motion-div"][data-motion-actions="true"]').exists()).toBe(true)
  })

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

  it('falls back to the session file id when the API returns an empty string', async () => {
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
      [{ fileId: 'session_file_1', fileName: 'empty-id.txt' }],
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
    expect(wrapper.text()).toContain('文件类型不符合要求。')
    expect(wrapper.emitted('rejected')?.[0]).toEqual([
      [{ fileName: 'evidence.txt', reason: '文件类型不符合要求。' }],
    ])
  })

  it('shows accepted file extensions in the upload affordance', () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf', 'image/png', 'image/jpeg', 'image/svg+xml'],
        maxFileSizeBytes: 10 * 1024 * 1024,
        maxFiles: 8,
      }),
    })

    expect(wrapper.get('[data-slot="file-upload-accept-hint"]').text()).toBe(
      '支持 .pdf、.png、.jpg、.jpeg、.svg · 单个文件不超过 10.0 MB · 最多 8 个文件',
    )
  })

  it('renders the default upload affordance as a component-library button', () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
      }),
    })

    const trigger = wrapper.get('[data-file-upload-button="true"]')
    expect(trigger.element.tagName).toBe('BUTTON')
    expect(trigger.attributes('data-slot')).toBe('button')
    expect(trigger.text()).toContain('选择文件')
    expect(wrapper.find('[data-slot="file-upload-dropzone"]').exists()).toBe(false)
    expect(wrapper.get('[data-slot="file-upload-accept-hint"]').text()).toContain('支持 .pdf')
  })

  it('validates extension accept entries against file names', async () => {
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
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['.png'],
        createUploadSession,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [new File(['png'], 'accepted-by-extension.png', { type: 'image/png' })])
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('accepted-by-extension.png')
  })

  it('does not consume upload slots for invalid files within the same batch', async () => {
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
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
        maxFiles: 1,
        createUploadSession,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [
      new File(['bad'], 'wrong-type.txt', { type: 'text/plain' }),
      new File(['pdf'], 'accepted.pdf', { type: 'application/pdf' }),
    ])
    await flushPromises()

    expect(wrapper.text()).toContain('文件类型不符合要求。')
    expect(wrapper.text()).toContain('accepted.pdf')
    expect(wrapper.text()).not.toContain('Maximum file count reached.')
    expect(createUploadSession).toHaveBeenCalledTimes(1)
  })

  it('renders a ReUI-style queue summary with total size and built-in actions', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
        maxFiles: 3,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([
      new File(['hello'], 'first.txt', { type: 'text/plain' }),
      new File(['world'], 'second.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-summary"]').text()).toContain('文件 (2/3)')
    expect(wrapper.get('[data-slot="file-upload-summary"]').text()).toContain('总计 10 B')
    expect(wrapper.get('[data-slot="file-upload-add-button"]').text()).toContain('添加文件')

    await wrapper.get('[data-slot="file-upload-clear-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
    expect(wrapper.find('[data-slot="file-upload-summary"]').exists()).toBe(false)
  })

  it('accepts dropped files and shows the active drop state', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        variant: 'queue',
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

  it('formats very large files with a GB label', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose
    const file = new File(['cad'], 'plant-model.step', { type: 'application/step' })
    Object.defineProperty(file, 'size', {
      configurable: true,
      value: 3 * 1024 * 1024 * 1024,
    })

    await upload.addFiles([file])
    await flushPromises()

    expect(wrapper.text()).toContain('3.0 GB')
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
    const createUploadSession = vi.fn()
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
    expect(wrapper.text()).toContain('已完成')
    expect(wrapper.emitted('completed')?.[0]).toEqual([
      [{ fileId: 'file_1', fileName: 'external-evidence.txt' }],
    ])
  })

  it('uses progress color instead of a completed row border highlight', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        variant: 'queue',
        transport: vi.fn().mockImplementation(({ onProgress }) => {
          onProgress(100)
          return Promise.resolve()
        }),
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['hello'], 'completed-evidence.txt', { type: 'text/plain' })])
    await upload.uploadQueued()
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-row"]').attributes('data-status')).toBe('completed')
    expect(wrapper.get('[data-slot="progress"]').attributes('class')).toContain(
      '[&_[data-slot=progress-indicator]]:bg-success',
    )
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

  it('renders image thumbnails for image uploads', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
        acceptedContentTypes: ['image/png'],
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([new File(['png'], 'defect-photo.png', { type: 'image/png' })])
    await flushPromises()

    const thumbnail = wrapper.get('[data-slot="file-upload-thumbnail"]')
    expect(thumbnail.attributes('src')).toBe('blob:nerv-iip-preview')
    expect(thumbnail.attributes('alt')).toBe('defect-photo.png')
  })

  it('supports queue, compact, gallery, table, avatar, and image variants', async () => {
    for (const variant of ['queue', 'compact', 'gallery', 'table', 'avatar', 'image'] as const) {
      const wrapper = mount(FileUpload, {
        props: createBaseProps({
          autoUpload: false,
          variant,
          acceptedContentTypes: ['image/png'],
        }),
      })
      const upload = wrapper.vm as unknown as FileUploadExpose

      await upload.addFiles([new File(['png'], `${variant}-upload.png`, { type: 'image/png' })])
      await flushPromises()

      expect(wrapper.get('[data-slot="file-upload"]').attributes('data-variant')).toBe(variant)

      if (variant === 'compact' || variant === 'gallery' || variant === 'image') {
        const cta = wrapper.get('[data-file-upload-cta="true"]')
        expect(cta.attributes('data-slot')).toBe('button')
        expect(cta.element.tagName).toBe('SPAN')
      }

      if (variant === 'avatar') {
        expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
        expect(wrapper.get('[data-slot="file-upload-thumbnail"]').attributes('alt')).toBe('avatar-upload.png')
        continue
      }

      expect(wrapper.get('[data-slot="file-upload-row"]').attributes('data-variant')).toBe(variant)

      if (variant === 'gallery' || variant === 'image') {
        expect(wrapper.find('[data-slot="file-upload-grid"]').exists()).toBe(true)
      }

      if (variant === 'table') {
        expect(wrapper.find('[data-slot="file-upload-table"]').exists()).toBe(true)
      }
    }
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
      purpose: 'quality-evidence',
    })

    await upload.uploadQueued()
    await flushPromises()

    expect(createUploadSession).toHaveBeenCalledWith(expect.objectContaining({
      organizationId: 'org_new',
      environmentId: 'env_new',
      filePurpose: 'quality-evidence',
      owner: expect.objectContaining({
        ownerId: 'inspection_new',
      }),
    }))
  })

  it('clears rows without emitting a synthetic empty completed event', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        autoUpload: false,
      }),
    })
    const upload = wrapper.vm as unknown as FileUploadExpose

    await upload.addFiles([
      new File(['hello'], 'queued-evidence.txt', { type: 'text/plain' }),
    ])
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

    await upload.addFiles([
      new File(['hello'], 'queued-evidence.txt', { type: 'text/plain' }),
    ])
    await wrapper.get('button[aria-label="移除 queued-evidence.txt"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-row"]').exists()).toBe(false)
    expect(wrapper.emitted('completed')).toBeUndefined()
  })

  it('uses semantic badge variants for completed and rejected rows', async () => {
    const wrapper = mount(FileUpload, {
      props: createBaseProps({
        acceptedContentTypes: ['application/pdf'],
        maxFiles: 3,
        transport: vi.fn().mockResolvedValue(undefined),
      }),
    })

    await selectFiles(wrapper, [
      new File(['pdf'], 'inspection.pdf', { type: 'application/pdf' }),
      new File(['hello'], 'wrong-type.txt', { type: 'text/plain' }),
    ])
    await flushPromises()

    const variants = wrapper.findAll('[data-slot="badge"]')
      .map(badge => badge.attributes('data-variant'))

    expect(variants).toContain('success')
    expect(variants).toContain('destructive')
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

    await selectFiles(wrapper, [
      new File(['hello'], 'wrong-type.txt', { type: 'text/plain' }),
    ])
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
    const files = Array.from({ length: 60 }, (_, index) =>
      new File(['hello'], `bulk-${index}.txt`, { type: 'text/plain' }))

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.find('[data-slot="file-upload-virtual-list"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-slot="file-upload-row"]').length).toBeLessThan(files.length)
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
    const files = Array.from({ length: 12 }, (_, index) =>
      new File(['hello'], `spaced-${index}.txt`, { type: 'text/plain' }))

    await upload.addFiles(files)
    await flushPromises()

    expect(wrapper.get('[data-slot="file-upload-virtual-list"]').attributes('style')).toContain('height: 1200px')
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
