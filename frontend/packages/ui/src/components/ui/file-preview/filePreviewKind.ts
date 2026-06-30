import type { Component, HTMLAttributes } from 'vue'
import {
  FileIcon,
  FileImageIcon,
  FileSpreadsheetIcon,
  FileTextIcon,
  FileTypeIcon,
  PresentationIcon,
} from 'lucide-vue-next'

import {
  formatFileSize,
  getFileExtension,
  getFileFamily,
  normalizeContentType,
} from '../../../lib/file'

export type FilePreviewKind = 'pdf' | 'image' | 'office-docx' | 'office-xlsx' | 'office-pptx' | 'unsupported'

export interface FilePreviewKindMeta {
  label: string
  icon: Component
  iconClass: string
  iconContainerClass: string
}

export interface FilePreviewProps {
  src?: string
  fileName: string
  contentType?: string
  sizeBytes?: number
  height?: number | string
  loading?: boolean
  error?: string | null
  showHeader?: boolean
  class?: HTMLAttributes['class']
}

export interface FilePreviewEmits {
  ready: [kind: FilePreviewKind]
  error: [message: string]
  openSource: [src: string]
}

export const filePreviewMotion = {
  fastInvoke: {
    duration: 0.187,
    ease: [0, 0, 0, 1] as const,
  },
  fastInvokeMedium: {
    duration: 0.333,
    ease: [0, 0, 0, 1] as const,
  },
  fastInvokeLong: {
    duration: 0.5,
    ease: [0, 0, 0, 1] as const,
  },
  strongInvoke: {
    duration: 0.667,
    ease: [0.13, 1.62, 0, 0.92] as const,
  },
  fastDismiss: {
    duration: 0.187,
    ease: [0, 0, 0, 1] as const,
  },
  fastDismissMedium: {
    duration: 0.333,
    ease: [0, 0, 0, 1] as const,
  },
  fastDismissLong: {
    duration: 0.5,
    ease: [0, 0, 0, 1] as const,
  },
  softDismiss: {
    duration: 0.167,
    ease: [1, 0, 1, 1] as const,
  },
  pointToPointShort: {
    duration: 0.187,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  pointToPoint: {
    duration: 0.333,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  pointToPointLong: {
    duration: 0.5,
    ease: [0.55, 0.55, 0, 1] as const,
  },
  fade: {
    duration: 0.083,
    ease: 'linear' as const,
  },
} as const

export function getFilePreviewKind(fileName: string, contentType = ''): FilePreviewKind {
  const extension = getFileExtension(fileName)
  const type = normalizeContentType(contentType)
  const family = getFileFamily(fileName, contentType)

  if (family === 'pdf') {
    return 'pdf'
  }

  if (family === 'image') {
    return 'image'
  }

  if (extension === 'docx' || type.includes('wordprocessingml.document')) {
    return 'office-docx'
  }

  if (extension === 'xlsx' || type.includes('spreadsheetml.sheet')) {
    return 'office-xlsx'
  }

  if (extension === 'pptx' || type.includes('presentationml.presentation')) {
    return 'office-pptx'
  }

  return 'unsupported'
}

export function isFilePreviewSupported(fileName: string, contentType = '') {
  return getFilePreviewKind(fileName, contentType) !== 'unsupported'
}

export function getFilePreviewKindMeta(kind: FilePreviewKind): FilePreviewKindMeta {
  switch (kind) {
    case 'pdf':
      return {
        label: 'PDF',
        icon: FileTypeIcon,
        iconClass: 'text-red-600 dark:text-red-300',
        iconContainerClass: 'border-red-500/25 bg-red-500/10 dark:border-red-400/25 dark:bg-red-400/10',
      }
    case 'image':
      return {
        label: 'Image',
        icon: FileImageIcon,
        iconClass: 'text-violet-600 dark:text-violet-300',
        iconContainerClass: 'border-violet-500/25 bg-violet-500/10 dark:border-violet-400/25 dark:bg-violet-400/10',
      }
    case 'office-docx':
      return {
        label: 'Word',
        icon: FileTextIcon,
        iconClass: 'text-blue-600 dark:text-blue-300',
        iconContainerClass: 'border-blue-500/25 bg-blue-500/10 dark:border-blue-400/25 dark:bg-blue-400/10',
      }
    case 'office-xlsx':
      return {
        label: 'Excel',
        icon: FileSpreadsheetIcon,
        iconClass: 'text-emerald-600 dark:text-emerald-300',
        iconContainerClass: 'border-emerald-500/25 bg-emerald-500/10 dark:border-emerald-400/25 dark:bg-emerald-400/10',
      }
    case 'office-pptx':
      return {
        label: 'PowerPoint',
        icon: PresentationIcon,
        iconClass: 'text-orange-600 dark:text-orange-300',
        iconContainerClass: 'border-orange-500/25 bg-orange-500/10 dark:border-orange-400/25 dark:bg-orange-400/10',
      }
    default:
      return {
        label: 'File',
        icon: FileIcon,
        iconClass: 'text-muted-foreground',
        iconContainerClass: 'border-border bg-muted/60',
      }
  }
}

export function formatFilePreviewSize(sizeBytes?: number | null) {
  return formatFileSize(sizeBytes)
}
