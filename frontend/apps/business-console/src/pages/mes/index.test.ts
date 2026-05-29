import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import MesIndexPage from './index.vue'

const overviewState = vi.hoisted(() => ({
  blockers: [] as Array<{ areaCode?: string, code?: string, count?: number, message?: string }>,
  counts: [] as Array<{ count?: number, key: string }>,
  overviewError: undefined as Error | undefined,
  overviewPending: false,
  pendingWork: [] as Array<{ count?: number }>,
  refreshOverview: vi.fn(),
}))

vi.mock('@/composables/useBusinessMes', () => {
  function readonlyRef<T>(read: () => T) {
    return {
      __v_isRef: true,
      get value() {
        return read()
      },
    }
  }

  return {
    useMesOverview: () => ({
      blockers: readonlyRef(() => overviewState.blockers),
      counts: readonlyRef(() => overviewState.counts),
      overviewError: readonlyRef(() => overviewState.overviewError),
      overviewPending: readonlyRef(() => overviewState.overviewPending),
      pendingWork: readonlyRef(() => overviewState.pendingWork),
      refreshOverview: overviewState.refreshOverview,
    }),
  }
})

describe('MES index page', () => {
  beforeEach(() => {
    overviewState.blockers = []
    overviewState.counts = []
    overviewState.overviewError = undefined
    overviewState.overviewPending = false
    overviewState.pendingWork = []
    overviewState.refreshOverview.mockReset()
  })

  function mountPage() {
    return mount(MesIndexPage, {
      global: {
        stubs: {
          BusinessLayout: {
            template: '<main><slot /></main>',
          },
          RouterLink: {
            props: ['to'],
            template: '<a data-router-link :data-to="typeof to === \'string\' ? to : to.path"><slot /></a>',
          },
        },
      },
    })
  }

  it('routes the blocker command card to capacity when blockers exist', () => {
    overviewState.blockers = [
      {
        areaCode: 'Equipment',
        code: 'AssetUnavailable',
        count: 2,
        message: '设备不可用',
      },
    ]

    const wrapper = mountPage()
    const blockerCard = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.text().includes('先处理阻塞'))

    expect(blockerCard).toBeDefined()
    expect(blockerCard!.attributes('data-to')).toBe('/mes/capacity')
    expect(blockerCard!.text()).toContain('查看异常与产能')
  })
})
