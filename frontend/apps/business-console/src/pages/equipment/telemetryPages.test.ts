import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, reactive, ref, shallowRef } from 'vue'

import TelemetryAlarmRulesPage from './telemetry/alarm-rules.vue'
import TelemetryHistoryPage from './telemetry/history.vue'
import TelemetryOeePage from './telemetry/oee.vue'
import TelemetryTagsPage from './telemetry/tags.vue'

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

const telemetryPageMocks = vi.hoisted(() => ({
  historyError: undefined as unknown,
  historyItems: [] as Array<Record<string, unknown>>,
  historyPending: false,
  replaceRoute: vi.fn(),
  route: undefined as unknown,
  saveAlarmRule: vi.fn(),
}))

vi.mock('@nerv-iip/ui', () => ({
  NvBadge: { template: '<span><slot /></span>' },
  NvButton: { template: '<button><slot /></button>' },
  // 级联范围选择桩件：把三级选中值挂到 data-* 上便于断言路由 ↔ 范围的同步。
  NvCascadePicker: {
    props: ['modelValue', 'levels'],
    emits: ['update:modelValue'],
    template: `
      <div
        data-testid="cascade-picker"
        :data-workshop="modelValue?.workshop ?? ''"
        :data-line="modelValue?.line ?? ''"
        :data-device="modelValue?.device ?? ''"
      />
    `,
  },
  NvDataTable: {
    props: ['rows', 'columns'],
    template: `
      <div>
        <div v-for="row in rows" :key="JSON.stringify(row)">
          <span v-for="col in columns" :key="col.key">
            {{ col.accessor ? col.accessor(row) : row[col.key] }}
          </span>
          <slot name="cell-actions" :row="row" />
        </div>
      </div>
    `,
  },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogClose: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDropdownMenuItem: { template: '<div><slot /></div>' },
  // 实体选择弹窗桩件：只关心取值，替成输入位（页面里它承担原来自由输入框的位置）。
  NvEntityPicker: {
    props: ['modelValue', 'id', 'options', 'loading', 'disabled'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvField: { template: '<div><slot /></div>' },
  NvFieldError: { props: ['errors'], template: '<div>{{ errors?.join(" ") }}</div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvLineChart: {
    props: ['data', 'series'],
    template: '<div data-testid="line-chart">{{ data.length }} {{ series[0]?.label }}</div>',
  },
  NvPageHeader: {
    props: ['title', 'count'],
    template:
      '<header><h1>{{ title }}</h1><span>{{ count }}</span><slot name="actions" /></header>',
  },
  NvRowActions: { template: '<div><slot /></div>' },
  NvSectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  NvSectionCards: { template: '<section><slot /></section>' },
  // 指标卡族桩件：保持「标签 空格 值」的可读文本形状，断言可以直接匹配一整段读数。
  // 日期区间桩件：把当前区间挂到 data-* 上便于断言，点击时提交一段固定区间，
  // 用来验证页面把"本地日历日"翻译成查询用的 ISO 瞬时。
  NvDateRangePicker: {
    props: ['modelValue', 'placeholder'],
    emits: ['update:modelValue'],
    template: `
      <button
        type="button"
        data-testid="date-range"
        :data-start="modelValue?.start ?? ''"
        :data-end="modelValue?.end ?? ''"
        @click="$emit('update:modelValue', { start: '2026-07-05', end: '2026-07-06' })"
      >{{ placeholder }}</button>
    `,
  },
  NvMetricCard: {
    props: ['label', 'value', 'unit', 'footStart', 'footEnd', 'facets', 'segments', 'status'],
    template: `
      <div data-testid="metric-card">
        {{ label }} {{ value }}{{ unit }}
        <span v-for="facet in facets ?? []" :key="facet.key">{{ facet.label }} {{ facet.value }}</span>
        <span v-for="segment in segments ?? []" :key="segment.key">{{ segment.label }} {{ segment.value }}</span>
        <span v-if="status">{{ status.label }}</span>
        <span v-if="footStart">{{ footStart }}</span>
      </div>
    `,
  },
  NvMetricRing: {
    props: ['label', 'value', 'centerCaption', 'segments'],
    template: `
      <div data-testid="metric-ring">
        {{ label }} {{ value }} {{ centerCaption }}
        <span v-for="segment in segments ?? []" :key="segment.key">{{ segment.label }} {{ segment.value }}</span>
      </div>
    `,
  },
  NvMetricStrip: {
    props: ['cells'],
    template: `
      <div data-testid="metric-strip">
        <span v-for="cell in cells ?? []" :key="cell.key">{{ cell.label }} {{ cell.value }}{{ cell.unit }} {{ cell.meta }}</span>
      </div>
    `,
  },
  NvTooltip: { template: '<div><slot /></div>' },
  NvTooltipContent: { template: '<div><slot /></div>' },
  NvTooltipProvider: { template: '<div><slot /></div>' },
  NvTooltipTrigger: { template: '<div><slot /></div>' },
  NvTimeline: {
    props: ['items'],
    template:
      '<ol data-testid="timeline"><li v-for="item in items" :key="item.key">{{ item.title }} {{ item.label }} {{ item.description }}</li></ol>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: { template: '<span><slot /></span>' },
  Spinner: { template: '<span />' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  toast: { success: vi.fn() },
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  const route = reactive({
    query: {
      deviceAssetId: 'DEV-CNC-01',
      tagKey: 'temperature',
      windowEndUtc: '2026-07-02T08:00:00.000Z',
      windowStartUtc: '2026-07-02T00:00:00.000Z',
    } as Record<string, string>,
  })
  telemetryPageMocks.route = route
  return {
    ...actual,
    RouterLink: { props: ['to'], template: '<a><slot /></a>' },
    useRoute: () => route,
    useRouter: () => ({ replace: telemetryPageMocks.replaceRoute }),
  }
})

// 级联范围选择 composable：真实实现依赖主数据 facade（pinia + query），页面测试给可控桩。
const scopeMocks = vi.hoisted(() => ({
  devicesInScope: [] as Array<Record<string, unknown>>,
}))

vi.mock('@/composables/useEquipmentScopeSelection', () => ({
  useEquipmentScopeSelection: (initial?: { workshop?: string; line?: string; device?: string }) => {
    const scope = ref({
      workshop: initial?.workshop ?? '',
      line: initial?.line ?? '',
      device: initial?.device ?? '',
    })
    return {
      scope,
      levels: computed(() => []),
      devicesInScope: computed(() => scopeMocks.devicesInScope),
      scopeLabel: computed(() => '全厂'),
      scopePending: shallowRef(false),
      selectedDevice: computed(() => undefined),
    }
  },
}))

// 设备 / 采集标签 / 单位目录走真实读面（useQuery）；单测给确定目录，只验页面行为。
vi.mock('@/composables/useEquipmentPickerCatalog', () => ({
  telemetryTagLabel: (tagKey: string) => tagKey,
  useEquipmentDeviceCatalog: () => ({
    deviceOptions: computed(() => [{ value: 'DEV-CNC-01', label: '五轴加工中心' }]),
    devicesPending: shallowRef(false),
  }),
  useEquipmentUomCatalog: () => ({
    uomOptions: computed(() => [{ value: 'CEL', label: '摄氏度' }]),
    uomsPending: shallowRef(false),
  }),
  useTelemetryTagCatalog: () => ({
    tagOptions: computed(() => [
      { value: 'temperature', label: '温度' },
      { value: 'spindle-temperature', label: '主轴温度' },
      { value: 'pressure', label: '压力' },
    ]),
    tagsPending: shallowRef(false),
    unitByTagKey: computed(() => new Map([['temperature', 'CEL']])),
  }),
}))

vi.mock('@/composables/useBusinessTelemetry', () => ({
  describeTelemetryOeeDegradation: (reason: string) => reason,
  describeTelemetryOeeLimitations: () => 'OEE = 可用率 × 性能率 × 质量率。',
  formatOeeQuantity: (value?: number) => (value === undefined ? '无数据' : `${value}`),
  formatOeeRate: (value?: number) =>
    value === undefined ? '无数据' : `${(value * 100).toFixed(1)}%`,
  useBusinessTelemetryAlarmRules: () => ({
    alarmRules: computed(() => [
      {
        alarmRuleId: 'rule-1',
        deviceAssetId: 'DEV-CNC-01',
        ruleCode: 'TEMP_HIGH',
        alarmCode: 'ALM-TEMP-HIGH',
        severity: 'critical',
        tagKey: 'temperature',
        comparisonOperator: '>',
        thresholdValue: 85,
        unitCode: 'CEL',
        isEnabled: true,
      },
    ]),
    alarmRulesError: shallowRef(),
    alarmRulesPending: shallowRef(false),
    alarmRulesTotal: computed(() => 1),
    filters: { deviceAssetId: '', isEnabled: 'all', skip: 0, take: 100 },
    refreshAlarmRules: vi.fn(),
    saveAlarmRule: telemetryPageMocks.saveAlarmRule,
    saveAlarmRuleError: shallowRef(),
    saveAlarmRulePending: shallowRef(false),
  }),
  useBusinessTelemetryHistory: () => ({
    filters: reactive({
      deviceAssetId: 'DEV-CNC-01',
      tagKey: 'temperature',
      windowStartUtc: '2026-07-02T00:00:00.000Z',
      windowEndUtc: '2026-07-02T08:00:00.000Z',
    }),
    historyError: shallowRef(telemetryPageMocks.historyError),
    historyItems: computed(() => []),
    historyPending: shallowRef(telemetryPageMocks.historyPending),
    refreshHistory: vi.fn(),
    visibleHistoryItems: computed(() => telemetryPageMocks.historyItems),
  }),
  useBusinessTelemetryOee: () => ({
    availabilityWindows: computed(() => [
      {
        deviceAssetId: 'DEV-CNC-01',
        availabilityStatus: 'unavailable',
        reasonCode: 'equipment.activeAlarm',
        severity: 'critical',
        startUtc: '2026-07-02T07:00:00.000Z',
        endUtc: '2026-07-02T08:00:00.000Z',
      },
      {
        deviceAssetId: 'DEV-CNC-01',
        availabilityStatus: 'unknown',
        reasonCode: 'equipment.stateUnknown',
        severity: 'warning',
        startUtc: '2026-07-02T08:00:00.000Z',
        endUtc: '2026-07-02T09:00:00.000Z',
      },
    ]),
    filters: {
      deviceAssetId: 'DEV-CNC-01',
      tagKey: '',
      windowStartUtc: '2026-07-02T00:00:00.000Z',
      windowEndUtc: '2026-07-02T08:00:00.000Z',
    },
    oee: computed(() => ({
      deviceAssetId: 'DEV-CNC-01',
      stateSampleCount: 10,
      availabilityRate: 0.82,
      loadingRate: 0.9,
      performanceRate: 0.9,
      qualityRate: 0.95,
      oeeRate: 0.7,
      isDegraded: false,
    })),
    oeeError: shallowRef(),
    oeePending: shallowRef(false),
    refreshOee: vi.fn(),
    runtimeAvailabilityError: shallowRef(),
  }),
  useBusinessTelemetryTags: () => ({
    filters: { deviceAssetId: '', isEnabled: 'all', skip: 0, take: 100 },
    refreshTags: vi.fn(),
    tags: computed(() => [
      {
        telemetryTagId: 'tag-1',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        valueType: 'number',
        unitCode: 'CEL',
        samplingPolicy: 'PT1M',
      },
    ]),
    tagsError: shallowRef(),
    tagsPending: shallowRef(false),
    tagsTotal: computed(() => 1),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvBadge: { template: '<span><slot /></span>' },
  NvButton: { template: '<button><slot /></button>' },
  NvDataTable: {
    props: ['rows', 'columns'],
    template: `
      <div>
        <div v-for="row in rows" :key="JSON.stringify(row)">
          <span v-for="col in columns" :key="col.key">
            {{ col.accessor ? col.accessor(row) : row[col.key] }}
          </span>
        </div>
        <slot v-for="row in rows" name="cell-actions" :row="row" />
      </div>
    `,
  },
  NvDropdownMenuItem: { template: '<div><slot /></div>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldError: { props: ['errors'], template: '<div>{{ errors?.join(" ") }}</div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvLineChart: {
    props: ['data', 'series'],
    template: '<div data-testid="line-chart">{{ data.length }} {{ series[0]?.label }}</div>',
  },
  PageHeader: {
    props: ['title', 'count'],
    template:
      '<header><h1>{{ title }}</h1><span>{{ count }}</span><slot name="actions" /></header>',
  },
  RowActions: { template: '<div><slot /></div>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  SectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  SectionCards: { template: '<section><slot /></section>' },
  NvSectionCard: {
    props: ['description', 'value', 'hint'],
    template: '<div>{{ description }} {{ value }} {{ hint }}</div>',
  },
  NvSectionCards: { template: '<section><slot /></section>' },
  NvTimeline: {
    props: ['items'],
    template:
      '<ol data-testid="timeline"><li v-for="item in items" :key="item.key">{{ item.title }} {{ item.label }} {{ item.description }}</li></ol>',
  },
  Spinner: { template: '<span />' },
  Toolbar: { template: '<div><slot name="filters" /></div>' },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogClose: { template: '<div><slot /></div>' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: { template: '<span><slot /></span>' },
}

describe('equipment telemetry pages', () => {
  beforeEach(() => {
    telemetryPageMocks.historyError = undefined
    telemetryPageMocks.historyPending = false
    telemetryPageMocks.historyItems = [
      {
        itemType: 'sample',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: '87.5',
        occurredAtUtc: '2026-07-02T07:30:00.000Z',
      },
      {
        itemType: 'hourly',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: '82.25',
        occurredAtUtc: '2026-07-02T06:30:00.000Z',
      },
      {
        itemType: 'state',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: 'running',
        occurredAtUtc: '2026-07-02T07:15:00.000Z',
      },
      {
        itemType: 'alarm',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: 'TEMP_HIGH',
        occurredAtUtc: '2026-07-02T07:20:00.000Z',
      },
    ]
    telemetryPageMocks.replaceRoute.mockClear()
    telemetryPageMocks.saveAlarmRule.mockClear()
    scopeMocks.devicesInScope = []
    ;(telemetryPageMocks.route as { query: Record<string, string> }).query = {
      deviceAssetId: 'DEV-CNC-01',
      tagKey: 'temperature',
      windowEndUtc: '2026-07-02T08:00:00.000Z',
      windowStartUtc: '2026-07-02T00:00:00.000Z',
    }
  })

  it('does not expose organization or environment context on telemetry pages', () => {
    for (const page of [
      TelemetryTagsPage,
      TelemetryAlarmRulesPage,
      TelemetryHistoryPage,
      TelemetryOeePage,
    ]) {
      const wrapper = mount(page, { global: { stubs } })

      expect(wrapper.text()).not.toContain('组织')
      expect(wrapper.text()).not.toContain('环境')
      expect(wrapper.html()).not.toContain('organizationId')
      expect(wrapper.html()).not.toContain('environmentId')
    }
  })

  it('shows real tag, rule, history, and explainable OEE fields', () => {
    expect(mount(TelemetryTagsPage, { global: { stubs } }).text()).toContain('temperature')
    expect(mount(TelemetryAlarmRulesPage, { global: { stubs } }).text()).toContain('TEMP_HIGH')
    expect(mount(TelemetryHistoryPage, { global: { stubs } }).text()).toContain('87.5')

    const oeeText = mount(TelemetryOeePage, { global: { stubs } }).text()
    expect(oeeText).toContain('82.0%')
    expect(oeeText).toContain('性能率')
    expect(oeeText).toContain('质量率')
  })

  it('renders numeric telemetry statistics, the real trend points, and event context together', () => {
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.get('[data-testid="line-chart"]').text()).toContain('1 遥测值')
    expect(wrapper.text()).toContain('最新值 87.5')
    expect(wrapper.text()).toContain('最小值 87.5')
    expect(wrapper.text()).toContain('最大值 87.5')
    expect(wrapper.text()).toContain('样本数 1')
    expect(wrapper.get('[data-testid="timeline"]').text()).toContain('报警记录')
    expect(wrapper.get('[data-testid="timeline"]').text()).toContain('状态记录')
  })

  it('uses the shared date range control and preserves the complete query scope', async () => {
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.findAll('[data-testid="date-range"]')).toHaveLength(1)
    expect(wrapper.findAll('input[type="datetime-local"]')).toHaveLength(0)
    await wrapper.findAll('input')[0]!.setValue('spindle-temperature')
    expect(telemetryPageMocks.replaceRoute).toHaveBeenCalledWith({
      query: expect.objectContaining({
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'spindle-temperature',
        windowEndUtc: '2026-07-02T08:00:00.000Z',
        windowStartUtc: '2026-07-02T00:00:00.000Z',
      }),
    })
  })

  it('restores filters when browser history changes the route query', async () => {
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })
    ;(telemetryPageMocks.route as { query: Record<string, string> }).query = {
      deviceAssetId: 'DEV-PRESS-02',
      tagKey: 'pressure',
      windowStartUtc: '2026-07-03T00:00:00.000Z',
      windowEndUtc: '2026-07-03T04:00:00.000Z',
    }
    await nextTick()

    // 设备改由级联范围选择承载：路由驱动的设备变化要反向同步回级联。
    expect(wrapper.get('[data-testid="cascade-picker"]').attributes('data-device')).toBe(
      'DEV-PRESS-02',
    )
    expect(wrapper.findAll('input')[0]?.element.value).toBe('pressure')
  })

  it('shows the scope device overview and drills down when no device is selected', async () => {
    ;(telemetryPageMocks.route as { query: Record<string, string> }).query = {}
    scopeMocks.devicesInScope = [
      { code: 'DEV-CNC-01', displayName: '五轴加工中心', workshopCode: 'WS-01', lineCode: 'LN-01' },
    ]
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })
    await nextTick()

    // 未下钻：不再是空态提示，而是范围设备总览 + 引导下钻。
    expect(wrapper.text()).toContain('范围设备')
    expect(wrapper.text()).toContain('DEV-CNC-01')
    const drill = wrapper.findAll('button').find((b) => b.text().includes('查看趋势'))
    expect(drill).toBeDefined()
    await drill!.trigger('click')
    await nextTick()

    expect(wrapper.get('[data-testid="cascade-picker"]').attributes('data-device')).toBe(
      'DEV-CNC-01',
    )
  })

  it('commits a picked local day range as an inclusive UTC window', async () => {
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })
    await wrapper.get('[data-testid="date-range"]').trigger('click')
    await nextTick()

    const query = telemetryPageMocks.replaceRoute.mock.calls.at(-1)?.[0]?.query as Record<
      string,
      string
    >
    // 开始＝所选首日 00:00；结束＝所选末日的次日 00:00，把末日整天包进窗口。
    expect(toLocalDay(query.windowStartUtc)).toBe('2026-07-05')
    expect(toLocalDay(query.windowEndUtc)).toBe('2026-07-07')
    // 回显退回用户实际选中的末日，而不是那个排他上界。
    expect(wrapper.get('[data-testid="date-range"]').attributes('data-end')).toBe('2026-07-06')
  })

  it('keeps existing trend content mounted while a refresh is pending', () => {
    telemetryPageMocks.historyPending = true
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.find('[data-testid="line-chart"]').exists()).toBe(true)
  })

  it('degrades a non-numeric tag to its original detail without drawing a zero-valued chart', () => {
    telemetryPageMocks.historyItems = [
      {
        itemType: 'sample',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: 'running',
        occurredAtUtc: '2026-07-02T07:30:00.000Z',
      },
    ]

    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.find('[data-testid="line-chart"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('没有可绘制的数值样本')
    expect(wrapper.text()).toContain('running')
  })

  it.each([
    ['403 forbidden', '没有权限执行此操作。'],
    ['network timeout', '网络异常，请检查连接后重试。'],
  ])('shows a clear failure state for %s without an empty chart', (message, expected) => {
    telemetryPageMocks.historyError = new Error(message)

    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.text()).toContain(expected)
    expect(wrapper.find('[data-testid="line-chart"]').exists()).toBe(false)
  })

  it('counts only unavailable runtime windows as unavailable windows', () => {
    const wrapper = mount(TelemetryOeePage, { global: { stubs } })

    expect(wrapper.text()).toMatch(/不可用窗口\s*1/)
  })

  it('requires a numeric threshold before saving an alarm rule', async () => {
    const wrapper = mount(TelemetryAlarmRulesPage, { global: { stubs } })
    const vm = wrapper.vm as unknown as {
      form: {
        alarmCode: string
        comparisonOperator: string
        deviceAssetId: string
        ruleCode: string
        tagKey: string
        thresholdValue?: string | number
        unitCode: string
      }
      submitRule: () => Promise<void>
    }

    Object.assign(vm.form, {
      alarmCode: 'ALM-TEMP-HIGH',
      comparisonOperator: '>',
      deviceAssetId: 'DEV-CNC-01',
      ruleCode: 'TEMP_HIGH',
      tagKey: 'temperature',
      thresholdValue: '',
      unitCode: 'CEL',
    })
    await vm.submitRule()

    expect(telemetryPageMocks.saveAlarmRule).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请填写设备、规则、报警、采集标签、阈值和单位。')
  })
})

/** 把 ISO 瞬时读回本地日历日（YYYY-MM-DD），断言不依赖运行机器的时区。 */
function toLocalDay(value: string) {
  const date = new Date(value)
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}
