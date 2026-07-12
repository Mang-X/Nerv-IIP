import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import ProductionReportsPage from './production-reports.vue'

// 报工列表 mock:同页只放三行,故意让"已冲销原单"(B)与其冲销行、"冲销行"(C)与其原单**不在同页**,
// 以验证已冲销判定与互链改为读服务端逐行字段(reversalReportNo / reversedReportNo)后跨分页仍稳定。
const rows = [
  // A 可冲销原单(工单在制、无冲销互链字段)
  {
    productionReportId: 'pr-a',
    reportNo: 'PRPT-000001',
    workOrderId: 'WO-1',
    operationTaskId: 'WO-1-OP-10',
    goodQuantity: 12,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T02:00:00Z',
    workOrderStatus: 'started',
  },
  // B 已冲销原单:reversalReportNo 指向冲销它的负向记录(该记录不在本页),服务端逐行反查得出
  {
    productionReportId: 'pr-b',
    reportNo: 'PRPT-000002',
    workOrderId: 'WO-2',
    operationTaskId: 'WO-2-OP-10',
    goodQuantity: 8,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T01:00:00Z',
    workOrderStatus: 'started',
    reversalReportNo: 'PRPT-000900',
  },
  // C 冲销记录行:reversedReportNo 指向被冲销的原单(该原单不在本页)
  {
    productionReportId: 'pr-c',
    reportNo: 'PRPT-000901',
    workOrderId: 'WO-3',
    operationTaskId: 'WO-3-OP-10',
    goodQuantity: -5,
    scrapQuantity: 0,
    reworkQuantity: 0,
    reportedAtUtc: '2026-07-12T03:00:00Z',
    workOrderStatus: 'started',
    reversedReportNo: 'PRPT-000899',
    reversalReason: '误报工',
  },
]

const routerState = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => routerState }))

const mesState = vi.hoisted(() => ({
  reverseProductionReport: vi.fn(async () => ({ data: { reportNo: 'PRPT-000902' } })),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesProductionReports: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    productionReports: computed(() => rows),
    productionReportsError: shallowRef(undefined),
    productionReportsPending: shallowRef(false),
    productionReportsTotal: computed(() => rows.length),
    refreshProductionReports: vi.fn(),
    reverseProductionReport: mesState.reverseProductionReport,
    reverseProductionReportPending: shallowRef(false),
  }),
}))

// DataTablePro 桩:逐行渲染 cell-reportNo 与 cell-actions 两个插槽,便于断言徽章/互链/操作按钮。
const tableStub = {
  props: ['rows'],
  template: `
    <section data-testid="data-table">
      <div v-for="row in rows" :key="row.productionReportId" data-testid="row">
        <slot name="cell-reportNo" :row="row" />
        <slot name="cell-actions" :row="row" />
      </div>
    </section>
  `,
}

function mountReports(permissionCodes: string[]) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'user-1',
      principalType: 'user',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      loginName: 'operator',
      permissionCodes,
    },
  })

  // NvUI 组件在测试里按其底层 Pro/reka 组件名 stub(Nv* 只是导出别名,VTU 按组件 name 匹配)。
  return mount(ProductionReportsPage, {
    global: {
      plugins: [pinia],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        WorkOrderQuickView: true,
        PageHeader: { template: '<header><slot name="actions" /></header>' },
        DataTablePro: tableStub,
        ButtonPro: {
          props: ['disabled'],
          template: '<button :disabled="disabled"><slot /></button>',
        },
        StatusBadgePro: {
          props: ['label'],
          template: '<span data-testid="badge">{{ label }}</span>',
        },
        TooltipProvider: { template: '<div><slot /></div>' },
        TooltipRoot: { template: '<div><slot /></div>' },
        TooltipTrigger: { template: '<div><slot /></div>' },
        TooltipProContent: { template: '<div><slot /></div>' },
        // 冲销确认框默认关闭(open=false),这些测试不驱动它,故不渲染其内部表单
        AlertDialogPro: { props: ['open'], template: '<section v-if="open"><slot /></section>' },
        Spinner: true,
      },
    },
  })
}

beforeEach(() => {
  mesState.reverseProductionReport.mockClear()
})

describe('production reports page — reversal permission & cross-page interlink', () => {
  it('shows the inline reverse action for a reversible report when the user has reporting write', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // A 行(可冲销):有启用的「冲销」动作按钮(文本恰为"冲销",区别于互链按钮"查看冲销单/冲销自")
    const reverseBtn = rowEls[0].findAll('button').find((b) => b.text().trim() === '冲销')
    expect(reverseBtn).toBeTruthy()
    expect(reverseBtn!.attributes('disabled')).toBeUndefined()
  })

  it('hides reverse and re-report actions for a read-only user but keeps records visible', async () => {
    const wrapper = mountReports(['business.mes.reporting.read'])
    await flushPromises()

    // 记录仍可见(读权限)
    expect(wrapper.text()).toContain('PRPT-000001')
    // 无任何冲销 / 重新报工 写入口(互链按钮"查看冲销单/冲销自"是读信息,不算写动作,故按精确文本判定)
    const buttons = wrapper.findAll('button')
    expect(buttons.some((b) => b.text().trim() === '冲销')).toBe(false)
    expect(buttons.some((b) => b.text().includes('重新报工'))).toBe(false)
  })

  it('marks an already-reversed original (server reversalReportNo) as non-reversible even when its reversal row is on another page', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // B 行(已冲销):徽章「已冲销」+ 互链「查看冲销单 PRPT-000900」+ 冲销按钮禁用
    expect(rowEls[1].text()).toContain('已冲销')
    expect(rowEls[1].text()).toContain('查看冲销单 PRPT-000900')
    const bReverseBtn = rowEls[1].findAll('button').find((b) => b.text().trim() === '冲销')
    expect(bReverseBtn!.attributes('disabled')).toBeDefined()
  })

  it('marks a reversal record row with a negative badge, back-link to its original, and a re-report action', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const rowEls = wrapper.findAll('[data-testid="row"]')
    // C 行(冲销记录):徽章「负向冲销」+ 互链「冲销自 PRPT-000899」+ 「重新报工」而非「冲销」
    expect(rowEls[2].text()).toContain('负向冲销')
    expect(rowEls[2].text()).toContain('冲销自 PRPT-000899')
    expect(rowEls[2].findAll('button').some((b) => b.text().includes('重新报工'))).toBe(true)
  })
})
