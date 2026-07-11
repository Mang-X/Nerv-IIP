import { afterEach, describe, expect, it, vi } from 'vitest'
import { openDownloadGrantBlob } from './downloadGrant'

describe('openDownloadGrantBlob', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('fetches the grant URL with download headers and opens the blob URL', async () => {
    const fetchMock = vi.fn(async () => ({
      ok: true,
      blob: vi.fn(async () => new Blob(['sop'])),
    }))
    const link = {
      href: '',
      target: '',
      rel: '',
      click: vi.fn(),
      remove: vi.fn(),
    }
    const appendChild = vi.fn()
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('document', {
      createElement: vi.fn(() => link),
      body: { appendChild },
    })
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:sop'),
      revokeObjectURL,
    })
    vi.stubGlobal('window', {
      setTimeout: vi.fn((callback: () => void) => {
        callback()
        return 1
      }),
    })

    await openDownloadGrantBlob({
      downloadUrl: '/api/business-console/v1/files/download-grants/grant-1/content',
      downloadHeaders: {
        'X-Organization-Id': 'org-001',
        'X-Environment-Id': 'env-dev',
      },
    })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/business-console/v1/files/download-grants/grant-1/content',
      expect.objectContaining({
        headers: {
          'X-Organization-Id': 'org-001',
          'X-Environment-Id': 'env-dev',
        },
      }),
    )
    expect(link.href).toBe('blob:sop')
    expect(link.target).toBe('_blank')
    expect(link.rel).toBe('noopener')
    expect(appendChild).toHaveBeenCalledWith(link)
    expect(link.click).toHaveBeenCalled()
    expect(link.remove).toHaveBeenCalled()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:sop')
  })

  it('uses an injected fetch (e.g. the PDA timeout fetch) and propagates its error', async () => {
    // A hung download on flaky Wi-Fi must fail via the injected timeout fetch, not hang.
    const globalFetch = vi.fn()
    vi.stubGlobal('fetch', globalFetch)
    const injectedFetch = vi.fn(async () => {
      throw new Error('网络超时，请检查连接后重试')
    })

    await expect(
      openDownloadGrantBlob(
        { downloadUrl: '/api/business-console/v1/files/download-grants/grant-1/content' },
        { fetch: injectedFetch as unknown as typeof fetch },
      ),
    ).rejects.toThrow('网络超时')
    expect(injectedFetch).toHaveBeenCalledTimes(1)
    expect(globalFetch).not.toHaveBeenCalled()
  })

  it('bounds a body read that stalls after headers arrive (aborts + timeout copy)', async () => {
    vi.useFakeTimers()
    // Headers arrive immediately, but blob() only settles when the signal aborts —
    // i.e. the body stream stalls. The overall ceiling must still abort it.
    const fetchMock = vi.fn((_url: string, init: { signal: AbortSignal }) =>
      Promise.resolve({
        ok: true,
        blob: () =>
          new Promise((_resolve, reject) => {
            init.signal.addEventListener('abort', () =>
              reject(new DOMException('The operation was aborted.', 'AbortError')),
            )
          }),
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const pending = openDownloadGrantBlob({ downloadUrl: '/x' }, { timeoutMs: 1_000 })
    const assertion = expect(pending).rejects.toThrow('网络超时')
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
    vi.useRealTimers()
  })

  it('rejects grants without a download URL', async () => {
    await expect(openDownloadGrantBlob({ downloadUrl: ' ' })).rejects.toThrow(
      '文件服务未返回可用的SOP查看链接。',
    )
  })
})
