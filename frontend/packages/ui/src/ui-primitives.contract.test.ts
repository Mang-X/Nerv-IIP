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
    const dropdownContent = readFileSync(
      resolve(process.cwd(), 'src/components/ui/dropdown-menu/DropdownMenuContent.vue'),
      'utf8',
    )
    const themeCss = readFileSync(
      resolve(process.cwd(), 'src/styles/theme.css'),
      'utf8',
    )

    expect(selectContent).toContain('ds-overlay-content ds-select-content')
    expect(selectContent).toContain('ds-select-content')
    expect(selectContent).toContain(':data-align-trigger="position === \'item-aligned\'"')
    expect(selectContent).not.toContain('<style')
    expect(dropdownContent).toContain('ds-overlay-content ds-dropdown-menu-content')
    expect(dropdownContent).not.toContain('<style')
    expect(themeCss).toContain('@keyframes ds-overlay-content-open-transform')
    expect(themeCss).toContain(".ds-overlay-content[data-align-trigger='true']")
    expect(themeCss).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
