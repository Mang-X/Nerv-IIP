<script setup lang="ts">
import {
  completeBusinessConsoleQualityEvidenceUpload,
  createBusinessConsoleQualityEvidenceUploadSession,
  getBusinessConsoleQualityEvidenceTusOffset,
  patchBusinessConsoleQualityEvidenceTusUpload,
} from '@nerv-iip/api-client'
import {
  FileUpload,
  type FileUploadCompletedFile,
  type FileUploadCompleteSessionRequest,
  type FileUploadCreateSessionRequest,
  type FileUploadExpose,
  type FileUploadRow,
  type FileUploadSession,
  type FileUploadTransport,
} from '@nerv-iip/ui'
import { onBeforeUnmount, shallowRef, useTemplateRef } from 'vue'
import { inlineErrorMessage, notifyOperationFailure, notifyWarning } from '@/utils/notify'

// The page keys this component by dialog opening, NCR and business scope.
const props = defineProps<{
  organizationId: string
  environmentId: string
  ncrId: string
  disabled?: boolean
}>()
const emit = defineEmits<{
  completed: [files: FileUploadCompletedFile[]]
  busy: [value: boolean]
}>()
const scope = {
  organizationId: props.organizationId,
  environmentId: props.environmentId,
  ncrId: props.ncrId,
}
const upload = useTemplateRef<FileUploadExpose>('upload')
const pendingRequests = shallowRef(0)
const signals = new Map<string, AbortSignal | undefined>()
let disposed = false
const acceptedContentTypes = [
  'application/pdf',
  'image/png',
  'image/jpeg',
  'text/plain',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
]

async function pending<T>(action: () => Promise<T>): Promise<T> {
  pendingRequests.value++
  emit('busy', true)
  try {
    return await action()
  } catch (error) {
    if (
      typeof error === 'object' &&
      error !== null &&
      'name' in error &&
      error.name === 'AbortError'
    ) {
      throw error
    }
    throw new Error(inlineErrorMessage(error, '上传失败，请重试。'), { cause: error })
  } finally {
    pendingRequests.value--
    if (!disposed) emit('busy', pendingRequests.value > 0)
  }
}

function createUploadSession(request: FileUploadCreateSessionRequest): Promise<FileUploadSession> {
  return pending(async () => {
    const { data: response } = await createBusinessConsoleQualityEvidenceUploadSession({
      body: {
        ...scope,
        fileName: request.fileName,
        contentType: request.contentType,
        expectedSizeBytes: request.expectedSizeBytes,
        checksum: request.checksum,
      },
      throwOnError: true,
    })
    const session = response.data
    if (!response.success || !session?.uploadSessionId)
      throw new Error('未能开始证据上传，请重试。')
    return {
      uploadSessionId: session.uploadSessionId,
      fileId: session.fileId!,
      expiresAtUtc: session.expiresAtUtc!,
      uploadMode: 'tus',
      provider: 'tus',
      upload: { url: session.uploadUrl!, headers: session.uploadHeaders ?? {} },
    }
  })
}

const transport: FileUploadTransport = ({ file, session, signal, onProgress }) =>
  pending(async () => {
    signals.set(session.uploadSessionId, signal)
    signal?.throwIfAborted()
    const path = { uploadSessionId: session.uploadSessionId }
    const headers = {
      ...session.upload.headers,
      'X-Organization-Id': scope.organizationId,
      'X-Environment-Id': scope.environmentId,
      'Tus-Resumable': '1.0.0',
    }
    const head = await getBusinessConsoleQualityEvidenceTusOffset({
      path,
      headers,
      signal,
      throwOnError: false,
    })
    // Final PATCH closes tus before its response arrives. Let complete verify this session;
    // FileUpload still requires its successful fileId before publishing any attachment.
    if (head.response?.status === 404) return
    if (head.error) throw head.error
    const raw = head.response.headers.get('Upload-Offset')
    const offset = raw !== null && /^\d+$/.test(raw) ? Number(raw) : NaN
    if (!Number.isSafeInteger(offset) || offset > file.size)
      throw new Error('无法确认上传进度，请重新选择证据。')
    if (offset < file.size) {
      await patchBusinessConsoleQualityEvidenceTusUpload({
        path,
        headers: { ...headers, 'Upload-Offset': String(offset) },
        body: file.slice(offset),
        signal,
        throwOnError: true,
      })
    }
    signal?.throwIfAborted()
    onProgress(95)
  })

function completeUploadSession(uploadSessionId: string, request: FileUploadCompleteSessionRequest) {
  return pending(async () => {
    const signal = signals.get(uploadSessionId)
    signal?.throwIfAborted()
    const { data: response } = await completeBusinessConsoleQualityEvidenceUpload({
      path: { uploadSessionId },
      body: { ...scope, checksum: request.checksum, sizeBytes: request.sizeBytes },
      signal,
      throwOnError: true,
    })
    signal?.throwIfAborted()
    if (!response.success || !response.data?.fileId) throw new Error('证据上传未完成，请重试。')
    return { fileId: response.data.fileId }
  })
}
function failed(row: FileUploadRow) {
  if (!disposed && !signals.get(row.uploadSession?.uploadSessionId ?? '')?.aborted)
    notifyOperationFailure(
      '证据上传失败',
      new Error(row.error ?? '上传失败'),
      '请重试或移除后重新选择。',
    )
}
onBeforeUnmount(() => {
  disposed = true
  upload.value?.clear()
})
</script>

<template>
  <div class="grid gap-2">
    <FileUpload
      ref="upload"
      purpose="quality-evidence"
      owner-service="business-quality"
      owner-type="nonconformance-report"
      :owner-id="scope.ncrId"
      :organization-id="scope.organizationId"
      :environment-id="scope.environmentId"
      :accepted-content-types="acceptedContentTypes"
      :disabled="disabled"
      :create-upload-session="createUploadSession"
      :complete-upload-session="completeUploadSession"
      :transport="transport"
      @completed="emit('completed', $event)"
      @failed="failed"
      @rejected="notifyWarning($event.map((file) => `${file.fileName}：${file.reason}`).join('；'))"
    />
    <p v-if="pendingRequests" class="text-xs text-muted-foreground" role="status">
      证据上传中，完成后可提交处置；移除文件可取消上传。
    </p>
  </div>
</template>
