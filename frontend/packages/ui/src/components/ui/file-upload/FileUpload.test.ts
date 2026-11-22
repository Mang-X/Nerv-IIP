import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { FileUpload } from '.'

import {
  stubFileUploadObjectUrls,
  createBaseProps,
  selectFiles,
  qualityEvidencePurpose,
} from './FileUpload.test.helpers'

vi.mock('motion-v', async () =>
  (await import('./FileUpload.test.helpers')).createFileUploadMotionMock(),
)

stubFileUploadObjectUrls()

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
        purpose: qualityEvidencePurpose,
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
      filePurpose: qualityEvidencePurpose,
      fileName: 'evidence.txt',
      contentType: 'text/plain',
      expectedSizeBytes: 5,
      checksum: null,
    })
    expect(completeUploadSession).toHaveBeenCalledWith('ups_1', {
      organizationId: 'org_1',
      environmentId: 'env_1',
      filePurpose: qualityEvidencePurpose,
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
        purpose: qualityEvidencePurpose,
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

    await selectFiles(wrapper, [
      new File(['png'], 'accepted-by-extension.png', { type: 'image/png' }),
    ])
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
})
