/**
 * 条码模板文件上传（#3856）：建会话 → tus `PATCH` 送字节 → complete，三跳都走 BusinessGateway
 * 的条码模板文件门面。用途、内容类型与 owner 的服务/类型由网关固定；owner 标识是这里传上去的模板编码，
 * 打印时服务端按它比对，所以上传时的编码必须与最终保存的模板编码一致。
 *
 * ## 为什么 `PATCH` 要显式传 `bodySerializer: null`
 *
 * tus 从未进 OpenAPI 契约（同交接班附件，#3085 / PR #3096 裁定），generated operation 的
 * `body` 是 `never`、也没有 header 参数。这里仍调用 generated operation——它带着全局的登录令牌、
 * 基址与 401 兜底——只是把原始字节与 tus 头原样塞进去，不另起一条手搓 fetch。
 *
 * ## 服务端硬约束（读自代码）
 *
 * - FileStorage `barcode-label-template` 用途：只收 `.json`、单文件 ≤ 65,536 字节、必须带
 *   SHA-256（建会话与 complete 各带一次且一致）。
 * - 打印时 BarcodeLabel 要求文件是 UTF-8 无 BOM、`format` 为 `nerv-iip.label-template`；这里提前
 *   把这两条挡在浏览器里，免得模板保存成功、到建打印批次时才失败。
 */
import {
  completeBusinessConsoleBarcodeTemplateAssetUpload,
  createBusinessConsoleBarcodeTemplateAssetUploadSession,
  getBusinessConsoleBarcodeTemplateAssetTusOffset,
  patchBusinessConsoleBarcodeTemplateAssetTusUpload,
} from '@nerv-iip/api-client'
import { sha256 } from '@noble/hashes/sha2.js'

/** FileStorage `PurposePolicies:barcode-label-template:MaximumFileSizeBytes`。 */
export const TEMPLATE_ASSET_MAX_BYTES = 65_536
export const TEMPLATE_ASSET_FORMAT = 'nerv-iip.label-template'

const TUS_RESUMABLE_VERSION = '1.0.0'
const TUS_PATCH_CONTENT_TYPE = 'application/offset+octet-stream'

/**
 * 浏览器预检不通过（文件格式、大小、编码、缺模板编码）：这是字段级提示，页面放在「模板文件」旁边；
 * 其它失败（网络、网关拒绝、上传不完整）是操作结果，页面走 toast。
 */
export class TemplateAssetPrecheckError extends Error {
  override name = 'TemplateAssetPrecheckError'
}

export interface TemplateAssetUploadScope {
  organizationId: string
  environmentId: string
  templateCode: string
}

export interface UploadedTemplateAsset {
  fileId: string
  fileName: string
  sizeBytes: number
}

/** 浏览器里能提前判断的文件问题；返回中文原因，没有问题返回空串。 */
export function templateAssetFileProblem(file: { name: string; size: number }) {
  if (!file.name.toLowerCase().endsWith('.json')) return '模板文件只支持 .json 格式。'
  if (file.size <= 0) return '模板文件是空的，请重新选择。'
  if (file.size > TEMPLATE_ASSET_MAX_BYTES) return '模板文件不能超过 64 KB。'
  return ''
}

/** 文件内容检查：UTF-8 无 BOM，且是平台标签模板格式。 */
export function templateAssetContentProblem(bytes: Uint8Array) {
  if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) {
    return '模板文件需要保存为不带 BOM 的 UTF-8 编码。'
  }
  let parsed: unknown
  try {
    parsed = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes))
  } catch {
    return '模板文件内容无法识别，请确认是平台导出的标签模板文件。'
  }
  const format = (parsed as { format?: unknown } | null)?.format
  if (format !== TEMPLATE_ASSET_FORMAT) {
    return '这不是平台的标签模板文件，请确认是平台导出的标签模板文件。'
  }
  return ''
}

function toHex(digest: Uint8Array) {
  return Array.from(digest, (byte) => byte.toString(16).padStart(2, '0')).join('')
}

/**
 * `sha256:{hex}`。优先用浏览器的 `crypto.subtle`；它只在 https / localhost 这类安全上下文里存在，
 * 现场用 http 加局域网地址打开控制台时退回纯 JS 实现（@noble/hashes），文件最大 64 KB，性能无碍。
 */
export async function templateAssetChecksum(
  bytes: Uint8Array,
  // 省略时取当前页面的 crypto.subtle；传 null 表示「不可用」（非安全上下文）。
  subtle: SubtleCrypto | null = globalThis.crypto?.subtle ?? null,
) {
  if (subtle) {
    const digest = await subtle.digest('SHA-256', bytes as Uint8Array<ArrayBuffer>)
    return `sha256:${toHex(new Uint8Array(digest))}`
  }
  return `sha256:${toHex(sha256(bytes))}`
}

function readOffset(response?: Response) {
  const raw = response?.headers.get('Upload-Offset')?.trim()
  return raw && /^\d+$/.test(raw) ? Number(raw) : undefined
}

/** 选好文件后一口气完成上传，返回写进模板的文件；任何一步失败都抛出中文原因。 */
export async function uploadTemplateAsset(
  file: File,
  scope: TemplateAssetUploadScope,
): Promise<UploadedTemplateAsset> {
  const fileProblem = templateAssetFileProblem(file)
  if (fileProblem) throw new TemplateAssetPrecheckError(fileProblem)
  const templateCode = scope.templateCode.trim()
  if (!templateCode) throw new TemplateAssetPrecheckError('请先填写模板编码，再上传模板文件。')

  const bytes = new Uint8Array(await file.arrayBuffer())
  const contentProblem = templateAssetContentProblem(bytes)
  if (contentProblem) throw new TemplateAssetPrecheckError(contentProblem)
  const checksum = await templateAssetChecksum(bytes)

  const { data: session } = await createBusinessConsoleBarcodeTemplateAssetUploadSession({
    body: {
      organizationId: scope.organizationId,
      environmentId: scope.environmentId,
      templateCode,
      fileName: file.name,
      expectedSizeBytes: bytes.length,
      checksum,
    },
    throwOnError: true,
  })
  const uploadSessionId = session.data?.uploadSessionId
  if (!session.success || !uploadSessionId) throw new Error('未能开始上传，请稍后重试。')

  const transferHeaders = {
    ...session.data?.uploadHeaders,
    'X-Organization-Id': scope.organizationId,
    'X-Environment-Id': scope.environmentId,
    'Tus-Resumable': TUS_RESUMABLE_VERSION,
  }
  const patched = await patchBusinessConsoleBarcodeTemplateAssetTusUpload({
    path: { uploadSessionId },
    headers: {
      ...transferHeaders,
      'Upload-Offset': '0',
      'Content-Type': TUS_PATCH_CONTENT_TYPE,
    },
    body: bytes as never,
    bodySerializer: null,
    throwOnError: true,
  })
  let confirmed = readOffset(patched.response)
  if (confirmed === undefined) {
    const head = await getBusinessConsoleBarcodeTemplateAssetTusOffset({
      path: { uploadSessionId },
      headers: transferHeaders,
      throwOnError: true,
    })
    confirmed = readOffset(head.response)
  }
  if (confirmed !== bytes.length) throw new Error('模板文件没有完整上传，请重新上传。')

  const { data: completed } = await completeBusinessConsoleBarcodeTemplateAssetUpload({
    path: { uploadSessionId },
    body: {
      organizationId: scope.organizationId,
      environmentId: scope.environmentId,
      checksum,
      sizeBytes: bytes.length,
    },
    throwOnError: true,
  })
  const asset = completed.data
  if (!completed.success || !asset?.fileId) throw new Error('模板文件上传未完成，请重新上传。')
  return {
    fileId: asset.fileId,
    fileName: asset.fileName ?? file.name,
    sizeBytes: asset.sizeBytes ?? bytes.length,
  }
}
