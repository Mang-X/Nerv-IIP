import { computed } from 'vue'
import {
  useBusinessSkus,
  useBusinessMasterDataResources,
} from '@/composables/useBusinessMasterData'

/**
 * MES 列表显示名前端解析（兜底 facade 当前为 null 的 *Name，见 #461）。
 * 用法：accessor 写 `r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无'`，
 * 后端回填 *Name 后自动优先用之、本兜底极少命中，可随后端落地移除。
 */
export function useMesDisplayNames() {
  const { skus } = useBusinessSkus()
  const { resources: workCenters } = useBusinessMasterDataResources('work-center')

  const skuByCode = computed(() => {
    const m = new Map<string, string>()
    for (const s of skus.value) if (s.code) m.set(s.code, s.displayName ?? s.code)
    return m
  })
  const workCenterByCode = computed(() => {
    const m = new Map<string, string>()
    for (const w of workCenters.value) if (w.code) m.set(w.code, w.displayName ?? w.code)
    return m
  })

  function resolveSku(code?: string | null): string | undefined {
    if (!code) return undefined
    return skuByCode.value.get(code) ?? code
  }
  /**
   * 物料展示串：优先主数据显示名，其次 facade 返回的人读编码。
   * 若拿到的是系统 GUID（部分 facade 仍回内部标识），一律不上屏——
   * 界面不暴露工程标识，读者也无法用它去任何地方查物料。
   */
  function resolveSkuLabel(value?: string | null): string {
    if (!value || SYSTEM_ID_PATTERN.test(value)) return '未指定物料'
    return skuByCode.value.get(value) ?? value
  }
  function resolveWorkCenter(code?: string | null): string | undefined {
    if (!code) return undefined
    return workCenterByCode.value.get(code) ?? code
  }

  return { resolveSku, resolveSkuLabel, resolveWorkCenter }
}

/** 系统内部标识（GUID）形态，用于判断某个值是否可以上屏。 */
const SYSTEM_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
