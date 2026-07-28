import { computed } from 'vue'
import { useBusinessSkus } from '@/composables/useBusinessMasterData'

/**
 * 物料显示名解析（按 SKU 编码查主数据中文名）。
 *
 * 背景：ERP / WMS / 质量等读面多数只回 `skuCode`（`RM-BAR-01`），单看编码判断不出是什么料。
 * 名称在主数据 SKU 里且是中文，这里在前端 join 出来；读面补上 *Name 后应优先用之。
 *
 * 与 `useMesDisplayNames` 的分工：那个是 MES 页面的组合解析（工作中心/班次/工人一起加载），
 * 这里只解析物料名，供非 MES 页面按需引用，不额外拉无关名录。
 */
export function useSkuNames() {
  const skuSource = useBusinessSkus()
  // 与 `useErpPickerCatalog` 的目录口径对齐（CATALOG_TAKE=500）：默认 100 条时，
  // 物料数过百就会有一批查不到名字、界面上退回显编码，且没有任何报错提示。
  skuSource.filters.take = 500
  const { skus, skusPending } = skuSource

  const skuByCode = computed(() => {
    const map = new Map<string, string>()
    for (const sku of skus.value) {
      if (sku.code) map.set(sku.code, sku.displayName ?? sku.code)
    }
    return map
  })

  /** 物料名称；名录里查不到返回 undefined，由调用方决定说法（不编造名字）。 */
  function resolveSkuName(code?: string | null): string | undefined {
    if (!code) return undefined
    return skuByCode.value.get(code)
  }

  /** 物料展示串：优先中文名，名录缺失时退回编码。 */
  function resolveSkuLabel(code?: string | null, fallback = '未指定物料'): string {
    if (!code) return fallback
    return skuByCode.value.get(code) ?? code
  }

  return { resolveSkuLabel, resolveSkuName, skuByCode, skusPending }
}
