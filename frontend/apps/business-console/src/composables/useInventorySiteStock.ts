/**
 * 全厂库存总览：不选物料也能看到的库存表。
 *
 * 背景（重要，别当成可以简化掉的实现细节）：
 * 库存域**没有**「不带物料的全量库存读面」。唯一的行级台账查询
 * `getBusinessConsoleInventoryAvailability` 强制 skuCode + uomCode + siteCode 三者必填、
 * 且不分页。`listBusinessConsoleInventoryExpiryAlerts` 虽然只要工厂就能跨物料出行，
 * 但它在 SQL 层硬过滤「有效期且临期/过期」，无效期的常规库存永远查不出来，
 * 拿它冒充全厂库存是错的。
 *
 * 所以首屏在后端补齐之前，这里用**按物料目录并发扫描 + 前端聚合**把可见集合做到最大：
 * 逐个物料真实查台账，再按物料汇总成一行。这不是伪造数据——每一行都来自真实查询；
 * 代价是覆盖面受扫描批次限制，因此 `scannedSkuCount` / `totalSkuCount` / `hasMore`
 * 必须在界面上如实说清楚，并给「继续扫描」的出路。
 *
 * 后端补齐 `GET /api/business-console/v1/inventory/ledgers`（工厂可选、分页）之后，
 * 这个 composable 应当整体删除、页面直接换成那个读面。缺口已登记在
 * `docs/architecture/inventory-module-product-design.md` 的「后端缺口」一节。
 */
import {
  getBusinessConsoleInventoryAvailability,
  type BusinessConsoleInventoryAvailabilityLineResponse,
} from '@nerv-iip/api-client'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'
import { useInventoryScopeCatalog } from './useInventoryScope'

/** 一次「继续扫描」推进的物料数——一屏能看完的量级，也避免一次打出上百个请求。 */
export const SITE_STOCK_SCAN_BATCH = 24
/** 并发上限：既让首屏快，又不至于把网关打满。 */
const SCAN_CONCURRENCY = 6

/**
 * 扫描过程中拿到的**逐行台账**（库位 × 批次 × 序列号 × 质量状态 × 货主）。
 * 批次与预留页要的就是这一层：它自带 skuCode，可以直接铺成全厂批次表。
 */
export interface SiteStockLine extends BusinessConsoleInventoryAvailabilityLineResponse {
  skuCode: string
  skuName: string
  uomCode: string
}

/** 按物料汇总后的一行「全厂库存」。 */
export interface SiteStockRow {
  skuCode: string
  skuName: string
  uomCode: string
  onHandQuantity: number
  reservedQuantity: number
  availableQuantity: number
  /** 该物料在本厂占用的台账行数（库位 × 批次 × 序列号 × 质量状态…）。 */
  lineCount: number
  /** 该物料分布的库位数——仓管判断「要不要归并」的直接依据。 */
  locationCount: number
  /** 最早到期日；不追效期物料为 undefined。 */
  earliestExpiry?: string
  /**
   * 该物料是否配置了效期追踪（任一台账行带保质期或效期）。
   * 用来区分「本来就不追效期」（成品总成等，中性事实）与
   * 「配置了保质期却缺效期数据」（真正需要警示的缺数据），见 #1418 B2。
   */
  tracksShelfLife: boolean
  /** 是否存在冻结/不可动用的台账行（质量冻结 / 盘点冻结 / 过期），与是否追效期无关。 */
  hasBlocked: boolean
}

function toRow(
  skuCode: string,
  skuName: string,
  uomCode: string,
  lines: BusinessConsoleInventoryAvailabilityLineResponse[],
  totals: { onHand: number; reserved: number; available: number },
): SiteStockRow {
  const locations = new Set<string>()
  let earliestExpiry: string | undefined
  let tracksShelfLife = false
  let hasBlocked = false

  for (const line of lines) {
    if (line.locationCode) locations.add(line.locationCode)
    if (line.isBlocked === true) hasBlocked = true
    if (line.shelfLifeDays != null || line.expiryDate != null) tracksShelfLife = true
    const expiry = line.expiryDate ?? undefined
    if (expiry && (earliestExpiry === undefined || expiry < earliestExpiry)) {
      earliestExpiry = expiry
    }
  }

  return {
    skuCode,
    skuName,
    uomCode,
    onHandQuantity: totals.onHand,
    reservedQuantity: totals.reserved,
    availableQuantity: totals.available,
    lineCount: lines.length,
    locationCount: locations.size,
    earliestExpiry,
    tracksShelfLife,
    hasBlocked,
  }
}

/** 简单的并发池：按 limit 并发跑完 items，不用引第三方依赖。 */
async function mapWithConcurrency<TItem, TResult>(
  items: TItem[],
  limit: number,
  run: (item: TItem) => Promise<TResult>,
): Promise<TResult[]> {
  const results: TResult[] = Array.from({ length: items.length })
  let cursor = 0

  async function worker() {
    while (cursor < items.length) {
      const index = cursor
      cursor += 1
      results[index] = await run(items[index]!)
    }
  }

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker))
  return results
}

/**
 * @param siteCode 当前工厂（由页面的筛选条给出，通常来自工厂主数据第一条）
 */
export function useInventorySiteStockOverview(siteCode: () => string) {
  const context = bindBusinessContext(reactive({ organizationId: '', environmentId: '' }))
  const catalog = useInventoryScopeCatalog()

  const rows = shallowRef<SiteStockRow[]>([])
  const lines = shallowRef<SiteStockLine[]>([])
  const scanning = ref(false)
  const scannedSkuCount = ref(0)
  /** 扫描过程中失败的物料数——不能让个别失败静默吞掉，界面要如实提示。 */
  const failedSkuCount = ref(0)
  const scanError = shallowRef<unknown>(undefined)
  /** 换工厂/换目录要作废在途的旧扫描，避免慢响应把新结果覆盖回去。 */
  const scanToken = ref(0)

  const totalSkuCount = computed(() => catalog.skuOptions.value.length)
  const hasMore = computed(() => scannedSkuCount.value < totalSkuCount.value)
  const ready = computed(
    () => hasBusinessContext(context) && siteCode().trim().length > 0 && totalSkuCount.value > 0,
  )

  /** 汇总口径：只统计真正有货的物料，零库存物料不占表格行（可用「显示零库存」放开）。 */
  const nonEmptyRows = computed(() => rows.value.filter((row) => row.lineCount > 0))

  async function scanOne(option: {
    value: string
    label: string
  }): Promise<{ row: SiteStockRow; lines: SiteStockLine[] } | null> {
    const uomCode = catalog.resolveUomCode(option.value)
    // 物料主档还没给出基本单位就跳过这条：台账查询是「单物料单单位」维度，
    // 猜一个单位只会查回空数据，还会让总览把这条算成「零库存」。
    if (!uomCode) return null
    const { data } = await getBusinessConsoleInventoryAvailability({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        skuCode: option.value,
        uomCode,
        siteCode: siteCode().trim(),
      },
    })

    // 总览要的是「这个物料在本厂到底有多少货」，所以不带 qualityStatus / ownerType 过滤，
    // 冻结件与寄售件也要计入现存量，否则总量对不上仓库实物。
    if (!data?.success || !data.data) return null
    const payload = data.data
    const items = payload.items ?? []
    return {
      row: toRow(option.value, option.label, uomCode, items, {
        onHand: payload.onHandQuantity ?? 0,
        reserved: payload.reservedQuantity ?? 0,
        available: payload.availableQuantity ?? 0,
      }),
      // 台账行本身不带物料信息（查询已经按物料定了），铺全厂批次表时要补回去。
      lines: items.map((item) => ({
        ...item,
        skuCode: option.value,
        skuName: option.label,
        uomCode,
      })),
    }
  }

  async function scanBatch(startIndex: number) {
    if (!ready.value) return
    const token = scanToken.value
    const slice = catalog.skuOptions.value.slice(startIndex, startIndex + SITE_STOCK_SCAN_BATCH)
    if (slice.length === 0) return

    scanning.value = true
    try {
      const settled = await mapWithConcurrency(slice, SCAN_CONCURRENCY, async (option) => {
        try {
          return await scanOne(option)
        } catch (error) {
          // 单个物料查失败不该让整张表塌掉，记数后继续。
          scanError.value ??= error
          return null
        }
      })
      // 期间换了工厂就丢弃这批结果。
      if (token !== scanToken.value) return

      const collected = settled.filter(
        (result): result is { row: SiteStockRow; lines: SiteStockLine[] } => result !== null,
      )
      failedSkuCount.value += slice.length - collected.length
      rows.value = [...rows.value, ...collected.map((result) => result.row)]
      lines.value = [...lines.value, ...collected.flatMap((result) => result.lines)]
      scannedSkuCount.value = startIndex + slice.length
    } finally {
      if (token === scanToken.value) scanning.value = false
    }
  }

  function reset() {
    scanToken.value += 1
    rows.value = []
    lines.value = []
    scannedSkuCount.value = 0
    failedSkuCount.value = 0
    scanError.value = undefined
    scanning.value = false
  }

  /** 工厂或物料目录变了就重扫——首屏因此不需要用户点任何东西。 */
  watch(
    [siteCode, totalSkuCount, () => context.organizationId, () => context.environmentId],
    () => {
      reset()
      if (ready.value) void scanBatch(0)
    },
    { immediate: true },
  )

  return {
    /** 有货的物料行（默认表格数据源）。 */
    siteStockRows: nonEmptyRows,
    /** 含零库存在内的全部已扫描物料行。 */
    siteStockAllRows: computed(() => rows.value),
    /** 逐行台账（批次/序列号维度），批次与预留页的首屏数据源。 */
    siteStockLines: computed(() => lines.value),
    /** 只保留带批次或序列号的台账行——批次页关心的是可追溯单元。 */
    siteStockTrackedLines: computed(() =>
      lines.value.filter((line) => Boolean(line.lotNo) || Boolean(line.serialNo)),
    ),
    siteStockScanning: scanning,
    siteStockError: scanError,
    siteStockFailedCount: computed(() => failedSkuCount.value),
    siteStockScannedCount: computed(() => scannedSkuCount.value),
    siteStockTotalSkuCount: totalSkuCount,
    siteStockHasMore: hasMore,
    siteStockCatalogPending: catalog.skusPending,
    scanMoreSiteStock: () => scanBatch(scannedSkuCount.value),
    refreshSiteStock: () => {
      reset()
      return ready.value ? scanBatch(0) : Promise.resolve()
    },
  }
}
