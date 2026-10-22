import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  createSession: vi.fn(),
  patch: vi.fn(),
  head: vi.fn(),
  complete: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleBarcodeTemplateAssetUploadSession: api.createSession,
  patchBusinessConsoleBarcodeTemplateAssetTusUpload: api.patch,
  getBusinessConsoleBarcodeTemplateAssetTusOffset: api.head,
  completeBusinessConsoleBarcodeTemplateAssetUpload: api.complete,
}))

import {
  templateAssetChecksum,
  templateAssetContentProblem,
  templateAssetFileProblem,
  uploadTemplateAsset,
} from './templateAssetUpload'

const TEMPLATE_JSON =
  '{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"barcode","x":40,"y":40,"moduleWidth":2,"height":100,"variable":"label.value"}]}'
const encoder = new TextEncoder()
const SCOPE = { organizationId: 'org-001', environmentId: 'env-dev', templateCode: 'BOX_LABEL' }

function jsonFile(content = TEMPLATE_JSON, name = 'box-label.json') {
  return new File([content], name, { type: 'application/json' })
}

function offsetResponse(offset?: number) {
  const headers = new Headers()
  if (offset !== undefined) headers.set('Upload-Offset', String(offset))
  return { data: undefined, response: new Response(null, { status: 204, headers }) }
}

async function expectedChecksum(content: string) {
  const digest = await crypto.subtle.digest('SHA-256', encoder.encode(content))
  return `sha256:${Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('')}`
}

describe('templateAssetChecksum', () => {
  // 「abc」的 SHA-256 是标准测试向量（FIPS 180-2）。两条路径必须给出同一个值。
  const ABC = 'sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'

  it('uses crypto.subtle when the page runs in a secure context', async () => {
    const subtle = {
      digest: vi.fn((alg: string, data: BufferSource) => crypto.subtle.digest(alg, data)),
    }
    const checksum = await templateAssetChecksum(
      encoder.encode('abc'),
      subtle as unknown as SubtleCrypto,
    )

    expect(subtle.digest).toHaveBeenCalledTimes(1)
    expect(subtle.digest.mock.calls[0]![0]).toBe('SHA-256')
    expect(checksum).toBe(ABC)
  })

  it('falls back to the pure JS digest when crypto.subtle is unavailable', async () => {
    // 传 null 才是「不可用」：传 undefined 会落到参数默认值、又走回 crypto.subtle，测不到回退路径。
    expect(await templateAssetChecksum(encoder.encode('abc'), null)).toBe(ABC)
  })
})

describe('template asset checks', () => {
  it.each([
    [{ name: 'box.txt', size: 10 }, '模板文件只支持 .json 格式。'],
    [{ name: 'box.json', size: 0 }, '模板文件是空的，请重新选择。'],
    [{ name: 'box.json', size: 65_537 }, '模板文件不能超过 64 KB。'],
    [{ name: 'BOX.JSON', size: 65_536 }, ''],
  ])('checks file %o', (file, problem) => {
    expect(templateAssetFileProblem(file)).toBe(problem)
  })

  it('rejects a UTF-8 BOM, unreadable JSON and a foreign format', () => {
    expect(templateAssetContentProblem(new Uint8Array([0xef, 0xbb, 0xbf, 0x7b, 0x7d]))).toContain(
      'BOM',
    )
    expect(templateAssetContentProblem(encoder.encode('{not json'))).toContain('无法识别')
    expect(templateAssetContentProblem(encoder.encode('{"format":"bartender"}'))).toContain(
      '不是平台的标签模板文件',
    )
    expect(templateAssetContentProblem(encoder.encode(TEMPLATE_JSON))).toBe('')
  })
})

describe('uploadTemplateAsset', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.createSession.mockResolvedValue({
      data: {
        success: true,
        data: {
          uploadSessionId: 'ups-template-1',
          fileId: 'file-template-1',
          uploadProtocol: 'tus',
          uploadUrl: '/api/business-console/v1/files/barcode-template-assets/tus/ups-template-1',
          uploadHeaders: { 'x-nerv-upload-mode': 'tus' },
        },
      },
    })
    api.patch.mockResolvedValue(offsetResponse(new Blob([TEMPLATE_JSON]).size))
    api.head.mockResolvedValue(offsetResponse(0))
    api.complete.mockResolvedValue({
      data: {
        success: true,
        data: { fileId: 'file-template-1', fileName: 'box-label.json', sizeBytes: 363 },
      },
    })
  })

  it('opens a session, sends the bytes over tus and completes with the same checksum', async () => {
    const size = encoder.encode(TEMPLATE_JSON).length
    const checksum = await expectedChecksum(TEMPLATE_JSON)

    const asset = await uploadTemplateAsset(jsonFile(), { ...SCOPE, templateCode: ' BOX_LABEL ' })

    expect(api.createSession).toHaveBeenCalledWith({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        templateCode: 'BOX_LABEL',
        fileName: 'box-label.json',
        expectedSizeBytes: size,
        checksum,
      },
      throwOnError: true,
    })
    const patch = api.patch.mock.calls[0]![0]
    expect(patch.path).toEqual({ uploadSessionId: 'ups-template-1' })
    expect(patch.bodySerializer).toBeNull()
    expect(new TextDecoder().decode(patch.body)).toBe(TEMPLATE_JSON)
    expect(patch.headers).toEqual({
      'x-nerv-upload-mode': 'tus',
      'X-Organization-Id': 'org-001',
      'X-Environment-Id': 'env-dev',
      'Tus-Resumable': '1.0.0',
      'Upload-Offset': '0',
      'Content-Type': 'application/offset+octet-stream',
    })
    expect(api.complete).toHaveBeenCalledWith({
      path: { uploadSessionId: 'ups-template-1' },
      body: { organizationId: 'org-001', environmentId: 'env-dev', checksum, sizeBytes: size },
      throwOnError: true,
    })
    expect(api.head).not.toHaveBeenCalled()
    expect(asset).toEqual({ fileId: 'file-template-1', fileName: 'box-label.json', sizeBytes: 363 })
  })

  it('rechecks the offset when the patch response does not carry it and refuses a partial upload', async () => {
    api.patch.mockResolvedValue(offsetResponse(undefined))
    api.head.mockResolvedValue(offsetResponse(10))

    await expect(uploadTemplateAsset(jsonFile(), SCOPE)).rejects.toThrow('模板文件没有完整上传')
    expect(api.head).toHaveBeenCalledTimes(1)
    expect(api.complete).not.toHaveBeenCalled()
  })

  it('stops before contacting the gateway when the file is not a platform label template', async () => {
    await expect(uploadTemplateAsset(jsonFile('{"format":"zpl"}'), SCOPE)).rejects.toThrow(
      '不是平台的标签模板文件',
    )
    await expect(uploadTemplateAsset(jsonFile(TEMPLATE_JSON, 'box.zpl'), SCOPE)).rejects.toThrow(
      '只支持 .json',
    )
    await expect(uploadTemplateAsset(jsonFile(), { ...SCOPE, templateCode: ' ' })).rejects.toThrow(
      '请先填写模板编码',
    )
    expect(api.createSession).not.toHaveBeenCalled()
  })
})
