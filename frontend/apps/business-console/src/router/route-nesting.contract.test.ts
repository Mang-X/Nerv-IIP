import { readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import type { RouteRecordRaw } from 'vue-router'
import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

const pagesDir = resolve(dirname(fileURLToPath(import.meta.url)), '../pages')

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: routes as RouteRecordRaw[],
  })
}

// Regression guard for the ERP demo blocker: `pages/erp/sales.vue` sat next to
// `pages/erp/sales/`, so unplugin-vue-router made it the PARENT of orders/quotations/
// deliveries. Without a <RouterView/> outlet the parent swallowed every child URL and
// `/erp/sales/orders` rendered the 销售机会 page instead.
const EXPECTED_ROUTES: Array<{
  path: string
  name: string
  page: () => Promise<{ default: unknown }>
}> = [
  { path: '/erp/sales', name: '/erp/sales/', page: () => import('../pages/erp/sales/index.vue') },
  {
    path: '/erp/sales/orders',
    name: '/erp/sales/orders',
    page: () => import('../pages/erp/sales/orders.vue'),
  },
  {
    path: '/erp/sales/quotations',
    name: '/erp/sales/quotations',
    page: () => import('../pages/erp/sales/quotations.vue'),
  },
  {
    path: '/erp/sales/deliveries',
    name: '/erp/sales/deliveries',
    page: () => import('../pages/erp/sales/deliveries.vue'),
  },
  {
    path: '/erp/finance',
    name: '/erp/finance/',
    page: () => import('../pages/erp/finance/index.vue'),
  },
  {
    path: '/erp/finance/ar-ap',
    name: '/erp/finance/ar-ap',
    page: () => import('../pages/erp/finance/ar-ap.vue'),
  },
  {
    path: '/erp/finance/vouchers',
    name: '/erp/finance/vouchers',
    page: () => import('../pages/erp/finance/vouchers.vue'),
  },
  {
    path: '/erp/finance/cost-candidates',
    name: '/erp/finance/cost-candidates',
    page: () => import('../pages/erp/finance/cost-candidates.vue'),
  },
]

describe('generated route nesting', () => {
  const router = makeRouter()

  for (const { path, name, page } of EXPECTED_ROUTES) {
    it(`${path} resolves to its own page component`, async () => {
      const resolved = router.resolve(path)

      expect(resolved.name, `${path} must not fall back to a parent/catch-all route`).toBe(name)

      // Only the leaf record may carry a component: every ancestor must be a pure
      // path-grouping record, otherwise the ancestor renders instead of the leaf.
      const withComponent = resolved.matched.filter((record) => {
        const components = record.components as Record<string, unknown> | null | undefined
        return components != null && Object.keys(components).length > 0
      })
      expect(withComponent).toHaveLength(1)
      expect(withComponent[0]?.name).toBe(name)

      // …and that single component is exactly this URL's page SFC.
      const loader = withComponent[0]?.components?.default as () => Promise<{ default: unknown }>
      const [loaded, expected] = await Promise.all([loader(), page()])
      expect(loaded.default).toBe(expected.default)
      // Loading a real page SFC pulls in the whole block library — allow for the transform.
    }, 60_000)
  }

  it('never leaves a component-bearing parent route without a <RouterView/> outlet', () => {
    const offenders: string[] = []

    const walk = (records: readonly RouteRecordRaw[]): void => {
      for (const record of records) {
        const components = record.components as Record<string, unknown> | null | undefined
        const hasComponent =
          Boolean(record.component) || (components != null && Object.keys(components).length > 0)
        if (hasComponent && record.children?.length) {
          offenders.push(String(record.name ?? record.path))
        }
        if (record.children?.length) walk(record.children)
      }
    }

    walk(routes as RouteRecordRaw[])

    // A component-bearing parent is only correct as an explicit layout with an outlet.
    // None exist today, so any new one is a routing accident until deliberately allowed.
    expect(offenders).toEqual([])
  })
})

describe('pages folder layout', () => {
  it('has no `x.vue` sitting next to an `x/` directory', () => {
    const offenders: string[] = []

    const walk = (dir: string): void => {
      const entries = readdirSync(dir)
      const directories = new Set(
        entries.filter((entry) => statSync(join(dir, entry)).isDirectory()),
      )

      for (const entry of entries) {
        if (!entry.endsWith('.vue')) continue
        const base = entry.slice(0, -'.vue'.length)
        if (!directories.has(base)) continue

        // Allowed only when the file is a real layout with an outlet.
        const source = readFileSync(join(dir, entry), 'utf8')
        if (/<RouterView\b|<router-view\b/.test(source)) continue

        offenders.push(relative(pagesDir, join(dir, entry)).replaceAll('\\', '/'))
      }

      for (const directory of directories) walk(join(dir, directory))
    }

    walk(pagesDir)

    expect(
      offenders,
      'move these pages to `<name>/index.vue` — a same-named sibling file becomes the parent route and swallows its children',
    ).toEqual([])
  })
})
