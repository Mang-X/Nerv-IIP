import { describe, expect, it } from 'vitest'
import { cssColor, escapeHtml } from './utils'

// The chart tooltips hand unovis a template *string* rendered as raw HTML, so
// point labels / categories / units / colours are HTML sinks fed by server data.
// These lock the neutralisation; without them the escaping is a silent no-op the
// moment someone "simplifies" the template back to bare interpolation.

describe('escapeHtml — chart tooltip HTML sink', () => {
  it('neutralises a script-bearing payload in a data label', () => {
    const out = escapeHtml('<img src=x onerror="alert(1)">')
    expect(out).not.toContain('<img')
    expect(out).not.toContain('onerror="')
    expect(out).toBe('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;')
  })

  it('stops a colour value from breaking out of the inline style attribute', () => {
    // rendered as style="background:${escapeHtml(color)}" — the quote must not close it
    const out = escapeHtml('red" onmouseover="alert(1)')
    expect(out).not.toContain('"')
    expect(out).toContain('&quot;')
  })

  it('escapes the ampersand without double-escaping the entities it produces', () => {
    expect(escapeHtml('A & B')).toBe('A &amp; B')
    expect(escapeHtml('&lt;')).toBe('&amp;lt;')
  })

  it('covers the full &<>"\' set and leaves ordinary values byte-identical', () => {
    expect(escapeHtml(`&<>"'`)).toBe('&amp;&lt;&gt;&quot;&#39;')
    // the values these templates actually carry in practice must render unchanged
    for (const plain of ['07-23', '1,284 件', '98.6%', 'var(--chart-1)', 'oklch(0.6 0.12 160)']) {
      expect(escapeHtml(plain)).toBe(plain)
    }
  })

  it('coerces non-string values (numbers, null) instead of throwing', () => {
    expect(escapeHtml(12480)).toBe('12480')
    expect(escapeHtml(null)).toBe('null')
    expect(escapeHtml(undefined)).toBe('undefined')
  })
})

describe('cssColor — chart tooltip inline-style sink', () => {
  it('passes through the colour forms the charts actually use', () => {
    for (const ok of [
      'var(--chart-1)',
      '#3b82f6',
      '#abc',
      'oklch(0.6 0.12 160)',
      'rgb(59, 130, 246)',
      'rgba(0,0,0,.4)',
      'currentColor',
      'transparent',
      'color-mix(in oklch, var(--nv-brand) 40%, transparent)',
    ]) {
      expect(cssColor(ok)).toBe(ok)
    }
  })

  it('drops a semicolon-injected extra declaration to currentColor', () => {
    // escapeHtml would keep this verbatim inside style="background:…" and the
    // ;-separated declarations would take effect — cssColor must neutralise it.
    expect(cssColor('red;position:fixed;inset:0')).toBe('currentColor')
    expect(cssColor('red}#x{color:red')).toBe('currentColor')
  })

  it('refuses url() even inside an otherwise valid var() fallback', () => {
    expect(cssColor('var(--x, url(https://example.invalid/x))')).toBe('currentColor')
    expect(cssColor('url(https://example.invalid/x)')).toBe('currentColor')
  })
})
