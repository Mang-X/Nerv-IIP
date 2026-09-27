import { describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'

import { useMasterDataDisplayNames } from './useMasterDataDisplayNames'

const deviceRows = [
  {
    code: 'EQ00001',
    displayName: '五轴加工中心',
    deviceAssetId: '0f8fad5b-d9cb-469f-a165-70867728950e',
  },
  { code: 'EQ00002', displayName: '清洗机', deviceAssetId: null },
]

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: string) => ({
    filters: { take: 0 },
    resources: computed(() => (resourceType === 'device-asset' ? deviceRows : [])),
  }),
}))

describe('useMasterDataDisplayNames 设备解析', () => {
  it('设备编码与设备公开 ID 解析到同一台设备的名称和编码', () => {
    const { resolveDevice, resolveDeviceCode } = useMasterDataDisplayNames({ devices: true })

    expect(resolveDevice('EQ00001')).toBe('五轴加工中心')
    expect(resolveDevice('0f8fad5b-d9cb-469f-a165-70867728950e')).toBe('五轴加工中心')
    expect(resolveDeviceCode('0f8fad5b-d9cb-469f-a165-70867728950e')).toBe('EQ00001')
    expect(resolveDeviceCode('EQ00002')).toBe('EQ00002')
  })

  it('名录里没有的引用不编造名字或编码', () => {
    const { resolveDevice, resolveDeviceCode } = useMasterDataDisplayNames({ devices: true })

    expect(resolveDevice('11111111-2222-3333-4444-555555555555')).toBeUndefined()
    expect(resolveDeviceCode('11111111-2222-3333-4444-555555555555')).toBeUndefined()
  })
})
