import {
  ArchiveIcon,
  FileAudioIcon,
  FileIcon,
  FileImageIcon,
  FileSpreadsheetIcon,
  FileTextIcon,
  FileVideoIcon,
  PresentationIcon,
} from 'lucide-vue-next'

export function getFileKind(fileName: string, contentType: string) {
  const extension = fileName.split('.').pop()?.toLowerCase() ?? ''
  const type = contentType.toLowerCase()

  if (['doc', 'docx'].includes(extension) || type.includes('wordprocessingml')) {
    return { label: 'Word', icon: FileTextIcon }
  }

  if (['xls', 'xlsx', 'csv'].includes(extension) || type.includes('spreadsheetml') || type === 'text/csv') {
    return { label: 'Excel', icon: FileSpreadsheetIcon }
  }

  if (['ppt', 'pptx'].includes(extension) || type.includes('presentationml')) {
    return { label: 'PowerPoint', icon: PresentationIcon }
  }

  if (extension === 'pdf' || type === 'application/pdf') {
    return { label: 'PDF', icon: FileTextIcon }
  }

  if (type.startsWith('image/') || ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'].includes(extension)) {
    return { label: 'Image', icon: FileImageIcon }
  }

  if (type.startsWith('audio/') || ['mp3', 'wav', 'flac', 'aac', 'm4a'].includes(extension)) {
    return { label: 'Audio', icon: FileAudioIcon }
  }

  if (type.startsWith('video/') || ['mp4', 'mov', 'avi', 'mkv', 'webm'].includes(extension)) {
    return { label: 'Video', icon: FileVideoIcon }
  }

  if (['zip', '7z', 'rar', 'tar', 'gz'].includes(extension)) {
    return { label: 'Archive', icon: ArchiveIcon }
  }

  return { label: 'File', icon: FileIcon }
}
