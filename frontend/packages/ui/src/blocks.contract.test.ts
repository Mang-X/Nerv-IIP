import { describe, expect, it } from 'vitest'

import * as ui from './index'

describe('@nerv-iip/ui FE-2 block library exports', () => {
  it('exports the layout + header blocks from the stable boundary', () => {
    expect(ui.AppShellInset).toBeDefined()
    expect(ui.PageHeader).toBeDefined()
    expect(ui.SectionCard).toBeDefined()
    expect(ui.SectionCards).toBeDefined()
    expect(ui.Toolbar).toBeDefined()
  })

  it('exports the data + status blocks from the stable boundary', () => {
    expect(ui.DataTable).toBeDefined()
    expect(ui.DataTablePagination).toBeDefined()
    expect(ui.StatusBadge).toBeDefined()
    expect(ui.RowActions).toBeDefined()
    expect(ui.resolveStatus).toBeTypeOf('function')
  })

  it('exports the theme-control blocks from the stable boundary', () => {
    expect(ui.ThemeToggle).toBeDefined()
    expect(ui.ThemePicker).toBeDefined()
  })

  it('resolves a known status to a localized label + tone', () => {
    expect(ui.resolveStatus('running')).toEqual({ label: '执行中', tone: 'info' })
    expect(ui.resolveStatus('ready')).toEqual({ label: '可开工', tone: 'success' })
    expect(ui.resolveStatus('blocked')).toEqual({ label: '阻塞', tone: 'danger' })
    expect(ui.resolveStatus('ScheduleInvalidated')).toEqual({ label: '排程已失效', tone: 'warning' })
    expect(ui.resolveStatus('').tone).toBe('neutral')
  })
})
