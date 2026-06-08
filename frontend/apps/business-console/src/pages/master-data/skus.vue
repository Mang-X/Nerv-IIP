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
  createSkuError,
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
const createSuccess = shallowRef('')

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
  defaultBarcodeRuleCode: 'code128-internal',
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
const createErrorMessage = computed(() => formatError(createSkuError.value))
const listErrorMessage = computed(() => formatError(skusError.value))
const skuActionErrorMessage = computed(() => formatError(skuActions.actionError.value))
const canCreateSku = computed(() =>
  [createForm.name, createForm.baseUomCode, createForm.category, createForm.materialType,
    createForm.batchTrackingPolicy, createForm.serialTrackingPolicy, createForm.shelfLifePolicyCode,
    createForm.storageConditionCode, createForm.defaultBarcodeRuleCode].every(isNonEmpty),
)

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '物料编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '物料名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'resourceType', header: '类型', width: 'w-24' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
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
async function submitSku() {
  if (!canCreateSku.value) {
    createShowErrors.value = true
    return
  }
  const body: BusinessConsoleCreateSkuRequest = {
    organizationId: createForm.organizationId.trim(),
    environmentId: createForm.environmentId.trim(),
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
    complianceTags: splitTags(createForm.complianceTags),
    idempotencyKey: createForm.idempotencyKey,
  }
  const response = await createSku(body)
  const createdCode = response?.data?.code ?? ''
  localSkus.value = [
    { resourceType: 'sku', code: createdCode, displayName: body.name, active: true, snapshotVersion: '本次录入' },
    ...localSkus.value,
  ]
  createSuccess.value = `物料「${body.name}」已创建${createdCode ? `，编号 ${createdCode}` : ''}。`
  resetCreateForm()
  createShowErrors.value = false
  createOpen.value = false
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
            <Button size="sm" type="button">
              <PlusIcon aria-hidden="true" />
              新建物料
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-3xl">
            <DialogHeader>
              <DialogTitle>新建物料</DialogTitle>
              <DialogDescription>为采购、生产、库存和销售建立统一的物料档案。带 * 为必填项。</DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitSku">
              <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>

              <p class="text-sm font-medium text-foreground">基础信息</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field>
                  <FieldLabel>物料编号</FieldLabel>
                  <div class="rounded-md border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">保存后由系统分配</div>
                  <FieldDescription>无需手填，系统自动编号。</FieldDescription>
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <FieldLabel for="sku-name">物料名称 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="sku-name" v-model="createForm.name" autocomplete="off" aria-required="true" required />
                </Field>
                <Field>
                  <FieldLabel for="sku-category">产品分类 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.category">
                    <SelectTrigger id="sku-category"><SelectValue placeholder="请选择分类" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in PRODUCT_CATEGORY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>来自数据字典 · 产品分类。缺少分类？去数据字典维护。</FieldDescription>
                </Field>
                <Field>
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
                <Field>
                  <FieldLabel for="sku-uom">基本单位 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.baseUomCode">
                    <SelectTrigger id="sku-uom"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in UOM_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>库存与核算的最小计量单位。</FieldDescription>
                </Field>
                <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
                  <FieldLabel for="sku-quality">投产前需质检</FieldLabel>
                  <Checkbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                </Field>
                <Field>
                  <FieldLabel>批次追踪 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.batchTrackingPolicy">
                    <SelectTrigger aria-label="批次追踪"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in BATCH_TRACKING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
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
                <Field>
                  <FieldLabel for="sku-shelf">保质期管理 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.shelfLifePolicyCode">
                    <SelectTrigger id="sku-shelf"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in SHELF_LIFE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="sku-storage">存储条件 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.storageConditionCode">
                    <SelectTrigger id="sku-storage"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in STORAGE_CONDITION_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
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
                <Button type="submit" :disabled="createSkuPending">
                  <Spinner v-if="createSkuPending" aria-hidden="true" />
                  保存物料
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
    <p v-else-if="skuActionErrorMessage" class="text-sm text-destructive" role="alert">{{ skuActionErrorMessage }}</p>
    <p v-else-if="createSuccess" class="text-sm text-success" role="status">{{ createSuccess }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="skusPending"
      empty-message="未找到物料。可清空筛选或新建物料。"
    >
      <template #cell-resourceType="{ value }">
        {{ value === 'sku' ? '物料' : value }}
      </template>
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-snapshotVersion="{ value }">
        <span class="tabular-nums">{{ value }}</span>
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="物料" :detail-fields="skuDetailFields(row)" :actions="skuActions" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="totalItems" />
  </BusinessLayout>
</template>
