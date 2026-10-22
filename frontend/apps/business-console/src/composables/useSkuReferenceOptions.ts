import { computed } from 'vue'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { useProductCategories } from '@/composables/usePromotedCatalogs'
import {
  BARCODE_RULE_OPTIONS,
  BATCH_TRACKING_OPTIONS,
  COMPLIANCE_TAG_OPTIONS,
  MATERIAL_TYPE_OPTIONS,
  SERIAL_TRACKING_OPTIONS,
  SHELF_LIFE_OPTIONS,
  STORAGE_CONDITION_OPTIONS,
  UOM_OPTIONS,
  mergeReferenceOptions,
} from '@/data/masterDataReference'

/**
 * 物料表单的字典化下拉选项，物料列表的列显示也用同一份。
 *
 * 「实时拉取 + 常量兜底」：每个 codeSet 一个 resources 查询，服务端按 codeSet 过滤；后端某些
 * codeSet 可能仍空，届时由对应常量兜底，保证表单始终可用、可选（见 `mergeReferenceOptions`）。
 * 产品分类已升为主数据（#400），从产品分类主数据取，不再走数据字典。
 */
export function useSkuReferenceOptions() {
  const { categories: productCategories, categoriesPending: productCategoryPending } =
    useProductCategories()
  const { resources: materialTypeResources, resourcesPending: materialTypePending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'material-type' })
  const { resources: batchPolicyResources, resourcesPending: batchPolicyPending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'batch-tracking-policy' })
  const { resources: serialPolicyResources, resourcesPending: serialPolicyPending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'serial-tracking-policy' })
  const { resources: shelfLifePolicyResources, resourcesPending: shelfLifePolicyPending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'shelf-life-policy' })
  const { resources: storageConditionResources, resourcesPending: storageConditionPending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'storage-condition' })
  const { resources: barcodeRuleResources, resourcesPending: barcodeRulePending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'barcode-rule' })
  const { resources: complianceTagResources, resourcesPending: complianceTagPending } =
    useBusinessMasterDataResources('reference-data', { codeSet: 'compliance-tag' })
  // 基本单位实时取真实 unit-of-measure 实体（非写死常量子集），实时为空回退 UOM_OPTIONS。
  const { resources: uomResources, resourcesPending: uomPending } =
    useBusinessMasterDataResources('unit-of-measure')

  // 产品分类选项来自分类主数据：value=categoryCode，label=分类名（带编码）。
  const productCategoryOptions = computed(() =>
    productCategories.value
      .filter((c) => c.enabled !== false && (c.categoryCode ?? '').trim().length > 0)
      .map((c) => ({
        value: c.categoryCode as string,
        label: `${c.categoryName ?? c.categoryCode} · ${c.categoryCode}`,
      })),
  )
  const materialTypeOptions = computed(() =>
    mergeReferenceOptions(materialTypeResources.value, MATERIAL_TYPE_OPTIONS),
  )
  const batchPolicyOptions = computed(() =>
    mergeReferenceOptions(batchPolicyResources.value, BATCH_TRACKING_OPTIONS),
  )
  const serialPolicyOptions = computed(() =>
    mergeReferenceOptions(serialPolicyResources.value, SERIAL_TRACKING_OPTIONS),
  )
  const shelfLifePolicyOptions = computed(() =>
    mergeReferenceOptions(shelfLifePolicyResources.value, SHELF_LIFE_OPTIONS),
  )
  const storageConditionOptions = computed(() =>
    mergeReferenceOptions(storageConditionResources.value, STORAGE_CONDITION_OPTIONS),
  )
  const barcodeRuleOptions = computed(() =>
    mergeReferenceOptions(barcodeRuleResources.value, BARCODE_RULE_OPTIONS),
  )
  const complianceTagOptions = computed(() =>
    mergeReferenceOptions(complianceTagResources.value, COMPLIANCE_TAG_OPTIONS),
  )
  const baseUomOptions = computed(() => mergeReferenceOptions(uomResources.value, UOM_OPTIONS))
  const pending = computed(
    () =>
      productCategoryPending.value ||
      materialTypePending.value ||
      batchPolicyPending.value ||
      serialPolicyPending.value ||
      shelfLifePolicyPending.value ||
      storageConditionPending.value ||
      barcodeRulePending.value ||
      complianceTagPending.value ||
      uomPending.value,
  )

  return {
    productCategoryOptions,
    materialTypeOptions,
    batchPolicyOptions,
    serialPolicyOptions,
    shelfLifePolicyOptions,
    storageConditionOptions,
    barcodeRuleOptions,
    complianceTagOptions,
    baseUomOptions,
    pending,
  }
}
