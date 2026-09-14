/**
 * 交接班附件（车间拍照）的 **tus 字节面** 传输。
 *
 * ## 为什么这里手搓 fetch，而不是用 generated operation
 *
 * `patchBusinessConsoleShiftHandoverAttachmentTusUpload` 这个 generated operation 在
 * `types.gen.ts` 里是 `body?: never` 且没有任何 header 参数（`PatchBusinessConsoleShiftHandoverAttachmentTusUploadData`）
 * ——**它传不了字节，也传不了 `Upload-Offset` / `X-Organization-Id`**。
 * 原因是 tus 从未进过 OpenAPI 契约（#3085 / PR #3096 第三轮归因裁定），不是缺陷，
 * 所以本文件不是在绕过 api-client，而是在补一条契约本来就没有描述的字节通路。
 * `HEAD`（取 offset）同理：generated operation 只声明 `204: void`，读不到响应头。
 *
 * 会话创建（`POST .../upload-sessions`）与提交（`POST .../{uploadSessionId}/complete`）
 * **仍然走 generated operation**，见 `@/composables/useBusinessShiftHandover`。
 * 本文件只负责中间那两跳。
 *
 * ## 服务端硬约束（读自代码，不是推断）
 *
 * - 网关字节面（`BusinessConsoleFileTransfer.ProxyAsync`）要求 `X-Organization-Id` /
 *   `X-Environment-Id`（header 或 query 二选一），缺了直接 400；权限是
 *   `business.mes.handovers.manage`。
 * - FileStorage 的 `PatchTusUploadEndpoint` 依次要求：`Tus-Resumable: 1.0.0`（否则 412）、
 *   `Content-Type: application/offset+octet-stream`（否则 415）、可解析的 `Upload-Offset`
 *   （否则 400），且该 offset 必须等于服务端当前 offset（否则 409）。
 * - 网关只转发 `Tus-Resumable` / `Upload-Offset` / `Upload-Checksum` 三个请求头 + Content-Type，
 *   所以别指望塞别的头能到达 FileStorage。
 * - `shift-handover-photo` 用途只允许 `image/png` / `image/jpeg`，上限 20,971,520 字节。
 *
 * ## 超时
 *
 * 复用 `./request-timeout` 的同一个 30s 上限与离线预检——PDA 只有这一个传输时限口径
 * （AGENTS 硬性规则 3 的意图）。这里不新开一套时限。
 */
import { createTimeoutFetch, resolveRequestTimeoutMs } from './request-timeout'

/** tus 协议版本；FileStorage 只接受这一个值（`PatchTusUploadEndpoint.TusVersion`）。 */
export const TUS_RESUMABLE_VERSION = '1.0.0'

/** tus `PATCH` 的强制 Content-Type（`PatchTusUploadEndpoint.OffsetOctetStreamContentType`）。 */
export const TUS_PATCH_CONTENT_TYPE = 'application/offset+octet-stream'

export const ORGANIZATION_HEADER = 'X-Organization-Id'
export const ENVIRONMENT_HEADER = 'X-Environment-Id'

/** `shift-handover-photo` 用途允许的内容类型（FileStorage `appsettings.json`）。 */
export const SHIFT_HANDOVER_PHOTO_CONTENT_TYPES = ['image/jpeg', 'image/png'] as const

/** `shift-handover-photo` 用途的单文件上限（FileStorage `MaximumFileSizeBytes`）。 */
export const SHIFT_HANDOVER_PHOTO_MAX_BYTES = 20_971_520

export type ShiftHandoverPhotoContentType = (typeof SHIFT_HANDOVER_PHOTO_CONTENT_TYPES)[number]

/** 上传会话读面里与字节传输有关的两个字段（其余字段由调用方自己消费）。 */
export interface ShiftHandoverTusTarget {
  uploadUrl?: string | null
  uploadHeaders?: Record<string, string> | null
}

export interface ShiftHandoverTusScope {
  organizationId: string
  environmentId: string
  /** 登录态 access token；字节面不经过 api-client 的请求拦截器，必须自己带 Authorization。 */
  accessToken?: string
}

export interface ShiftHandoverTusTransport {
  /** 注入 fetch（测试用）。默认是本模块的 30s 超时 + 离线预检 fetch。 */
  fetch?: typeof fetch
  /** 网关绝对基址；Web/dev 留空走 vite 代理的相对路径，APK 必须给绝对值。 */
  baseUrl?: string
}

export interface ShiftHandoverTusTransferResult {
  /** 本次 `PATCH` 实际发出的字节数。 */
  sentBytes: number
  /**
   * 服务端复查到的最终 offset。
   *
   * `undefined` 表示**没读到** `Upload-Offset` 响应头——不是「复查通过」。
   * 跨源部署（APK 直连网关）若没有 `Access-Control-Expose-Headers: Upload-Offset`，
   * 浏览器就不把这个头交给脚本。此时本函数不谎报复查成功，由后续 `complete`
   * 做权威判定：FileStorage 的 `TusUploadCompletionValidator` 会比对 store offset
   * 与 `ExpectedSizeBytes`，字节没写完 complete 必失败。
   */
  confirmedOffset: number | undefined
}

export function isShiftHandoverPhotoContentType(
  contentType?: string | null,
): contentType is ShiftHandoverPhotoContentType {
  const normalized = (contentType ?? '').split(';', 1)[0].trim().toLowerCase()
  return (SHIFT_HANDOVER_PHOTO_CONTENT_TYPES as readonly string[]).includes(normalized)
}

/**
 * 把会话返回的网关相对路径拼成可请求的 URL。
 *
 * 网关只会回 `/api/business-console/v1/files/shift-handover-attachments/tus/{id}`
 * 这种**相对路径**（`FileStorageRoutes.RewriteProxiedUrl` 保证前缀，绝对 URL 在网关侧
 * 就已经失败关闭）。所以：绝对 URL 一律拒绝——它只可能来自被改写过的响应，跟着走就是
 * 把登录令牌发给第三方主机。
 */
export function resolveTusRequestUrl(uploadUrl: string, baseUrl?: string): string {
  const trimmed = uploadUrl.trim()
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
    throw new Error('附件上传地址不是网关内部路径，已拒绝上传。')
  }
  const base = (baseUrl ?? '').trim().replace(/\/+$/, '')
  return base ? `${base}${trimmed}` : trimmed
}

/** 从响应头里读 `Upload-Offset`；读不到（含跨源未 expose）返回 undefined，绝不当 0 用。 */
export function readUploadOffset(response: { headers?: Headers }): number | undefined {
  const raw = response.headers?.get('Upload-Offset')
  if (raw === null || raw === undefined) return undefined
  const trimmed = raw.trim()
  if (!/^\d+$/.test(trimmed)) return undefined
  const value = Number(trimmed)
  return Number.isSafeInteger(value) ? value : undefined
}

let sharedTransferFetch: typeof fetch | undefined

function defaultTransferFetch(): typeof fetch {
  sharedTransferFetch ??= createTimeoutFetch({ timeoutMs: resolveRequestTimeoutMs() })
  return sharedTransferFetch
}

function scopeHeaders(scope: ShiftHandoverTusScope): Record<string, string> {
  const headers: Record<string, string> = {
    [ORGANIZATION_HEADER]: scope.organizationId,
    [ENVIRONMENT_HEADER]: scope.environmentId,
  }
  if (scope.accessToken) headers.Authorization = `Bearer ${scope.accessToken}`
  return headers
}

function sessionHeaders(target: ShiftHandoverTusTarget): Record<string, string> {
  const entries = Object.entries(target.uploadHeaders ?? {}).filter(
    ([key, value]) => key.trim() && typeof value === 'string' && value.trim(),
  )
  return Object.fromEntries(entries)
}

function transferFailure(action: string, status: number): Error {
  if (status === 401) return new Error('登录已失效，请重新登录后再传照片。')
  if (status === 403) return new Error('当前账号没有交接班附件上传权限。')
  if (status === 404) return new Error('上传会话已失效或已过期，请重新拍照。')
  if (status === 409) return new Error('上传进度与服务端不一致，请重新拍照上传。')
  if (status === 413) return new Error('照片超出交接班附件大小上限，请重拍或压缩后再传。')
  if (status === 415) return new Error('照片格式不被接受，交接班附件只支持 JPG / PNG。')
  return new Error(`${action}失败（HTTP ${status}），请重试。`)
}

/**
 * 送一张照片的字节：`HEAD` 取 offset → `PATCH` 送字节 → 复查 offset。
 *
 * 只覆盖 #3085 通路的第 2、3、4 步；建会话与 complete 由调用方走 generated operation。
 * 大图不分片——`shift-handover-photo` 上限 20MB，单次 `PATCH` 在 30s 上限内是现实的，
 * 且分片会把「幂等重试」这件事变复杂而本票没有断点续传需求。`HEAD` 读到的 offset 仍被
 * 用作续传起点，所以同一会话重试不会重复追加字节。
 */
export async function sendShiftHandoverAttachmentBytes(
  target: ShiftHandoverTusTarget,
  bytes: Blob,
  scope: ShiftHandoverTusScope,
  transport: ShiftHandoverTusTransport = {},
): Promise<ShiftHandoverTusTransferResult> {
  const uploadUrl = target.uploadUrl?.trim()
  if (!uploadUrl) throw new Error('文件服务未返回可用的附件上传地址。')
  if (!scope.organizationId.trim() || !scope.environmentId.trim()) {
    throw new Error('缺少组织或环境范围，未发起附件上传。')
  }

  const url = resolveTusRequestUrl(uploadUrl, transport.baseUrl)
  const doFetch = transport.fetch ?? defaultTransferFetch()
  const baseHeaders = { ...sessionHeaders(target), ...scopeHeaders(scope) }

  const head = await doFetch(url, {
    method: 'HEAD',
    headers: { ...baseHeaders, 'Tus-Resumable': TUS_RESUMABLE_VERSION },
  })
  if (!head.ok) throw transferFailure('读取上传进度', head.status)

  // 读不到 offset 时按「新建会话 = 0」起传。这不是「复查通过」，只是起点假设：
  // 起点若真的不是 0，FileStorage 会以 409 顶回来（PatchTusUploadEndpoint 比对 currentOffset），
  // 不会静默写歪。
  const startOffset = readUploadOffset(head) ?? 0
  if (startOffset > bytes.size) {
    throw new Error('服务端已记录的上传进度超过本张照片大小，请重新拍照上传。')
  }

  const payload = startOffset > 0 ? bytes.slice(startOffset) : bytes
  const patch = await doFetch(url, {
    method: 'PATCH',
    headers: {
      ...baseHeaders,
      'Tus-Resumable': TUS_RESUMABLE_VERSION,
      'Content-Type': TUS_PATCH_CONTENT_TYPE,
      'Upload-Offset': String(startOffset),
    },
    body: payload,
  })
  if (!patch.ok) throw transferFailure('上传照片', patch.status)

  // 复查 offset：优先读 PATCH 自己的响应头，读不到再补一次 HEAD。
  let confirmedOffset = readUploadOffset(patch)
  if (confirmedOffset === undefined) {
    const recheck = await doFetch(url, {
      method: 'HEAD',
      headers: { ...baseHeaders, 'Tus-Resumable': TUS_RESUMABLE_VERSION },
    })
    if (recheck.ok) confirmedOffset = readUploadOffset(recheck)
  }
  if (confirmedOffset !== undefined && confirmedOffset !== bytes.size) {
    throw new Error(`照片未完整写入（已写入 ${confirmedOffset} / 共 ${bytes.size} 字节），请重试。`)
  }

  return { sentBytes: payload.size, confirmedOffset }
}
