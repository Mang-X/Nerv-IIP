/**
 * 一次取字节请求的目标：URL + 随请求发出的头。
 *
 * #3314 之前这里装的是网关回给调用方的 download grant（`downloadUrl` 指向
 * `/files/download-grants/{grantId}/content`）。该形状已被移除：FileStorage 的 grant id 是
 * 全服务共用命名空间、兑换面既不看用途也不看签发门面，实测可以在权限口径不同的另一条网关
 * 路由上兑换成功。现在网关只暴露以 `fileId` 为入参的单跳字节路由，grant 在服务端签发并
 * 立即兑换，调用方拿不到 grant id（ADR 0030 决策 3）。
 */
export interface FileContentTarget {
  downloadUrl?: string | null
  downloadHeaders?: Record<string, string> | null
}

export const ORGANIZATION_HEADER = 'X-Organization-Id'
export const ENVIRONMENT_HEADER = 'X-Environment-Id'

export interface BusinessScopeLike {
  organizationId: string
  environmentId: string
}

/**
 * 工程 SOP 文件的字节路由。组织/环境经头部传给网关字节面
 * （`BusinessConsoleFileTransfer.ProxyAsync` 缺了直接 400）。
 */
export function sopFileContentTarget(fileId: string, scope: BusinessScopeLike): FileContentTarget {
  return {
    downloadUrl: `/api/business-console/v1/files/sop-documents/${encodeURIComponent(fileId)}/content`,
    downloadHeaders: {
      [ORGANIZATION_HEADER]: scope.organizationId,
      [ENVIRONMENT_HEADER]: scope.environmentId,
    },
  }
}

export interface OpenFileContentOptions {
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

export async function openFileContentBlob(
  target: FileContentTarget,
  options: OpenFileContentOptions = {},
): Promise<void> {
  const downloadUrl = target.downloadUrl?.trim()
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
      headers: normalizeHeaders(target.downloadHeaders),
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
