export interface DownloadGrantLike {
  downloadUrl?: string | null
  downloadHeaders?: Record<string, string> | null
}

export interface OpenDownloadGrantOptions {
  /**
   * Fetch used for the blob download. Inject a timeout/offline-aware fetch (e.g. the PDA
   * global fetch) so a hung download on flaky 车间 Wi-Fi surfaces a bounded, actionable
   * error instead of an unbounded await. Defaults to `globalThis.fetch`.
   */
  fetch?: typeof fetch
}

export async function openDownloadGrantBlob(
  grant: DownloadGrantLike,
  options: OpenDownloadGrantOptions = {},
): Promise<void> {
  const downloadUrl = grant.downloadUrl?.trim()
  if (!downloadUrl) throw new Error('文件服务未返回可用的SOP查看链接。')

  const doFetch = options.fetch ?? globalThis.fetch
  const response = await doFetch(downloadUrl, {
    headers: normalizeHeaders(grant.downloadHeaders),
  })
  if (!response.ok) throw new Error('无法下载SOP文件，请稍后重试。')

  const blobUrl = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = blobUrl
  link.target = '_blank'
  link.rel = 'noopener'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60_000)
}

function normalizeHeaders(headers?: Record<string, string> | null): HeadersInit {
  if (!headers) return {}
  return Object.fromEntries(
    Object.entries(headers).filter(([key, value]) => key.trim() && value.trim()),
  )
}
