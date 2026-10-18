import { configureApiClient } from '@nerv-iip/api-client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { openSopFileContent } from './fileContent'

const SCOPE = { organizationId: 'org-001', environmentId: 'env-dev' }

describe('openSopFileContent', () => {
  const link = { href: '', target: '', rel: '', click: vi.fn(), remove: vi.fn() }
  const createObjectURL = vi.fn<(blob: Blob | MediaSource) => string>(() => 'blob:sop')

  beforeEach(() => {
    vi.stubGlobal('document', { createElement: () => link, body: { appendChild: vi.fn() } })
    vi.spyOn(URL, 'createObjectURL').mockImplementation(createObjectURL)
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    vi.stubGlobal('window', { setTimeout: (callback: () => void) => callback() })
  })

  afterEach(() => {
    vi.useRealTimers()
    configureApiClient()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    vi.clearAllMocks()
  })

  it('downloads actual bytes with the configured bearer token and business scope, then opens the blob', async () => {
    const fetchMock = vi.fn<typeof fetch>(
      async () => new Response('SOP 工序指导', { headers: { 'Content-Type': 'application/pdf' } }),
    )
    configureApiClient({
      baseUrl: 'https://gateway.example',
      accessTokenProvider: () => 'synthetic-token',
      fetch: fetchMock,
    })

    await openSopFileContent('file sop/v2', SCOPE)

    const request = fetchMock.mock.calls[0]?.[0] as Request
    expect(request.url).toBe(
      'https://gateway.example/api/business-console/v1/files/sop-documents/file%20sop%2Fv2/content',
    )
    expect(request.headers.get('Authorization')).toBe('Bearer synthetic-token')
    expect(request.headers.get('X-Organization-Id')).toBe(SCOPE.organizationId)
    expect(request.headers.get('X-Environment-Id')).toBe(SCOPE.environmentId)
    const blob = createObjectURL.mock.calls[0]?.[0] as Blob
    expect(await blob.text()).toBe('SOP 工序指导')
    expect(blob.type).toBe('application/pdf')
    expect(link.href).toBe('blob:sop')
    expect(link.target).toBe('_blank')
    expect(link.rel).toBe('noopener')
    expect(link.click).toHaveBeenCalledOnce()
    expect(link.remove).toHaveBeenCalledOnce()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:sop')
  })

  it('uses the configured transport and propagates its timeout error', async () => {
    configureApiClient({
      baseUrl: 'https://gateway.example',
      fetch: async () => {
        throw new Error('网络超时，请检查连接后重试')
      },
    })
    await expect(openSopFileContent('file-sop-1', SCOPE)).rejects.toThrow('网络超时')
    expect(link.click).not.toHaveBeenCalled()
  })

  it('runs the shared unauthorized handler on 401 without opening a file', async () => {
    const onUnauthorized = vi.fn()
    configureApiClient({
      baseUrl: 'https://gateway.example',
      onUnauthorized,
      fetch: async () => new Response('Unauthorized', { status: 401 }),
    })
    await expect(openSopFileContent('file-sop-1', SCOPE)).rejects.toMatchObject({ status: 401 })
    expect(onUnauthorized).toHaveBeenCalledOnce()
    expect(link.click).not.toHaveBeenCalled()
  })

  it('bounds a body read that stalls after headers arrive', async () => {
    vi.useFakeTimers()
    let bodyStarted!: () => void
    const started = new Promise<void>((resolve) => {
      bodyStarted = resolve
    })
    configureApiClient({
      baseUrl: 'https://gateway.example',
      fetch: async (input) => {
        const request = input as Request
        return new Response(
          new ReadableStream({
            start(controller) {
              request.signal.addEventListener('abort', () =>
                controller.error(new DOMException('Aborted', 'AbortError')),
              )
              bodyStarted()
            },
          }),
        )
      },
    })
    const pending = openSopFileContent('file-sop-1', SCOPE, { timeoutMs: 1_000 })
    const assertion = expect(pending).rejects.toThrow('网络超时')
    await started
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
    expect(link.click).not.toHaveBeenCalled()
  })
})
