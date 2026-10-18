import { expect, it } from 'vitest'
import {
  createBusinessConsoleQualityEvidenceUploadSession,
  getBusinessConsoleQualityEvidenceTusOffset,
  patchBusinessConsoleQualityEvidenceTusUpload,
  completeBusinessConsoleQualityEvidenceUpload,
  type BusinessConsoleCreateQualityEvidenceUploadSessionRequest,
} from './index'

it('uploads NCR evidence with raw tus bytes and scoped completion through the stable client', async () => {
  // #4491 公开合同：adapter 消费同一 NCR 的会话/字节/完成链，不对二进制做 JSON 编码。
  const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
  const ncrId = 'b871553a-d3e0-45d8-960b-df0e9c2eac88'
  const sessions = '/api/business-console/v1/files/quality-evidence/upload-sessions'
  const tus = '/api/business-console/v1/files/quality-evidence/tus/session-1'
  const bytes = new Blob(['evidence'])
  const body: BusinessConsoleCreateQualityEvidenceUploadSessionRequest = {
    ...scope,
    ncrId,
    fileName: 'evidence.png',
    contentType: 'image/png',
    expectedSizeBytes: 8,
  }
  const requests: string[] = []
  const fetch: typeof globalThis.fetch = async (input) => {
    const request = input as Request
    const url = new URL(request.url).pathname
    requests.push(`${request.method} ${url}`)
    if (url === sessions) {
      expect(await request.json()).toEqual(body)
      return Response.json({
        data: {
          uploadSessionId: 'session-1',
          fileId: 'file-1',
          uploadProtocol: 'tus',
          uploadUrl: tus,
        },
      })
    }
    if (url === tus) {
      expect(request.headers.get('X-Organization-Id')).toBe(scope.organizationId)
      expect(request.headers.get('X-Environment-Id')).toBe(scope.environmentId)
      expect(request.headers.get('Tus-Resumable')).toBe('1.0.0')
      if (request.method === 'HEAD')
        return new Response(null, { headers: { 'Upload-Offset': '0' } })
      expect(request.headers.get('Content-Type')).toBe('application/offset+octet-stream')
      expect(request.headers.get('Upload-Offset')).toBe('0')
      expect(await request.text()).toBe('evidence')
      return new Response(null, { status: 204 })
    }
    expect(url).toBe(`${sessions}/session-1/complete`)
    expect(await request.json()).toEqual({ ...scope, ncrId, sizeBytes: 8 })
    return Response.json({
      data: { fileId: 'file-1', fileName: 'evidence.png', contentType: 'image/png', sizeBytes: 8 },
    })
  }
  const options = { baseUrl: 'https://gateway.example.test', fetch }
  const session = await createBusinessConsoleQualityEvidenceUploadSession({ ...options, body })
  expect(session.data?.data?.uploadUrl).toBe(tus)
  const path = { uploadSessionId: 'session-1' }
  const headers = {
    'X-Organization-Id': scope.organizationId,
    'X-Environment-Id': scope.environmentId,
    'Tus-Resumable': '1.0.0',
  }
  const offset = await getBusinessConsoleQualityEvidenceTusOffset({ ...options, path, headers })
  expect(offset.response?.headers.get('Upload-Offset')).toBe('0')
  await patchBusinessConsoleQualityEvidenceTusUpload({
    ...options,
    path,
    body: bytes,
    headers: { ...headers, 'Upload-Offset': '0' },
  })
  const completed = await completeBusinessConsoleQualityEvidenceUpload({
    ...options,
    path,
    body: { ...scope, ncrId, sizeBytes: 8 },
  })
  expect(completed.data?.data?.fileId).toBe('file-1')
  expect(requests).toEqual([
    `POST ${sessions}`,
    `HEAD ${tus}`,
    `PATCH ${tus}`,
    `POST ${sessions}/session-1/complete`,
  ])
})
