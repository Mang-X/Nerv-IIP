import { describe, expect, it, vi } from 'vitest'
import {
  isShiftHandoverPhotoContentType,
  readUploadOffset,
  resolveTusRequestUrl,
  sendShiftHandoverAttachmentBytes,
  SHIFT_HANDOVER_PHOTO_MAX_BYTES,
  TUS_PATCH_CONTENT_TYPE,
  TUS_RESUMABLE_VERSION,
} from './shift-handover-upload'

interface FakeCall {
  url: string
  method: string
  headers: Record<string, string>
  body: unknown
}

function fakeResponse(status: number, headers: Record<string, string> = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(headers),
  } as unknown as Response
}

/** 依次回放脚本化响应，并记录每一跳的方法/头/体，供断言。 */
function scriptedFetch(responses: Array<Response | (() => Response)>) {
  const calls: FakeCall[] = []
  let index = 0
  const doFetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const headerEntries = Object.entries((init?.headers ?? {}) as Record<string, string>)
    calls.push({
      url: String(input),
      method: init?.method ?? 'GET',
      headers: Object.fromEntries(headerEntries.map(([k, v]) => [k.toLowerCase(), v])),
      body: init?.body,
    })
    const next = responses[index++]
    if (!next) throw new Error(`unexpected extra fetch call #${index}`)
    return typeof next === 'function' ? next() : next
  }) as unknown as typeof fetch
  return { doFetch, calls }
}

const scope = { organizationId: 'org-001', environmentId: 'env-dev', accessToken: 'tok-1' }
const target = {
  uploadUrl: '/api/business-console/v1/files/shift-handover-attachments/tus/ups-1',
  uploadHeaders: { 'x-nerv-upload-mode': 'tus' },
}

describe('resolveTusRequestUrl', () => {
  it('keeps the gateway-relative path as-is when no absolute base URL is configured (web/dev proxy)', () => {
    expect(resolveTusRequestUrl('/api/business-console/v1/files/x/tus/s1')).toBe(
      '/api/business-console/v1/files/x/tus/s1',
    )
  })

  it('joins the APK absolute gateway base URL without doubling the slash', () => {
    expect(resolveTusRequestUrl('/api/x', 'https://gw.example.com/')).toBe(
      'https://gw.example.com/api/x',
    )
    expect(resolveTusRequestUrl('/api/x', 'https://gw.example.com')).toBe(
      'https://gw.example.com/api/x',
    )
  })

  it('refuses an absolute or protocol-relative upload URL', () => {
    // 网关只会回内部相对路径；出现绝对 URL 只可能是响应被改写，跟着走就是把 Bearer 发给第三方。
    expect(() => resolveTusRequestUrl('https://evil.example.com/tus/s1')).toThrow('网关内部路径')
    expect(() => resolveTusRequestUrl('//evil.example.com/tus/s1')).toThrow('网关内部路径')
    expect(() => resolveTusRequestUrl('api/files/v1/tus/s1')).toThrow('网关内部路径')
  })
})

describe('readUploadOffset', () => {
  it('parses a non-negative integer Upload-Offset', () => {
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': '70' }))).toBe(70)
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': '0' }))).toBe(0)
  })

  it('returns undefined (NOT 0) when the header is absent or unusable', () => {
    // 跨源部署没 expose 这个头时读不到；当 0 用会把「没复查」谎报成「从头写起/复查通过」。
    expect(readUploadOffset(fakeResponse(204))).toBeUndefined()
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': '' }))).toBeUndefined()
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': '-1' }))).toBeUndefined()
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': '7.5' }))).toBeUndefined()
    expect(readUploadOffset(fakeResponse(204, { 'Upload-Offset': 'abc' }))).toBeUndefined()
  })
})

describe('isShiftHandoverPhotoContentType', () => {
  it('accepts only the two purpose-allowed image types, parameters stripped', () => {
    expect(isShiftHandoverPhotoContentType('image/jpeg')).toBe(true)
    expect(isShiftHandoverPhotoContentType('IMAGE/PNG')).toBe(true)
    expect(isShiftHandoverPhotoContentType('image/jpeg; charset=binary')).toBe(true)
  })

  it('rejects everything else the purpose policy would reject server-side', () => {
    expect(isShiftHandoverPhotoContentType('image/heic')).toBe(false)
    expect(isShiftHandoverPhotoContentType('application/pdf')).toBe(false)
    expect(isShiftHandoverPhotoContentType('')).toBe(false)
    expect(isShiftHandoverPhotoContentType(undefined)).toBe(false)
  })

  it('pins the purpose size ceiling to the FileStorage config value', () => {
    expect(SHIFT_HANDOVER_PHOTO_MAX_BYTES).toBe(20_971_520)
  })
})

describe('sendShiftHandoverAttachmentBytes', () => {
  it('runs HEAD → PATCH → offset re-check and sends every header FileStorage/the gateway demand', async () => {
    const bytes = new Blob([new Uint8Array(70)])
    const { doFetch, calls } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '0' }),
      fakeResponse(204, { 'Upload-Offset': '70' }),
    ])

    const result = await sendShiftHandoverAttachmentBytes(target, bytes, scope, { fetch: doFetch })

    expect(result).toEqual({ sentBytes: 70, confirmedOffset: 70 })
    expect(calls).toHaveLength(2)

    expect(calls[0].method).toBe('HEAD')
    expect(calls[0].url).toBe(target.uploadUrl)
    expect(calls[0].headers['tus-resumable']).toBe(TUS_RESUMABLE_VERSION)
    expect(calls[0].headers['x-organization-id']).toBe('org-001')
    expect(calls[0].headers['x-environment-id']).toBe('env-dev')
    expect(calls[0].headers.authorization).toBe('Bearer tok-1')

    expect(calls[1].method).toBe('PATCH')
    // 这三个头缺任何一个，FileStorage 分别回 412 / 415 / 400（PatchTusUploadEndpoint）。
    expect(calls[1].headers['tus-resumable']).toBe(TUS_RESUMABLE_VERSION)
    expect(calls[1].headers['content-type']).toBe(TUS_PATCH_CONTENT_TYPE)
    expect(calls[1].headers['upload-offset']).toBe('0')
    // 网关字节面缺 org/env 直接 400（BusinessConsoleFileTransfer.ProxyAsync）。
    expect(calls[1].headers['x-organization-id']).toBe('org-001')
    expect(calls[1].headers['x-environment-id']).toBe('env-dev')
    expect(calls[1].headers.authorization).toBe('Bearer tok-1')
    expect((calls[1].body as Blob).size).toBe(70)
  })

  it('forwards the session-supplied upload headers alongside the tus ones', async () => {
    const { doFetch, calls } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '0' }),
      fakeResponse(204, { 'Upload-Offset': '4' }),
    ])
    await sendShiftHandoverAttachmentBytes(target, new Blob([new Uint8Array(4)]), scope, {
      fetch: doFetch,
    })
    expect(calls[0].headers['x-nerv-upload-mode']).toBe('tus')
    expect(calls[1].headers['x-nerv-upload-mode']).toBe('tus')
  })

  it('resumes from the server offset instead of re-sending bytes already stored', async () => {
    const bytes = new Blob([new Uint8Array(100)])
    const { doFetch, calls } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '40' }),
      fakeResponse(204, { 'Upload-Offset': '100' }),
    ])

    const result = await sendShiftHandoverAttachmentBytes(target, bytes, scope, { fetch: doFetch })

    expect(calls[1].headers['upload-offset']).toBe('40')
    expect((calls[1].body as Blob).size).toBe(60)
    expect(result).toEqual({ sentBytes: 60, confirmedOffset: 100 })
  })

  it('re-checks with a second HEAD when PATCH did not expose Upload-Offset', async () => {
    const { doFetch, calls } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '0' }),
      fakeResponse(204),
      fakeResponse(204, { 'Upload-Offset': '8' }),
    ])

    const result = await sendShiftHandoverAttachmentBytes(
      target,
      new Blob([new Uint8Array(8)]),
      scope,
      { fetch: doFetch },
    )

    expect(calls.map((c) => c.method)).toEqual(['HEAD', 'PATCH', 'HEAD'])
    expect(result.confirmedOffset).toBe(8)
  })

  it('reports confirmedOffset=undefined rather than claiming a verified upload when no offset is readable', async () => {
    // 跨源未 expose Upload-Offset 时的诚实读数：复查没做成，不是复查通过。
    // 权威判定交给 complete（FileStorage TusUploadCompletionValidator 比对 ExpectedSizeBytes）。
    const { doFetch } = scriptedFetch([fakeResponse(204), fakeResponse(204), fakeResponse(204)])

    const result = await sendShiftHandoverAttachmentBytes(
      target,
      new Blob([new Uint8Array(8)]),
      scope,
      { fetch: doFetch },
    )

    expect(result.confirmedOffset).toBeUndefined()
    expect(result.sentBytes).toBe(8)
  })

  it('fails when the confirmed offset is short of the photo size', async () => {
    const { doFetch } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '0' }),
      fakeResponse(204, { 'Upload-Offset': '5' }),
    ])

    await expect(
      sendShiftHandoverAttachmentBytes(target, new Blob([new Uint8Array(9)]), scope, {
        fetch: doFetch,
      }),
    ).rejects.toThrow('照片未完整写入（已写入 5 / 共 9 字节）')
  })

  it('fails when the server already stored more bytes than this photo has', async () => {
    const { doFetch } = scriptedFetch([fakeResponse(204, { 'Upload-Offset': '900' })])

    await expect(
      sendShiftHandoverAttachmentBytes(target, new Blob([new Uint8Array(9)]), scope, {
        fetch: doFetch,
      }),
    ).rejects.toThrow('超过本张照片大小')
  })

  it.each([
    [401, '登录已失效，请重新登录后再传照片。'],
    [403, '当前账号没有交接班附件上传权限。'],
    [404, '上传会话已失效或已过期，请重新拍照。'],
    [409, '上传进度与服务端不一致，请重新拍照上传。'],
    [413, '照片超出交接班附件大小上限，请重拍或压缩后再传。'],
    [415, '照片格式不被接受，交接班附件只支持 JPG / PNG。'],
    [500, '上传照片失败（HTTP 500），请重试。'],
  ])('turns a PATCH %i into actionable Chinese copy', async (status, copy) => {
    const { doFetch } = scriptedFetch([
      fakeResponse(204, { 'Upload-Offset': '0' }),
      fakeResponse(status),
    ])

    await expect(
      sendShiftHandoverAttachmentBytes(target, new Blob([new Uint8Array(2)]), scope, {
        fetch: doFetch,
      }),
    ).rejects.toThrow(copy)
  })

  it('surfaces a failed HEAD without ever dispatching the PATCH', async () => {
    const { doFetch, calls } = scriptedFetch([fakeResponse(404)])

    await expect(
      sendShiftHandoverAttachmentBytes(target, new Blob([new Uint8Array(2)]), scope, {
        fetch: doFetch,
      }),
    ).rejects.toThrow('上传会话已失效或已过期，请重新拍照。')
    expect(calls.map((c) => c.method)).toEqual(['HEAD'])
  })

  it('refuses to dispatch anything when the session has no upload URL or the scope is empty', async () => {
    const { doFetch, calls } = scriptedFetch([])

    await expect(
      sendShiftHandoverAttachmentBytes({ uploadUrl: '  ' }, new Blob(['x']), scope, {
        fetch: doFetch,
      }),
    ).rejects.toThrow('文件服务未返回可用的附件上传地址。')

    await expect(
      sendShiftHandoverAttachmentBytes(
        target,
        new Blob(['x']),
        { organizationId: '', environmentId: 'env-dev' },
        { fetch: doFetch },
      ),
    ).rejects.toThrow('缺少组织或环境范围')

    expect(calls).toHaveLength(0)
  })
})
