export interface DownloadGrantLike {
  downloadUrl?: string | null
  downloadHeaders?: Record<string, string> | null
}

export interface OpenDownloadGrantOptions {
  /**
   * Fetch used for the blob download. Inject a timeout/offline-aware fetch (e.g. the PDA
   * global fetch) so an offline device fails fast. Defaults to `globalThis.fetch`.
   */
  fetch?: typeof fetch
  /**
   * Overall ceiling (ms) for the WHOLE download, INCLUDING the response body read.
   *
   * OPT-IN: when omitted the download stays UNBOUNDED — this preserves existing
   * PC/console behavior so a legitimately large SOP download is never cut off (#814 is
   * a PDA-scoped fallback and must not silently tighten other callers). PDA passes its
   * 30s ceiling together with a timeout/offline-aware `fetch`; the AbortController's
   * signal is forwarded to that fetch, which keeps the caller→signal link alive past
   * headers so a body stalling after headers is aborted too.
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
  const { timeoutMs } = options

  // Only arm an overall ceiling when the caller opted in; otherwise stay unbounded.
  const controller = timeoutMs === undefined ? undefined : new AbortController()
  let timedOut = false
  const timer =
    controller === undefined
      ? undefined
      : setTimeout(() => {
          timedOut = true
          controller.abort()
        }, timeoutMs)

  try {
    const response = await doFetch(downloadUrl, {
      headers: normalizeHeaders(grant.downloadHeaders),
      ...(controller ? { signal: controller.signal } : {}),
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
    // Our opted-in ceiling fired (headers or a stalled body) → actionable, retryable copy.
    if (timedOut) throw new Error('网络超时，请检查连接后重试')
    throw error
  } finally {
    if (timer !== undefined) clearTimeout(timer)
  }
}

function normalizeHeaders(headers?: Record<string, string> | null): HeadersInit {
  if (!headers) return {}
  return Object.fromEntries(
    Object.entries(headers).filter(([key, value]) => key.trim() && value.trim()),
  )
}
