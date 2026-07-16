import {
  ArchiveIcon,
  FileAudioIcon,
  FileIcon,
  FileImageIcon,
  FileSpreadsheetIcon,
  FileTextIcon,
  FileVideoIcon,
  PresentationIcon,
} from '@lucide/vue'

import { getFileFamily } from '../../../lib/file'

export function getFileKind(fileName: string, contentType: string) {
  const family = getFileFamily(fileName, contentType)

  if (family === 'word') {
    return { label: 'Word', icon: FileTextIcon }
  }

  if (family === 'spreadsheet') {
    return { label: 'Excel', icon: FileSpreadsheetIcon }
  }

  if (family === 'presentation') {
    return { label: 'PowerPoint', icon: PresentationIcon }
  }

  if (family === 'pdf') {
    return { label: 'PDF', icon: FileTextIcon }
  }

  if (family === 'image') {
    return { label: 'Image', icon: FileImageIcon }
  }

  if (family === 'audio') {
    return { label: 'Audio', icon: FileAudioIcon }
  }

  if (family === 'video') {
    return { label: 'Video', icon: FileVideoIcon }
  }

  if (family === 'archive') {
    return { label: 'Archive', icon: ArchiveIcon }
  }

  return { label: 'File', icon: FileIcon }
}
