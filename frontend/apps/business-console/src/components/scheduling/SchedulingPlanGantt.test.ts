import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { computed, defineComponent, nextTick } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPlanGantt from './SchedulingPlanGantt.vue'

vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({ resolveSkuName: () => undefined }),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveWorkCenter: (id: string) =>
      ({
        'WC-ROD-01': '活塞杆加工中心一线',
        'WC-CNC-02': '缸筒加工中心二线',
        'WC-UNASSIGNED': '未配置归属的工作中心',
      })[id] ?? id,
    resolveWorkCenterCategory: () => undefined,
    workCenterResources: computed(() => [
      {
        code: 'WC-ROD-01',
        displayName: '活塞杆加工中心一线',
        workshopCode: 'WS-01',
        lineCode: 'LINE-01',
      },
      {
        code: 'WC-CNC-02',
        displayName: '缸筒加工中心二线',
        workshopCode: 'WS-02',
        lineCode: 'LINE-02',
      },
      {
        code: 'WC-UNASSIGNED',
        displayName: '未配置归属的工作中心',
      },
    ]),
    workshopResources: computed(() => [
      { code: 'WS-01', displayName: '一车间 · 机加车间' },
      { code: 'WS-02', displayName: '二车间 · 装配车间' },
      { code: 'WS-03', displayName: '三车间 · 表面与包装' },
    ]),
    lineResources: computed(() => [
      { code: 'LINE-01', displayName: '活塞杆一线' },
      { code: 'LINE-02', displayName: '缸筒二线' },
      { code: 'LINE-03', displayName: '精磨线' },
      { code: 'LINE-04', displayName: '包装线' },
    ]),
  }),
}))

/**
 * #1399 M7 —— 「切个页签风险 chip 就消失」。
 *
 * 走查报告把根因记在「读面丢弃 risks」（根因 C）上，实机复核已推翻：网关 facade 的
 * SchedulePlanContract 带 materialRisks / equipmentRisks，codegen 也生成了这两个字段，
 * aps-mapper 会按 orderId+operationId 把风险挂回工序。链路是通的。
 *
 * 但这条链上任何一环断掉都不会报错，只会安静地少两项图例——正是最难发现的那种回归。
 * 所以在这里钉死：**方案带风险时，甘特页签必须把两项风险图例点亮**。
 *
 * 注意判定要成对：只验「有风险时点亮」会漏掉「无风险时也硬列」的反向错误
 * （图例列了但图上一条都没有，同样是假事实）。
 */

const BASE_ASSIGNMENTS = [
  {
    assignmentId: 'a1',
    orderId: 'WO-2026-03008',
    operationId: 'OP-10',
    operationSequence: 10,
    resourceId: 'WC-ROD-01',
    workCenterId: 'WC-ROD-01',
    startUtc: '2026-08-01T00:00:00.000Z',
    endUtc: '2026-08-01T03:00:00.000Z',
    isLocked: false,
  },
  {
    assignmentId: 'a2',
    orderId: 'WO-2026-03008',
    operationId: 'OP-20',
    operationSequence: 20,
    resourceId: 'WC-CNC-02',
    workCenterId: 'WC-CNC-02',
    startUtc: '2026-08-01T03:00:00.000Z',
    endUtc: '2026-08-01T06:00:00.000Z',
    isLocked: false,
  },
]

function plan(patch: Record<string, unknown> = {}) {
  return {
    planId: 'plan-risk-1',
    status: 'generated',
    algorithmVersion: 'heuristic-1',
    generatedAtUtc: '2026-08-01T00:00:00.000Z',
    assignments: BASE_ASSIGNMENTS,
    ...patch,
  }
}

function mountGantt(props: Record<string, unknown>) {
  return mount(SchedulingPlanGantt, {
    props: { workOrders: [], ...props },
    global: { plugins: [createPinia()] },
  })
}

const GROUP_ASSIGNMENTS = [
  {
    assignmentId: 'a1',
    orderId: 'WO-GROUP-001',
    operationId: 'OP-10',
    operationSequence: 10,
    resourceId: 'WC-ROD-01',
    workCenterId: 'WC-ROD-01',
    startUtc: '2026-08-01T00:00:00.000Z',
    endUtc: '2026-08-01T03:00:00.000Z',
    isLocked: false,
  },
  {
    assignmentId: 'a2',
    orderId: 'WO-GROUP-001',
    operationId: 'OP-20',
    operationSequence: 20,
    resourceId: 'WC-CNC-02',
    workCenterId: 'WC-CNC-02',
    startUtc: '2026-08-01T03:00:00.000Z',
    endUtc: '2026-08-01T06:00:00.000Z',
    isLocked: false,
  },
  {
    assignmentId: 'a3',
    orderId: 'WO-GROUP-002',
    operationId: 'OP-30',
    operationSequence: 10,
    resourceId: 'WC-UNASSIGNED',
    workCenterId: 'WC-UNASSIGNED',
    startUtc: '2026-08-01T06:00:00.000Z',
    endUtc: '2026-08-01T08:00:00.000Z',
    isLocked: false,
  },
]

function groupedPlan() {
  return plan({ assignments: GROUP_ASSIGNMENTS })
}

async function settle() {
  await flushPromises()
  await nextTick()
  await flushPromises()
}

async function switchGroup(wrapper: ReturnType<typeof mount>, groupBy: string) {
  wrapper.findComponent({ name: 'SchedulingToolbar' }).vm.$emit('groupChange', groupBy)
  await settle()
}

async function setLaneOrder(wrapper: ReturnType<typeof mount>, laneOrder: string) {
  wrapper.findComponent({ name: 'SchedulingToolbar' }).vm.$emit('laneOrderChange', laneOrder)
  await settle()
}

function laneNames(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('[data-resource-lane] .nv-timeline-label__name').map((lane) => lane.text())
}

function sortedLaneNames(wrapper: ReturnType<typeof mount>) {
  return [...laneNames(wrapper)].sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'))
}

function taskCount(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('[data-resource-lane] [data-task-id]').length
}

describe('SchedulingPlanGantt 风险图例 (#1399 M7)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('方案带物料/设备风险时，两项风险图例都点亮', () => {
    const wrapper = mountGantt({
      plan: plan({
        materialRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-10',
            reasonCodes: ['material.shortage'],
            shortages: [],
            message: '缺 2 项物料',
          },
        ],
        equipmentRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-20',
            resourceId: 'WC-CNC-02',
            reasonCodes: ['equipment.snapshot.missing'],
            message: '设备无状态快照',
          },
        ],
      }),
    })

    const text = wrapper.text()
    expect(text).toContain('缺料待备')
    expect(text).toContain('设备状态未知')
  })

  it('方案没有风险时，这两项图例不出现（不硬列出图上根本没有的语义）', () => {
    const wrapper = mountGantt({ plan: plan({ materialRisks: [], equipmentRisks: [] }) })

    const text = wrapper.text()
    expect(text).not.toContain('缺料待备')
    expect(text).not.toContain('设备状态未知')
  })

  it('风险按 orderId + operationId 挂到具体工序上，挂不上的不点亮', () => {
    // 工序号对不上（OP-99 不在本方案里）：图例不该因为"数组非空"就点亮。
    const wrapper = mountGantt({
      plan: plan({
        materialRisks: [
          {
            orderId: 'WO-2026-03008',
            operationId: 'OP-99',
            reasonCodes: [],
            shortages: [],
            message: '对不上的风险',
          },
        ],
        equipmentRisks: [],
      }),
    })

    expect(wrapper.text()).not.toContain('缺料待备')
  })
})

describe('SchedulingPlanGantt 分组维度', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('按工作中心、车间、产线切换泳道，保留中文名称与未归属兜底', async () => {
    const wrapper = mountGantt({ plan: groupedPlan() })
    await settle()

    expect(wrapper.find('[aria-label="分组维度"]').exists()).toBe(true)
    // 只比集合不比顺序：泳道顺序由「泳道排序」独立决定（默认忙闲降序），
    // 由下面两条排序用例覆盖；混在一起会让这条用例每次改排序都误红。
    expect(sortedLaneNames(wrapper)).toEqual(
      ['活塞杆加工中心一线', '缸筒加工中心二线', '未配置归属的工作中心'].sort((a, b) =>
        a.localeCompare(b, 'zh-Hans-CN'),
      ),
    )

    const workCenterTaskCount = taskCount(wrapper)
    await switchGroup(wrapper, 'workshop')
    expect(sortedLaneNames(wrapper)).toEqual(
      ['一车间 · 机加车间', '二车间 · 装配车间', '三车间 · 表面与包装', '未归属车间（1 项）'].sort(
        (a, b) => a.localeCompare(b, 'zh-Hans-CN'),
      ),
    )
    expect(taskCount(wrapper)).toBe(workCenterTaskCount)

    await switchGroup(wrapper, 'productionLine')
    expect(sortedLaneNames(wrapper)).toEqual(
      ['活塞杆一线', '缸筒二线', '精磨线', '包装线', '未归属产线（1 项）'].sort((a, b) =>
        a.localeCompare(b, 'zh-Hans-CN'),
      ),
    )
    expect(taskCount(wrapper)).toBe(workCenterTaskCount)
  })

  it('切换维度后保留搜索关键词、选中工序和视口定位上下文', async () => {
    const wrapper = mountGantt({ plan: groupedPlan() })
    await settle()

    const search = wrapper.find('input[aria-label="搜索工序"]')
    await search.setValue('OP-20')
    await settle()
    expect(wrapper.find('[data-testid="scheduling-task-detail"]').text()).toContain('OP-20')

    await switchGroup(wrapper, 'workshop')

    expect((search.element as HTMLInputElement).value).toBe('OP-20')
    expect(wrapper.find('[data-testid="scheduling-task-detail"]').text()).toContain('OP-20')
    expect(wrapper.find('[data-task-id="a2"]').exists()).toBe(true)
  })

  it('当前时间窗没有排程的车间和产线仍保留为空泳道', async () => {
    const wrapper = mountGantt({ plan: groupedPlan() })
    await settle()

    await switchGroup(wrapper, 'workshop')
    const emptyWorkshop = wrapper
      .findAll('[data-resource-lane]')
      .find((lane) => lane.text().includes('三车间 · 表面与包装'))
    expect(emptyWorkshop?.findAll('[data-task-id]')).toHaveLength(0)

    await switchGroup(wrapper, 'productionLine')
    const emptyLine = wrapper
      .findAll('[data-resource-lane]')
      .find((lane) => lane.text().includes('包装线'))
    expect(emptyLine?.findAll('[data-task-id]')).toHaveLength(0)
  })

  /**
   * 第五轮走查实测：按产线分组时首屏是两条空产线，往下滚才见到有活的那条——
   * 演示第一眼像坏了。默认改成忙闲降序。
   */
  it('默认按忙闲降序排泳道，有排程的排在空泳道前面', async () => {
    const wrapper = mountGantt({ plan: groupedPlan() })
    await settle()
    await switchGroup(wrapper, 'productionLine')

    const names = laneNames(wrapper)
    const counts = wrapper
      .findAll('[data-resource-lane]')
      .map((lane) => lane.findAll('[data-task-id]').length)
    const firstEmpty = counts.indexOf(0)
    // 第一条空泳道之后不允许再出现有排程的泳道。
    if (firstEmpty >= 0) {
      expect(counts.slice(firstEmpty).every((count) => count === 0)).toBe(true)
    }
    expect(names.length).toBeGreaterThan(0)
  })

  it('「仅有排程」隐藏空泳道，「名称升序」按名字排且空泳道仍在', async () => {
    const wrapper = mountGantt({ plan: groupedPlan() })
    await settle()
    await switchGroup(wrapper, 'productionLine')
    const allLanes = laneNames(wrapper).length

    await setLaneOrder(wrapper, 'onlyScheduled')
    const scheduledCounts = wrapper
      .findAll('[data-resource-lane]')
      .map((lane) => lane.findAll('[data-task-id]').length)
    expect(scheduledCounts.length).toBeGreaterThan(0)
    expect(scheduledCounts.every((count) => count > 0)).toBe(true)
    expect(scheduledCounts.length).toBeLessThan(allLanes)

    // 「仅有排程」只是可选项：切回名称升序，空泳道必须回来——
    // 「这条线这周没活」本身是调度员要看的信息（#1428 的设计意图，不许被悄悄撤销）。
    await setLaneOrder(wrapper, 'name')
    const byName = laneNames(wrapper)
    expect(byName.length).toBe(allLanes)
    expect([...byName].sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'))).toEqual(byName)
  })
})

// DomainInvariant: #3628 侧栏点击经包内命令同时选中、定位甘特。
it('销售单侧栏工序选择同步甘特选中和定位，不离开页面', async () => {
  const command = vi.fn()
  const boardStub = defineComponent({
    setup(_, { expose }) {
      expose({ command })
      return () => null
    },
  })
  const wrapper = mount(SchedulingPlanGantt, {
    props: {
      plan: { ...plan(), status: 'generated' },
      workOrders: [
        {
          workOrderId: 'WO-2026-03008',
          commercialSourceFacts: {
            status: 'available',
            salesOrders: [{ salesOrderNo: 'SO-08001' }],
          },
        },
      ],
    },
    global: { plugins: [createPinia()], stubs: { ResourceSchedulerBoard: boardStub } },
  })
  await settle()
  expect(wrapper.find('[aria-label="销售订单计划视角"]').exists()).toBe(true)
  await wrapper.get('button[data-operation="a2"]').trigger('click')
  expect(command).toHaveBeenCalledWith({ kind: 'selectTask', taskId: 'a2' })
  expect(command).toHaveBeenCalledWith({ kind: 'revealTask', taskId: 'a2' })
  expect(wrapper.get('button[data-operation="a2"]').attributes('aria-pressed')).toBe('true')
  expect(wrapper.get('[data-testid="scheduling-task-detail"]').text()).toContain('OP-20')
})

// #4124 / ADR 0032 §3: server windows are presentation facts, not downtime or a browser freeze set.
describe('SchedulingPlanGantt 服务端稳定窗口', () => {
  it('各中心使用覆盖或默认窗口，精确裁剪到时间轴；零窗口不画，工序仍可选', async () => {
    const wrapper = mountGantt({
      plan: plan({
        assignments: GROUP_ASSIGNMENTS,
        freezeContext: {
          asOfUtc: '2026-08-01T01:00:00.000Z',
          defaultWindowEndUtc: '2026-08-01T04:00:00.000Z',
          workCenterWindows: [
            { workCenterId: 'WC-ROD-01', endUtc: '2026-08-01T02:30:00.000Z' },
            { workCenterId: 'WC-UNASSIGNED', endUtc: '2026-08-01T01:00:00.000Z' },
          ],
          assignments: [],
        },
      }),
    })
    await settle()
    const rod = wrapper.get('[data-resource-lane="WC-ROD-01"] [data-freeze-window]')
    const cnc = wrapper.get('[data-resource-lane="WC-CNC-02"] [data-freeze-window]')
    expect(rod.attributes('data-start-utc')).toBe('2026-08-01T01:00:00.000Z')
    expect(rod.attributes('data-end-utc')).toBe('2026-08-01T02:30:00.000Z')
    expect(cnc.attributes('data-end-utc')).toBe('2026-08-01T04:00:00.000Z')
    expect(parseFloat((cnc.element as HTMLElement).style.width)).toBeCloseTo(
      parseFloat((rod.element as HTMLElement).style.width) * 2,
    )
    expect(wrapper.find('[data-resource-lane="WC-UNASSIGNED"] [data-freeze-window]').exists()).toBe(
      false,
    )
    expect(wrapper.text()).toContain('仍可安排新工序')
    expect(taskCount(wrapper)).toBe(3)
    await wrapper.get('[data-task-id="a1"]').trigger('click')
    expect(wrapper.text()).toContain('工序详情')
    await switchGroup(wrapper, 'workshop')
    expect(wrapper.find('[data-freeze-window]').exists()).toBe(false)
    expect(wrapper.text()).toContain('切换到工作中心查看各中心窗口')
    wrapper.unmount()
  })

  it('旧方案没有冻结信息时不宣称存在稳定窗口', async () => {
    const wrapper = mountGantt({ plan: plan({ freezeContext: null }) })
    await settle()
    expect(wrapper.find('[data-freeze-window]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('仍可安排新工序')
    wrapper.unmount()
  })
})
