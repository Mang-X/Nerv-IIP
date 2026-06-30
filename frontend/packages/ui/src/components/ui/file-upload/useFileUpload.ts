import type {
  FileUploadCompletedFile,
  FileUploadCompleteSessionRequest,
  FileUploadCreateSessionRequest,
  FileUploadRejectedFile,
  FileUploadRow,
  FileUploadSession,
  FileUploadTransport,
} from './types'
import type { MaybeRefOrGetter } from 'vue'
import { computed, reactive, toValue } from 'vue'
import { formatFileSize as formatSharedFileSize } from '../../../lib/file'
import { getFileKind } from './fileKind'

interface UseFileUploadOptions {
  purpose: MaybeRefOrGetter<string>
  ownerService: MaybeRefOrGetter<string>
  ownerType: MaybeRefOrGetter<string>
  ownerId: MaybeRefOrGetter<string>
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  acceptedContentTypes: MaybeRefOrGetter<string[]>
  maxFileSizeBytes?: MaybeRefOrGetter<number | undefined>
  maxFiles: MaybeRefOrGetter<number>
  autoUpload: MaybeRefOrGetter<boolean>
  createUploadSession: (request: FileUploadCreateSessionRequest) => Promise<FileUploadSession>
  completeUploadSession: (
    uploadSessionId: string,
    request: FileUploadCompleteSessionRequest,
  ) => Promise<{ fileId: string }>
  transport: FileUploadTransport
  onCompleted: (files: FileUploadCompletedFile[]) => void
  onRejected: (files: FileUploadRejectedFile[]) => void
  onFailed: (row: FileUploadRow) => void
}

export function useFileUpload(options: UseFileUploadOptions) {
  const rows = reactive<FileUploadRow[]>([])
  const controllers = new Map<string, AbortController>()

  const completedFiles = computed(() =>
    rows
      .filter((row) => row.status === 'completed' && row.fileId !== null)
      .map((row) => ({ fileId: row.fileId!, fileName: row.fileName })),
  )
  const hasRows = computed(() => rows.length > 0)
  const hasQueuedRows = computed(() => rows.some((row) => row.status === 'queued'))
  const isUploading = computed(() => rows.some((row) => row.status === 'uploading'))
  const occupiedSlotCount = computed(() => rows.filter(isSlotOccupyingRow).length)

  function appendRow(row: FileUploadRow) {
    rows.push(row)
    return rows[rows.length - 1]!
  }

  async function addFiles(files: File[]) {
    let acceptedSlotCount = occupiedSlotCount.value
    const acceptedRows: FileUploadRow[] = []
    const rejectedFiles: FileUploadRejectedFile[] = []

    for (const file of files) {
      const rejection = validateFile(file, toValue(options.acceptedContentTypes), toValue(options.maxFileSizeBytes))

      if (rejection) {
        rejectedFiles.push({ fileName: file.name, reason: rejection })
        rows.push(createRow(file, 'rejected', rejection))
        continue
      }

      if (acceptedSlotCount >= toValue(options.maxFiles)) {
        rejectedFiles.push({ fileName: file.name, reason: '已达到最大文件数量限制。' })
        continue
      }

      const row = appendRow(createRow(file, 'queued'))
      acceptedRows.push(row)
      acceptedSlotCount += 1
    }

    if (rejectedFiles.length > 0) {
      options.onRejected(rejectedFiles)
    }

    if (toValue(options.autoUpload)) {
      await uploadRows(acceptedRows)
    }
  }

  async function uploadQueued() {
    await uploadRows(rows.filter((row) => row.status === 'queued'))
  }

  async function uploadRows(uploadRows: FileUploadRow[]) {
    await Promise.all(uploadRows.map(uploadRow))
  }

  async function uploadRow(row: FileUploadRow) {
    row.status = 'uploading'
    row.progress = 1
    row.error = null
    const controller = new AbortController()
    controllers.set(row.id, controller)

    try {
      const session = row.uploadSession ?? await options.createUploadSession({
        organizationId: toValue(options.organizationId),
        environmentId: toValue(options.environmentId),
        owner: {
          ownerService: toValue(options.ownerService),
          ownerType: toValue(options.ownerType),
          ownerId: toValue(options.ownerId),
        },
        filePurpose: toValue(options.purpose),
        fileName: row.fileName,
        contentType: row.contentType,
        expectedSizeBytes: row.sizeBytes,
        checksum: null,
      })
      row.uploadSession = session

      await options.transport({
        file: row.file,
        session,
        onProgress: (progress) => {
          row.progress = clampProgress(progress)
        },
        signal: controller.signal,
      })

      const completed = await options.completeUploadSession(session.uploadSessionId, {
        organizationId: toValue(options.organizationId),
        environmentId: toValue(options.environmentId),
        filePurpose: toValue(options.purpose),
        checksum: null,
        sizeBytes: row.sizeBytes,
      })

      row.fileId = completed.fileId || session.fileId
      row.status = 'completed'
      row.progress = 100
      options.onCompleted(completedFiles.value)
    }
    catch (error) {
      if (isAbortError(error) && isPausedRow(row)) {
        row.error = null
        return
      }

      row.status = 'failed'
      row.error = error instanceof Error ? error.message : '上传失败。'
      options.onFailed(row)
    }
    finally {
      if (controllers.get(row.id) === controller) {
        controllers.delete(row.id)
      }
    }
  }

  function removeRow(id: string) {
    controllers.get(id)?.abort()
    controllers.delete(id)
    const index = rows.findIndex((row) => row.id === id)

    if (index >= 0) {
      const removed = rows[index]!
      rows.splice(index, 1)

      if (removed.status === 'completed') {
        options.onCompleted(completedFiles.value)
      }
    }
  }

  function pauseRow(id: string) {
    const row = rows.find((item) => item.id === id)

    if (!row || row.status !== 'uploading') {
      return
    }

    row.status = 'paused'
    row.error = null
    controllers.get(id)?.abort()
  }

  async function resumeRow(id: string) {
    const row = rows.find((item) => item.id === id)

    if (!row || row.status !== 'paused') {
      return
    }

    await uploadRow(row)
  }

  async function retryRow(id: string) {
    const row = rows.find((item) => item.id === id)

    if (!row || row.status !== 'failed') {
      return
    }

    row.error = null
    if (row.uploadSession && isUploadSessionExpired(row.uploadSession)) {
      row.uploadSession = null
    }
    await uploadRow(row)
  }

  function pauseAll() {
    for (const row of rows) {
      if (row.status === 'uploading') {
        pauseRow(row.id)
      }
    }
  }

  async function resumeAll() {
    await uploadRows(rows.filter((row) => row.status === 'paused'))
  }

  async function retryFailed() {
    await uploadRows(rows.filter((row) => row.status === 'failed'))
  }

  function clear() {
    for (const controller of controllers.values()) {
      controller.abort()
    }

    controllers.clear()
    rows.splice(0)
  }

  return {
    rows,
    completedFiles,
    hasRows,
    hasQueuedRows,
    isUploading,
    addFiles,
    uploadQueued,
    removeRow,
    pauseRow,
    resumeRow,
    retryRow,
    pauseAll,
    resumeAll,
    retryFailed,
    clear,
  }
}

function createRow(
  file: File,
  status: FileUploadRow['status'],
  error: string | null = null,
): FileUploadRow {
  return {
    id: `${file.name}:${file.size}:${file.lastModified}:${globalThis.crypto.randomUUID()}`,
    file,
    fileName: file.name,
    sizeBytes: file.size,
    contentType: file.type || 'application/octet-stream',
    status,
    progress: 0,
    fileId: null,
    error,
    uploadSession: null,
  }
}

function validateFile(
  file: File,
  acceptedContentTypes: string[],
  maxFileSizeBytes?: number,
) {
  if (maxFileSizeBytes && file.size > maxFileSizeBytes) {
    return '文件超过最大大小限制。'
  }

  if (acceptedContentTypes.length > 0 && !isAcceptedType(file, acceptedContentTypes)) {
    return '文件类型不符合要求。'
  }

  return null
}

function isAcceptedType(file: File, acceptedContentTypes: string[]) {
  return acceptedContentTypes.some((acceptedType) => {
    const normalizedAcceptedType = acceptedType.trim().toLowerCase()

    if (!normalizedAcceptedType) {
      return false
    }

    if (normalizedAcceptedType.startsWith('.')) {
      return file.name.toLowerCase().endsWith(normalizedAcceptedType)
    }

    if (normalizedAcceptedType.endsWith('/*')) {
      return file.type.toLowerCase().startsWith(normalizedAcceptedType.slice(0, -1))
    }

    return file.type.toLowerCase() === normalizedAcceptedType
  })
}

function clampProgress(progress: number) {
  if (Number.isNaN(progress)) {
    return 0
  }

  return Math.min(Math.max(Math.round(progress), 0), 100)
}

export function isSlotOccupyingRow(row: FileUploadRow) {
  return row.status !== 'rejected' && row.status !== 'failed'
}

export function rowKind(row: FileUploadRow) {
  return getFileKind(row.fileName, row.contentType)
}

export function formatFileSize(bytes: number) {
  return formatSharedFileSize(bytes)
}

function isAbortError(error: unknown) {
  return typeof error === 'object'
    && error !== null
    && 'name' in error
    && error.name === 'AbortError'
}

function isPausedRow(row: FileUploadRow) {
  return row.status === 'paused'
}

function isUploadSessionExpired(session: FileUploadSession) {
  const expiresAt = Date.parse(session.expiresAtUtc)

  if (Number.isNaN(expiresAt)) {
    return false
  }

  return expiresAt <= Date.now()
}
