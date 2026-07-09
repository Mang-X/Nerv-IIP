import { describe, expect, it } from 'vitest'

import * as ui from './index'

describe('@nerv-iip/ui FE-2 block library exports', () => {
  it('exports the layout + header blocks from the stable boundary (Nv names)', () => {
    expect(ui.NvAppShellInset).toBeDefined()
    expect(ui.NvPageHeader).toBeDefined()
    expect(ui.NvSectionCard).toBeDefined()
    expect(ui.NvSectionCards).toBeDefined()
    expect(ui.NvToolbar).toBeDefined()
  })

  it('exports the data + status blocks from the stable boundary (Nv names)', () => {
    // The superseded blocks DataTable/DataTablePagination/StatusBadge were deleted at
    // the #789 closeout; the pro-layer NvDataTable/NvDataTablePagination/NvStatusBadge
    // are the canonical exports. `resolveStatus` (a helper, not a component) stays.
    expect(ui.NvDataTable).toBeDefined()
    expect(ui.NvDataTablePagination).toBeDefined()
    expect(ui.NvStatusBadge).toBeDefined()
    expect(ui.NvRowActions).toBeDefined()
    expect(ui.resolveStatus).toBeTypeOf('function')
  })

  it('exports the theme-control blocks from the stable boundary (Nv names)', () => {
    expect(ui.NvThemeToggle).toBeDefined()
    expect(ui.NvThemePicker).toBeDefined()
  })

  it('resolves a known status to a localized label + tone', () => {
    expect(ui.resolveStatus('running')).toEqual({ label: '执行中', tone: 'info' })
    expect(ui.resolveStatus('ready')).toEqual({ label: '可开工', tone: 'success' })
    expect(ui.resolveStatus('blocked')).toEqual({ label: '阻塞', tone: 'danger' })
    expect(ui.resolveStatus('ScheduleInvalidated')).toEqual({
      label: '排程已失效',
      tone: 'warning',
    })
    expect(ui.resolveStatus('').tone).toBe('neutral')
  })
})
