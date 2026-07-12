import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import ProductionReportsPage from './production-reports.vue'

// 报工列表 mock:同页只放三行,故意让"已冲销原单"(B)与其冲销行、"冲销行"(C)与其原单**不在同页**,
// 以验证已冲销判定/互链读服务端逐行字段(reversalReportNo / reversedReportNo)与跨页点击定位。
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

// 按对方报工单号 keyword 过滤时,服务端应返回对方那一行(模拟跨页定位后端返回)。
const counterpartRows: Record<string, (typeof rows)[number][]> = {
  // B(PRPT-000002)的冲销负向记录,平时不在本页
  'PRPT-000900': [
    {
      productionReportId: 'pr-rev-b',
      reportNo: 'PRPT-000900',
      workOrderId: 'WO-2',
      operationTaskId: 'WO-2-OP-10',
      goodQuantity: -8,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-07-12T01:30:00Z',
      workOrderStatus: 'started',
      reversedReportNo: 'PRPT-000002',
      reversalReason: '误报工',
    },
  ],
  // C(PRPT-000901)冲销的原单,平时不在本页
  'PRPT-000899': [
    {
      productionReportId: 'pr-orig-c',
      reportNo: 'PRPT-000899',
      workOrderId: 'WO-3',
      operationTaskId: 'WO-3-OP-10',
      goodQuantity: 5,
      scrapQuantity: 0,
      reworkQuantity: 0,
      reportedAtUtc: '2026-07-12T02:30:00Z',
      workOrderStatus: 'started',
      reversalReportNo: 'PRPT-000901',
    },
  ],
}

// filters / pending 在 mock 工厂里用真实 reactive/shallowRef 创建,供测试可控与断言。
const mesState = vi.hoisted(() => ({
  reverseProductionReport: vi.fn(
    async (
      _reportNo: string,
      _body: { reason: string; reversedAtUtc?: string; idempotencyKey?: string },
    ) => ({
      data: { reportNo: 'PRPT-000902' },
    }),
  ),
  filters: undefined as unknown as Record<string, string | number>,
  pending: undefined as unknown as { value: boolean },
}))

const routerState = vi.hoisted(() => ({ push: vi.fn() }))
vi.mock('vue-router', () => ({ useRouter: () => routerState }))

vi.mock('@/composables/useBusinessMes', async () => {
  const { reactive, shallowRef, computed } = await import('vue')
  mesState.filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: 100,
    keyword: '',
  })
  mesState.pending = shallowRef(false)
  return {
    useMesProductionReports: () => ({
      filters: mesState.filters,
      // 按 keyword 过滤时返回对方那一行(模拟服务端跨页返回),否则返回本页三行——让跨页定位的
      // watch(flush:'post')→ 滚动/高亮后半条链在测试里真实触发。
      productionReports: computed(() => {
        const keyword = String(mesState.filters.keyword ?? '')
        return keyword && counterpartRows[keyword] ? counterpartRows[keyword] : rows
      }),
      productionReportsError: shallowRef(undefined),
      productionReportsPending: shallowRef(false),
      productionReportsTotal: computed(() => rows.length),
      refreshProductionReports: vi.fn(),
      reverseProductionReport: mesState.reverseProductionReport,
      reverseProductionReportPending: mesState.pending,
    }),
    // main 上并行合入的遥测报工候选队列(同页):这些冲销相关用例不驱动它,给个空队列桩即可。
    useMesTelemetryProductionReportCandidates: () => ({
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'pending-confirmation',
        skip: 0,
        take: 100,
      }),
      candidates: computed(() => []),
      total: computed(() => 0),
      pending: shallowRef(false),
      error: shallowRef(undefined),
      refresh: vi.fn(),
      promote: vi.fn(async () => undefined),
      dismiss: vi.fn(async () => undefined),
      actionPending: computed(() => false),
    }),
  }
})

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
  // attachTo document.body:页面跨页定位用 document.querySelector 找 [data-report-no] 行,游离 DOM 查不到。
  return mount(ProductionReportsPage, {
    attachTo: document.body,
    global: {
      plugins: [pinia],
      stubs: {
        // NvUI 组件按其底层组件名 stub。#787 已把 pro/ 重命名为 pc/ 且组件文件 XxxPro.vue → NvXxx.vue,
        // 故 __name 现在就是 Nv* 名;Tooltip 的 Root/Trigger/Provider 仍是 reka 名(TooltipRoot 等)。
        BusinessLayout: { template: '<main><slot /></main>' },
        WorkOrderQuickView: true,
        PageHeader: { template: '<header><slot name="actions" /></header>' },
        NvDataTable: tableStub,
        NvButton: {
          props: ['disabled'],
          template: '<button :disabled="disabled"><slot /></button>',
        },
        NvStatusBadge: {
          props: ['label'],
          template: '<span data-testid="badge">{{ label }}</span>',
        },
        TooltipProvider: { template: '<div><slot /></div>' },
        TooltipRoot: { template: '<div><slot /></div>' },
        TooltipTrigger: { template: '<div><slot /></div>' },
        NvTooltipContent: { template: '<div><slot /></div>' },
        // 冲销确认框内容不渲染(否则 reka Dialog 内层需 DialogRoot context);key 复用/关闭路径测试
        // 直接驱动 setup 方法(openReverse/submitReverse/onReverseOpenChange),不依赖弹窗 DOM。
        NvAlertDialog: { props: ['open'], template: '<div />' },
        // main 合入的候选队列 section 在主模板直接用 Select/Input,需 stub 避免缺 context 破坏整页渲染
        NvSelect: { template: '<div><slot /></div>' },
        NvSelectTrigger: { template: '<div><slot /></div>' },
        NvSelectContent: { template: '<div><slot /></div>' },
        NvSelectItem: { template: '<div><slot /></div>' },
        SelectValue: { template: '<span />' },
        NvInput: { template: '<input />' },
        Spinner: true,
      },
    },
  })
}

beforeEach(() => {
  mesState.reverseProductionReport.mockReset()
  mesState.reverseProductionReport.mockResolvedValue({ data: { reportNo: 'PRPT-000902' } })
  if (mesState.filters) {
    mesState.filters.keyword = ''
    mesState.filters.skip = 0
  }
  if (mesState.pending) mesState.pending.value = false
  // jsdom 未实现 scrollIntoView,定义为可断言的 mock(跨页定位滚动)
  Element.prototype.scrollIntoView = vi.fn()
  // attachTo 会把上一个 wrapper 的 DOM 留在 body,清掉避免跨用例串扰
  document.body.innerHTML = ''
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

  it('locates a cross-page reversal from the original: filters, resets page, scrolls and highlights the arriving row', async () => {
    mesState.filters.skip = 30 // 处于非首页,验证定位会回到第一页
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    // B 行的互链对方 PRPT-000900 不在本页;点击「查看冲销单」按其单号过滤 + 回首页
    const link = wrapper
      .findAll('[data-testid="row"]')[1]
      .findAll('button')
      .find((b) => b.text().includes('查看冲销单'))!
    await link.trigger('click')

    expect(mesState.filters.keyword).toBe('PRPT-000900')
    expect(mesState.filters.skip).toBe(0)

    // keyword 变更后 mock 返回对方那一行 → watch → nextTick → 滚动 + 高亮
    await flushPromises()
    const arrivedRow = wrapper
      .findAll('[data-testid="row"]')
      .find((r) => r.text().includes('PRPT-000900'))!
    expect(arrivedRow).toBeTruthy()
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
    // 高亮:定位到的行报工单单元格带 ring 类
    expect(arrivedRow.html()).toContain('ring-brand')
  })

  it('locates a cross-page original from the reversal row (reverse direction) then clears the locate filter', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    // C 行(冲销记录)的互链对方原单 PRPT-000899 不在本页;点击「冲销自」反向定位
    const backLink = wrapper
      .findAll('[data-testid="row"]')[2]
      .findAll('button')
      .find((b) => b.text().includes('冲销自'))!
    await backLink.trigger('click')
    await flushPromises()

    expect(mesState.filters.keyword).toBe('PRPT-000899')
    expect(Element.prototype.scrollIntoView).toHaveBeenCalled()
    expect(wrapper.text()).toContain('PRPT-000899')

    // 清除筛选:keyword 清空 + 原三行列表恢复 + 定位高亮清除
    const clearBtn = wrapper.findAll('button').find((b) => b.text().includes('清除筛选'))!
    await clearBtn.trigger('click')
    await flushPromises()
    expect(mesState.filters.keyword).toBe('')
    const restored = wrapper.findAll('[data-testid="row"]')
    expect(restored).toHaveLength(3)
    expect(wrapper.text()).toContain('PRPT-000001')
    expect(wrapper.html()).not.toContain('ring-brand')
  })

  it('freezes both idempotencyKey and reversedAtUtc across close (e.g. Escape) and reopen for the same report until success', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      submitReverse: () => Promise<void>
      reverseForm: { reasonCode: string; remark: string }
    }

    // 第一次:打开 A、选原因、提交失败(服务端未知结果)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // 经 Escape / 组件自身 close 关闭(绕过「返回」),再对同一报工重开、重试
    vm.onReverseOpenChange(false)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]

    // key 与 reversedAtUtc 都必须跨重试冻结:否则后端 fingerprint(含 ReversedAtUtc)变 → 幂等冲突而非重放
    expect(first?.idempotencyKey).toBeTruthy()
    expect(first?.reversedAtUtc).toBeTruthy()
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
    expect(second?.reversedAtUtc).toBe(first?.reversedAtUtc)
  })

  it('keeps the first reason on retry even if the user changes it after reopening (fingerprint also covers reason)', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      submitReverse: () => Promise<void>
      reverseReasonLocked: boolean
      reverseForm: { reasonCode: string; remark: string }
    }

    // 首次:原因「误报工」提交失败(结果未知)
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // Escape 关闭后重开:原因已锁定,即便用户试图改成「重复报工」,提交仍沿用首次冻结的原因
    vm.onReverseOpenChange(false)
    vm.openReverse(rows[0])
    expect(vm.reverseReasonLocked).toBe(true)
    expect(vm.reverseForm.reasonCode).toBe('mis-report') // 重开恢复首次原因
    vm.reverseForm.reasonCode = 'duplicate' // 试图更改(界面已禁用,此处直接改绕过)
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]

    // reason / key / reversedAtUtc 三者都跨重试稳定 → 后端 fingerprint 稳定,命中幂等重放
    expect(first?.reason).toBe('误报工')
    expect(second?.reason).toBe(first?.reason)
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
    expect(second?.reversedAtUtc).toBe(first?.reversedAtUtc)
  })

  it('keeps a submitted-but-unknown intent locked after 返回 (cancel) then reopen — 返回 does not discard it', async () => {
    mesState.reverseProductionReport.mockRejectedValue(new Error('timeout'))
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      cancelReverse: () => void
      submitReverse: () => Promise<void>
      reverseReasonLocked: boolean
      reverseForm: { reasonCode: string; remark: string }
    }

    // 首次:原因「误报工」提交失败(结果未知)→ reason 已冻结
    vm.openReverse(rows[0])
    vm.reverseForm.reasonCode = 'mis-report'
    await vm.submitReverse()
    const first = mesState.reverseProductionReport.mock.calls[0]?.[1]

    // 点「返回」:已提交(结果未知)的意图有意保留,不丢弃
    vm.cancelReverse()
    // 重开同一报工:仍锁定、仍恢复原原因,不能借「返回」换原因
    vm.openReverse(rows[0])
    expect(vm.reverseReasonLocked).toBe(true)
    expect(vm.reverseForm.reasonCode).toBe('mis-report')
    vm.reverseForm.reasonCode = 'duplicate' // 试图更改
    await vm.submitReverse()
    const second = mesState.reverseProductionReport.mock.calls[1]?.[1]
    expect(second?.reason).toBe(first?.reason)
    expect(second?.idempotencyKey).toBe(first?.idempotencyKey)
  })

  it('blocks closing the reverse dialog while the request is pending', async () => {
    const wrapper = mountReports(['business.mes.reporting.read', 'business.mes.reporting.write'])
    await flushPromises()

    const vm = wrapper.vm as unknown as {
      openReverse: (row: (typeof rows)[number]) => void
      onReverseOpenChange: (next: boolean) => void
      reverseOpen: boolean
    }

    vm.openReverse(rows[0])
    expect(vm.reverseOpen).toBe(true)
    // 请求进行中:Escape / 组件自身 close 触发的 update:open=false 被拦下,弹窗保持打开
    mesState.pending.value = true
    vm.onReverseOpenChange(false)
    expect(vm.reverseOpen).toBe(true)
    // 请求结束后允许关闭
    mesState.pending.value = false
    vm.onReverseOpenChange(false)
    expect(vm.reverseOpen).toBe(false)
  })
})
