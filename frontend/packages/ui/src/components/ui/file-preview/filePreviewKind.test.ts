import { describe, expect, it } from 'vitest'

import {
  filePreviewMotion,
  formatFilePreviewSize,
  getFilePreviewKind,
  isFilePreviewSupported,
} from './filePreviewKind'

describe('file preview kind helpers', () => {
  it('matches previewable file families from extension or content type', () => {
    expect(getFilePreviewKind('inspection.pdf', '')).toBe('pdf')
    expect(getFilePreviewKind('photo', 'image/png')).toBe('image')
    expect(getFilePreviewKind('download', 'application/pdf; charset=utf-8')).toBe('pdf')
    expect(getFilePreviewKind('work-instruction.docx', '')).toBe('office-docx')
    expect(getFilePreviewKind('mrp', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')).toBe('office-xlsx')
    expect(getFilePreviewKind('review.pptx', '')).toBe('office-pptx')
  })

  it('does not claim unsupported legacy office files are previewable', () => {
    expect(getFilePreviewKind('legacy.doc', 'application/msword')).toBe('unsupported')
    expect(getFilePreviewKind('legacy.xls', 'application/vnd.ms-excel')).toBe('unsupported')
    expect(getFilePreviewKind('archive.zip', 'application/zip')).toBe('unsupported')
    expect(isFilePreviewSupported('legacy.ppt', 'application/vnd.ms-powerpoint')).toBe(false)
  })

  it('formats byte sizes for dense file metadata', () => {
    expect(formatFilePreviewSize()).toBe('')
    expect(formatFilePreviewSize(512)).toBe('512 B')
    expect(formatFilePreviewSize(-500)).toBe('0 B')
    expect(formatFilePreviewSize(1536)).toBe('1.5 KB')
    expect(formatFilePreviewSize(2 * 1024 * 1024)).toBe('2.0 MB')
  })

  it('does not treat extensionless names as previewable extensions', () => {
    expect(getFilePreviewKind('pdf', '')).toBe('unsupported')
    expect(getFilePreviewKind('png', '')).toBe('unsupported')
  })

  it('exposes Windows motion values for the component animation layer', () => {
    expect(filePreviewMotion.fastInvoke).toEqual({
      duration: 0.187,
      ease: [0, 0, 0, 1],
    })
    expect(filePreviewMotion.strongInvoke).toEqual({
      duration: 0.667,
      ease: [0.13, 1.62, 0, 0.92],
    })
    expect(filePreviewMotion.softDismiss).toEqual({
      duration: 0.167,
      ease: [1, 0, 1, 1],
    })
    expect(filePreviewMotion.fade).toEqual({
      duration: 0.083,
      ease: 'linear',
    })
  })
})
