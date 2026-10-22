import { describe, expect, it, vi } from 'vitest'
import { computed, effectScope, reactive, shallowRef } from 'vue'
import { useTelemetryTagCatalog } from './useEquipmentPickerCatalog'

vi.mock('./useBusinessTelemetry', () => ({
  useBusinessTelemetryTags: () => ({
    filters: reactive({ deviceAssetId: '' }),
    tagsPending: shallowRef(false),
    tags: computed(() => [
      {
        tagKey: 'spindle.speed',
        displayName: ' 主轴设定转速 ',
        unitCode: 'rpm',
        valueType: 'number',
      },
      { tagKey: 'temperature', displayName: null, unitCode: 'degC', valueType: 'number' },
      { tagKey: 'connector.private', displayName: '  ', valueType: 'text' },
    ]),
  }),
}))

describe('registered telemetry candidates', () => {
  it('uses registered names, keeps original keys and units, and does not invent missing names', () => {
    const scope = effectScope()
    const catalog = scope.run(() => useTelemetryTagCatalog('EQ00001'))!
    expect(catalog.tagOptions.value).toEqual(
      expect.arrayContaining([
        { value: 'spindle.speed', label: '主轴设定转速', hint: 'rpm · 数值' },
        { value: 'temperature', label: '—', hint: 'degC · 数值' },
        { value: 'connector.private', label: '—', hint: '文本' },
      ]),
    )
    expect(catalog.unitByTagKey.value.get('spindle.speed')).toBe('rpm')
    scope.stop()
  })
})
