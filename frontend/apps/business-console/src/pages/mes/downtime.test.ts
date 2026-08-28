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
  reasonCode: 'DT-MECH',
  reasonName: '机械故障（轴承/传动/密封）',
}
const recoveredRow = {
  ...openRow,
  downtimeEventId: 'DT-0002',
  status: 'Recovered',
  recoveredAtUtc: '2026-07-30T02:00:00Z',
  // 目录里没有的历史自由文本原因：门面解不出名字，页面照实显示原值。
  reasonCode: '换型调整',
  reasonName: null,
}

// 历史停机事件可能连原因码都没有（读面契约里 reasonCode / reasonName 都可空）。
const unreasonedRow = {
  ...openRow,
  downtimeEventId: 'DT-0003',
  reasonCode: null,
  reasonName: null,
}
const downtimeRows = ref<Array<typeof openRow | typeof recoveredRow | typeof unreasonedRow>>([])

// #2696：读面（原因筛选）的词表随停机列表从自己的门面回来，是纯名称口径。
const readFaceReasonCatalog = [
  { value: 'DT-MECH', label: '机械故障（轴承/传动/密封）' },
  { value: 'DT-PM', label: '计划保养' },
]
// 写面（登记弹窗）的词表仍来自 `downtime-reason` 目录，仍是「名称（码）」口径——两份不同源、
// 文案也不同，读面若退回去用写面这份，下拉里就会重新出现原因码。
const writeFaceReasonOptions = [
  { value: 'DT-MECH', label: '机械故障（轴承/传动/密封）（DT-MECH）' },
  { value: 'DT-PM', label: '计划保养（DT-PM）' },
]
const reasonCatalog = ref<Array<{ value: string; label: string }>>([])
const reasonOptions = ref<Array<{ value: string; label: string }>>([])

const recordDowntimeEvent = vi.fn()
const recoverDowntimeEvent = vi.fn().mockResolvedValue(undefined)
const refreshDowntimeEvents = vi.fn().mockResolvedValue(undefined)
const refreshOperationTasks = vi.fn().mockResolvedValue(undefined)
const refreshDowntimeWriteScope = vi.fn().mockResolvedValue(undefined)
const { notifySuccess, notifyOperationFailure } = notifyMocks
const filters = reactive<{
  organizationId: string
  environmentId: string
  skip: number
  take: number
  status?: string
  keyword?: string
  reasonCode?: string
}>({ organizationId: 'org', environmentId: 'dev', skip: 0, take: 10 })
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
    downtimeEvents: computed(() => downtimeRows.value),
    downtimeEventsError: ref(undefined),
    downtimeEventsPending: ref(false),
    downtimeEventsTotal: computed(() => 2),
    downtimeReasonCatalog: computed(() => reasonCatalog.value),
    downtimeReasonOptions: computed(() => reasonOptions.value),
    downtimeReasonSummary: computed(() => [
      {
        reasonCode: 'DT-MECH',
        reasonName: '机械故障（轴承/传动/密封）',
        openCount: 1,
        durationMinutes: 90,
      },
      { reasonCode: '换型调整', reasonName: null, openCount: 0, durationMinutes: 30 },
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

function mountPage(stubOverrides: Record<string, unknown> = {}) {
  return mount(DowntimePage, { global: { stubs: { ...stubs, ...stubOverrides } } })
}

/** 逐列渲染的表格桩：默认桩只出「操作」列，读面列断言必须自己把 accessor 跑一遍。 */
const cellRenderingTable = {
  props: ['rows', 'columns'],
  template:
    '<section><div v-for="(row, index) in rows" :key="index" data-row>' +
    '<span v-for="column in columns" :key="column.key" :data-cell="column.key">' +
    '{{ column.accessor ? column.accessor(row) : row[column.key] }}</span>' +
    '</div></section>',
}

/** 渲染分段的指标卡桩：默认桩是空 div，汇总断言看不到任何东西。 */
const segmentRenderingMetricCard = {
  props: ['label', 'value', 'unit', 'segments'],
  template:
    '<div :data-metric="label">{{ value }}{{ unit }}' +
    '<span v-for="segment in segments ?? []" :key="segment.key" :data-segment="segment.key">' +
    '{{ segment.label }}={{ segment.value }}</span></div>',
}

beforeEach(() => {
  filters.organizationId = 'org'
  filters.environmentId = 'dev'
  filters.reasonCode = undefined
  downtimeRows.value = [openRow, recoveredRow]
  reasonCatalog.value = readFaceReasonCatalog.map((option) => ({ ...option }))
  reasonOptions.value = writeFaceReasonOptions.map((option) => ({ ...option }))
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
  await wrapper.get('[aria-label="停机原因"]').setValue('DT-MECH')
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
      reasonCode: 'DT-MECH',
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

    await wrapper.get('[aria-label="停机原因"]').setValue('DT-MECH')
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

// #1947：停机读面必须能看见原因、按原因筛选、按原因看时长构成。
describe('MES downtime reason read face', () => {
  it('renders the reason column from the facade-resolved name and keeps the raw code when unresolved', () => {
    const wrapper = mountPage({ NvDataTable: cellRenderingTable })

    const cells = wrapper.findAll('[data-cell="reasonCode"]')
    expect(cells).toHaveLength(2)
    expect(cells[0]!.text()).toBe('机械故障（轴承/传动/密封）')
    expect(cells[1]!.text()).toBe('换型调整')
  })

  it('labels a downtime event that carries neither a reason name nor a reason code', () => {
    // 两者皆空是读面契约里可达的一支（历史上没填原因码的停机事件）。
    // 这一格必须说人话，不能留成空白单元格——空白在表格里读起来像还没加载完或坏了。
    downtimeRows.value = [unreasonedRow]
    const wrapper = mountPage({ NvDataTable: cellRenderingTable })

    const cell = wrapper.get('[data-cell="reasonCode"]')
    expect(cell.text()).toBe('未指定')
  })

  it('offers the authoritative reason directory as the filter and pushes the selection into the list query', async () => {
    const wrapper = mountPage()

    // 弹窗里的「停机原因」下拉与工具栏筛选同名，这里按选项内容锁定工具栏那一个。
    const select = wrapper.findAll('select').find((item) => item.text().includes('全部原因'))
    expect(select).toBeDefined()
    // 选项取自权威字典而不是本次汇总：汇总只含「当前筛选下出现过的原因」，
    // 用它当选项会让选中的原因在切筛选后从下拉里消失，过滤却还生效。
    // 只读面一律显示纯名称：不把原因码打在界面上，也与列/分段卡的文字一致。
    expect(select!.findAll('option').map((option) => option.text())).toEqual([
      '全部原因',
      '机械故障（轴承/传动/密封）',
      '计划保养',
    ])

    await select!.setValue('DT-PM')
    expect(filters.reasonCode).toBe('DT-PM')

    await select!.setValue('all')
    expect(filters.reasonCode).toBeUndefined()
  })

  // #2696 判据 1/2：只授 business.mes.downtime.read 的角色拿不到写面那个目录（它钉在
  // MaintenanceWorkOrdersRead 上），写面词表为空。读面筛选必须照样有选项——它读的是随停机
  // 列表回来的那份词表。
  it('keeps the reason filter usable when the write-side directory yields no options', () => {
    reasonOptions.value = []
    const wrapper = mountPage()

    const select = wrapper.findAll('select').find((item) => item.text().includes('全部原因'))
    expect(select!.findAll('option').map((option) => option.text())).toEqual([
      '全部原因',
      '机械故障（轴承/传动/密封）',
      '计划保养',
    ])
  })

  // #2696：读面词表为空时**不得**回落到写面目录——回落会把「名称（码）」打回筛选下拉（破坏判据 3），
  // 并且把只读角色重新绑回 MaintenanceWorkOrdersRead（破坏判据 1）。空就是空，那是门面取目录失败的实情。
  it('does not fall back to the write-side directory when the read-face catalog is empty', () => {
    reasonCatalog.value = []
    const wrapper = mountPage()

    const select = wrapper.findAll('select').find((item) => item.text().includes('全部原因'))
    expect(select!.findAll('option').map((option) => option.text())).toEqual(['全部原因'])
  })

  // #2696 判据 3：读面三处（原因列、时长构成卡分段、筛选下拉）对同一原因必须是同一串纯名称。
  it('shows one and the same plain reason name across the column, the breakdown card and the filter', () => {
    const wrapper = mountPage({
      NvDataTable: cellRenderingTable,
      NvMetricCard: segmentRenderingMetricCard,
    })

    const columnText = wrapper.findAll('[data-cell="reasonCode"]')[0]!.text()
    const segmentText = wrapper
      .get('[data-metric="停机时长按原因"]')
      .get('[data-segment="DT-MECH"]')
      .text()
    const filterText = wrapper
      .findAll('select')
      .find((item) => item.text().includes('全部原因'))!
      .findAll('option')[1]!
      .text()

    expect(columnText).toBe('机械故障（轴承/传动/密封）')
    expect(segmentText).toBe(`${columnText}=1.5`)
    expect(filterText).toBe(columnText)
    expect(filterText).not.toContain('DT-MECH')
  })

  it('breaks total downtime hours down by reason', () => {
    const wrapper = mountPage({ NvMetricCard: segmentRenderingMetricCard })

    const card = wrapper.get('[data-metric="停机时长按原因"]')
    expect(card.text()).toContain('2小时')
    expect(card.findAll('[data-segment]').map((segment) => segment.text())).toEqual([
      '机械故障（轴承/传动/密封）=1.5',
      '换型调整=0.5',
    ])
  })
})
