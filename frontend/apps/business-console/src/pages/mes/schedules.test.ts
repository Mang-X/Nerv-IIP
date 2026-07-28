import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import SchedulesPage from './schedules.vue'

const historyRow = {
  scheduleVersion: 43,
  trigger: 'RushOrder',
  scheduledAtUtc: '2026-07-04T08:00:00Z',
  assignmentCount: 1,
  affectedWorkOrderCount: 1,
  affectedWorkOrderIds: ['WO-002'],
  assignments: [
    {
      workOrderId: 'WO-002',
      operationTaskId: 'OP-20',
      workCenterId: 'WC-02',
      startUtc: '2026-07-04T09:00:00Z',
      endUtc: '2026-07-04T10:00:00Z',
      reason: '急件插单重排',
    },
  ],
}

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

// 排程页用它把工作中心标识解析成名称；名录不是本用例被测对象，给稳定桩。
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (v?: string | null) => v ?? undefined,
    resolveSkuLabel: (v?: string | null) => v ?? '未指定物料',
    resolveWorkCenter: (v?: string | null) => v ?? undefined,
    resolveShiftLabel: (v?: string | null) => v ?? '未排班',
    resolveWorker: () => undefined,
  }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesSchedules: () => ({
    filters: { organizationId: 'org', environmentId: 'dev', skip: 0, take: 20 },
    lastSchedule: computed(() => ({
      scheduleVersion: 42,
      trigger: 'Manual',
      scheduledAtUtc: '2026-07-03T08:00:00Z',
      assignments: [
        {
          workOrderId: 'WO-001',
          operationTaskId: 'OP-10',
          workCenterId: 'WC-01',
          startUtc: '2026-07-03T09:00:00Z',
          endUtc: '2026-07-03T10:00:00Z',
          reason: '手动触发',
        },
      ],
      affectedWorkOrderIds: ['WO-001'],
    })),
    scheduleHistory: computed(() => [historyRow]),
    scheduleHistoryTotal: computed(() => 72),
    scheduleHistoryError: ref(undefined),
    scheduleHistoryPending: ref(false),
    refreshScheduleHistory: vi.fn(),
    runSchedule: vi.fn(),
    runScheduleError: ref(undefined),
    runSchedulePending: ref(false),
  }),
}))

vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => ({
    organizationId: 'org',
    environmentId: 'dev',
  }),
}))

const stubs = {
  BusinessLayout: {
    template: '<main><slot /></main>',
  },
  NvButton: {
    template: '<button v-bind="$attrs"><slot /></button>',
  },
  NvDataTable: {
    props: ['rows', 'columns', 'emptyMessage'],
    template:
      '<section>{{ emptyMessage }}<div v-for="(row, index) in rows" :key="index">{{ row.workOrderId }} {{ row.workCenterId }} {{ row.scheduleVersion }} {{ row.trigger }}</div></section>',
  },
  NvDialog: {
    props: ['open'],
    template: '<div><slot /></div>',
  },
  NvDialogContent: {
    template: '<div><slot /></div>',
  },
  NvDialogDescription: {
    template: '<p><slot /></p>',
  },
  NvDialogFooter: {
    template: '<div><slot /></div>',
  },
  NvDialogHeader: {
    template: '<div><slot /></div>',
  },
  NvDialogTitle: {
    template: '<h2><slot /></h2>',
  },
  NvField: {
    template: '<div><slot /></div>',
  },
  NvFieldGroup: {
    template: '<div><slot /></div>',
  },
  NvFieldLabel: {
    template: '<label><slot /></label>',
  },
  PageHeader: {
    props: ['title', 'breadcrumbs', 'count'],
    template: '<header><h1>{{ title }}</h1><p>{{ count }}</p><slot name="actions" /></header>',
  },
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="typeof to === \'string\' ? to : to.path"><slot /></a>',
  },
  SectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  SectionCards: {
    template: '<div><slot /></div>',
  },
  NvSelect: {
    template: '<div><slot /></div>',
  },
  NvSelectContent: {
    template: '<div><slot /></div>',
  },
  NvSelectItem: {
    props: ['value'],
    template: '<div><slot /></div>',
  },
  NvSelectTrigger: {
    template: '<button><slot /></button>',
  },
  // NvSelectValue resolves to the underlying reka SelectValue component name in VTU.
  SelectValue: {
    template: '<span />',
  },
  Spinner: true,
  NvStatusBadge: {
    props: ['label'],
    template: '<span>{{ label }}</span>',
  },
}

describe('MES rule scheduling page IA copy', () => {
  it('uses business-facing source copy and links to the scheduling workbench', () => {
    const wrapper = mount(SchedulesPage, { global: { stubs } })
    const text = wrapper.text()

    expect(text).toContain('规则排程')
    expect(text).toContain('排产工作台')
    expect(text).not.toContain('过渡')
    expect(text).not.toContain('正式 APS')

    // 历史排程结果来自服务端读面，不再只有「本次会话刚跑的那一次」。
    expect(text).toContain('历史排程运行')
    expect(text).toContain('共 72 次')
    expect(text).toContain('WO-002')

    const schedulingLink = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.attributes('data-to') === '/scheduling')

    expect(schedulingLink).toBeDefined()
    expect(schedulingLink!.text()).toContain('排产工作台')
  })
})
