import { downloadBusinessConsoleSopFileContent } from '@nerv-iip/api-client'

/**
 * 业务范围：网关字节面从 `X-Organization-Id` / `X-Environment-Id` 取范围，缺了直接 400。
 */
export interface BusinessScopeLike {
  organizationId: string
  environmentId: string
}

export interface OpenFileContentOptions {
  /**
   * Overall ceiling (ms) for the WHOLE download, INCLUDING the response body read.
   *
   * OPT-IN: when omitted the download stays UNBOUNDED — this preserves existing
   * PC/console behavior so a legitimately large SOP download is never cut off. PDA
   * uses its globally configured SDK timeout/offline-aware fetch. This additional
   * signal stays live through the SDK body read so a stalled body is aborted too.
   */
  timeoutMs?: number
}

export const ORGANIZATION_HEADER = 'X-Organization-Id'
export const ENVIRONMENT_HEADER = 'X-Environment-Id'

/** 使用已配置鉴权的 generated SDK 读取 SOP 字节，再打开文件。 */
export async function openSopFileContent(
  fileId: string,
  scope: BusinessScopeLike,
  options: OpenFileContentOptions = {},
): Promise<void> {
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
    const { data } = await downloadBusinessConsoleSopFileContent({
      path: { fileId },
      parseAs: 'blob',
      throwOnError: true,
      headers: {
        [ORGANIZATION_HEADER]: scope.organizationId,
        [ENVIRONMENT_HEADER]: scope.environmentId,
      },
      ...(controller ? { signal: controller.signal } : {}),
    })
    const blobUrl = URL.createObjectURL(data)
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
