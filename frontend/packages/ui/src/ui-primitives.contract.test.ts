import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

import * as filePreview from './components/ui/file-preview'
import * as ui from './index'

describe('@nerv-iip/ui foundation primitive exports', () => {
  it('exports tabs primitives from the stable package boundary', () => {
    expect(ui.Tabs).toBeDefined()
    expect(ui.TabsList).toBeDefined()
    expect(ui.TabsTrigger).toBeDefined()
    expect(ui.TabsContent).toBeDefined()
  })

  it('exports sheet primitives from the stable package boundary', () => {
    expect(ui.Sheet).toBeDefined()
    expect(ui.SheetClose).toBeDefined()
    expect(ui.SheetContent).toBeDefined()
    expect(ui.SheetDescription).toBeDefined()
    expect(ui.SheetFooter).toBeDefined()
    expect(ui.SheetHeader).toBeDefined()
    expect(ui.SheetTitle).toBeDefined()
    expect(ui.SheetTrigger).toBeDefined()
  })

  it('exports progress and scroll area primitives from the stable package boundary', () => {
    expect(ui.Progress).toBeDefined()
    expect(ui.ScrollArea).toBeDefined()
    expect(ui.ScrollBar).toBeDefined()
  })

  it('exports file upload primitives from the stable package boundary', () => {
    expect(ui.FileUpload).toBeDefined()
    expect(ui.uploadWithNativeFileStorageTransport).toBeDefined()
    expect(ui.useFileUpload).toBeDefined()
  })

  it('exports lightweight file preview helpers from the root package boundary', () => {
    expect(ui.getFilePreviewKind).toBeDefined()
    expect(ui.isFilePreviewSupported).toBeDefined()
    expect(ui.filePreviewMotion).toBeDefined()
  })

  it('exports the file preview component from its stable subpath boundary', () => {
    expect(filePreview.FilePreview).toBeDefined()
    expect(filePreview.getFilePreviewKind).toBeDefined()
    expect(filePreview.isFilePreviewSupported).toBeDefined()
    expect(filePreview.filePreviewMotion).toBeDefined()
  })

  it('keeps select dropdown motion enabled in the shared component library', () => {
    const selectContent = readFileSync(
      resolve(process.cwd(), 'src/components/ui/select/SelectContent.vue'),
      'utf8',
    )

    expect(selectContent).toContain('ds-select-content')
    expect(selectContent).toContain('@keyframes ds-select-content-open-transform')
    expect(selectContent).toContain('@media (prefers-reduced-motion: reduce)')
    expect(selectContent).not.toContain('data-[align-trigger=true]:animate-none')
    expect(selectContent).not.toContain('<style scoped>')
  })
})
