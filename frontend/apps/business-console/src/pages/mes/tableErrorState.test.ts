import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, type Component } from 'vue'

import CapacityPage from './capacity.vue'
import DowntimePage from './downtime.vue'
import FoundationPage from './foundation.vue'
import HandoversPage from './handovers.vue'
import MaterialsPage from './materials.vue'
import OperationTasksPage from './operation-tasks.vue'
import PlansPage from './plans.vue'
import ProductionReportsPage from './production-reports.vue'
import QualityPage from './quality.vue'
import ReceiptsPage from './receipts.vue'
import TraceabilityPage from './traceability.vue'
import WipPage from './wip.vue'
import WorkOrderDetailPage from './work-orders/[workOrderId].vue'
import WorkOrdersPage from './work-orders/index.vue'

/**
 * MES 列表页读面失败时必须落到 `NvDataTable` 的**错误态**，而不是「暂无…」空态（#2854）。
 *
 * 夹具口径：只把每个读面 composable 的 `*Error` 换成一个已失败的错误，其余字段（rows/total/
 * filters/写操作）保持真实实现——这样断言检验的是页面模板把错误接到了表格上，而不是测试
 * 自己搭的一套假页面。
 */

const readFailure = vi.hoisted(() => new Error('mes-read-face-unavailable'))

// 页面读面 composable → 失败字段。真实 hook 先跑，再覆写这些字段。
// 一个 hook 供多张表时（工单详情）列出全部错误字段，逐表各自落错误态。
const overrides = vi.hoisted(() => ({
  useMesCapacityImpacts: ['capacityImpactsError'],
  useMesDowntimeEvents: ['downtimeEventsError'],
  useMesFinishedGoodsReceipts: ['receiptRequestsError'],
  useMesFoundationReadiness: ['readinessError'],
  useMesMaterialIssueRequests: ['materialIssueRequestsError'],
  useMesOperationTasks: ['operationTasksError'],
  useMesProductionPlans: ['productionPlansError'],
  useMesProductionReports: ['productionReportsError'],
  useMesRelatedQualityItems: ['qualityItemsError'],
  useMesShiftHandovers: ['handoversError'],
  useMesTraceability: ['traceabilityError'],
  useMesWipSummary: ['wipError'],
  useMesWorkOrders: ['workOrdersError'],
  useMesWorkOrderDetail: ['detailError', 'materialReadinessError', 'materialIssueRequestsError'],
}))

/**
 * 错误态「重新加载」按钮的 handler：按钮无论有没有绑 `@retry` 都恒渲染，只断言按钮在场
 * 抓不到「控件在、行为不在」。这里把本 PR 新接的 4 张表的 refresh 换成间谍，点按钮验行为。
 */
const retryHandlers = vi.hoisted(
  () =>
    ({
      useMesFoundationReadiness: ['refreshReadiness'],
      useMesWorkOrderDetail: [
        'refreshDetail',
        'refreshMaterialReadiness',
        'refreshMaterialIssueRequests',
      ],
    }) as Record<string, string[]>,
)

const retrySpies = vi.hoisted(() =>
  Object.fromEntries(
    Object.values(retryHandlers)
      .flat()
      .map((key) => [key, vi.fn()]),
  ),
)

vi.mock('@/composables/useBusinessMes', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/composables/useBusinessMes')>()
  const patched: Record<string, unknown> = { ...actual }
  for (const [hook, errorKeys] of Object.entries(overrides)) {
    const original = actual[hook as keyof typeof actual] as (...args: unknown[]) => object
    const refreshKeys = retryHandlers[hook] ?? []
    patched[hook] = (...args: unknown[]) => ({
      ...original(...args),
      ...Object.fromEntries(errorKeys.map((key) => [key, computed(() => readFailure)])),
      ...Object.fromEntries(refreshKeys.map((key) => [key, retrySpies[key]])),
    })
  }
  return patched
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ query: {}, params: {}, path: '/mes', fullPath: '/mes', name: 'mes' }),
    useRouter: () => ({
      push: vi.fn(),
      replace: vi.fn(),
      resolve: () => ({ href: '#' }),
    }),
    onBeforeRouteLeave: () => {},
    RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  }
})

async function mountPage(page: Component) {
  const pinia = createPinia()
  const wrapper = mount(page, {
    global: {
      plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 0 } }]],
      // 只桩掉外壳（侧边导航等与本断言无关、但每次挂载都要整棵渲染的部分）。
      // 表格与页面自身一律用真组件，错误态必须由 `NvDataTable` 真的渲染出来。
      stubs: { BusinessLayout: { template: '<main><slot /></main>' } },
    },
  })
  await flushPromises()
  return wrapper
}

/**
 * 页面模块 → 读面失败时**绝不允许出现**的文案片段：表格空态逐表各给一条；同一读面喂养的
 * 卡片结论若会把「读失败」说成别的状态（如「尚未检查」），一并列在这里。
 */
const pages: Array<{
  name: string
  page: Component
  absentText: string[]
}> = [
  {
    name: '产能影响',
    page: CapacityPage,
    absentText: ['暂无产能影响'],
  },
  {
    name: '设备与停机',
    page: DowntimePage,
    absentText: ['暂无停机事件'],
  },
  {
    name: '生产准备检查',
    page: FoundationPage,
    // 后两条是就绪卡：读面挂了不等于「还没查」，卡片不许给出这个假状态（#2946）。
    absentText: ['暂无检查结果', '尚未检查', '填写上方范围后点'],
  },
  {
    name: '班次交接',
    page: HandoversPage,
    absentText: ['暂无班次交接'],
  },
  {
    name: '领料跟踪',
    page: MaterialsPage,
    absentText: ['暂无领料申请'],
  },
  {
    name: '工序执行',
    page: OperationTasksPage,
    absentText: ['当前没有工序任务'],
  },
  {
    name: '生产计划',
    page: PlansPage,
    absentText: ['还没有可执行的生产计划'],
  },
  {
    name: '报工记录',
    page: ProductionReportsPage,
    absentText: ['还没有报工记录'],
  },
  {
    name: '质量记录',
    page: QualityPage,
    absentText: ['暂无质量或不良记录'],
  },
  {
    name: '完工入库',
    page: ReceiptsPage,
    absentText: ['还没有完工入库登记'],
  },
  {
    name: '批次追溯',
    page: TraceabilityPage,
    absentText: ['暂无追溯数据'],
  },
  {
    name: '在制跟踪',
    page: WipPage,
    absentText: ['暂无在制数据'],
  },
  {
    name: '生产工单',
    page: WorkOrdersPage,
    absentText: ['当前筛选下没有工单'],
  },
  {
    // 工单详情三张子表读三个面，三条空态文案互不相同：任一条绑定被摘掉，
    // 对应那句空态就会重新出现，断言随即变红。
    // 末条是用料齐套卡：页面级错误条已删，卡片不许再把用户指向「上方」那个不存在的控件。
    name: '工单详情',
    page: WorkOrderDetailPage,
    absentText: ['暂无工序任务', '暂无用料行', '本工单还没有领料单', '先解决上方读取阻塞'],
  },
]

describe('MES 列表页读面失败时落到表格错误态（#2854）', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new Error('测试夹具：不发真实请求'))),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  for (const { name, page, absentText } of pages) {
    it(`${name}：显示错误态与重试入口，不显示空态或「未发起」文案`, async () => {
      const wrapper = await mountPage(page)
      const text = wrapper.text()

      expect(text).toContain('数据加载失败')
      expect(text).toContain('重新加载')
      // 分层透传（MAN-691 / #1259）：错误原文只进 console，上屏的必须是映射后的中文。
      // 摘掉 `:error-message` 时 NvDataTable 回落 `error.message`，这条随即变红。
      expect(text).not.toContain(readFailure.message)
      for (const absent of absentText) {
        expect(text).not.toContain(absent)
      }
    })
  }

  // 本 PR 新接的 4 张表：按钮点得动才算接上（`@retry` 丢了按钮照样渲染）。
  const retryPages: Array<{ name: string; page: Component; handlers: string[] }> = [
    {
      name: '生产准备检查',
      page: FoundationPage,
      handlers: retryHandlers.useMesFoundationReadiness,
    },
    { name: '工单详情', page: WorkOrderDetailPage, handlers: retryHandlers.useMesWorkOrderDetail },
  ]

  for (const { name, page, handlers } of retryPages) {
    it(`${name}：错误态点「重新加载」真的触发重试`, async () => {
      const wrapper = await mountPage(page)
      for (const key of handlers) retrySpies[key].mockClear()

      const retryButtons = wrapper
        .findAll('button')
        .filter((button) => button.text().includes('重新加载'))
      // 这行不只是防手滑：`handlers` 与间谍派生自同一份 `retryHandlers`，名单漏一个名字
      // 时两边一起漏、点击与断言都少一张表，只有「页面实渲染的按钮数」还站在名单外面。
      // 删掉它 + 名单缩水 = 16 条全绿而实际只检验了两张表（实测存活）。别当冗余删。
      expect(retryButtons).toHaveLength(handlers.length)
      for (const button of retryButtons) await button.trigger('click')
      await flushPromises()

      for (const key of handlers) expect(retrySpies[key]).toHaveBeenCalled()
    })
  }
})
