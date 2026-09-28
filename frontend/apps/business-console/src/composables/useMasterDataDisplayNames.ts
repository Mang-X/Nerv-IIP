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
  /** 车间（workshop）。 */
  workshops?: boolean
  /** 产线（production-line）。 */
  lines?: boolean
  /** 工厂（site）。 */
  sites?: boolean
  /** 班次（shift）。 */
  shifts?: boolean
  /** 员工（worker）：把登录账号 userId 解析成姓名。 */
  users?: boolean
}

/**
 * 主数据显示名解析（按需加载名录，只付用到的那几次请求）。
 *
 * 背景：设备 / 库存 / WMS / 维保多数读面只回 `deviceAssetId`、`locationCode`、`uomCode`，
 * 界面上只有编码没有名称。名称在主数据里且是中文，这里在前端按编码 join 出来；
 * 设备引用既可能是设备编码也可能是设备公开 ID，两种都能解析。
 * 读面补上 *Name 字段后应优先用之，本兜底可随之移除。
 *
 * 用法：`const { resolveDevice } = useMasterDataDisplayNames({ devices: true })`
 * 然后 `r.deviceAssetName ?? resolveDevice(r.deviceAssetId) ?? readFaceText(r.deviceAssetId)`
 * （`readFaceText` 挡住公开 ID，不把原始 ID 送上屏）。
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
    // 历史单据可能引用已停用的主数据，名称照样要解析出来。
    catalog.filters.includeDisabled = true
    return catalog
  }

  const deviceSource = source(options.devices, 'device-asset')
  const locationSource = source(options.locations, 'location')
  const workCenterSource = source(options.workCenters, 'work-center')
  const teamSource = source(options.teams, 'team')
  const uomSource = source(options.uoms, 'unit-of-measure')
  const workshopSource = source(options.workshops, 'workshop')
  const lineSource = source(options.lines, 'production-line')
  const siteSource = source(options.sites, 'site')
  const shiftSource = source(options.shifts, 'shift')
  const userSource = source(options.users, 'worker')

  function indexOf(items: { code?: string | null; displayName?: string | null }[] | undefined) {
    const map = new Map<string, string>()
    for (const item of items ?? []) {
      if (item.code) map.set(item.code, item.displayName ?? item.code)
    }
    return map
  }

  // 设备引用在全平台可以是设备编码，也可以是设备公开 ID（GUID），两种都要解析到同一台设备。
  const deviceByReference = computed(() => {
    const map = new Map<string, { code: string; displayName: string }>()
    for (const item of deviceSource?.resources.value ?? []) {
      if (!item.code) continue
      const device = { code: item.code, displayName: item.displayName ?? item.code }
      map.set(item.code, device)
      if (item.deviceAssetId) map.set(item.deviceAssetId, device)
    }
    return map
  })
  const locationByCode = computed(() => indexOf(locationSource?.resources.value))
  const workCenterByCode = computed(() => indexOf(workCenterSource?.resources.value))
  const teamByCode = computed(() => indexOf(teamSource?.resources.value))
  const uomByCode = computed(() => indexOf(uomSource?.resources.value))
  const workshopByCode = computed(() => indexOf(workshopSource?.resources.value))
  const lineByCode = computed(() => indexOf(lineSource?.resources.value))
  const siteByCode = computed(() => indexOf(siteSource?.resources.value))
  const shiftByCode = computed(() => indexOf(shiftSource?.resources.value))
  const userById = computed(() => {
    const map = new Map<string, string>()
    for (const item of userSource?.resources.value ?? []) {
      if (item.userId) map.set(item.userId, item.displayName ?? item.code ?? item.userId)
    }
    return map
  })

  const resolver = (index: typeof locationByCode) => (code?: string | null) => {
    if (!code) return undefined
    return index.value.get(code)
  }

  return {
    /** 设备名（按设备编码或设备公开 ID）；查不到返回 undefined（不编造名字）。 */
    resolveDevice: (reference?: string | null) =>
      reference ? deviceByReference.value.get(reference)?.displayName : undefined,
    /** 设备编码（按设备编码或设备公开 ID）；查不到返回 undefined。 */
    resolveDeviceCode: (reference?: string | null) =>
      reference ? deviceByReference.value.get(reference)?.code : undefined,
    resolveLocation: resolver(locationByCode),
    resolveWorkCenter: resolver(workCenterByCode),
    resolveTeam: resolver(teamByCode),
    resolveUom: resolver(uomByCode),
    resolveWorkshop: resolver(workshopByCode),
    resolveLine: resolver(lineByCode),
    resolveSite: resolver(siteByCode),
    resolveShift: resolver(shiftByCode),
    /** 员工姓名；账号不在员工名录里（如系统管理员）时返回 undefined，由调用方显示「—」。 */
    resolveUser: resolver(userById),
    /** 计量单位展示串：「件 (pcs)」，名录缺失时只显编码。 */
    formatUom(code?: string | null, fallback = ''): string {
      if (!code) return fallback
      const name = uomByCode.value.get(code)
      return name && name !== code ? `${name} (${code})` : code
    },
    locationByCode,
    workCenterByCode,
    teamByCode,
    uomByCode,
    workshopByCode,
    lineByCode,
    siteByCode,
    shiftByCode,
  }
}
