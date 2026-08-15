import { mount } from '@vue/test-utils'
import { computed, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import DowntimePage from './downtime.vue'

// #1323：停机恢复入口用例——恢复通道不能只活在 API 里，页面必须可点。

const openRow = {
  downtimeEventId: 'DT-0001',
  workOrderId: null,
  operationTaskId: null,
  deviceAssetId: 'EQ-001',
  status: 'Open',
  startedAtUtc: '2026-07-30T01:00:00Z',
  recoveredAtUtc: null,
  workCenterId: 'WC-01',
  reasonCode: 'equipment-fault',
}
const recoveredRow = {
  ...openRow,
  downtimeEventId: 'DT-0002',
  status: 'Recovered',
  recoveredAtUtc: '2026-07-30T02:00:00Z',
}

const recoverDowntimeEvent = vi.fn().mockResolvedValue(undefined)
const refreshDowntimeEvents = vi.fn()
let permissionCodes: string[] = []

vi.mock('@/composables/useBusinessMes', () => ({
  useMesDowntimeEvents: () => ({
    downtimeEvents: computed(() => [openRow, recoveredRow]),
    downtimeEventsError: ref(undefined),
    downtimeEventsPending: ref(false),
    downtimeEventsTotal: computed(() => 2),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', skip: 0, take: 10 }),
    recoverDowntimeEvent,
    recoverDowntimeEventPending: ref(false),
    refreshDowntimeEvents,
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { permissionCodes },
    displayName: '王恢复',
  }),
}))

// 名录解析不是本用例被测对象；给稳定桩，避免真实实现要求装 Pinia。
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      deviceByCode: emptyIndex,
    }),
  }
})

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifySuccess: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvPageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
  },
  NvMetricCard: { template: '<div />' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvInput: { template: '<input />' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  SelectValue: { template: '<span />' },
  NvStatusBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  NvDataTable: {
    props: ['rows', 'columns'],
    template:
      '<section><div v-for="(row, index) in rows" :key="index" data-row>' +
      '<slot name="cell-actions" :row="row" /></div></section>',
  },
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
}

function mountPage() {
  return mount(DowntimePage, { global: { stubs } })
}

beforeEach(() => {
  recoverDowntimeEvent.mockClear()
  refreshDowntimeEvents.mockClear()
})

describe('MES downtime recovery entry', () => {
  it('shows the recover action only on open rows for users with downtime.manage', async () => {
    permissionCodes = ['business.mes.downtime.read', 'business.mes.downtime.manage']
    const wrapper = mountPage()

    const rows = wrapper.findAll('[data-row]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.findAll('button')).toHaveLength(1)
    expect(rows[0]!.text()).toContain('恢复')
    expect(rows[1]!.findAll('button')).toHaveLength(0)
  })

  it('hides the recover action without downtime.manage permission', () => {
    permissionCodes = ['business.mes.downtime.read']
    const wrapper = mountPage()

    expect(wrapper.findAll('[data-row] button')).toHaveLength(0)
    // 未选中恢复目标时，确认弹窗不应携带任何事件明细。
    expect(wrapper.text()).not.toContain('DT-0001')
    expect(wrapper.text()).not.toContain('王恢复')
  })

  it('confirms recovery in a dialog stating actor and start-release semantics, then calls the facade', async () => {
    permissionCodes = ['business.mes.downtime.manage']
    const wrapper = mountPage()

    await wrapper.find('[data-row] button').trigger('click')

    const text = wrapper.text()
    expect(text).toContain('确认恢复停机')
    expect(text).toContain('解除停机拦截')
    expect(text).toContain('王恢复')
    expect(text).toContain('DT-0001')
    expect(text).toContain('WC-01')

    const confirmButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认恢复'))
    expect(confirmButton).toBeDefined()
    await confirmButton!.trigger('click')

    expect(recoverDowntimeEvent).toHaveBeenCalledTimes(1)
    const [eventId, body] = recoverDowntimeEvent.mock.calls[0]!
    expect(eventId).toBe('DT-0001')
    expect(body.organizationId).toBe('org')
    expect(body.environmentId).toBe('dev')
    expect(body.recoveredAtUtc).toBeTruthy()
    // #1219：幂等键对同一停机事件稳定（不含时间戳），二次点击不产生新键。
    expect(body.idempotencyKey).toBe('downtime-recover-DT-0001')
  })
})
