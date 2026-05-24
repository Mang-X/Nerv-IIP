import { describe, expect, it } from 'vitest'

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
})
