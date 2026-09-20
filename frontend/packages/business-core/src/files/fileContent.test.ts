import { afterEach, describe, expect, it, vi } from 'vitest'
import { openSopFileContent } from './fileContent'

const SCOPE = { organizationId: 'org-001', environmentId: 'env-dev' }

describe('openSopFileContent', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('fetches the single-hop fileId route with the business scope and opens the blob URL', async () => {
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

    await openSopFileContent('file-sop-1', SCOPE)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/business-console/v1/files/sop-documents/file-sop-1/content',
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
      openSopFileContent('file-sop-1', SCOPE, {
        fetch: injectedFetch as unknown as typeof fetch,
      }),
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

    const pending = openSopFileContent('file-sop-1', SCOPE, { timeoutMs: 1_000 })
    const assertion = expect(pending).rejects.toThrow('网络超时')
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
    vi.useRealTimers()
  })

  // #3314：原先还有一条「grant 没带 downloadUrl 就报错」的用例。URL 现在由本函数用 fileId
  // 本地拼出，**那个输入在新结构下不可表达**，用例随之删除（不是不变量消失，是状态消失）。
  // 接替它的是下面这条：URL 必须是单跳 fileId 路由、fileId 必须被转义、不得含 download-grants 段。
  it('builds an escaped single-hop fileId route that carries no grant segment', async () => {
    const fetchMock = vi.fn(async () => ({ ok: false }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(openSopFileContent('file sop/v2', SCOPE)).rejects.toThrow('无法下载SOP文件')

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/business-console/v1/files/sop-documents/file%20sop%2Fv2/content')
    expect(url).not.toContain('download-grants')
    expect(init.headers).toEqual({
      'X-Organization-Id': 'org-001',
      'X-Environment-Id': 'env-dev',
    })
  })
})
