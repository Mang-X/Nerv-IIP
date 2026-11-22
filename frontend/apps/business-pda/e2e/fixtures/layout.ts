import { type Page, expect } from '@playwright/test'

export async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow).toBeLessThanOrEqual(1)
}

/**
 * Every enabled interactive control must meet the 44px touch-target floor.
 *
 * Query set covers real-world interactive shapes — including `<div role="button">`
 * (e.g. ListRow) and `role="link"`, which are the forms most likely to render too
 * small. Disabled controls (native `:disabled` or `aria-disabled="true"`) are excluded.
 *
 * Pass rule — deliberately strict so a large layout container can't mask a genuinely
 * small control:
 *  - The control's OWN box meets the floor (>=44 in both dimensions) → pass.
 *  - ONLY for a bare `<input>` declared full-width (CSS `width:100%`, i.e. a flex
 *    full-row input) whose parent row is >=44px tall does the row supply the tap
 *    surface (e.g. ScanBar, where the input shares a 48px `min-h-touch` row with a
 *    decorative icon). The width-intent check is read from the declared style, not the
 *    rendered box, so an icon/padding sibling can't fail it — yet a small `role=button`
 *    tucked inside a tall wrapper is NOT excused.
 *  - Everything else (including role=button / div) is judged by its own box.
 */
export async function expectTouchTargets(page: Page) {
  const tooSmall = await page.evaluate(() => {
    const FLOOR = 44
    const els = [
      ...document.querySelectorAll<HTMLElement>(
        'button:not([disabled]), a[href], input, [role="button"]:not([aria-disabled="true"]), [role="link"]',
      ),
    ]
    return els
      .map((el) => {
        const own = el.getBoundingClientRect()
        let effW = own.width
        let effH = own.height
        // Legal full-row input: a full-width (`width:100%`) <input> whose >=44px-tall
        // parent row carries the tap surface. Restricted to <input> + declared
        // full-width intent so it cannot excuse a small control inside a tall wrapper.
        if (el.tagName === 'INPUT' && el.parentElement) {
          const row = el.parentElement.getBoundingClientRect()
          const declaredFullWidth =
            getComputedStyle(el).width === '100%' || el.classList.contains('w-full')
          if (row.height >= FLOOR && own.width > 0 && declaredFullWidth) {
            effW = Math.max(own.width, row.width)
            effH = row.height
          }
        }
        return { tag: el.tagName, role: el.getAttribute('role'), w: effW, h: effH }
      })
      .filter((m) => m.w > 0 && m.h > 0 && (m.w < FLOOR || m.h < FLOOR))
  })
  expect(tooSmall).toEqual([])
}
