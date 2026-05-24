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

  const completedFiles = computed(() =>
    rows
      .filter((row) => row.status === 'completed' && row.fileId)
      .map((row) => ({ fileId: row.fileId!, fileName: row.fileName })),
  )
  const isUploading = computed(() => rows.some((row) => row.status === 'uploading'))

  async function addFiles(files: File[]) {
    const availableSlots = Math.max(options.maxFiles - rows.length, 0)
    const acceptedRows: FileUploadRow[] = []
    const rejectedFiles: FileUploadRejectedFile[] = []

    for (const file of files.slice(0, availableSlots)) {
      const rejection = validateFile(file, options.acceptedContentTypes, options.maxFileSizeBytes)

      if (rejection) {
        rejectedFiles.push({ fileName: file.name, reason: rejection })
        rows.push(createRow(file, 'rejected', rejection))
        continue
      }

      const row = createRow(file, 'queued')
      rows.push(row)
      acceptedRows.push(row)
    }

    for (const file of files.slice(availableSlots)) {
      rejectedFiles.push({ fileName: file.name, reason: 'Maximum file count reached.' })
    }

    if (rejectedFiles.length > 0) {
      options.onRejected(rejectedFiles)
    }

    await Promise.all(acceptedRows.map(uploadRow))
  }

  async function uploadRow(row: FileUploadRow) {
    row.status = 'uploading'
    row.progress = 1
    row.error = null

    try {
      const session = await options.createUploadSession({
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

      await options.transport({
        file: row.file,
        session,
        onProgress: (progress) => {
          row.progress = clampProgress(progress)
        },
      })

      const completed = await options.completeUploadSession(session.uploadSessionId, {
        organizationId: options.organizationId,
        environmentId: options.environmentId,
        filePurpose: options.purpose,
        checksum: null,
        sizeBytes: row.sizeBytes,
      })

      row.fileId = completed.fileId || session.fileId
      row.status = 'completed'
      row.progress = 100
      options.onCompleted(completedFiles.value)
    }
    catch (error) {
      row.status = 'failed'
      row.error = error instanceof Error ? error.message : 'Upload failed.'
      options.onFailed(row)
    }
  }

  function removeRow(id: string) {
    const index = rows.findIndex((row) => row.id === id)

    if (index >= 0) {
      rows.splice(index, 1)
      options.onCompleted(completedFiles.value)
    }
  }

  return {
    rows,
    completedFiles,
    isUploading,
    addFiles,
    removeRow,
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
