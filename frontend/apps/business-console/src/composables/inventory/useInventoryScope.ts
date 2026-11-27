import type { EntityPickerOption } from '@nerv-iip/ui'
import { computed, reactive, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useInventoryExpiryAlerts } from './useBusinessInventory'
import { toBaseUomBySku } from '../master-data/skuBaseUom'
import { useBusinessMasterDataResources, useBusinessSkus } from '../master-data'

const siteSelections = reactive(new Map<string, string>())
const SITE_STORAGE_PREFIX = 'nerv-iip.business-console.inventory-site.v1:'

function storedSite(key: string) {
  try {
    return globalThis.localStorage?.getItem(SITE_STORAGE_PREFIX + key)
  } catch {
    return undefined
  }
}

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
    siteCatalog.resourcesError?.value
      ? []
      : siteCatalog.resources.value.flatMap((site) => toOption(site.code, site.displayName)),
  )
  const baseUomBySku = toBaseUomBySku(skuCatalog.skus)

  return {
    baseUomBySku,
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
export function useInventoryScopeDefaults(
  filters: InventoryScopeFilters,
  linkedSite: () => string = () => '',
  options: { rememberSite?: boolean } = {},
) {
  const catalog = useInventoryScopeCatalog()

  const context = useBusinessContextStore()
  const auth = useAuthStore()
  const rememberSite = options.rememberSite ?? true
  const selectionKey = computed(() =>
    JSON.stringify([
      auth.principal?.principalId || auth.sessionId || 'unrestored-session',
      context.organizationId,
      context.environmentId,
    ]),
  )
  const localSelection = ref<string>()
  watch(
    [linkedSite, selectionKey],
    () => {
      localSelection.value = undefined
    },
    { flush: 'sync' },
  )
  const isLegal = (site?: string | null) =>
    catalog.siteOptions.value.some((option) => option.value === site)
  const siteSelection = computed({
    get: () => {
      if (isLegal(localSelection.value)) return localSelection.value!
      const linked = linkedSite().trim()
      if (isLegal(linked)) return linked
      const remembered = rememberSite
        ? (siteSelections.get(selectionKey.value) ?? storedSite(selectionKey.value))
        : undefined
      if (isLegal(remembered)) return remembered!
      if (isLegal(context.siteCode)) return context.siteCode
      return catalog.siteOptions.value.length === 1 ? catalog.siteOptions.value[0]!.value : ''
    },
    set: (site: string) => {
      if (!isLegal(site)) return
      localSelection.value = site
      if (!rememberSite) return
      const key = selectionKey.value
      siteSelections.set(key, site)
      try {
        globalThis.localStorage?.setItem(SITE_STORAGE_PREFIX + key, site)
      } catch {
        // 内存选择仍可在本次会话继承。
      }
    },
  })
  watch(
    siteSelection,
    (site) => {
      filters.siteCode = site
    },
    { immediate: true, flush: 'sync' },
  )
  watch(
    [() => filters.skuCode, catalog.baseUomBySku],
    ([skuCode]) => {
      const trimmed = (skuCode ?? '').trim()
      filters.uomCode = trimmed ? catalog.resolveUomCode(trimmed) : ''
    },
    { immediate: true },
  )

  return { ...catalog, siteSelection }
}

/**
 * 全厂效期概览：`listBusinessConsoleInventoryExpiryAlerts` 是库存域唯一「只要工厂就能跨物料
 * 出行」的读面，所以首屏在用户选物料之前先给这块**真实**信息。
 *
 * 口径要说清楚：它只覆盖**有效期且已过期或临近到期**的台账行，不是全厂库存总量——
 * 后端没有不带物料的库存汇总查询，
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
