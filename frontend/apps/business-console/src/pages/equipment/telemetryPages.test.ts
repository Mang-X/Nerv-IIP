import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import TelemetryAlarmRulesPage from './telemetry/alarm-rules.vue'
import TelemetryHistoryPage from './telemetry/history.vue'
import TelemetryOeePage from './telemetry/oee.vue'
import TelemetryTagsPage from './telemetry/tags.vue'

const telemetryPageMocks = vi.hoisted(() => ({
  historyError: undefined as unknown,
  historyItems: [] as Array<Record<string, unknown>>,
  replaceRoute: vi.fn(),
  saveAlarmRule: vi.fn(),
}))

vi.mock('@nerv-iip/ui', () => ({
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
  NvField: { template: '<div><slot /></div>' },
  NvFieldError: { props: ['errors'], template: '<div>{{ errors?.join(" ") }}</div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: { template: '<input />' },
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
  return {
    ...actual,
    RouterLink: { props: ['to'], template: '<a><slot /></a>' },
    useRoute: () => ({
      query: {
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        windowEndUtc: '2026-07-02T08:00:00.000Z',
        windowStartUtc: '2026-07-02T00:00:00.000Z',
      },
    }),
    useRouter: () => ({ replace: telemetryPageMocks.replaceRoute }),
  }
})

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
    filters: {
      deviceAssetId: 'DEV-CNC-01',
      tagKey: 'temperature',
      windowStartUtc: '2026-07-02T00:00:00.000Z',
      windowEndUtc: '2026-07-02T08:00:00.000Z',
    },
    historyError: shallowRef(telemetryPageMocks.historyError),
    historyItems: computed(() => []),
    historyPending: shallowRef(false),
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
  NvInput: { template: '<input />' },
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

    expect(wrapper.get('[data-testid="line-chart"]').text()).toContain('2 遥测值')
    expect(wrapper.text()).toContain('最新值 87.5')
    expect(wrapper.text()).toContain('最小值 82.25')
    expect(wrapper.text()).toContain('最大值 87.5')
    expect(wrapper.text()).toContain('样本数 2')
    expect(wrapper.get('[data-testid="timeline"]').text()).toContain('报警记录')
    expect(wrapper.get('[data-testid="timeline"]').text()).toContain('状态记录')
  })

  it('uses local datetime inputs and preserves the complete query scope', () => {
    const wrapper = mount(TelemetryHistoryPage, { global: { stubs } })

    expect(wrapper.findAll('input[type="datetime-local"]')).toHaveLength(2)
    expect(telemetryPageMocks.replaceRoute).toHaveBeenCalledWith({
      query: expect.objectContaining({
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        windowEndUtc: '2026-07-02T08:00:00.000Z',
        windowStartUtc: '2026-07-02T00:00:00.000Z',
      }),
    })
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
