<script setup lang="ts">
import type { BusinessConsoleCreateSkuRequest, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useBusinessSkus, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  BARCODE_RULE_OPTIONS,
  BATCH_TRACKING_OPTIONS,
  MATERIAL_TYPE_OPTIONS,
  PRODUCT_CATEGORY_OPTIONS,
  SERIAL_TRACKING_OPTIONS,
  SHELF_LIFE_OPTIONS,
  STORAGE_CONDITION_OPTIONS,
  UOM_OPTIONS,
} from '@/data/masterDataReference'

definePage({ meta: { requiresAuth: true, title: '物料与产品' } })

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

// Optimistic rows for items the user created in this session (real entries, never placeholders).
const localSkus = shallowRef<BusinessConsoleResourceItem[]>([])
const createOpen = shallowRef(false)
const createShowErrors = ref(false)
// 编辑态：null=新建，否则=正在编辑的物料编码。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)

const keyword = ref('')
const includeDisabled = ref(false)
const sort = ref<DataTableSort | null>(null)
const page = ref(1)
const pageSize = ref('10')

watch(includeDisabled, (value) => {
  filters.includeDisabled = value
})

// 默认值取平台中性值（非样板业务词）；产品分类留空，强制用户主动选择。
const SKU_FORM_DEFAULTS = {
  name: '',
  baseUomCode: 'PCS',
  category: '',
  materialType: 'finished-goods',
  batchTrackingPolicy: 'none',
  serialTrackingPolicy: 'none',
  shelfLifePolicyCode: 'none',
  storageConditionCode: 'ambient',
  defaultBarcodeRuleCode: 'code128',
  qualityRequired: true,
  complianceTags: '',
}
const createForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  ...SKU_FORM_DEFAULTS,
  idempotencyKey: newSkuIdempotencyKey(),
})

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
      [sku.code, sku.displayName, sku.resourceType, sku.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(kw))
    return activeMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(a[key as keyof BusinessConsoleResourceItem] ?? '')
      .localeCompare(String(b[key as keyof BusinessConsoleResourceItem] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)
const totalItems = computed(() => skusTotal.value + pendingLocalSkus.value.length)

const activeCount = computed(() => listRows.value.filter((s) => s.active !== false).length)
const disabledCount = computed(() => listRows.value.filter((s) => s.active === false).length)
const listErrorMessage = computed(() => formatError(skusError.value))
// 字典化字段必须取自对应选项集（防止默认值/旧值漂移后提交字典里不存在的码值）。
function inOptions(options: readonly { value: string }[], value: string) {
  return options.some((option) => option.value === value)
}
const canCreateSku = computed(() =>
  isNonEmpty(createForm.name)
  && inOptions(UOM_OPTIONS, createForm.baseUomCode)
  && inOptions(PRODUCT_CATEGORY_OPTIONS, createForm.category)
  && inOptions(MATERIAL_TYPE_OPTIONS, createForm.materialType)
  && inOptions(BATCH_TRACKING_OPTIONS, createForm.batchTrackingPolicy)
  && inOptions(SERIAL_TRACKING_OPTIONS, createForm.serialTrackingPolicy)
  && inOptions(SHELF_LIFE_OPTIONS, createForm.shelfLifePolicyCode)
  && inOptions(STORAGE_CONDITION_OPTIONS, createForm.storageConditionCode)
  && inOptions(BARCODE_RULE_OPTIONS, createForm.defaultBarcodeRuleCode),
)

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '物料编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '物料名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'category', header: '产品分类', width: 'w-28', accessor: (r) => labelOf(PRODUCT_CATEGORY_OPTIONS, r.category) || '无' },
  { key: 'materialType', header: '物料类型', width: 'w-28', accessor: (r) => labelOf(MATERIAL_TYPE_OPTIONS, r.materialType) || '无' },
  { key: 'baseUomCode', header: '基本单位', width: 'w-24', accessor: (r) => labelOf(UOM_OPTIONS, r.baseUomCode) || '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function labelOf(options: ReadonlyArray<{ value: string, label: string }>, value?: string | null) {
  if (!value) return ''
  return options.find((o) => o.value === value)?.label ?? value
}
function skuDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '物料编码', value: row.code ?? '' },
    { label: '物料名称', value: row.displayName ?? '' },
    { label: '产品分类', value: labelOf(PRODUCT_CATEGORY_OPTIONS, row.category) },
    { label: '物料类型', value: labelOf(MATERIAL_TYPE_OPTIONS, row.materialType) },
    { label: '基本单位', value: labelOf(UOM_OPTIONS, row.baseUomCode) },
  ]
}

watch([keyword, includeDisabled, pageSize], () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function resetFilters() {
  keyword.value = ''
  includeDisabled.value = false
}
function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? 'sku'}:${item.code || item.displayName || ''}`
}
function splitTags(value: string) {
  const tags = value.split(',').map((tag) => tag.trim()).filter(Boolean)
  return tags.length ? tags : undefined
}
function resetCreateForm() {
  Object.assign(createForm, { ...SKU_FORM_DEFAULTS, idempotencyKey: newSkuIdempotencyKey() })
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
    }
    else {
      const body: BusinessConsoleCreateSkuRequest = {
        organizationId: createForm.organizationId.trim(),
        environmentId: createForm.environmentId.trim(),
        ...skuFieldPatch(),
        complianceTags: splitTags(createForm.complianceTags),
        idempotencyKey: createForm.idempotencyKey,
      }
      const response = await createSku(body)
      const createdCode = response?.data?.code ?? ''
      localSkus.value = [
        { resourceType: 'sku', code: createdCode, displayName: body.name, active: true, snapshotVersion: '本次录入' },
        ...localSkus.value,
      ]
      notifySuccess(`物料「${body.name}」已创建${createdCode ? `，编号 ${createdCode}` : ''}。`)
    }
    resetCreateForm()
    editingCode.value = null
    createShowErrors.value = false
    createOpen.value = false
  }
  catch (error) {
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
      baseUomCode: d?.baseUomCode || 'PCS',
      category: d?.category ?? '',
      materialType: d?.materialType ?? SKU_FORM_DEFAULTS.materialType,
      batchTrackingPolicy: d?.batchTrackingPolicy ?? SKU_FORM_DEFAULTS.batchTrackingPolicy,
      serialTrackingPolicy: d?.serialTrackingPolicy ?? SKU_FORM_DEFAULTS.serialTrackingPolicy,
      shelfLifePolicyCode: d?.shelfLifePolicyCode ?? SKU_FORM_DEFAULTS.shelfLifePolicyCode,
      storageConditionCode: d?.storageConditionCode ?? SKU_FORM_DEFAULTS.storageConditionCode,
      defaultBarcodeRuleCode: d?.defaultBarcodeRuleCode ?? SKU_FORM_DEFAULTS.defaultBarcodeRuleCode,
      qualityRequired: d?.qualityRequired ?? true,
      complianceTags: '',
    })
  }
  finally {
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
    <PageHeader title="物料与产品" :breadcrumbs="[{ label: '基础数据' }]" :count="`${totalItems} 个物料`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="skusPending" @click="refreshSkus">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="createOpen" @update:open="syncContextFromFilters">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建物料
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-3xl">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? `编辑物料 · ${editingCode}` : '新建物料' }}</DialogTitle>
              <DialogDescription>
                {{ editingCode ? '修改物料档案（编码不可修改）。带 * 为必填项。' : '为采购、生产、库存和销售建立统一的物料档案。带 * 为必填项。' }}
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitSku">
              <p v-if="createShowErrors && !canCreateSku" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>

              <p class="text-sm font-medium text-foreground">基础信息</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field>
                  <FieldLabel>物料编号</FieldLabel>
                  <div
                    class="rounded-md border bg-muted/40 px-3 py-2 text-sm"
                    :class="editingCode ? 'font-medium text-foreground' : 'text-muted-foreground'"
                  >
                    {{ editingCode || '保存后由系统分配' }}
                  </div>
                  <FieldDescription>{{ editingCode ? '编码由系统分配，不可修改。' : '无需手填，系统自动编号。' }}</FieldDescription>
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <FieldLabel for="sku-name">物料名称 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="sku-name" v-model="createForm.name" autocomplete="off" aria-required="true" required />
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(PRODUCT_CATEGORY_OPTIONS, createForm.category)">
                  <FieldLabel for="sku-category">产品分类 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.category">
                    <SelectTrigger id="sku-category"><SelectValue placeholder="请选择分类" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in PRODUCT_CATEGORY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>平台预置的产品分类（数据字典种齐并启用后改为实时拉取、可自助维护）。</FieldDescription>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(MATERIAL_TYPE_OPTIONS, createForm.materialType)">
                  <FieldLabel>物料类型 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.materialType">
                    <SelectTrigger aria-label="物料类型"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in MATERIAL_TYPE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </FieldGroup>

              <p class="text-sm font-medium text-foreground">单位与追踪</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !inOptions(UOM_OPTIONS, createForm.baseUomCode)">
                  <FieldLabel for="sku-uom">基本单位 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.baseUomCode">
                    <SelectTrigger id="sku-uom"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in UOM_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>库存与核算的最小计量单位。</FieldDescription>
                </Field>
                <Field class="self-start">
                  <FieldLabel>质检要求</FieldLabel>
                  <label
                    for="sku-quality"
                    class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm"
                  >
                    <span>投产前需质检</span>
                    <Checkbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                  </label>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(BATCH_TRACKING_OPTIONS, createForm.batchTrackingPolicy)">
                  <FieldLabel>批次追踪 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.batchTrackingPolicy">
                    <SelectTrigger aria-label="批次追踪"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in BATCH_TRACKING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(SERIAL_TRACKING_OPTIONS, createForm.serialTrackingPolicy)">
                  <FieldLabel>序列号追踪 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.serialTrackingPolicy">
                    <SelectTrigger aria-label="序列号追踪"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in SERIAL_TRACKING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              </FieldGroup>

              <p class="text-sm font-medium text-foreground">存储与条码</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !inOptions(SHELF_LIFE_OPTIONS, createForm.shelfLifePolicyCode)">
                  <FieldLabel for="sku-shelf">保质期管理 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.shelfLifePolicyCode">
                    <SelectTrigger id="sku-shelf"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in SHELF_LIFE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(STORAGE_CONDITION_OPTIONS, createForm.storageConditionCode)">
                  <FieldLabel for="sku-storage">存储条件 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.storageConditionCode">
                    <SelectTrigger id="sku-storage"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in STORAGE_CONDITION_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(BARCODE_RULE_OPTIONS, createForm.defaultBarcodeRuleCode)">
                  <FieldLabel for="sku-barcode">默认条码规则 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.defaultBarcodeRuleCode">
                    <SelectTrigger id="sku-barcode"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in BARCODE_RULE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="sku-tags">质量/合规标签</FieldLabel>
                  <Input id="sku-tags" v-model="createForm.complianceTags" placeholder="如 RoHS、IATF16949" />
                  <FieldDescription>多个标签用逗号分隔，可留空。</FieldDescription>
                </Field>
              </FieldGroup>
              <DialogFooter>
                <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                <Button type="submit" :disabled="createSkuPending || skuActions.updatePending.value || editLoading">
                  <Spinner v-if="createSkuPending || skuActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存物料' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="物料总数" :value="totalItems" hint="后端分页总数" />
      <SectionCard description="本页启用" :value="activeCount" hint="可用于计划、采购、生产" />
      <SectionCard description="本页停用" :value="disabledCount" hint="已归档或停用" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选物料编码、名称">
      <template #filters>
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          <Checkbox v-model:checked="includeDisabled" />
          包含停用
        </label>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="skusPending"
      empty-message="未找到物料。可清空筛选或新建物料。"
    >
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-snapshotVersion="{ row }">
        <span class="tabular-nums text-muted-foreground">{{ formatDateTime(row.snapshotVersion) }}</span>
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="物料" :detail-fields="skuDetailFields(row)" :actions="skuActions" @edit="openEdit" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="totalItems" />
  </BusinessLayout>
</template>
