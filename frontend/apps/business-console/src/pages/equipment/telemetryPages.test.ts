import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, shallowRef } from 'vue'

import TelemetryAlarmRulesPage from './telemetry/alarm-rules.vue'
import TelemetryHistoryPage from './telemetry/history.vue'
import TelemetryOeePage from './telemetry/oee.vue'
import TelemetryTagsPage from './telemetry/tags.vue'

const telemetryPageMocks = vi.hoisted(() => ({
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
      },
    }),
  }
})

vi.mock('@/composables/useBusinessTelemetry', () => ({
  describeTelemetryOeeLimitations: () =>
    '当前 OEE 只按设备运行状态计算可用率，性能与质量不作为真实测量值；P0 仅用于判断设备运行事实覆盖和停机影响。',
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
    historyError: shallowRef(),
    historyItems: computed(() => []),
    historyPending: shallowRef(false),
    refreshHistory: vi.fn(),
    visibleHistoryItems: computed(() => [
      {
        itemType: 'sample',
        deviceAssetId: 'DEV-CNC-01',
        tagKey: 'temperature',
        value: '87.5',
        occurredAtUtc: '2026-07-02T07:30:00.000Z',
      },
    ]),
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
      performanceRate: 0,
      qualityRate: 0,
      oeeRate: 0,
      performanceRateEstimated: true,
      qualityRateEstimated: true,
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
  BadgePro: { template: '<span><slot /></span>' },
  ButtonPro: { template: '<button><slot /></button>' },
  DataTablePro: {
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
  DropdownMenuProItem: { template: '<div><slot /></div>' },
  FieldPro: { template: '<div><slot /></div>' },
  FieldProError: { props: ['errors'], template: '<div>{{ errors?.join(" ") }}</div>' },
  FieldProGroup: { template: '<div><slot /></div>' },
  FieldProLabel: { template: '<label><slot /></label>' },
  InputPro: { template: '<input />' },
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
  Spinner: { template: '<span />' },
  Toolbar: { template: '<div><slot name="filters" /></div>' },
  DialogPro: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProClose: { template: '<div><slot /></div>' },
  SelectPro: { template: '<div><slot /></div>' },
  SelectProContent: { template: '<div><slot /></div>' },
  SelectProItem: { template: '<div><slot /></div>' },
  SelectProTrigger: { template: '<button><slot /></button>' },
  SelectProValue: { template: '<span><slot /></span>' },
}

describe('equipment telemetry pages', () => {
  beforeEach(() => {
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

  it('shows real tag, rule, history, and OEE fields without claiming estimated factors are measured', () => {
    expect(mount(TelemetryTagsPage, { global: { stubs } }).text()).toContain('temperature')
    expect(mount(TelemetryAlarmRulesPage, { global: { stubs } }).text()).toContain('TEMP_HIGH')
    expect(mount(TelemetryHistoryPage, { global: { stubs } }).text()).toContain('87.5')

    const oeeText = mount(TelemetryOeePage, { global: { stubs } }).text()
    expect(oeeText).toContain('82.0%')
    expect(oeeText).toContain('性能与质量不作为真实测量值')
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
