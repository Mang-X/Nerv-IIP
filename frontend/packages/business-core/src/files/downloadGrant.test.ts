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
      {
        headers: {
          'X-Organization-Id': 'org-001',
          'X-Environment-Id': 'env-dev',
        },
      },
    )
    expect(link.href).toBe('blob:sop')
    expect(link.target).toBe('_blank')
    expect(link.rel).toBe('noopener')
    expect(appendChild).toHaveBeenCalledWith(link)
    expect(link.click).toHaveBeenCalled()
    expect(link.remove).toHaveBeenCalled()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:sop')
  })

  it('rejects grants without a download URL', async () => {
    await expect(openDownloadGrantBlob({ downloadUrl: ' ' })).rejects.toThrow('文件服务未返回可用的SOP查看链接。')
  })
})
