import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, type Component } from 'vue'

import CapacityPage from './capacity.vue'
import DowntimePage from './downtime.vue'
import HandoversPage from './handovers.vue'
import MaterialsPage from './materials.vue'
import OperationTasksPage from './operation-tasks.vue'
import PlansPage from './plans.vue'
import ProductionReportsPage from './production-reports.vue'
import QualityPage from './quality.vue'
import ReceiptsPage from './receipts.vue'
import TraceabilityPage from './traceability.vue'
import WipPage from './wip.vue'
import WorkOrdersPage from './work-orders/index.vue'

/**
 * MES 列表页读面失败时必须落到 `NvDataTable` 的**错误态**，而不是「暂无…」空态（#2854）。
 *
 * 夹具口径：只把每个读面 composable 的 `*Error` 换成一个已失败的错误，其余字段（rows/total/
 * filters/写操作）保持真实实现——这样断言检验的是页面模板把错误接到了表格上，而不是测试
 * 自己搭的一套假页面。
 */

const readFailure = vi.hoisted(() => new Error('mes-read-face-unavailable'))

// 页面读面 composable → 失败字段。真实 hook 先跑，再覆写这一个字段。
const overrides = vi.hoisted(
  () =>
    ({
      useMesCapacityImpacts: 'capacityImpactsError',
      useMesDowntimeEvents: 'downtimeEventsError',
      useMesFinishedGoodsReceipts: 'receiptRequestsError',
      useMesMaterialIssueRequests: 'materialIssueRequestsError',
      useMesOperationTasks: 'operationTasksError',
      useMesProductionPlans: 'productionPlansError',
      useMesProductionReports: 'productionReportsError',
      useMesRelatedQualityItems: 'qualityItemsError',
      useMesShiftHandovers: 'handoversError',
      useMesTraceability: 'traceabilityError',
      useMesWipSummary: 'wipError',
      useMesWorkOrders: 'workOrdersError',
    }) as const,
)

vi.mock('@/composables/useBusinessMes', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/composables/useBusinessMes')>()
  const patched: Record<string, unknown> = { ...actual }
  for (const [hook, errorKey] of Object.entries(overrides)) {
    const original = actual[hook as keyof typeof actual] as (...args: unknown[]) => object
    patched[hook] = (...args: unknown[]) => ({
      ...original(...args),
      [errorKey]: computed(() => readFailure),
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

/** 页面模块 → 该页表格的空态文案片段（错误态下绝不允许出现）。 */
const pages: Array<{
  name: string
  page: Component
  emptyText: string
}> = [
  {
    name: '产能影响',
    page: CapacityPage,
    emptyText: '暂无产能影响',
  },
  {
    name: '设备与停机',
    page: DowntimePage,
    emptyText: '暂无停机事件',
  },
  {
    name: '班次交接',
    page: HandoversPage,
    emptyText: '暂无班次交接',
  },
  {
    name: '领料跟踪',
    page: MaterialsPage,
    emptyText: '暂无领料申请',
  },
  {
    name: '工序执行',
    page: OperationTasksPage,
    emptyText: '当前没有工序任务',
  },
  {
    name: '生产计划',
    page: PlansPage,
    emptyText: '还没有可执行的生产计划',
  },
  {
    name: '报工记录',
    page: ProductionReportsPage,
    emptyText: '还没有报工记录',
  },
  {
    name: '质量记录',
    page: QualityPage,
    emptyText: '暂无质量或不良记录',
  },
  {
    name: '完工入库',
    page: ReceiptsPage,
    emptyText: '还没有完工入库登记',
  },
  {
    name: '批次追溯',
    page: TraceabilityPage,
    emptyText: '暂无追溯数据',
  },
  {
    name: '在制跟踪',
    page: WipPage,
    emptyText: '暂无在制数据',
  },
  {
    name: '生产工单',
    page: WorkOrdersPage,
    emptyText: '当前筛选下没有工单',
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

  for (const { name, page, emptyText } of pages) {
    it(`${name}：显示错误态与重试入口，不显示空态文案`, async () => {
      const wrapper = await mountPage(page)
      const text = wrapper.text()

      expect(text).toContain('数据加载失败')
      expect(text).toContain('重新加载')
      expect(text).not.toContain(emptyText)
    })
  }
})
