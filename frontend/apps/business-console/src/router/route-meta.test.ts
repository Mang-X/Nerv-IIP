import { describe, expect, it } from 'vitest'
import type { RouteRecordRaw } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

function findRouteByName(records: RouteRecordRaw[], name: string): RouteRecordRaw | undefined {
  for (const record of records) {
    if (record.name === name) return record
    const child = record.children ? findRouteByName(record.children, name) : undefined
    if (child) return child
  }
}

describe('generated route metadata', () => {
  it('keeps approval route permission metadata statically evaluable', () => {
    const route = findRouteByName(routes as RouteRecordRaw[], '/approval/')

    expect(route?.meta?.requiredPermissions).toEqual([
      'business.approvals.read',
      'business.approvals.manage',
    ])
  })
})
