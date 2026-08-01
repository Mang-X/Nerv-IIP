import { computed, reactive, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import HandoversPage from './handovers.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const TECHNICAL_USER_PATTERN = /user-emp-/i

const state = vi.hoisted(() => ({
  catalogResolved: true,
  row: {
    handoverId: '019fbb41-1111-7111-8111-111111111111',
    shiftId: '019fbb41-2222-7222-8222-222222222222',
    teamId: '019fbb41-3333-7333-8333-333333333333',
    teamName: '总装早班一组' as string | undefined,
    handoverStatus: 'open',
    openIssueCount: 1,
    createdAtUtc: '2026-08-01T08:00:00Z',
  },
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesShiftHandovers: () => ({
    filters: reactive({ status: undefined, keyword: '', skip: 0, take: 20 }),
    handovers: computed(() => [state.row]),
    handoversError: ref(),
    handoversPending: ref(false),
    handoversTotal: ref(1),
    refreshHandovers: vi.fn(),
  }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveShiftLabel: () => (state.catalogResolved ? '早班' : '未排班'),
  }),
}))

vi.mock('@/composables/useMasterDataDisplayNames', () => ({
  useMasterDataDisplayNames: () => ({
    resolveTeam: () => (state.catalogResolved ? '总装早班一组' : undefined),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref(20) }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: { template: '<button><slot /></button>' },
  NvDataTable: {
    props: ['columns', 'rows'],
    template: `
      <section>
        <div v-for="row in rows" :key="row.handoverId">
          <span v-for="column in columns" :key="column.key">
            {{ column.accessor ? column.accessor(row) : '' }}
          </span>
          <slot name="cell-handoverStatus" :row="row" />
          <slot name="cell-openIssueCount" :row="row" />
          <slot name="cell-createdAtUtc" :row="row" />
        </div>
      </section>
    `,
  },
  NvInput: { template: '<input />' },
  NvMetricCard: { props: ['label', 'value'], template: '<div>{{ label }} {{ value }}</div>' },
  NvPageHeader: {
    props: ['title'],
    template: '<header>{{ title }}<slot name="actions" /></header>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<div><slot /></div>' },
  SelectValue: { template: '<span />' },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
}

describe('MES handovers read-face guard', () => {
  beforeEach(() => {
    state.catalogResolved = true
    state.row.teamName = '总装早班一组'
  })

  it('shows the DTO team name and never exposes technical identifiers', () => {
    const wrapper = mount(HandoversPage, { global: { stubs } })
    const visibleText = wrapper.text()

    expect(visibleText).toContain('总装早班一组')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('shows neutral placeholders instead of raw identifiers when the directory cannot resolve them', () => {
    state.catalogResolved = false
    state.row.teamName = undefined
    const wrapper = mount(HandoversPage, { global: { stubs } })
    const visibleText = wrapper.text()

    expect(visibleText).toContain('—')
    expect(visibleText).toContain('未指派')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })
})
