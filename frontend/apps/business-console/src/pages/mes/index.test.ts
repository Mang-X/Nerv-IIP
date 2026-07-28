import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import MesIndexPage from './index.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const overviewState = vi.hoisted(() => ({
  blockers: [] as Array<{ areaCode?: string; code?: string; count?: number; message?: string }>,
  counts: [] as Array<{ count?: number; key: string }>,
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
    // 阻塞原因中文化后首页要用它把 reason 码翻成「说法 + 下一步」；
    // 这里给和真实实现同构的最小桩，保持用例只关心路由跳转。
    describeMesReadinessReason: (reason: string) => ({
      code: reason,
      label: reason,
      nextStep: '查看阻塞详情并按来源业务页面处理',
    }),
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
            template:
              '<a data-router-link :data-to="typeof to === \'string\' ? to : to.path"><slot /></a>',
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
