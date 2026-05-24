import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

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
})

async function flushPromises() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}
