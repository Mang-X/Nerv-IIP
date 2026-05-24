export type FileUploadStatus = 'queued' | 'uploading' | 'completed' | 'failed' | 'rejected'

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
  uploadMode: 'server-proxy' | 'tus' | string
  provider: 'server-proxy' | 'tus' | string
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
}
