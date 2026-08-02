import type { RouteRecordRaw } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import { describe, expect, it } from 'vitest'

function routePaths(records: readonly RouteRecordRaw[], parent = ''): string[] {
  return records.flatMap((route) => {
    const path = route.path.startsWith('/')
      ? route.path
      : `${parent.replace(/\/$/, '')}/${route.path}`
    return [path, ...routePaths(route.children ?? [], path)]
  })
}

describe('PDA private page components', () => {
  it('keeps page-private components out of the generated route table', () => {
    const paths = routePaths(routes)

    expect(paths).toContain('/mes/operation')
    expect(paths.some((path) => path.includes('/components/'))).toBe(false)
    expect(paths.some((path) => path.includes('MesOperationExecutionPanel'))).toBe(false)
  })
})
