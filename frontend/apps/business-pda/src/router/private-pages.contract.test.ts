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

  it('routes the three shift-handover pages and keeps their private components out (#2784)', () => {
    const paths = routePaths(routes)

    // 交班录入 / 接班列表 / 接班详情三条都要真的进路由表——首页入口指的就是这三条。
    expect(paths).toContain('/mes/handover')
    expect(paths).toContain('/mes/handovers')
    expect(paths.some((path) => path.startsWith('/mes/handovers/:handoverId'))).toBe(true)

    // 交班录入与接班列表不能塌成同一条路由（差一个 s）。
    expect(paths.filter((path) => path === '/mes/handover')).toHaveLength(1)
    expect(paths.filter((path) => path === '/mes/handovers')).toHaveLength(1)

    // pages/mes/components/ 下的三个交接班私有组件不得成为公开路由。
    for (const componentName of [
      'ShiftHandoverEntryForm',
      'ShiftHandoverPhotoCapture',
      'ShiftHandoverDetailSections',
    ]) {
      expect(paths.some((path) => path.includes(componentName))).toBe(false)
    }
  })
})
