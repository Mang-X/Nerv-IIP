/**
 * 经营管理（ERP）域的选择器目录：把主数据 / 单据读面映射成 `EntityPickerOption`。
 *
 * 背景：采购、销售、财务几张单据的新建弹窗过去要求手输供应商 / 客户 / 物料 / 单位 /
 * 工厂 / 来源单据编码，任何一个字符敲错都是提交后才报错。这里按「一域一目录」收敛，
 * 让各页只声明用哪几组目录，避免每页重复写一遍映射。
 *
 * 口径：`value` 一律是人读业务编码（提交体不变），`label` 是名称，`hint` 放辅助识别
 * 信息（角色 / 基本单位 / 客户 / 状态）。目录为空时页面用 `empty-text` 指路去维护主数据。
 */
import type { EntityPickerOption } from '@nerv-iip/ui'
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { computed } from 'vue'
import {
  useErpDeliveryOrders,
  useErpPurchaseOrders,
  useErpSalesOrders,
} from './useBusinessErp'
import {
  useBusinessMasterDataResources,
  useBusinessPartners,
  useBusinessSkus,
  useBusinessUoms,
} from './useBusinessMasterData'

/** 主数据目录取数上限——ERP 现场的物料 / 伙伴目录量级在数百条。 */
const CATALOG_TAKE = 500
/** 单据目录取数上限：来源单据只在「最近的在办单据」里挑，不做全量拉取。 */
const DOCUMENT_CATALOG_TAKE = 200

function toOption(
  code?: string | null,
  name?: string | null,
  hint?: string | null,
): EntityPickerOption[] {
  const value = code?.trim()
  if (!value) return []
  const label = name?.trim() || value
  const trimmedHint = hint?.trim()
  return [{ value, label, ...(trimmedHint ? { hint: trimmedHint } : {}) }]
}

function byLabel(a: EntityPickerOption, b: EntityPickerOption) {
  return a.label.localeCompare(b.label, 'zh-Hans-CN')
}

/** 伙伴的全部角色：主角色 partnerType + 附加角色 partnerRoles（只取真实 typed 字段）。 */
function partnerRoles(row: BusinessConsoleResourceItem): string[] {
  return [row.partnerType, ...(row.partnerRoles ?? [])]
    .map((role) => (role ?? '').trim())
    .filter(Boolean)
}

/**
 * 业务伙伴目录：一次列表查询，按角色拆成客户与供应商两组。
 * 伙伴既可以是客户又可以是供应商（附加角色），所以两组按角色包含关系分别筛，不是互斥切分。
 */
export function useErpPartnerCatalog() {
  const partners = useBusinessPartners()
  partners.filters.take = CATALOG_TAKE

  const withRole = (role: string) =>
    computed<EntityPickerOption[]>(() =>
      partners.partners.value
        .filter((row) => row.active !== false && partnerRoles(row).includes(role))
        .flatMap((row) => toOption(row.code, row.displayName))
        .sort(byLabel),
    )

  return {
    customerOptions: withRole('customer'),
    supplierOptions: withRole('supplier'),
    partnersPending: partners.partnersPending,
  }
}

/** 物料与单位目录：采购 / 销售单据行的两个必填码值。 */
export function useErpItemCatalog() {
  const skuCatalog = useBusinessSkus()
  const uomCatalog = useBusinessUoms()
  skuCatalog.filters.take = CATALOG_TAKE
  uomCatalog.filters.take = CATALOG_TAKE

  return {
    skuOptions: computed<EntityPickerOption[]>(() =>
      skuCatalog.skus.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName, row.baseUomCode))
        .sort(byLabel),
    ),
    skusPending: skuCatalog.skusPending,
    uomOptions: computed<EntityPickerOption[]>(() =>
      uomCatalog.uoms.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName))
        .sort(byLabel),
    ),
    uomsPending: uomCatalog.uomsPending,
    /** 所选物料的基本单位，用来在选完物料后自动带出单位，省去二次选择。 */
    baseUomBySku: computed(() => {
      const map = new Map<string, string>()
      for (const row of skuCatalog.skus.value) {
        const code = row.code?.trim()
        const uom = row.baseUomCode?.trim()
        if (code && uom) map.set(code, uom)
      }
      return map
    }),
  }
}

/** 工厂目录：采购收货工厂 / 销售履约工厂。 */
export function useErpSiteCatalog() {
  const siteCatalog = useBusinessMasterDataResources('site')
  siteCatalog.filters.take = CATALOG_TAKE

  return {
    siteOptions: computed<EntityPickerOption[]>(() =>
      siteCatalog.resources.value
        .filter((row) => row.active !== false)
        .flatMap((row) => toOption(row.code, row.displayName))
        .sort(byLabel),
    ),
    sitesPending: siteCatalog.resourcesPending,
  }
}

/**
 * 应收的来源单据：销售订单 + 发货单。
 * 两类都能成为开票依据（先货后票挂发货单、按单开票挂销售订单），所以合并成一组供选择，
 * `hint` 标出单据类型与客户，避免两类单号混在一起分不清。
 */
export function useErpReceivableSourceCatalog() {
  const salesOrders = useErpSalesOrders({ take: DOCUMENT_CATALOG_TAKE })
  const deliveryOrders = useErpDeliveryOrders({ take: DOCUMENT_CATALOG_TAKE })

  return {
    receivableSourceOptions: computed<EntityPickerOption[]>(() => [
      ...salesOrders.salesOrders.value.flatMap((row) =>
        toOption(
          row.salesOrderNo,
          row.salesOrderNo,
          ['销售订单', row.customerCode].filter(Boolean).join(' · '),
        ),
      ),
      ...deliveryOrders.items.value.flatMap((row) =>
        toOption(
          row.deliveryOrderNo,
          row.deliveryOrderNo,
          ['发货单', row.customerCode].filter(Boolean).join(' · '),
        ),
      ),
    ]),
    receivableSourcesPending: computed(
      () => salesOrders.salesOrdersPending.value || deliveryOrders.pending.value,
    ),
  }
}

/**
 * 应付的来源单据：采购订单。
 * 采购收货没有独立的列表读面（只有登记入口），所以入账依据落在采购订单号上。
 */
export function useErpPayableSourceCatalog() {
  const purchaseOrders = useErpPurchaseOrders({ take: DOCUMENT_CATALOG_TAKE })

  return {
    payableSourceOptions: computed<EntityPickerOption[]>(() =>
      purchaseOrders.items.value.flatMap((row) =>
        toOption(
          row.purchaseOrderNo,
          row.purchaseOrderNo,
          ['采购订单', row.supplierCode].filter(Boolean).join(' · '),
        ),
      ),
    ),
    payableSourcesPending: purchaseOrders.pending,
  }
}
