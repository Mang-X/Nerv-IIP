export type FileFamily = 'word' | 'spreadsheet' | 'presentation' | 'pdf' | 'image' | 'audio' | 'video' | 'archive' | 'file'

const wordExtensions = new Set(['doc', 'docx'])
const spreadsheetExtensions = new Set(['xls', 'xlsx', 'csv'])
const presentationExtensions = new Set(['ppt', 'pptx'])
const imageExtensions = new Set(['avif', 'bmp', 'gif', 'jpeg', 'jpg', 'png', 'svg', 'webp'])
const audioExtensions = new Set(['aac', 'flac', 'm4a', 'mp3', 'wav'])
const videoExtensions = new Set(['avi', 'mkv', 'mov', 'mp4', 'webm'])
const archiveExtensions = new Set(['7z', 'gz', 'rar', 'tar', 'zip'])

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
