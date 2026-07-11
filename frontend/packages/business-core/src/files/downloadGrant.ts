export interface DownloadGrantLike {
  downloadUrl?: string | null
  downloadHeaders?: Record<string, string> | null
}

const DEFAULT_DOWNLOAD_TIMEOUT_MS = 30_000

export interface OpenDownloadGrantOptions {
  /**
   * Fetch used for the blob download. Inject a timeout/offline-aware fetch (e.g. the PDA
   * global fetch) so an offline device fails fast. Defaults to `globalThis.fetch`.
   */
  fetch?: typeof fetch
  /**
   * Overall ceiling (ms) for the WHOLE download, including the response body read. The
   * injected fetch only bounds time-to-headers, but `response.blob()` below can stall
   * after headers arrive — this AbortController-backed ceiling covers that too. Defaults
   * to 30s.
   */
  timeoutMs?: number
}

export async function openDownloadGrantBlob(
  grant: DownloadGrantLike,
  options: OpenDownloadGrantOptions = {},
): Promise<void> {
  const downloadUrl = grant.downloadUrl?.trim()
  if (!downloadUrl) throw new Error('文件服务未返回可用的SOP查看链接。')

  const doFetch = options.fetch ?? globalThis.fetch
  const timeoutMs = options.timeoutMs ?? DEFAULT_DOWNLOAD_TIMEOUT_MS

  // Bound headers AND body: aborting the signal also aborts an in-flight `blob()` read,
  // so a body that stalls after the headers arrive still fails within the ceiling.
  const controller = new AbortController()
  let timedOut = false
  const timer = setTimeout(() => {
    timedOut = true
    controller.abort()
  }, timeoutMs)

  try {
    const response = await doFetch(downloadUrl, {
      headers: normalizeHeaders(grant.downloadHeaders),
      signal: controller.signal,
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
  } catch (error) {
    // Our ceiling fired (headers or a stalled body) → actionable, retryable copy.
    if (timedOut) throw new Error('网络超时，请检查连接后重试')
    throw error
  } finally {
    clearTimeout(timer)
  }
}

function normalizeHeaders(headers?: Record<string, string> | null): HeadersInit {
  if (!headers) return {}
  return Object.fromEntries(
    Object.entries(headers).filter(([key, value]) => key.trim() && value.trim()),
  )
}
