import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  create: vi.fn(),
  head: vi.fn(),
  patch: vi.fn(),
  complete: vi.fn(),
  read: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleSopFileUploadSession: api.create,
  getBusinessConsoleSopFileTusOffset: api.head,
  patchBusinessConsoleSopFileTusUpload: api.patch,
  completeBusinessConsoleSopFileUpload: api.complete,
  downloadBusinessConsoleSopFileContent: api.read,
}))
import {
  readSopFile,
  createSopUploadSession,
  transferSopFile,
  completeSopUploadSession,
} from './sopFileTransfer'

const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const file = new File(['SOP: torque 12 Nm.'], 'work-instruction.txt', { type: 'text/plain' })
async function uploadSopFile(file: File, scope: typeof importScope) {
  const session = await createSopUploadSession({
    ...scope,
    fileName: file.name,
    contentType: file.type,
    expectedSizeBytes: file.size,
    owner: { ownerService: '', ownerType: '', ownerId: '' },
    filePurpose: 'engineering-document',
    checksum: null,
  })
  await transferSopFile({ file, session, onProgress: vi.fn() }, scope)
  return completeSopUploadSession(session.uploadSessionId, {
    ...scope,
    sizeBytes: file.size,
    filePurpose: 'engineering-document',
    checksum: null,
  })
}
const importScope = scope
function offset(value: number) {
  return {
    response: new Response(null, { status: 204, headers: { 'Upload-Offset': String(value) } }),
  }
}
beforeEach(() => {
  vi.resetAllMocks()
  api.create.mockResolvedValue({
    data: {
      success: true,
      data: {
        uploadSessionId: 'ups-sop-1',
        fileId: 'file-pending',
        uploadHeaders: {},
        expiresAtUtc: '2099-01-01',
        uploadUrl: '/controlled',
      },
    },
  })
  api.head.mockResolvedValue(offset(0))
  api.patch.mockResolvedValue(offset(file.size))
  api.complete.mockResolvedValue({
    data: {
      success: true,
      data: { fileId: 'file-sop-1', fileName: file.name, contentType: file.type },
    },
  })
})

describe('SOP file transfer', () => {
  it.each(['create', 'head', 'patch', 'complete'] as const)(
    '%s 的 JSON 业务拒绝保留服务端原因',
    async (stage) => {
      api[stage].mockRejectedValueOnce({ message: '上传内容与声明的文件类型不匹配' })
      await expect(uploadSopFile(file, scope)).rejects.toThrow('上传内容与声明的文件类型不匹配')
    },
  )

  it('传输取消保留 AbortError 身份', async () => {
    const aborted = new DOMException('The operation was aborted.', 'AbortError')
    api.patch.mockRejectedValueOnce(aborted)
    await expect(uploadSopFile(file, scope)).rejects.toBe(aborted)
    expect(api.complete).not.toHaveBeenCalled()
  })

  it('sends the selected bytes through HEAD/PATCH and uses only the completion file reference', async () => {
    expect(await uploadSopFile(file, scope)).toEqual({
      fileId: 'file-sop-1',
      fileName: file.name,
      contentType: file.type,
    })
    expect(api.create).toHaveBeenCalledWith({
      body: { ...scope, fileName: file.name, contentType: file.type, expectedSizeBytes: file.size },
      throwOnError: true,
    })
    expect(api.head.mock.invocationCallOrder[0]).toBeLessThan(
      api.patch.mock.invocationCallOrder[0]!,
    )
    expect(api.patch.mock.invocationCallOrder[0]).toBeLessThan(
      api.complete.mock.invocationCallOrder[0]!,
    )
    const request = api.patch.mock.calls[0]![0]
    expect(request.path).toEqual({ uploadSessionId: 'ups-sop-1' })
    expect(request.body).toBe(file)
    expect(request.bodySerializer).toBeNull()
    expect(request.headers).toEqual({
      'X-Organization-Id': scope.organizationId,
      'X-Environment-Id': scope.environmentId,
      'Tus-Resumable': '1.0.0',
      'Upload-Offset': '0',
      'Content-Type': 'application/offset+octet-stream',
    })
    expect(api.complete).toHaveBeenCalledWith({
      path: { uploadSessionId: 'ups-sop-1' },
      body: { ...scope, sizeBytes: file.size },
      throwOnError: true,
    })
  })

  it('does not complete an incomplete upload or expose a failed completion reference', async () => {
    api.patch.mockResolvedValueOnce(offset(file.size - 1))
    await expect(uploadSopFile(file, scope)).rejects.toThrow('没有完整上传')
    expect(api.complete).not.toHaveBeenCalled()
    api.complete.mockRejectedValueOnce(new Error('上传内容与声明的文件类型不匹配'))
    await expect(uploadSopFile(file, scope)).rejects.toThrow('上传内容与声明的文件类型不匹配')
  })

  it('reads the authenticated content as bytes with the same organization/environment', async () => {
    const blob = new Blob(['SOP: torque 12 Nm.'])
    api.read.mockResolvedValue({ data: blob })
    expect(await readSopFile('file-sop-1', scope)).toBe(blob)
    expect(api.read).toHaveBeenCalledWith({
      path: { fileId: 'file-sop-1' },
      headers: {
        'X-Organization-Id': scope.organizationId,
        'X-Environment-Id': scope.environmentId,
      },
      parseAs: 'blob',
      throwOnError: true,
    })
  })
})
