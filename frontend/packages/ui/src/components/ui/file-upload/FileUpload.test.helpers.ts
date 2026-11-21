import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import fileStorageSettings from '../../../../../../../backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/appsettings.json'

export const qualityEvidencePurpose =
  Object.keys(fileStorageSettings.FileStorage.PurposePolicies).find(
    (purpose) => purpose === 'quality-evidence',
  ) ??
  (() => {
    throw new Error('FileStorage purpose configuration must register quality-evidence.')
  })()

export function stubFileUploadObjectUrls() {
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
}

export function createFileUploadMotionMock() {
  const passthrough = (slot: string, tag = 'div') =>
    defineComponent({
      name: slot,
      inheritAttrs: false,
      setup(_, { attrs, slots }) {
        const hasAttr = (name: string) => attrs[name] !== undefined

        return () =>
          h(
            tag,
            {
              ...attrs,
              'data-motion-component': slot,
              'data-has-while-hover':
                hasAttr('whileHover') || hasAttr('while-hover') ? 'true' : undefined,
              'data-has-while-tap':
                hasAttr('whileTap') || hasAttr('while-tap') ? 'true' : undefined,
              'data-has-layout': hasAttr('layout') ? 'true' : undefined,
            },
            slots.default?.(),
          )
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
}

export function createBaseProps(overrides: Record<string, unknown> = {}) {
  return {
    purpose: qualityEvidencePurpose,
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

export async function selectFiles(wrapper: ReturnType<typeof mount>, files: File[]) {
  const input = wrapper.get('input[type="file"]')
  Object.defineProperty(input.element, 'files', {
    configurable: true,
    value: files,
  })

  await input.trigger('change')
}

export function createDeferredTransport() {
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
