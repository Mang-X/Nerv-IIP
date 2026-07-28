import { computed } from 'vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'

export interface MasterDataDisplayNameOptions {
  /** 设备台账（device-asset）：把 deviceAssetId / 设备编码解析成设备名。 */
  devices?: boolean
  /** 库位（location）：把 locationCode 解析成库位名。 */
  locations?: boolean
  /** 工作中心（work-center）。 */
  workCenters?: boolean
  /** 班组（team）。 */
  teams?: boolean
  /** 计量单位（unit-of-measure）。 */
  uoms?: boolean
}

/**
 * 主数据显示名解析（按需加载名录，只付用到的那几次请求）。
 *
 * 背景：设备 / 库存 / WMS / 维保多数读面只回 `deviceAssetId`、`locationCode`、`uomCode`，
 * 界面上只有编码没有名称。名称在主数据里且是中文，这里在前端按编码 join 出来。
 * 读面补上 *Name 字段后应优先用之，本兜底可随之移除。
 *
 * 用法：`const { resolveDevice } = useMasterDataDisplayNames({ devices: true })`
 * 然后 `r.deviceAssetName ?? resolveDevice(r.deviceAssetId) ?? r.deviceAssetId`。
 */
/**
 * 名录取数上限，与 `useErpPickerCatalog` / `useEquipmentPickerCatalog` 的 CATALOG_TAKE 一致。
 * 默认 100 条时，设备或库位数过百就会有一批查不到名字、界面上悄悄退回显编码。
 */
const CATALOG_TAKE = 500

export function useMasterDataDisplayNames(options: MasterDataDisplayNameOptions = {}) {
  function source(enabled: boolean | undefined, resourceType: string) {
    if (!enabled) return undefined
    const catalog = useBusinessMasterDataResources(resourceType)
    catalog.filters.take = CATALOG_TAKE
    return catalog
  }

  const deviceSource = source(options.devices, 'device-asset')
  const locationSource = source(options.locations, 'location')
  const workCenterSource = source(options.workCenters, 'work-center')
  const teamSource = source(options.teams, 'team')
  const uomSource = source(options.uoms, 'unit-of-measure')

  function indexOf(items: { code?: string | null; displayName?: string | null }[] | undefined) {
    const map = new Map<string, string>()
    for (const item of items ?? []) {
      if (item.code) map.set(item.code, item.displayName ?? item.code)
    }
    return map
  }

  const deviceByCode = computed(() => indexOf(deviceSource?.resources.value))
  const locationByCode = computed(() => indexOf(locationSource?.resources.value))
  const workCenterByCode = computed(() => indexOf(workCenterSource?.resources.value))
  const teamByCode = computed(() => indexOf(teamSource?.resources.value))
  const uomByCode = computed(() => indexOf(uomSource?.resources.value))

  const resolver = (index: typeof deviceByCode) => (code?: string | null) => {
    if (!code) return undefined
    return index.value.get(code)
  }

  return {
    /** 设备名；查不到返回 undefined（不编造名字）。 */
    resolveDevice: resolver(deviceByCode),
    resolveLocation: resolver(locationByCode),
    resolveWorkCenter: resolver(workCenterByCode),
    resolveTeam: resolver(teamByCode),
    resolveUom: resolver(uomByCode),
    /** 计量单位展示串：「件 (pcs)」，名录缺失时只显编码。 */
    formatUom(code?: string | null, fallback = ''): string {
      if (!code) return fallback
      const name = uomByCode.value.get(code)
      return name && name !== code ? `${name} (${code})` : code
    },
    deviceByCode,
    locationByCode,
    workCenterByCode,
    teamByCode,
    uomByCode,
  }
}
