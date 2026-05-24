import type {
  FileUploadCompletedFile,
  FileUploadCompleteSessionRequest,
  FileUploadCreateSessionRequest,
  FileUploadRejectedFile,
  FileUploadRow,
  FileUploadSession,
  FileUploadTransport,
} from './types'
import { computed, reactive } from 'vue'
import { getFileKind } from './fileKind'

interface UseFileUploadOptions {
  purpose: string
  ownerService: string
  ownerType: string
  ownerId: string
  organizationId: string
  environmentId: string
  acceptedContentTypes: string[]
  maxFileSizeBytes?: number
  maxFiles: number
  autoUpload: boolean
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
  const isUploading = computed(() => rows.some((row) => row.status === 'uploading'))
  const occupiedSlotCount = computed(() => rows.filter(isSlotOccupyingRow).length)

  function appendRow(row: FileUploadRow) {
    rows.push(row)
    return rows[rows.length - 1]!
  }

  async function addFiles(files: File[]) {
    const availableSlots = Math.max(options.maxFiles - occupiedSlotCount.value, 0)
    const acceptedRows: FileUploadRow[] = []
    const rejectedFiles: FileUploadRejectedFile[] = []

    for (const file of files.slice(0, availableSlots)) {
      const rejection = validateFile(file, options.acceptedContentTypes, options.maxFileSizeBytes)

      if (rejection) {
        rejectedFiles.push({ fileName: file.name, reason: rejection })
        rows.push(createRow(file, 'rejected', rejection))
        continue
      }

      const row = appendRow(createRow(file, 'queued'))
      acceptedRows.push(row)
    }

    for (const file of files.slice(availableSlots)) {
      rejectedFiles.push({ fileName: file.name, reason: 'Maximum file count reached.' })
    }

    if (rejectedFiles.length > 0) {
      options.onRejected(rejectedFiles)
    }

    if (options.autoUpload) {
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
        organizationId: options.organizationId,
        environmentId: options.environmentId,
        owner: {
          ownerService: options.ownerService,
          ownerType: options.ownerType,
          ownerId: options.ownerId,
        },
        filePurpose: options.purpose,
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
        organizationId: options.organizationId,
        environmentId: options.environmentId,
        filePurpose: options.purpose,
        checksum: null,
        sizeBytes: row.sizeBytes,
      })

      row.fileId = completed.fileId ?? session.fileId
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
      row.error = error instanceof Error ? error.message : 'Upload failed.'
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
      rows.splice(index, 1)
      options.onCompleted(completedFiles.value)
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
    return 'File is larger than the maximum size.'
  }

  if (acceptedContentTypes.length > 0 && !isAcceptedType(file, acceptedContentTypes)) {
    return 'File type is not accepted.'
  }

  return null
}

function isAcceptedType(file: File, acceptedContentTypes: string[]) {
  return acceptedContentTypes.some((acceptedType) => {
    if (acceptedType.endsWith('/*')) {
      return file.type.startsWith(acceptedType.slice(0, -1))
    }

    return file.type === acceptedType
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
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  if (bytes < 1024 * 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  }

  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`
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
