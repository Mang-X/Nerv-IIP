<script setup lang="ts">
import type {
  BusinessConsoleCreateSkuRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import { useProductCategories } from '@/composables/usePromotedCatalogs'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'
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

definePage({
  meta: {
    requiresAuth: true,
    title: '物料与产品',
    requiredPermissions: ['business.masterdata.products.read'],
  },
})

const {
  createSku,
  createSkuPending,
  filters,
  refreshSkus,
  skus,
  skusError,
  skusPending,
  skusTotal,
} = useBusinessSkus()
const skuActions = useMasterDataResourceActions('sku')

// 字典化下拉「实时拉取 + 常量兜底」：每个 codeSet 一个 resources 查询，服务端按 codeSet 过滤；
// 后端某些 codeSet 可能仍空，届时由对应常量兜底，保证表单始终可用、可选。
// 产品分类已升为主数据（#400），从产品分类主数据取，不再走数据字典。
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

// Optimistic rows for items the user created in this session (real entries, never placeholders).
const localSkus = shallowRef<BusinessConsoleResourceItem[]>([])
const createOpen = shallowRef(false)
const createShowErrors = ref(false)
// 编辑态：null=新建，否则=正在编辑的物料编码。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)

const keyword = ref('')
const includeDisabled = ref(false)
const sort = ref<NvDataTableSort | null>(null)
const page = ref(1)
const pageSize = ref('10')

watch(includeDisabled, (value) => {
  filters.includeDisabled = value
})

interface CreateSkuForm {
  organizationId: string
  environmentId: string
  name: string
  baseUomCode: string
  category: string
  materialType: string
  batchTrackingPolicy: string
  serialTrackingPolicy: string
  shelfLifePolicyCode: string
  storageConditionCode: string
  defaultBarcodeRuleCode: string
  qualityRequired: boolean
  complianceTags: string[]
  idempotencyKey: string
}

type CreateSkuFormDefaults = Omit<
  CreateSkuForm,
  'organizationId' | 'environmentId' | 'idempotencyKey'
>

// 默认值取平台中性值（非样板业务词）；产品分类留空，强制用户主动选择。
const SKU_FORM_DEFAULTS: CreateSkuFormDefaults = {
  name: '',
  baseUomCode: 'pcs',
  category: '',
  materialType: 'finished-goods',
  batchTrackingPolicy: 'none',
  serialTrackingPolicy: 'none',
  shelfLifePolicyCode: 'none',
  storageConditionCode: 'ambient',
  defaultBarcodeRuleCode: 'code128',
  qualityRequired: true,
  complianceTags: [],
}
const createForm = reactive<CreateSkuForm>({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  ...SKU_FORM_DEFAULTS,
  complianceTags: [...SKU_FORM_DEFAULTS.complianceTags],
  idempotencyKey: newSkuIdempotencyKey(),
})

// 字典选项「实时优先、英文名用常量中文覆盖、整体为空回退常量」——见 mergeReferenceOptions。
const referenceOptions = mergeReferenceOptions

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
  referenceOptions(materialTypeResources.value, MATERIAL_TYPE_OPTIONS),
)
const batchPolicyOptions = computed(() =>
  referenceOptions(batchPolicyResources.value, BATCH_TRACKING_OPTIONS),
)
const serialPolicyOptions = computed(() =>
  referenceOptions(serialPolicyResources.value, SERIAL_TRACKING_OPTIONS),
)
const shelfLifePolicyOptions = computed(() =>
  referenceOptions(shelfLifePolicyResources.value, SHELF_LIFE_OPTIONS),
)
const storageConditionOptions = computed(() =>
  referenceOptions(storageConditionResources.value, STORAGE_CONDITION_OPTIONS),
)
const barcodeRuleOptions = computed(() =>
  referenceOptions(barcodeRuleResources.value, BARCODE_RULE_OPTIONS),
)
const complianceTagOptions = computed(() =>
  referenceOptions(complianceTagResources.value, COMPLIANCE_TAG_OPTIONS),
)
const baseUomOptions = computed(() => referenceOptions(uomResources.value, UOM_OPTIONS))
const dictionaryPending = computed(
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
const hasRequiredDictionaryOptions = computed(() =>
  [
    productCategoryOptions.value,
    materialTypeOptions.value,
    batchPolicyOptions.value,
    serialPolicyOptions.value,
    shelfLifePolicyOptions.value,
    storageConditionOptions.value,
    barcodeRuleOptions.value,
    baseUomOptions.value,
  ].every((options) => options.length > 0),
)

// Show an optimistic row only until the (invalidated) query refetches it from the
// server — otherwise the created SKU would appear twice with a colliding rowKey.
const pendingLocalSkus = computed(() => {
  const serverCodes = new Set(skus.value.map((s) => s.code).filter(Boolean))
  return localSkus.value.filter((s) => !s.code || !serverCodes.has(s.code))
})
const sourceSkus = computed(() => {
  return [...pendingLocalSkus.value, ...skus.value]
})
const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return sourceSkus.value.filter((sku) => {
    const activeMatched = includeDisabled.value || sku.active !== false
    const kwMatched =
      !kw ||
      [sku.code, sku.displayName, sku.resourceType, sku.snapshotVersion].some((value) =>
        (value ?? '').toLowerCase().includes(kw),
      )
    return activeMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort(
    (a, b) =>
      String(a[key as keyof BusinessConsoleResourceItem] ?? '').localeCompare(
        String(b[key as keyof BusinessConsoleResourceItem] ?? ''),
        'zh-Hans-CN',
      ) * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)
const totalItems = computed(() => skusTotal.value + pendingLocalSkus.value.length)

const listErrorMessage = computed(() => formatError(skusError.value))
function inOptions(options: readonly { value: string }[], value: string) {
  return options.some((option) => option.value === value)
}
// 字典化字段必须取自对应「实时选项」（实时为空时由 referenceOptions 已回退常量），防止默认值/旧值漂移后提交字典里不存在的码值。
const canCreateSku = computed(
  () =>
    !dictionaryPending.value &&
    hasRequiredDictionaryOptions.value &&
    isNonEmpty(createForm.name) &&
    inOptions(baseUomOptions.value, createForm.baseUomCode) &&
    inOptions(productCategoryOptions.value, createForm.category) &&
    inOptions(materialTypeOptions.value, createForm.materialType) &&
    inOptions(batchPolicyOptions.value, createForm.batchTrackingPolicy) &&
    inOptions(serialPolicyOptions.value, createForm.serialTrackingPolicy) &&
    inOptions(shelfLifePolicyOptions.value, createForm.shelfLifePolicyCode) &&
    inOptions(storageConditionOptions.value, createForm.storageConditionCode) &&
    inOptions(barcodeRuleOptions.value, createForm.defaultBarcodeRuleCode),
)

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '物料编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '物料名称', accessor: (r) => r.displayName ?? '无' },
  {
    key: 'category',
    header: '产品分类',
    width: 'w-28',
    accessor: (r) => labelOf(productCategoryOptions.value, r.category) || '无',
  },
  {
    key: 'materialType',
    header: '物料类型',
    width: 'w-28',
    accessor: (r) => labelOf(materialTypeOptions.value, r.materialType) || '无',
  },
  {
    key: 'baseUomCode',
    header: '基本单位',
    width: 'w-24',
    accessor: (r) => labelOf(baseUomOptions.value, r.baseUomCode) || '无',
  },
  { key: 'active', header: '状态', width: 'w-24' },
  {
    key: 'snapshotVersion',
    header: '更新时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.snapshotVersion),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function labelOf(options: ReadonlyArray<{ value: string; label: string }>, value?: string | null) {
  if (!value) return ''
  return options.find((o) => o.value === value)?.label ?? value
}
function skuDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '物料编码', value: row.code ?? '' },
    { label: '物料名称', value: row.displayName ?? '' },
    { label: '产品分类', value: labelOf(productCategoryOptions.value, row.category) },
    { label: '物料类型', value: labelOf(materialTypeOptions.value, row.materialType) },
    { label: '基本单位', value: labelOf(baseUomOptions.value, row.baseUomCode) },
  ]
}

watch([keyword, includeDisabled, pageSize], () => {
  page.value = 1
})

watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

function resetFilters() {
  keyword.value = ''
  includeDisabled.value = false
}
function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? 'sku'}:${item.code || item.displayName || ''}`
}
function setComplianceTag(code: string, checked: boolean) {
  if (checked && !createForm.complianceTags.includes(code)) {
    createForm.complianceTags.push(code)
    return
  }
  if (!checked) {
    createForm.complianceTags = createForm.complianceTags.filter((tag) => tag !== code)
  }
}
function resetCreateForm() {
  Object.assign(createForm, {
    ...SKU_FORM_DEFAULTS,
    complianceTags: [...SKU_FORM_DEFAULTS.complianceTags],
    idempotencyKey: newSkuIdempotencyKey(),
  })
}
// 物料字段（编辑/新建共用），编辑时随 update 一并提交（编码不可改）。
function skuFieldPatch() {
  return {
    name: createForm.name.trim(),
    baseUomCode: createForm.baseUomCode.trim(),
    category: createForm.category.trim(),
    materialType: createForm.materialType.trim(),
    batchTrackingPolicy: createForm.batchTrackingPolicy.trim(),
    serialTrackingPolicy: createForm.serialTrackingPolicy.trim(),
    shelfLifePolicyCode: createForm.shelfLifePolicyCode.trim(),
    storageConditionCode: createForm.storageConditionCode.trim(),
    defaultBarcodeRuleCode: createForm.defaultBarcodeRuleCode.trim(),
    qualityRequired: createForm.qualityRequired,
  }
}
async function submitSku() {
  if (!canCreateSku.value) {
    createShowErrors.value = true
    return
  }
  try {
    if (editingCode.value) {
      await skuActions.update(editingCode.value, skuFieldPatch())
      notifySuccess(`物料「${createForm.name.trim()}」已更新。`)
    } else {
      const body: BusinessConsoleCreateSkuRequest = {
        organizationId: createForm.organizationId.trim(),
        environmentId: createForm.environmentId.trim(),
        ...skuFieldPatch(),
        complianceTags: createForm.complianceTags.length ? createForm.complianceTags : undefined,
        idempotencyKey: createForm.idempotencyKey,
      }
      const response = await createSku(body)
      const createdCode = response?.data?.code ?? ''
      localSkus.value = [
        {
          resourceType: 'sku',
          code: createdCode,
          displayName: body.name,
          active: true,
          snapshotVersion: '本次录入',
        },
        ...localSkus.value,
      ]
      notifySuccess(`物料「${body.name}」已创建${createdCode ? `，编号 ${createdCode}` : ''}。`)
    }
    resetCreateForm()
    editingCode.value = null
    createShowErrors.value = false
    createOpen.value = false
  } catch (error) {
    notifyError(error)
  }
}
function openCreate() {
  editingCode.value = null
  resetCreateForm()
  createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
  createOpen.value = true
}
// 编辑：拉全字段详情回填后打开同一对话框（编码不可改）。
async function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  editingCode.value = row.code
  createShowErrors.value = false
  editLoading.value = true
  createOpen.value = true
  try {
    const d = await skuActions.fetchDetail(row.code)
    Object.assign(createForm, {
      name: d?.name ?? row.displayName ?? '',
      baseUomCode: d?.baseUomCode || 'pcs',
      category: d?.category ?? '',
      materialType: d?.materialType ?? SKU_FORM_DEFAULTS.materialType,
      batchTrackingPolicy: d?.batchTrackingPolicy ?? SKU_FORM_DEFAULTS.batchTrackingPolicy,
      serialTrackingPolicy: d?.serialTrackingPolicy ?? SKU_FORM_DEFAULTS.serialTrackingPolicy,
      shelfLifePolicyCode: d?.shelfLifePolicyCode ?? SKU_FORM_DEFAULTS.shelfLifePolicyCode,
      storageConditionCode: d?.storageConditionCode ?? SKU_FORM_DEFAULTS.storageConditionCode,
      defaultBarcodeRuleCode: d?.defaultBarcodeRuleCode ?? SKU_FORM_DEFAULTS.defaultBarcodeRuleCode,
      qualityRequired: d?.qualityRequired ?? true,
      complianceTags: [],
    })
  } finally {
    editLoading.value = false
  }
}
function newSkuIdempotencyKey() {
  return `sku-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}
function syncContextFromFilters(open: boolean) {
  if (open) createShowErrors.value = false
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="物料与产品"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${totalItems} 个物料`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="skusPending"
          @click="refreshSkus"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="createOpen" @update:open="syncContextFromFilters">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建物料
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-3xl">
            <NvDialogHeader>
              <NvDialogTitle>{{
                editingCode ? `编辑物料 · ${editingCode}` : '新建物料'
              }}</NvDialogTitle>
              <NvDialogDescription>
                {{
                  editingCode
                    ? '修改物料档案（编码不可修改）。带 * 为必填项。'
                    : '为采购、生产、库存和销售建立统一的物料档案。带 * 为必填项。'
                }}
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitSku">
              <p
                v-if="createShowErrors && !canCreateSku"
                class="text-sm text-destructive"
                role="alert"
              >
                请完整填写带 * 的必填项（已标红）。
              </p>

              <FormSectionTitle>基础信息</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField>
                  <NvFieldLabel>物料编号</NvFieldLabel>
                  <div
                    class="rounded-md border bg-muted/40 px-3 py-2 text-sm"
                    :class="editingCode ? 'font-medium text-foreground' : 'text-muted-foreground'"
                  >
                    {{ editingCode || '保存后由系统分配' }}
                  </div>
                  <NvFieldDescription>{{
                    editingCode ? '编码由系统分配，不可修改。' : '无需手填，系统自动编号。'
                  }}</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <NvFieldLabel for="sku-name"
                    >物料名称 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="sku-name"
                    v-model="createForm.name"
                    autocomplete="off"
                    aria-required="true"
                    required
                  />
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors && !inOptions(productCategoryOptions, createForm.category)
                  "
                >
                  <NvFieldLabel for="sku-category"
                    >产品分类 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.category">
                    <NvSelectTrigger id="sku-category"
                      ><NvSelectValue placeholder="请选择分类"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in productCategoryOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                  <NvFieldDescription
                    >来自数据字典 · 产品分类。缺少分类？去数据字典维护。</NvFieldDescription
                  >
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors && !inOptions(materialTypeOptions, createForm.materialType)
                  "
                >
                  <NvFieldLabel>物料类型 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.materialType">
                    <NvSelectTrigger aria-label="物料类型"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in materialTypeOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </NvFieldGroup>

              <FormSectionTitle>单位与追踪</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField
                  :data-invalid="
                    createShowErrors && !inOptions(baseUomOptions, createForm.baseUomCode)
                  "
                >
                  <NvFieldLabel for="sku-uom"
                    >基本单位 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.baseUomCode">
                    <NvSelectTrigger id="sku-uom"
                      ><NvSelectValue placeholder="请选择单位"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in baseUomOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                  <NvFieldDescription
                    >库存与核算的最小计量单位，取自「计量单位」维护页。</NvFieldDescription
                  >
                </NvField>
                <NvField class="self-start">
                  <NvFieldLabel>质检要求</NvFieldLabel>
                  <label
                    for="sku-quality"
                    class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm"
                  >
                    <span>投产前需质检</span>
                    <NvCheckbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                  </label>
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors &&
                    !inOptions(batchPolicyOptions, createForm.batchTrackingPolicy)
                  "
                >
                  <NvFieldLabel>批次追踪 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.batchTrackingPolicy">
                    <NvSelectTrigger aria-label="批次追踪"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in batchPolicyOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors &&
                    !inOptions(serialPolicyOptions, createForm.serialTrackingPolicy)
                  "
                >
                  <NvFieldLabel>序列号追踪 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.serialTrackingPolicy">
                    <NvSelectTrigger aria-label="序列号追踪"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in serialPolicyOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
              </NvFieldGroup>

              <FormSectionTitle>存储与条码</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField
                  :data-invalid="
                    createShowErrors &&
                    !inOptions(shelfLifePolicyOptions, createForm.shelfLifePolicyCode)
                  "
                >
                  <NvFieldLabel for="sku-shelf"
                    >保质期管理 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.shelfLifePolicyCode">
                    <NvSelectTrigger id="sku-shelf"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in shelfLifePolicyOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors &&
                    !inOptions(storageConditionOptions, createForm.storageConditionCode)
                  "
                >
                  <NvFieldLabel for="sku-storage"
                    >存储条件 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.storageConditionCode">
                    <NvSelectTrigger id="sku-storage"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in storageConditionOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField
                  :data-invalid="
                    createShowErrors &&
                    !inOptions(barcodeRuleOptions, createForm.defaultBarcodeRuleCode)
                  "
                >
                  <NvFieldLabel for="sku-barcode"
                    >默认条码规则 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.defaultBarcodeRuleCode">
                    <NvSelectTrigger id="sku-barcode"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in barcodeRuleOptions"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                  <NvFieldDescription>
                    <RouterLink
                      class="text-primary underline-offset-4 hover:underline"
                      :to="{
                        path: '/barcode/rules',
                        query: { ruleCode: createForm.defaultBarcodeRuleCode },
                      }"
                    >
                      去条码规则维护
                    </RouterLink>
                  </NvFieldDescription>
                </NvField>
                <NvField class="sm:col-span-2">
                  <NvFieldLabel>质量/合规标签</NvFieldLabel>
                  <div class="grid gap-2 rounded-md border p-3 sm:grid-cols-3">
                    <label
                      v-for="option in complianceTagOptions"
                      :key="option.value"
                      class="flex items-center gap-2 text-sm"
                    >
                      <NvCheckbox
                        :checked="createForm.complianceTags.includes(option.value)"
                        @update:checked="setComplianceTag(option.value, $event === true)"
                      />
                      {{ option.label }}
                    </label>
                  </div>
                  <NvFieldDescription>来自数据字典 · 合规标签，可多选、可留空。</NvFieldDescription>
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="createOpen = false"
                  >取消</NvButton
                >
                <NvButton
                  type="submit"
                  :disabled="
                    createSkuPending ||
                    skuActions.updatePending.value ||
                    editLoading ||
                    dictionaryPending ||
                    !canCreateSku
                  "
                >
                  <Spinner
                    v-if="createSkuPending || skuActions.updatePending.value || dictionaryPending"
                    aria-hidden="true"
                  />
                  {{ editingCode ? '保存修改' : '保存物料' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选物料编码、名称">
      <template #filters>
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          <NvCheckbox v-model:checked="includeDisabled" />
          包含停用
        </label>
      </template>
      <template #actions>
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="totalItems"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="skusPending"
      empty-message="未找到物料。可清空筛选或新建物料。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-active="{ row }">
        <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-snapshotVersion="{ row }">
        <span class="tabular-nums text-muted-foreground">{{
          formatDateTime(row.snapshotVersion)
        }}</span>
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions
          :row="row"
          entity-label="物料"
          :detail-fields="skuDetailFields(row)"
          :actions="skuActions"
          @edit="openEdit"
        />
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
