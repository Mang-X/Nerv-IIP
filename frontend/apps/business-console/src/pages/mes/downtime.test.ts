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

// #2793：停机原因目录读失败的归因。真实通道抛的是「解析后的响应体 + 被 error 拦截器挂上去的
// 原始 Response」——`error.response.status` 才是状态码来源（响应体里的 `code` 字段不算，
// `errorStatusCode` 只认 status/statusCode）。这里照这个形状造夹具；该形状本身由
// `src/composables/downtimeReasonDirectoryForbidden.contract.test.ts` 用真实客户端实证。
function directoryFailure(status: number, message: string) {
  const body = { success: false, message, code: status, data: null, errorData: [] }
  Object.defineProperty(body, 'response', {
    configurable: true,
    enumerable: false,
    value: new Response(JSON.stringify(body), { status }),
  })
  return body
}
const downtimeReasonsError = ref<unknown>(undefined)

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
  windowStartUtc: string
  windowEndUtc: string
}>({
  organizationId: 'org',
  environmentId: 'dev',
  skip: 0,
  take: 10,
  windowStartUtc: '2026-07-31T00:00:00.000Z',
  windowEndUtc: '2026-08-30T08:00:00.000Z',
})
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
    downtimeReasonOptions: computed(() => [
      {
        value: 'DT-MECH',
        label: '机械故障（轴承/传动/密封）（DT-MECH）',
        name: '机械故障（轴承/传动/密封）',
      },
      { value: 'DT-PM', label: '计划保养（DT-PM）', name: '计划保养' },
    ]),
    downtimeReasonSummary: computed(() => [
      {
        reasonCode: 'DT-MECH',
        reasonName: '机械故障（轴承/传动/密封）',
        openCount: 1,
        durationMinutes: 90,
      },
      { reasonCode: '换型调整', reasonName: null, openCount: 0, durationMinutes: 30 },
    ]),
    downtimeReasonsError,
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

vi.mock('@/utils/notify', async () => {
  // isForbiddenError 必须用**真实实现**：给桩的话「403 走权限文案」就退化成
  // 「桩返回 true 时走权限文案」的同义反复，抓不到归因被改错。
  const actual = await vi.importActual<typeof import('@/utils/notify')>('@/utils/notify')
  return {
    inlineErrorMessage: () => '',
    isForbiddenError: actual.isForbiddenError,
    notifySuccess: notifyMocks.notifySuccess,
    notifyOperationFailure: notifyMocks.notifyOperationFailure,
  }
})

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvPageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
  },
  NvMetricCard: { template: '<div />' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvDateRangePicker: {
    props: ['modelValue', 'placeholder'],
    emits: ['update:modelValue'],
    template:
      '<button type="button" data-testid="downtime-window" @click="$emit(\'update:modelValue\', { start: \'2026-08-01\', end: \'2026-08-15\' })">{{ modelValue.start }}~{{ modelValue.end }}</button>',
  },
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
  NvSelectValue: { template: '<span />' },
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
  filters.windowStartUtc = '2026-07-31T00:00:00.000Z'
  filters.windowEndUtc = '2026-08-30T08:00:00.000Z'
  filters.skip = 0
  downtimeRows.value = [openRow, recoveredRow]
  writeScope.value = { kind: 'work-center', id: 'WC-01', displayName: '装配一线' }
  operationTasks.value = [{ ...operationTaskFixture }]
  permissionCodes = ['business.mes.downtime.read', 'business.mes.downtime.manage']
  downtimeReasonsError.value = undefined
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
    // 此刻组织/环境/写入范围/工序/原因目录全部就绪，唯独缺 downtime.manage：
    // eligibleDowntimeTargets 内部同样判空这条权限，故仅凭 disabled 状态抓不住
    // recordEntryBlocker 里 `!canManageDowntime.value` 这第一条早返回——删掉它后
    // blocker 会落到"当前授权范围内暂无…工序"分支，disabled 依旧为真。精确文案断言
    // 能抓住这条差异。
    expect(button?.attributes('title')).toBe('没有停机登记权限')

    permissionCodes = ['business.mes.downtime.read', 'business.mes.downtime.manage']
    filters.organizationId = ''
    await button!.trigger('click')
    expect(recordDowntimeEvent).not.toHaveBeenCalled()
  })

  // #2793：真正要钉的是「403 → 权限文案 / 非 403 → 原文案」这个**映射**，不是文案长什么样。
  // 三组用例共用同一套夹具，唯一变量是状态码；把归因里的 isForbiddenError 分流删掉
  // （退回单一「读取失败」文案），403 组必红、503 组仍绿——鉴别力落在分流本身。
  //
  // 文案字面量只在**下面这一条锚点用例**里出现一次（归因文案的唯一登记处）；其它消费者
  // 一律断言「关键语义 + 与锚点同一句话」，而不是把同一串文案抄 N 遍。抄 N 遍才是脆性来源：
  // 改一次措辞会误红一片。锚点保留全等是有意的——这句文案是 UX 承重物，静默改写应当被复审看见。
  it('attributes a forbidden downtime-reason directory read to permissions instead of dictionary setup', async () => {
    // 可达性前提：此刻 downtime.manage 权限、组织/环境、写入范围、工序可见范围、工序列表、
    // 原因目录 pending 全部就绪，所以 blocker 一定走到「目录读失败」这一条；且原因选项非空
    // （默认桩给了两条），删掉本分支不会掉进「组织尚未配置」而变成另一种红。
    downtimeReasonsError.value = directoryFailure(403, 'Forbidden.')
    const wrapper = mountPage()
    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button?.attributes('disabled')).toBeDefined()
    expect(button?.attributes('title')).toBe('当前角色没有停机原因词表的读取权限，请联系管理员开通')
  })

  it('keeps a non-permission downtime-reason directory failure on the retry copy', async () => {
    downtimeReasonsError.value = directoryFailure(503, 'Authorization service unavailable.')
    const wrapper = mountPage()
    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button?.attributes('disabled')).toBeDefined()
    expect(button?.attributes('title')).toBe('停机原因读取失败，请刷新后重试')
  })

  // 同一修复必须覆盖两个消费者：读面此前对 403 完全沉默——原因筛选下拉静默只剩「全部原因」，
  // 用户看不出是没权限还是真没配。读面与写面共用同一个归因 computed，这里连「是不是同一句话」
  // 一起钉住：若有人再在 blocker 里内联一份分叉的文案，本断言必红。
  it('explains a forbidden downtime-reason directory read on the read face too, with the same sentence', async () => {
    downtimeReasonsError.value = directoryFailure(403, 'Forbidden.')
    const wrapper = mountPage()

    const message = wrapper.find('[data-testid="downtime-reasons-message"]')
    expect(message.exists()).toBe(true)
    expect(message.text()).toContain('权限')
    // 反向：读面绝不能复述被本票推翻的那句误诊。
    expect(message.text()).not.toContain('尚未配置')

    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(message.text()).toBe(button?.attributes('title'))
  })

  it('reuses the retry copy on the read face for a non-permission failure', async () => {
    downtimeReasonsError.value = directoryFailure(503, 'Authorization service unavailable.')
    const wrapper = mountPage()

    const message = wrapper.find('[data-testid="downtime-reasons-message"]')
    expect(message.exists()).toBe(true)
    expect(message.text()).toContain('刷新')
    expect(message.text()).not.toContain('权限')
  })

  // 负向对照：目录读没出错时读面不得挂常驻错误条。缺了这条，上面两条「exists() 为真」
  // 就可能寄生在一个恒渲染的段落上。
  it('renders no downtime-reason banner when the directory read succeeds', async () => {
    const wrapper = mountPage()
    expect(wrapper.find('[data-testid="downtime-reasons-message"]').exists()).toBe(false)
  })

  it('does not open the record dialog when the entry guard trips between render and click (isolated stale-DOM interleave)', async () => {
    // 以确定性交错模拟 stale-DOM 时序（同族手法见上一条用例）：按钮渲染时守卫未拦截，业务上下文
    // 在同一 tick 内失效——DOM 的 disabled 属性还没来得及重渲染，trigger() 读到的仍是旧值，
    // 点击得以派发，从而真正跑进 openRecordDialog 内部的 `if (recordEntryBlocker.value) return`。
    // 不能证明项：本用例只证明「blocker 为真时该行会早返回」，不证明真实浏览器点击与 Vue
    // microtask flush 之间确有这个时序窗口——那属于 ProviderBehavior，本 lane（jsdom +
    // vue-test-utils）证不到。
    // 断言取登记对话框的开合信号：`recordDialogOpen` 只在守卫放行后由 openRecordDialog
    // 置真，提交按钮随之渲染。删掉 openRecordDialog 里的那行早返回，对话框会打开、
    // 提交按钮出现，此用例必须变红。
    const wrapper = mountPage()
    const button = wrapper.findAll('button').find((item) => item.text().includes('登记停机'))
    expect(button).toBeDefined()
    expect(button!.attributes('disabled')).toBeUndefined()

    filters.organizationId = ''
    await button!.trigger('click')

    expect(wrapper.find('[data-testid="record-downtime-submit"]').exists()).toBe(false)
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
  it('applies a calendar-day window and returns to the first page', async () => {
    filters.skip = 20
    const wrapper = mountPage()

    expect(wrapper.get('[data-testid="downtime-window"]').text()).toBe('2026-07-31~2026-08-30')
    await wrapper.get('[data-testid="downtime-window"]').trigger('click')

    expect(filters.windowStartUtc).toBe(new Date(2026, 7, 1).toISOString())
    expect(filters.windowEndUtc).toBe(new Date(2026, 7, 16).toISOString())
    expect(wrapper.get('[data-testid="downtime-window"]').text()).toBe('2026-08-01~2026-08-15')
    expect(filters.skip).toBe(0)
  })

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
