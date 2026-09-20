/**
 * 业务范围：网关字节面从 `X-Organization-Id` / `X-Environment-Id` 取范围，缺了直接 400。
 */
export interface BusinessScopeLike {
  organizationId: string
  environmentId: string
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

export const ORGANIZATION_HEADER = 'X-Organization-Id'
export const ENVIRONMENT_HEADER = 'X-Environment-Id'

/**
 * 打开一个工程 SOP 文件的字节。
 *
 * #3314 之前这里收的是网关回给调用方的 download grant（`downloadUrl` 指向
 * `/files/download-grants/{grantId}/content`），所以入参必须是「可能缺 URL」的可空形状，
 * 并配一条运行期空值守卫。该形状已被移除：FileStorage 的 grant id 是全服务共用命名空间、
 * 兑换面既不看用途也不看签发门面，实测可以在权限口径不同的另一条网关路由上兑换成功。
 *
 * 现在网关只暴露以 `fileId` 为入参的单跳字节路由，grant 在服务端签发并立即兑换
 * （ADR 0030 决策 3）。URL 由本函数本地拼出，**结构上不可能为空**——所以可空入参、
 * 空值守卫与两个 app 各自的转发包装都一并删掉，不留一个已经不成立的状态。
 */
export async function openSopFileContent(
  fileId: string,
  scope: BusinessScopeLike,
  options: OpenFileContentOptions = {},
): Promise<void> {
  const downloadUrl = `/api/business-console/v1/files/sop-documents/${encodeURIComponent(fileId)}/content`
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
      headers: {
        [ORGANIZATION_HEADER]: scope.organizationId,
        [ENVIRONMENT_HEADER]: scope.environmentId,
      },
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
