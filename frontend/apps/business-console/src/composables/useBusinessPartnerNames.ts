import { computed } from 'vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'

/**
 * 业务伙伴（客户 / 供应商 / 承运商）显示名解析。
 *
 * 背景：ERP 销售、采购、财务的读面只回 `customerCode` / `supplierCode`（后端缺 *Name 字段，
 * 已登记后端缺口），界面上直接摆 `CUST-WB-001` 没人看得懂是谁。客户名在主数据
 * BusinessPartner 里且是中文，这里按 code 建索引在前端 join 出来。
 *
 * 用法：主列显示 `resolvePartner(code)`（名称），编号降为次要信息放副行。
 * 后端读面补上 *Name 后应优先用之，本兜底可随之移除。
 */
export function useBusinessPartnerNames() {
  const partnerSource = useBusinessMasterDataResources('business-partner')
  // 与 `useErpPickerCatalog` 的目录口径对齐（CATALOG_TAKE=500）：默认 100 条时，
  // 伙伴数过百就会有一批查不到名字、界面上退回显编码，且没有任何报错提示。
  partnerSource.filters.take = 500
  const { resources: partners, resourcesPending: partnersPending } = partnerSource

  const partnerByCode = computed(() => {
    const map = new Map<string, string>()
    for (const partner of partners.value) {
      if (partner.code) map.set(partner.code, partner.displayName ?? partner.code)
    }
    return map
  })

  /**
   * 伙伴名称；名录里查不到就返回 undefined，由调用方决定说法——
   * 不在这里编一个「未知客户」，那会把「没填客户」和「名录还没加载出来」混为一谈。
   */
  function resolvePartner(code?: string | null): string | undefined {
    if (!code) return undefined
    return partnerByCode.value.get(code)
  }

  /** 伙伴展示串：优先中文名，名录缺失时退回编号（至少不空）。 */
  function resolvePartnerLabel(code?: string | null, fallback = '未指定'): string {
    if (!code) return fallback
    return partnerByCode.value.get(code) ?? code
  }

  return { partnerByCode, partners, partnersPending, resolvePartner, resolvePartnerLabel }
}
