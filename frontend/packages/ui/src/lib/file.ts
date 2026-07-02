export type FileFamily = 'word' | 'spreadsheet' | 'presentation' | 'pdf' | 'image' | 'audio' | 'video' | 'archive' | 'file'

export const fileFamilyExtensions = {
  word: ['doc', 'docx'],
  spreadsheet: ['xls', 'xlsx', 'csv'],
  presentation: ['ppt', 'pptx'],
  pdf: ['pdf'],
  image: ['avif', 'bmp', 'gif', 'jpeg', 'jpg', 'png', 'svg', 'webp'],
  audio: ['aac', 'flac', 'm4a', 'mp3', 'wav'],
  video: ['avi', 'mkv', 'mov', 'mp4', 'webm'],
  archive: ['7z', 'gz', 'rar', 'tar', 'zip'],
  file: [],
} as const satisfies Record<FileFamily, readonly string[]>

const wordExtensions = new Set<string>(fileFamilyExtensions.word)
const spreadsheetExtensions = new Set<string>(fileFamilyExtensions.spreadsheet)
const presentationExtensions = new Set<string>(fileFamilyExtensions.presentation)
const imageExtensions = new Set<string>(fileFamilyExtensions.image)
const audioExtensions = new Set<string>(fileFamilyExtensions.audio)
const videoExtensions = new Set<string>(fileFamilyExtensions.video)
const archiveExtensions = new Set<string>(fileFamilyExtensions.archive)

const contentTypeExtensionHints: Record<string, readonly string[]> = {
  'application/pdf': ['pdf'],
  'application/msword': ['doc'],
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': ['docx'],
  'application/vnd.ms-excel': ['xls'],
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['xlsx'],
  'application/vnd.ms-powerpoint': ['ppt'],
  'application/vnd.openxmlformats-officedocument.presentationml.presentation': ['pptx'],
  'application/zip': ['zip'],
  'application/x-zip-compressed': ['zip'],
  'application/json': ['json'],
  'application/xml': ['xml'],
  'text/plain': ['txt'],
  'text/csv': ['csv'],
  'text/*': ['txt', 'csv'],
  'image/*': fileFamilyExtensions.image,
  'image/png': ['png'],
  'image/jpeg': ['jpg', 'jpeg'],
  'image/gif': ['gif'],
  'image/webp': ['webp'],
  'image/svg+xml': ['svg'],
  'audio/*': fileFamilyExtensions.audio,
  'audio/mpeg': ['mp3'],
  'video/*': fileFamilyExtensions.video,
  'video/mp4': ['mp4'],
}

export function getFileExtension(fileName: string) {
  const normalizedName = fileName.trim()
  const dotIndex = normalizedName.lastIndexOf('.')

  if (dotIndex <= 0 || dotIndex === normalizedName.length - 1) {
    return ''
  }

  return normalizedName.slice(dotIndex + 1).toLowerCase()
}

export function normalizeContentType(contentType = '') {
  return contentType.toLowerCase().split(';', 1)[0]?.trim() ?? ''
}

export function getAcceptedFileExtensions(acceptedType: string) {
  const normalized = acceptedType.trim().toLowerCase()

  if (!normalized) {
    return []
  }

  if (normalized.startsWith('.')) {
    const extension = normalized.slice(1)
    return extension ? [extension] : []
  }

  const explicitExtensions = contentTypeExtensionHints[normalized]
  if (explicitExtensions) {
    return [...explicitExtensions]
  }

  const family = getFileFamily('', normalized)
  if (family !== 'file') {
    return [...fileFamilyExtensions[family]]
  }

  return []
}

export function getFileFamily(fileName: string, contentType = ''): FileFamily {
  const extension = getFileExtension(fileName)
  const type = normalizeContentType(contentType)

  if (wordExtensions.has(extension) || type.includes('wordprocessingml') || type === 'application/msword') {
    return 'word'
  }

  if (spreadsheetExtensions.has(extension) || type.includes('spreadsheetml') || type === 'text/csv' || type === 'application/vnd.ms-excel') {
    return 'spreadsheet'
  }

  if (presentationExtensions.has(extension) || type.includes('presentationml') || type === 'application/vnd.ms-powerpoint') {
    return 'presentation'
  }

  if (extension === 'pdf' || type === 'application/pdf') {
    return 'pdf'
  }

  if (type.startsWith('image/') || imageExtensions.has(extension)) {
    return 'image'
  }

  if (type.startsWith('audio/') || audioExtensions.has(extension)) {
    return 'audio'
  }

  if (type.startsWith('video/') || videoExtensions.has(extension)) {
    return 'video'
  }

  if (archiveExtensions.has(extension)) {
    return 'archive'
  }

  return 'file'
}

export function formatFileSize(sizeBytes?: number | null) {
  if (sizeBytes == null || Number.isNaN(sizeBytes)) {
    return ''
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = Math.max(0, sizeBytes)
  let unitIndex = 0

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }

  if (unitIndex === 0) {
    return `${Math.round(value)} ${units[unitIndex]}`
  }

  return `${value.toFixed(1)} ${units[unitIndex]}`
}
