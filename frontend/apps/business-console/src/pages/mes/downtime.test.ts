import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import DowntimePage from './downtime.vue'

// #1323：停机恢复入口用例——恢复通道不能只活在 API 里，页面必须可点。

const notifyMocks = vi.hoisted(() => ({
  notifySuccess: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
}))

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

const recordDowntimeEvent = vi.fn()
const recoverDowntimeEvent = vi.fn().mockResolvedValue(undefined)
const refreshDowntimeEvents = vi.fn().mockResolvedValue(undefined)
const refreshOperationTasks = vi.fn().mockResolvedValue(undefined)
const refreshDowntimeWriteScope = vi.fn().mockResolvedValue(undefined)
const { notifySuccess, notifyOperationFailure } = notifyMocks
const filters = reactive({ organizationId: 'org', environmentId: 'dev', skip: 0, take: 10 })
const writeScope = ref({ kind: 'work-center', id: 'WC-01', displayName: '装配一线' })
const operationTaskFixture = {
  operationTaskId: 'OP-001',
  operationTaskNo: 'OP-10',
  workOrderId: 'WO-ID-001',
  workOrderNo: 'WO-20260826-001',
  workCenterId: 'WC-01',
  workCenterName: '装配一线',
  deviceAssetId: 'DEVICE-ID-001',
  deviceAssetCode: 'EQ-001',
  deviceAssetName: '一号装配机',
}
const operationTasks = ref<
  Array<Omit<typeof operationTaskFixture, 'deviceAssetId'> & { deviceAssetId: string | null }>
>([{ ...operationTaskFixture }])
let permissionCodes: string[] = []
const fakeNow = new Date('2026-08-26T01:00:00.000Z')
const startedAt = new Date('2026-08-26T00:30:00.000Z')

function toLocalDateTimeInput(date: Date) {
  const localOffset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - localOffset).toISOString().slice(0, 16)
}

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: () => 'record-downtime-stable-key',
  useMesDowntimeEvents: () => ({
    downtimeEvents: computed(() => [openRow, recoveredRow]),
    downtimeEventsError: ref(undefined),
    downtimeEventsPending: ref(false),
    downtimeEventsTotal: computed(() => 2),
    downtimeReasonOptions: computed(() => [
      { value: 'equipment-fault', label: '设备故障（equipment-fault）' },
    ]),
    downtimeReasonsError: ref(undefined),
    downtimeReasonsPending: ref(false),
    downtimeWriteCoversWorkOrder: (
      candidate: { operationTasks?: Array<{ workCenterId?: string }> },
      scope: { kind: string; id: string },
    ) =>
      scope.kind === 'work-center' &&
      candidate.operationTasks?.some((task) => task.workCenterId === scope.id) === true,
    downtimeWriteScope: writeScope,
    downtimeWriteScopeMessage: computed(() =>
      writeScope.value ? '' : '当前主体没有可用的停机登记范围',
    ),
    downtimeWriteScopePending: ref(false),
    downtimeWriteScopeReady: computed(() => Boolean(writeScope.value)),
    filters,
    recordDowntimeEvent,
    recordDowntimeEventPending: ref(false),
    recoverDowntimeEvent,
    recoverDowntimeEventPending: ref(false),
    refreshDowntimeWriteScope,
    refreshDowntimeEvents,
  }),
  useMesOperationTasks: () => ({
    operationTasks,
    operationTasksPending: ref(false),
    operationListScopeMessage: ref(''),
    operationListScopeReady: ref(true),
    refreshOperationTasks,
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
  notifySuccess: notifyMocks.notifySuccess,
  notifyOperationFailure: notifyMocks.notifyOperationFailure,
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvPageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
  },
  NvMetricCard: { template: '<div />' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" v-bind="$attrs" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  NvSelectTrigger: { template: '<span v-bind="$attrs"><slot /></span>' },
  SelectValue: { template: '<span />' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
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
  filters.organizationId = 'org'
  filters.environmentId = 'dev'
  writeScope.value = { kind: 'work-center', id: 'WC-01', displayName: '装配一线' }
  operationTasks.value = [{ ...operationTaskFixture }]
  permissionCodes = ['business.mes.downtime.read', 'business.mes.downtime.manage']
  recordDowntimeEvent.mockReset()
  recordDowntimeEvent.mockResolvedValue({
    data: { accepted: true, downstreamDocumentId: 'DT-NEW-001' },
  })
  recoverDowntimeEvent.mockClear()
  refreshDowntimeEvents.mockClear()
  refreshOperationTasks.mockClear()
  refreshDowntimeWriteScope.mockClear()
  notifySuccess.mockClear()
  notifyOperationFailure.mockClear()
})

async function openRecordDialog(wrapper: ReturnType<typeof mountPage>) {
  const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
  expect(button).toBeDefined()
  await button!.trigger('click')
}

async function fillValidRecordForm(wrapper: ReturnType<typeof mountPage>) {
  await wrapper.get('[aria-label="工单与工序"]').setValue('operation:OP-001')
  await wrapper.get('[aria-label="停机原因"]').setValue('equipment-fault')
  await wrapper.get('[aria-label="停机开始时间"]').setValue(toLocalDateTimeInput(startedAt))
}

describe('MES downtime record entry', () => {
  it('submits the v2 contract with real operation context and refreshes the list', async () => {
    vi.setSystemTime(fakeNow)
    const wrapper = mountPage()

    await openRecordDialog(wrapper)
    await fillValidRecordForm(wrapper)
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()

    expect(recordDowntimeEvent).toHaveBeenCalledTimes(1)
    expect(recordDowntimeEvent).toHaveBeenCalledWith({
      workOrderId: 'WO-ID-001',
      operationTaskId: 'OP-001',
      workCenterId: 'WC-01',
      deviceAssetId: 'DEVICE-ID-001',
      reasonCode: 'equipment-fault',
      startedAtUtc: '2026-08-26T00:30:00.000Z',
      idempotencyKey: 'record-downtime-stable-key',
      scopeKind: 'work-center',
      scopeId: 'WC-01',
    })
    expect(refreshDowntimeEvents).toHaveBeenCalled()
    expect(notifySuccess).toHaveBeenCalledWith('停机事件 DT-NEW-001 已登记。')
  })

  it('fails closed without permission or business context', async () => {
    permissionCodes = ['business.mes.downtime.read']
    const wrapper = mountPage()
    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button?.attributes('disabled')).toBeDefined()

    permissionCodes = ['business.mes.downtime.read', 'business.mes.downtime.manage']
    filters.organizationId = ''
    await button!.trigger('click')
    expect(recordDowntimeEvent).not.toHaveBeenCalled()
  })

  it('does not populate the record form when the entry guard trips between render and click (stale-DOM race)', async () => {
    // 同一竞态手法（见上一条用例）：按钮渲染时守卫未拦截，业务上下文在同一 tick 内失效——
    // DOM 的 disabled 属性还没来得及重渲染，trigger() 读到的仍是旧值，点击得以派发，
    // 从而真正跑进 openRecordDialog 内部的 `if (recordEntryBlocker.value) return`。
    // 断言取「开始时间」输入框——它只在守卫放行后由 openRecordDialog 写入当前时间，是
    // 不依赖 NvDialog 开合状态的业务信号。删掉那一行，此用例必须变红。
    const wrapper = mountPage()
    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button).toBeDefined()
    expect(button!.attributes('disabled')).toBeUndefined()

    filters.organizationId = ''
    await button!.trigger('click')

    expect(wrapper.get<HTMLInputElement>('[aria-label="停机开始时间"]').element.value).toBe('')
    expect(recordDowntimeEvent).not.toHaveBeenCalled()
  })

  it('fails closed without an operation target, configured reason or valid time', async () => {
    vi.setSystemTime(fakeNow)
    const wrapper = mountPage()

    await openRecordDialog(wrapper)
    await wrapper.get('[aria-label="停机开始时间"]').setValue(toLocalDateTimeInput(startedAt))
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()
    expect(recordDowntimeEvent).not.toHaveBeenCalled()

    await wrapper.get('[aria-label="工单与工序"]').setValue('operation:OP-001')
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()
    expect(recordDowntimeEvent).not.toHaveBeenCalled()

    await wrapper.get('[aria-label="停机原因"]').setValue('equipment-fault')
    await wrapper.get('[aria-label="停机开始时间"]').setValue('invalid-time')
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()
    expect(recordDowntimeEvent).not.toHaveBeenCalled()
  })

  it('fails closed when the selected operation has no real device context', async () => {
    operationTasks.value = [{ ...operationTaskFixture, deviceAssetId: null }]
    const wrapper = mountPage()

    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button?.attributes('disabled')).toBeDefined()
    await button!.trigger('click')
    expect(recordDowntimeEvent).not.toHaveBeenCalled()
  })

  it('preserves the service error and reuses the idempotency key for a safe retry', async () => {
    vi.setSystemTime(fakeNow)
    const serviceError = { message: '所选设备不属于该工作中心', status: 400 }
    recordDowntimeEvent.mockRejectedValue(serviceError)
    const wrapper = mountPage()

    await openRecordDialog(wrapper)
    await fillValidRecordForm(wrapper)
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="record-downtime-submit"]').trigger('click')
    await flushPromises()

    expect(recordDowntimeEvent).toHaveBeenCalledTimes(2)
    expect(recordDowntimeEvent.mock.calls[0]![0].idempotencyKey).toBe(
      recordDowntimeEvent.mock.calls[1]![0].idempotencyKey,
    )
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '停机登记失败',
      serviceError,
      '停机登记失败，请根据服务端原因检查后重试。',
    )
  })
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
