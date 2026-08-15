import { computed, nextTick, reactive, shallowRef } from 'vue'
import { config, shallowMount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import CapasPage from './capas.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const TECHNICAL_USER_PATTERN = /user-emp-/i

const state = vi.hoisted(() => ({
  catalogResolved: true,
  capa: {
    correctiveActionId: '019fbb41-1111-7111-8111-111111111111',
    capaCode: 'CAPA-2026-023',
    sourceNcrId: '019fbb41-ff93-7fd8-b1a8-6a65c3a445d2',
    sourceNcrCode: 'NCR-2026-D0042' as string | undefined,
    rootCause: '装配参数漂移',
    containmentAction: '隔离在制品',
    ownerUserId: 'user-emp-041',
    dueAtUtc: '2026-08-10T00:00:00Z',
    status: 'open',
    effectivenessVerifiedByUserId: 'user-emp-042',
    effectivenessResult: '复检通过',
    effectivenessVerifiedAtUtc: '2026-08-01T08:00:00Z',
    closedByUserId: 'user-emp-043',
    closedAtUtc: '2026-08-01T09:00:00Z',
    actionCount: 1,
    completedActionCount: 1,
    overdue: false,
    actions: [
      {
        correctiveActionItemId: '019fbb41-2222-7222-8222-222222222222',
        actionType: 'corrective',
        description: '校准装配参数',
        ownerUserId: 'user-emp-044',
        dueAtUtc: '2026-08-05T00:00:00Z',
        status: 'completed',
        overdue: false,
      },
    ],
  },
}))

vi.mock('@/composables/useBusinessQualityLedgers', () => ({
  capaActionTypeLabel: (value?: string) => value ?? '未知',
  capaStatusLabel: (value?: string) => value ?? '未知',
  capaStatusTone: () => 'neutral',
  useQualityCapas: () => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      status: undefined,
      overdueOnly: undefined,
      keyword: undefined,
      skip: 0,
      take: 20,
    }),
    capas: computed(() => [state.capa]),
    capasError: shallowRef(),
    capasPending: shallowRef(false),
    capasTotal: computed(() => 1),
    capaOpenCount: computed(() => 1),
    capaEffectivenessVerifiedCount: computed(() => 0),
    capaClosedCount: computed(() => 0),
    capaOverdueCount: computed(() => 0),
    refreshCapas: vi.fn(),
  }),
  useQualityCapaDetail: () => ({
    capaDetail: computed(() => state.capa),
    capaDetailError: shallowRef(),
    capaDetailPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useQualityPickerCatalog', () => ({
  useQualityReadFaceCatalog: () => ({
    ncrCodeById: computed(() =>
      state.catalogResolved
        ? new Map([[state.capa.sourceNcrId, state.capa.sourceNcrCode ?? '']])
        : new Map<string, string>(),
    ),
    workerLabelById: computed(() =>
      state.catalogResolved
        ? new Map([
            ['user-emp-041', '张伟 · EMP-041'],
            ['user-emp-042', '李娜 · EMP-042'],
            ['user-emp-043', '王强 · EMP-043'],
            ['user-emp-044', '赵敏 · EMP-044'],
          ])
        : new Map<string, string>(),
    ),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef(20) }),
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: { template: '<button><slot /></button>' },
  NvDataTable: {
    props: ['columns', 'rows'],
    template: `
      <div>
        <div v-for="row in rows" :key="JSON.stringify(row)">
          <span v-for="column in columns" :key="column.key">
            {{ column.accessor ? column.accessor(row) : '' }}
          </span>
        </div>
      </div>
    `,
  },
  DataTable: {
    props: ['columns', 'rows'],
    template: `
      <div>
        <div v-for="row in rows" :key="JSON.stringify(row)">
          <span v-for="column in columns" :key="column.key">
            {{ column.accessor ? column.accessor(row) : '' }}
          </span>
        </div>
      </div>
    `,
  },
  NvMetricCard: { props: ['label', 'value'], template: '<div>{{ label }} {{ value }}</div>' },
  NvPageHeader: {
    props: ['title'],
    template: '<header>{{ title }}<slot name="actions" /></header>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<div><slot /></div>' },
  NvSelectValue: { template: '<span />' },
  NvSheet: { template: '<section><slot /></section>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  Spinner: { template: '<span />' },
}

describe('quality CAPA read-face guard', () => {
  beforeEach(() => {
    config.global.renderStubDefaultSlot = true
    state.catalogResolved = true
    state.capa.sourceNcrCode = 'NCR-2026-D0042'
    vi.clearAllMocks()
  })

  it('never exposes UUIDs or technical employee user IDs in the list and detail sheet', async () => {
    const wrapper = shallowMount(CapasPage, { global: { stubs } })
    ;(wrapper.vm as unknown as { openCapa: (row: typeof state.capa) => void }).openCapa(state.capa)
    await nextTick()

    const visibleText = wrapper.text()
    expect(visibleText).toContain('NCR-2026-D0042')
    expect(visibleText).toContain('张伟 · EMP-041')
    expect(visibleText).toContain('李娜 · EMP-042')
    expect(visibleText).toContain('王强 · EMP-043')
    expect(visibleText).toContain('赵敏 · EMP-044')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('shows neutral placeholders instead of raw identifiers when catalogs cannot resolve them', async () => {
    state.catalogResolved = false
    state.capa.sourceNcrCode = undefined
    const wrapper = shallowMount(CapasPage, { global: { stubs } })
    ;(wrapper.vm as unknown as { openCapa: (row: typeof state.capa) => void }).openCapa(state.capa)
    await nextTick()

    const visibleText = wrapper.text()
    expect(visibleText).toContain('未指派')
    expect(visibleText).toContain('—')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })
})
