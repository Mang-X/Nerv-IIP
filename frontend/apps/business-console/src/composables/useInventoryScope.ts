/**
 * 库存查询范围（物料 / 单位 / 工厂）的目录、默认值与全厂效期概览。
 *
 * 背景：库存可用量读面（`getBusinessConsoleInventoryAvailability`）是**单物料单单位**的台账
 * 维度查询，后端强制 skuCode + uomCode + siteCode 三者必填，没有「不选物料的全局汇总」。
 * 过去这三项在前端都是空字符串的自由输入框，首屏因此永远不发请求、永远空白。
 *
 * 这里把三项拆成三种不同性质：
 * - **物料**：唯一需要人做的选择，走物料主数据的可搜索选择弹窗，只选不填。
 * - **单位**：不是独立选择项——台账维度上单位由物料决定（原材料按品类是 kg / l，
 *   计件件号才是 pcs），所以跟随所选物料的基本单位自动带出，避免手输错单位查不到货。
 * - **工厂**：绝大多数现场只有一个工厂，默认取工厂主数据的第一条，不劳用户先选。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { useInventoryExpiryAlerts } from './useBusinessInventory'
import { toBaseUomBySku } from './skuBaseUom'
import { useBusinessMasterDataResources, useBusinessSkus } from './useBusinessMasterData'

/**
 * 工厂主数据尚未就绪（目录为空 / 加载失败）时的兜底工厂编码。
 * 与库存台账种子写入的站点一致，保证单工厂现场首屏仍能出数；主数据一旦返回工厂就以主数据为准。
 */
export const FALLBACK_INVENTORY_SITE_CODE = 'SITE-001'

/** 目录里挑不到「物料主数据」时的取数上限——库存现场的物料目录量级在数百条。 */
const CATALOG_TAKE = 500

/** 库存与 WMS 两侧的筛选条件对象形状不同（后者三项都可选），这里按可选取交集。 */
export interface InventoryScopeFilters {
  skuCode?: string
  uomCode?: string
  siteCode?: string
}

function toOption(code?: string | null, name?: string | null, hint?: string | null) {
  const value = code?.trim()
  if (!value) return []
  return [{ value, label: name?.trim() || value, ...(hint?.trim() ? { hint: hint.trim() } : {}) }]
}

/**
 * 物料与工厂目录（含各自的加载态与来源说明），供实体选择弹窗直接消费。
 */
export function useInventoryScopeCatalog() {
  const skuCatalog = useBusinessSkus()
  const siteCatalog = useBusinessMasterDataResources('site')
  skuCatalog.filters.take = CATALOG_TAKE
  siteCatalog.filters.take = CATALOG_TAKE

  const skuOptions = computed<EntityPickerOption[]>(() =>
    skuCatalog.skus.value.flatMap((sku) => toOption(sku.code, sku.displayName, sku.baseUomCode)),
  )
  const siteOptions = computed<EntityPickerOption[]>(() =>
    siteCatalog.resources.value.flatMap((site) => toOption(site.code, site.displayName)),
  )
  const baseUomBySku = toBaseUomBySku(skuCatalog.skus)
  const defaultSiteCode = computed(
    () => siteOptions.value[0]?.value ?? FALLBACK_INVENTORY_SITE_CODE,
  )

  return {
    baseUomBySku,
    defaultSiteCode,
    /**
     * 所选物料的基本单位；目录里查不到就返回空串，绝不猜一个通用单位。
     * 单位是物料主档的事实（钢材 kg、油品 l、计件件号才是 pcs），主档建物料时 baseUomCode 必填，
     * 查不到只可能是目录还没到——那就等目录（本 computed 一变，调用侧的 watch 会重算）。
     * 猜出来的单位查库存查不到货，写库存更会让后端单位换算失败。
     */
    resolveUomCode: (skuCode: string) => baseUomBySku.value.get(skuCode.trim()) ?? '',
    siteOptions,
    sitesPending: siteCatalog.resourcesPending,
    skuOptions,
    skusPending: skuCatalog.skusPending,
  }
}

/**
 * 把目录接到一组库存筛选条件上：工厂缺省填默认工厂，单位始终跟随所选物料。
 * 页面只需要负责让用户选物料。
 */
export function useInventoryScopeDefaults(filters: InventoryScopeFilters) {
  const catalog = useInventoryScopeCatalog()

  watch(
    catalog.defaultSiteCode,
    (siteCode) => {
      // 深链带进来的工厂优先，不被默认值盖掉。
      if (!(filters.siteCode ?? '').trim()) filters.siteCode = siteCode
    },
    { immediate: true },
  )
  watch(
    [() => filters.skuCode, catalog.baseUomBySku],
    ([skuCode]) => {
      const trimmed = (skuCode ?? '').trim()
      filters.uomCode = trimmed ? catalog.resolveUomCode(trimmed) : ''
    },
    { immediate: true },
  )

  return catalog
}

/**
 * 全厂效期概览：`listBusinessConsoleInventoryExpiryAlerts` 是库存域唯一「只要工厂就能跨物料
 * 出行」的读面，所以首屏在用户选物料之前先给这块**真实**信息。
 *
 * 口径要说清楚：它只覆盖**有效期且已过期或临近到期**的台账行，不是全厂库存总量——
 * 后端没有不带物料的库存汇总查询（缺口见 `artifacts/ui-remediation/inventory-batch-backend-findings.md`），
 * 所以这里不冒充总量。
 */
export function useInventorySiteExpiryOverview(siteCode: () => string) {
  const query = useInventoryExpiryAlerts(() => siteCode().trim().length > 0)
  query.expiryAlertsPageSize.value = 5

  watch(
    siteCode,
    (code) => {
      query.filters.siteCode = code
    },
    { immediate: true },
  )

  const response = query.expiryAlertsResponse

  return {
    overviewError: query.expiryAlertsError,
    overviewExpiredCount: computed(() => response.value?.expiredCount ?? 0),
    overviewNearExpiryCount: computed(() => response.value?.nearExpiryCount ?? 0),
    overviewPending: query.expiryAlertsPending,
    overviewSkuCount: computed(() => response.value?.skuCount ?? 0),
    overviewTotalCount: computed(() => response.value?.totalCount ?? 0),
    /** 最早到期的几条批次，给「现在该先动哪一批」一个落点。 */
    overviewUrgentLines: query.expiryAlerts,
    refreshOverview: query.refreshExpiryAlerts,
  }
}
