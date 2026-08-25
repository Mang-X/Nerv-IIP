import { computed, reactive, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import HandoversPage from './handovers.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const TECHNICAL_USER_PATTERN = /user-emp-/i

const state = vi.hoisted(() => ({
  catalogResolved: true,
  principal: {
    permissionCodes: ['business.mes.handovers.manage'] as string[],
  },
  filters: {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    status: undefined as string | undefined,
    keyword: '',
    skip: 0,
    take: 20,
  },
  resources: {
    shift: [{ code: 'EARLY', displayName: '早班', active: true }],
    team: [{ code: 'TEAM-A', displayName: '总装早班一组', active: true }],
  },
  row: {
    handoverId: 'handover-001',
    shiftId: 'EARLY',
    teamId: 'TEAM-A',
    teamName: '总装早班一组' as string | undefined,
    handoverStatus: 'open',
    openIssueCount: 1,
    createdAtUtc: '2026-08-01T08:00:00Z',
  },
}))

const mutations = vi.hoisted(() => ({
  createShiftHandover: vi.fn(),
  acceptShiftHandover: vi.fn(),
  refreshHandovers: vi.fn(),
  makeIdempotencyKey: vi.fn((prefix: string) => `${prefix}-stable`),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  useMesShiftHandovers: () => ({
    filters: reactive(state.filters),
    handovers: computed(() => [state.row]),
    handoversError: ref(),
    handoversPending: ref(false),
    handoversTotal: ref(1),
    createShiftHandover: mutations.createShiftHandover,
    acceptShiftHandover: mutations.acceptShiftHandover,
    refreshHandovers: mutations.refreshHandovers,
  }),
  makeIdempotencyKey: mutations.makeIdempotencyKey,
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: string) => ({
    filters: reactive({ take: 100 }),
    resources: computed(() =>
      state.catalogResolved
        ? (state.resources[resourceType as keyof typeof state.resources] ?? [])
        : [],
    ),
    resourcesPending: ref(false),
    resourcesError: ref(),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: state.principal }),
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifyError: mutations.notifyError,
  notifyOperationFailure: mutations.notifyOperationFailure,
  notifySuccess: mutations.notifySuccess,
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref(20) }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
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
          <slot name="cell-actions" :row="row" />
        </div>
      </section>
    `,
  },
  NvDialog: {
    props: ['open'],
    emits: ['update:open'],
    template: '<div v-if="open"><slot /></div>',
  },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvMetricCard: { props: ['label', 'value'], template: '<div>{{ label }} {{ value }}</div>' },
  NvPageHeader: {
    props: ['title'],
    template: '<header>{{ title }}<slot name="actions" /></header>',
  },
  NvRowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  NvDropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
  DropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" :disabled="disabled"><slot /></button>',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  Spinner: { template: '<span />' },
}

function acceptedResponse(outcome: 'accepted' | 'confirmed' = 'accepted') {
  return {
    data: {
      accepted: true,
      operationReceipt: { outcome },
    },
  }
}

function mountPage() {
  return mount(HandoversPage, { global: { stubs } })
}

describe('MES handovers read-face guard', () => {
  beforeEach(() => {
    state.catalogResolved = true
    state.principal.permissionCodes = ['business.mes.handovers.manage']
    state.filters.organizationId = 'org-001'
    state.filters.environmentId = 'env-dev'
    state.row.handoverId = 'handover-001'
    state.row.shiftId = 'EARLY'
    state.row.teamId = 'TEAM-A'
    state.row.teamName = '总装早班一组'
    state.row.handoverStatus = 'open'
    mutations.createShiftHandover.mockReset()
    mutations.acceptShiftHandover.mockReset()
    mutations.refreshHandovers.mockReset().mockResolvedValue(undefined)
    mutations.makeIdempotencyKey.mockClear()
    mutations.notifySuccess.mockReset()
    mutations.notifyError.mockReset()
    mutations.notifyOperationFailure.mockReset()
  })

  it('shows the DTO team name and never exposes technical identifiers', () => {
    const wrapper = mountPage()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('总装早班一组')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('shows neutral placeholders instead of raw identifiers when the directory cannot resolve them', () => {
    state.catalogResolved = false
    state.row.teamName = undefined
    const wrapper = mountPage()
    const visibleText = wrapper.text()

    expect(visibleText).toContain('—')
    expect(visibleText).toContain('未指派')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toMatch(TECHNICAL_USER_PATTERN)
  })

  it('validates the create form before issuing a mutation', async () => {
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')

    expect(mutations.createShiftHandover).not.toHaveBeenCalled()
    expect(wrapper.get('[role="alert"]').text()).toContain('请选择班次和班组')
    expect(wrapper.get('[data-testid="handover-create-shift"]').attributes('data-invalid')).toBe('')
    expect(wrapper.get('[data-testid="handover-create-team"]').attributes('data-invalid')).toBe('')
  })

  it('creates from visible directory values, shows the real receipt outcome, and refreshes', async () => {
    mutations.createShiftHandover.mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="handover-create-shift"]').setValue('EARLY')
    await wrapper.get('[data-testid="handover-create-team"]').setValue('TEAM-A')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.createShiftHandover).toHaveBeenCalledWith({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      shiftId: 'EARLY',
      teamId: 'TEAM-A',
      teamName: '总装早班一组',
      idempotencyKey: 'mes-handover-create-stable',
    })
    expect(mutations.createShiftHandover.mock.calls[0]?.[0]).not.toHaveProperty('openIssueIds')
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifySuccess).toHaveBeenCalledWith('班次交接创建成功，服务端已受理。')
  })

  it('keeps the create idempotency key for a retry after an operation failure', async () => {
    mutations.createShiftHandover
      .mockRejectedValueOnce(new Error('network unavailable'))
      .mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[aria-label="新建班次交接"]').trigger('click')
    await wrapper.get('[data-testid="handover-create-shift"]').setValue('EARLY')
    await wrapper.get('[data-testid="handover-create-team"]').setValue('TEAM-A')
    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.notifyOperationFailure).toHaveBeenCalledWith(
      '创建班次交接失败',
      expect.any(Error),
      '创建班次交接失败，请稍后重试。',
    )
    expect(wrapper.find('[data-testid="create-handover-form"]').exists()).toBe(true)

    await wrapper.get('[data-testid="create-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.createShiftHandover).toHaveBeenCalledTimes(2)
    expect(mutations.createShiftHandover.mock.calls[0]?.[0].idempotencyKey).toBe(
      'mes-handover-create-stable',
    )
    expect(mutations.createShiftHandover.mock.calls[1]?.[0].idempotencyKey).toBe(
      'mes-handover-create-stable',
    )
  })

  it('accepts an open handover with current context and refreshes after the receipt', async () => {
    mutations.acceptShiftHandover.mockResolvedValueOnce(acceptedResponse())
    const wrapper = mountPage()

    await wrapper.get('[data-testid="accept-handover"]').trigger('click')
    await wrapper.get('[data-testid="accept-handover-form"]').trigger('submit')
    await flushPromises()

    expect(mutations.acceptShiftHandover).toHaveBeenCalledWith('handover-001', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      idempotencyKey: 'mes-handover-accept-stable',
    })
    expect(mutations.refreshHandovers).toHaveBeenCalledTimes(1)
    expect(mutations.notifySuccess).toHaveBeenCalledWith('接班已受理，服务端已受理。')
  })

  it.each([
    ['已接班状态', () => (state.row.handoverStatus = 'accepted')],
    ['缺少业务上下文', () => (state.filters.environmentId = '')],
    ['没有接班权限', () => (state.principal.permissionCodes = [])],
  ])('在%s时不发起接班请求', async (_label, arrange) => {
    arrange()
    const wrapper = mountPage()
    const action = wrapper.find('[data-testid="accept-handover"]')

    if (action.exists()) await action.trigger('click')
    await flushPromises()

    expect(mutations.acceptShiftHandover).not.toHaveBeenCalled()
  })
})
