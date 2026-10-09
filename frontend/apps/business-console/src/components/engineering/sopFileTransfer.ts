import {
  completeBusinessConsoleSopFileUpload,
  createBusinessConsoleSopFileUploadSession,
  downloadBusinessConsoleSopFileContent,
  getBusinessConsoleSopFileTusOffset,
  patchBusinessConsoleSopFileTusUpload,
} from '@nerv-iip/api-client'

interface Scope {
  organizationId: string
  environmentId: string
}

export async function uploadSopFile(file: File, scope: Scope) {
  if (!file.size) throw new Error('文件是空的，请重新选择。')
  const contentType = file.type || 'application/octet-stream'
  const { data: session } = await createBusinessConsoleSopFileUploadSession({
    body: { ...scope, fileName: file.name, contentType, expectedSizeBytes: file.size },
    throwOnError: true,
  })
  const uploadSessionId = session.data?.uploadSessionId
  if (!session.success || !uploadSessionId) throw new Error('未能开始上传，请重新选择文件。')
  const headers = {
    ...session.data?.uploadHeaders,
    'X-Organization-Id': scope.organizationId,
    'X-Environment-Id': scope.environmentId,
    'Tus-Resumable': '1.0.0',
  }
  const head = await getBusinessConsoleSopFileTusOffset({
    path: { uploadSessionId },
    headers,
    parseAs: 'text',
    throwOnError: true,
  })
  const offset = Number(head.response.headers.get('Upload-Offset'))
  if (head.response.headers.get('Upload-Offset') === null || offset !== 0) {
    throw new Error('上传起点无法确认，请重新选择文件。')
  }
  const patch = await patchBusinessConsoleSopFileTusUpload({
    path: { uploadSessionId },
    headers: {
      ...headers,
      'Upload-Offset': '0',
      'Content-Type': 'application/offset+octet-stream',
    },
    body: file as never,
    bodySerializer: null,
    throwOnError: true,
  })
  if (Number(patch.response.headers.get('Upload-Offset')) !== file.size) {
    throw new Error('文件没有完整上传，请重新选择文件。')
  }
  const { data: completed } = await completeBusinessConsoleSopFileUpload({
    path: { uploadSessionId },
    body: { ...scope, sizeBytes: file.size },
    throwOnError: true,
  })
  const uploaded = completed.data
  if (!completed.success || !uploaded?.fileId || !uploaded.fileName || !uploaded.contentType) {
    throw new Error('文件上传未完成，请重新选择文件。')
  }
  return { fileId: uploaded.fileId, fileName: uploaded.fileName, contentType: uploaded.contentType }
}

export async function readSopFile(fileId: string, scope: Scope) {
  const { data } = await downloadBusinessConsoleSopFileContent({
    path: { fileId },
    headers: { 'X-Organization-Id': scope.organizationId, 'X-Environment-Id': scope.environmentId },
    parseAs: 'blob',
    throwOnError: true,
  })
  return data!
}
