export type FileUploadStatus = 'queued' | 'uploading' | 'paused' | 'completed' | 'failed' | 'rejected'
export type FileUploadMode = 'server-proxy' | 'tus'
export type FileUploadProvider = 'server-proxy' | 'tus'
export type FileUploadVariant = 'default' | 'queue' | 'compact' | 'avatar' | 'gallery' | 'table' | 'image'

export interface FileUploadOwner {
  ownerService: string
  ownerType: string
  ownerId: string
}

export interface FileUploadCreateSessionRequest {
  organizationId: string
  environmentId: string
  owner: FileUploadOwner
  filePurpose: string
  fileName: string
  contentType: string
  expectedSizeBytes: number
  checksum: string | null
}

export interface FileUploadCompleteSessionRequest {
  organizationId: string
  environmentId: string
  filePurpose: string
  checksum: string | null
  sizeBytes: number
}

export interface FileUploadSession {
  uploadSessionId: string
  fileId: string
  uploadMode: FileUploadMode
  provider: FileUploadProvider
  expiresAtUtc: string
  upload: {
    url: string
    headers: Record<string, string>
  }
}

export interface FileUploadTransportContext {
  file: File
  session: FileUploadSession
  onProgress: (progress: number) => void
  signal?: AbortSignal
}

export type FileUploadTransport = (context: FileUploadTransportContext) => Promise<void>

export interface FileUploadCompletedFile {
  fileId: string
  fileName: string
}

export interface FileUploadRejectedFile {
  fileName: string
  reason: string
}

export interface FileUploadRow {
  id: string
  file: File
  fileName: string
  sizeBytes: number
  contentType: string
  status: FileUploadStatus
  progress: number
  fileId: string | null
  error: string | null
  uploadSession: FileUploadSession | null
}

export interface FileUploadExpose {
  readonly hasRows: boolean
  readonly hasQueuedRows: boolean
  addFiles: (files: File[]) => Promise<void>
  uploadQueued: () => Promise<void>
  pauseAll: () => void
  resumeAll: () => Promise<void>
  retryFailed: () => Promise<void>
  clear: () => void
  browse: () => void
}
