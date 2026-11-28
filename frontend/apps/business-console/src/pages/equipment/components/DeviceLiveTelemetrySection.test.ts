import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'
import DeviceLiveTelemetrySection from './DeviceLiveTelemetrySection.vue'

vi.mock('@/composables/equipment/useBusinessTelemetry', () => ({
  useBusinessTelemetryTags: () => ({
    filters: reactive({ deviceAssetId: 'DEV-CNC-01' }),
    tags: ref([
      { tagKey: 'temperature', displayName: '主轴温度', unitCode: '°C', latestValue: 79.2 },
      { tagKey: 'speed', displayName: '主轴转速', unitCode: 'rpm', latestValue: 1200 },
    ]),
    tagsPending: ref(false),
    tagsError: ref(),
  }),
  useBusinessTelemetryAlarmRules: () => ({
    filters: reactive({ deviceAssetId: 'DEV-CNC-01' }),
    alarmRules: ref([
      {
        tagKey: 'temperature',
        comparisonOperator: '>',
        thresholdValue: 72,
        isEnabled: true,
        unitCode: '°C',
      },
    ]),
    alarmRulesError: ref(),
  }),
  useBusinessTelemetryHistory: () => {
    const filters = reactive({ deviceAssetId: 'DEV-CNC-01', tagKey: '' })
    return {
      filters,
      historyError: ref(),
      historyPending: ref(false),
      visibleHistoryItems: computed(() => [
        {
          itemType: 'sample',
          tagKey: filters.tagKey,
          value: filters.tagKey === 'speed' ? '1200' : '79.2',
          occurredAtUtc: '2026-11-28T09:00:00Z',
        },
      ]),
    }
  },
}))

it('highlights the breached reading and changes the curve and threshold when picking a second point', async () => {
  const wrapper = mount(DeviceLiveTelemetrySection, {
    props: { deviceAssetId: 'DEV-CNC-01' },
    global: {
      stubs: {
        NvLineChart: {
          props: ['data', 'series', 'referenceLines'],
          template:
            '<div data-chart>{{ series[0].label }} {{ data[0].value }} <span v-for="line in referenceLines">{{ line.label }}</span></div>',
        },
      },
    },
  })
  expect(wrapper.get('button[aria-pressed=true]').text()).toContain('79.2')
  expect(wrapper.get('button[aria-pressed=true] strong').classes()).toContain('text-destructive')
  expect(wrapper.get('[data-chart]').text()).toContain('> 72')
  await wrapper
    .findAll('button')
    .find((button) => button.text().includes('主轴转速'))!
    .trigger('click')
  expect(wrapper.get('[data-chart]').text()).toContain('主轴转速 1200')
  expect(wrapper.get('[data-chart]').text()).not.toContain('> 72')
  wrapper.unmount()
})
