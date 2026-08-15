import { describe, expect, it, vi } from 'vitest'
import { computed, nextTick, reactive, shallowRef } from 'vue'

import { useEquipmentScopeSelection } from './useEquipmentScopeSelection'

// 主数据资源目录桩：车间 / 产线 / 设备三套目录，层级关联字段与真实 facade 一致
// （production-line.workshopCode、device-asset.lineCode / workshopCode）。
const catalog = vi.hoisted(() => ({
  workshop: [
    { code: 'WS-01', displayName: '冲压车间' },
    { code: 'WS-02', displayName: '装配车间' },
  ] as Array<Record<string, unknown>>,
  'production-line': [
    { code: 'LN-01', displayName: '冲压一线', workshopCode: 'WS-01' },
    { code: 'LN-02', displayName: '装配一线', workshopCode: 'WS-02' },
  ] as Array<Record<string, unknown>>,
  'device-asset': [
    { code: 'DEV-PRESS-01', displayName: '冲压机 01', workshopCode: 'WS-01', lineCode: 'LN-01' },
    { code: 'DEV-ASSY-01', displayName: '拧紧枪 01', workshopCode: 'WS-02', lineCode: 'LN-02' },
    { code: 'DEV-FLOAT-01', displayName: '未挂产线设备', workshopCode: 'WS-01', lineCode: null },
  ] as Array<Record<string, unknown>>,
}))

vi.mock('./useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: keyof typeof catalog) => ({
    filters: reactive({ take: 0 }),
    resources: computed(() => catalog[resourceType]),
    resourcesError: shallowRef(),
    resourcesPending: shallowRef(false),
    resourcesTotal: computed(() => catalog[resourceType].length),
    refreshResources: vi.fn(),
  }),
}))

describe('useEquipmentScopeSelection', () => {
  it('defaults to the whole plant with every device in scope', () => {
    const { devicesInScope, levels, scopeLabel } = useEquipmentScopeSelection()

    expect(scopeLabel.value).toBe('全厂')
    expect(devicesInScope.value.map((d) => d.code)).toEqual([
      'DEV-PRESS-01',
      'DEV-ASSY-01',
      'DEV-FLOAT-01',
    ])
    expect(levels.value.map((level) => level.key)).toEqual(['workshop', 'line', 'device'])
    expect(levels.value[0]!.allLabel).toBe('全厂')
  })

  it('narrows lines and devices by workshop, including devices without a line', async () => {
    const { devicesInScope, levels, scope, scopeLabel } = useEquipmentScopeSelection()
    scope.value = { workshop: 'WS-01', line: '', device: '' }
    await nextTick()

    expect(scopeLabel.value).toBe('冲压车间（WS-01）')
    expect(levels.value[1]!.options.map((o) => o.value)).toEqual(['LN-01'])
    // 车间收窄：既含挂在本车间产线上的设备，也含直接挂车间、未挂产线的设备。
    expect(devicesInScope.value.map((d) => d.code)).toEqual(['DEV-PRESS-01', 'DEV-FLOAT-01'])
  })

  it('narrows to a single device and labels the scope with its display name', async () => {
    const { devicesInScope, scope, scopeLabel, selectedDevice } = useEquipmentScopeSelection({
      device: 'DEV-ASSY-01',
    })
    await nextTick()

    expect(scope.value.device).toBe('DEV-ASSY-01')
    expect(devicesInScope.value.map((d) => d.code)).toEqual(['DEV-ASSY-01'])
    expect(selectedDevice.value?.displayName).toBe('拧紧枪 01')
    expect(scopeLabel.value).toBe('拧紧枪 01（DEV-ASSY-01）')
  })

  it('silently resets a device that falls out of the narrowed scope', async () => {
    const { scope } = useEquipmentScopeSelection()
    scope.value = { workshop: '', line: 'LN-02', device: 'DEV-PRESS-01' }
    await nextTick()

    // DEV-PRESS-01 不在 LN-02 上：已选设备静默回退到「全部」，而不是留着一个失效值。
    expect(scope.value.device).toBe('')
    expect(scope.value.line).toBe('LN-02')
  })
})
