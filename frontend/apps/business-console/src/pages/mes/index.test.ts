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
  // 读面四态：驾驶舱的所有结论都以它为准，'ready' 之外一律不许下结论。
  readState: 'ready' as 'idle' | 'loading' | 'error' | 'ready',
  pendingWork: [] as Array<{ count?: number }>,
  refreshOverview: vi.fn(),
}))

// 「我的班组」那块的数字来自工序任务读面（按登录人授权作业范围过滤后的服务端 total）。
// 这里按 status 分别给桩，用例才能验「待开工 / 进行中」各自取到了自己的数。
const myScopeState = vi.hoisted(() => ({
  scope: undefined as { kind: string; id: string; displayName?: string } | undefined,
  scopeMessage: '',
  readState: 'ready' as 'idle' | 'loading' | 'error' | 'ready',
  totals: {} as Record<string, number>,
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
      overviewState: readonlyRef(() => overviewState.readState),
      pendingWork: readonlyRef(() => overviewState.pendingWork),
      refreshOverview: overviewState.refreshOverview,
    }),
    mesWorkScopeKindLabel: (kind: string) =>
      ({ team: '班组', 'work-center': '工作中心' })[kind] ?? kind,
    useMesOperationTasks: () => {
      const filters = { status: undefined as string | undefined, take: 0 }
      return {
        filters,
        operationTasksTotal: readonlyRef(() => myScopeState.totals[filters.status ?? ''] ?? 0),
        operationTasksState: readonlyRef(() => myScopeState.readState),
        operationListScope: readonlyRef(() => myScopeState.scope),
        operationListScopeMessage: readonlyRef(() => myScopeState.scopeMessage),
      }
    },
  }
})

describe('MES index page', () => {
  beforeEach(() => {
    overviewState.blockers = []
    overviewState.counts = []
    overviewState.overviewError = undefined
    overviewState.overviewPending = false
    overviewState.readState = 'ready'
    overviewState.pendingWork = []
    overviewState.refreshOverview.mockReset()
    myScopeState.scope = { kind: 'team', id: 'TEAM-A', displayName: '注塑一班' }
    myScopeState.scopeMessage = ''
    myScopeState.readState = 'ready'
    myScopeState.totals = { queued: 0, inProgress: 0, paused: 0, scheduleInvalidated: 0 }
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

  // 读面取不到数据时，页面绝不能替现场下「没有阻塞 / 可以继续推进」的结论。
  const SAFETY_CLAIMS = ['没有汇总阻塞', '没有阻塞', '没有生产阻塞', '暂无阻塞', '一切正常']

  it('读面失败时不断言现场无阻塞，改为明说取不到并给重试', () => {
    overviewState.readState = 'error'
    overviewState.overviewError = new Error('网关暂时不可用')

    const text = mountPage().text()

    for (const claim of SAFETY_CLAIMS) {
      expect(text).not.toContain(claim)
    }
    expect(text).toContain('现场数据获取失败，无法判断当前是否存在阻塞')
    expect(text).toContain('重试')
    expect(text).toContain('无法判断现场是否存在阻塞')
  })

  it('读面失败时指标显示占位而不是 0', () => {
    overviewState.readState = 'error'
    overviewState.counts = [
      { key: 'work-orders', count: 12 },
      { key: 'operation-tasks', count: 34 },
    ]

    const text = mountPage().text()

    expect(text).toContain('—')
    expect(text).toContain('数据获取失败')
    expect(text).not.toContain('12')
    expect(text).not.toContain('34')
  })

  it('业务上下文未就绪时不渲染 0，也不下任何结论', () => {
    overviewState.readState = 'idle'

    const text = mountPage().text()

    for (const claim of SAFETY_CLAIMS) {
      expect(text).not.toContain(claim)
    }
    expect(text).toContain('尚未选择业务范围')
    expect(text).toContain('—')
  })

  it('确实读到数据且为空时才说没有阻塞', () => {
    overviewState.readState = 'ready'

    const text = mountPage().text()

    expect(text).toContain('本次读取的汇总里没有阻塞')
    expect(text).toContain('进入工单与派工')
  })

  // 走查台账 #50：驾驶舱只有全厂总量，班组长看不到「我这一摊」。
  describe('我的班组维度', () => {
    it('按作业范围给出四个未终态计数，并写明这是谁的范围', () => {
      myScopeState.totals = { queued: 12, inProgress: 3, paused: 5, scheduleInvalidated: 7 }

      const wrapper = mountPage()
      const text = wrapper.text()

      expect(text).toContain('我的班组 · 现在该干什么')
      expect(text).toContain('作业范围：注塑一班（班组）')
      expect(text).toContain('我的范围 · 待开工')
      expect(text).toContain('12')
      expect(text).toContain('我的范围 · 进行中')
      expect(text).toContain('3')
      // 暂停与排程失效此前完全漏在外面（「进行中」的文案还把 paused 一起讲了进去）。
      expect(text).toContain('我的范围 · 已暂停')
      expect(text).toContain('5')
      expect(text).toContain('我的范围 · 排程已失效')
      expect(text).toContain('7')

      const queueLink = wrapper
        .findAll('[data-router-link]')
        .find((link) => link.text().includes('打开我的工序队列'))
      expect(queueLink?.attributes('data-to')).toBe('/mes/operation-tasks')
    })

    // 一格一个后端状态码，不做归并：文案不许替某个状态"代言"另一个状态。
    it('四格逐一对应未终态状态码，终态不混进来', () => {
      const text = mountPage().text()

      expect(text).toContain('已排程、尚未开工')
      expect(text).toContain('已开工、正在做')
      expect(text).toContain('开工后被挂起，等着恢复')
      expect(text).toContain('排程作废，需重新排产才能开工')
      // 旧文案把 paused 归并进「进行中」，这句不许再出现。
      expect(text).not.toContain('等着报工或完工')
    })

    // 全厂总量与「我的范围」并排出现，口径必须自带标注，否则两组数字会被读成同一回事。
    it('全厂那一条明确标注全厂口径，不再叫「在制」', () => {
      const text = mountPage().text()

      expect(text).toContain('全厂工单')
      expect(text).toContain('全厂工序任务')
      expect(text).not.toContain('在制工单')
    })

    it('范围数字没读到时显占位，不拿 0 当结论', () => {
      myScopeState.readState = 'error'
      myScopeState.totals = { queued: 12, inProgress: 3, paused: 5, scheduleInvalidated: 7 }

      const text = mountPage().text()

      expect(text).not.toContain('12')
      expect(text).toContain('—')
    })

    it('作业范围本身还没确定时，把原因说出来而不是空着', () => {
      myScopeState.scope = undefined
      myScopeState.scopeMessage = '你的账号还没有配置作业范围，请联系管理员。'

      expect(mountPage().text()).toContain('你的账号还没有配置作业范围，请联系管理员。')
    })
  })
})
