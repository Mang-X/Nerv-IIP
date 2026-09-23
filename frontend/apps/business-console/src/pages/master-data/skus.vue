<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import type { DirectoryCreatedItem } from '@/components/business/directoryCreators'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import SkuFormDialog from '@/components/masterData/SkuFormDialog.vue'
import { useIncludeDisabledFilter } from '@/composables/masterDataIncludeDisabled'
import { useMasterDataLifecycleConfirm } from '@/composables/masterDataLifecycleConfirm'
import { useBusinessSkus, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import { useSkuReferenceOptions } from '@/composables/useSkuReferenceOptions'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvPageHeader, NvStatusBadge, NvToolbar } from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { inlineErrorMessage } from '@/utils/notify'
import { MATERIAL_TYPE_OPTIONS } from '@/data/masterDataReference'

definePage({
  meta: {
    requiresAuth: true,
    title: '物料与产品',
    requiredPermissions: ['business.masterdata.products.read'],
  },
})

const { filters, refreshSkus, skus, skusError, skusPending, skusTotal } = useBusinessSkus()
const skuActions = useMasterDataResourceActions('sku')
// 停用/启用确认框收在页面层单实例，行操作只负责指向当前行（#1591）。
const lifecycle = useMasterDataLifecycleConfirm()
// 列表的分类 / 类型 / 单位列与物料表单取同一份字典选项。
const { productCategoryOptions, materialTypeOptions, baseUomOptions } = useSkuReferenceOptions()

// Optimistic rows for items the user created in this session (real entries, never placeholders).
const localSkus = shallowRef<BusinessConsoleResourceItem[]>([])
// 物料弹窗：每次打开递增作 key，拿到全新实例（按 editing 初始化表单）；0 表示还没打开过。
const formOpen = shallowRef(false)
const formSession = shallowRef(0)
const formEditing = shallowRef<BusinessConsoleResourceItem>()

const keyword = ref('')
// 「包含停用」收在共享 composable，与其余主数据页同一实现（#1594）。
// 分页重置由下方 watch([keyword, includeDisabled, pageSize]) 统一负责，这里不再传回调。
const includeDisabled = useIncludeDisabledFilter([filters])
const sort = ref<NvDataTableSort | null>(null)
const page = ref(1)
const pageSize = ref('10')

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

const listErrorMessage = computed(() => inlineErrorMessage(skusError.value))

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '物料编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '物料名称', accessor: (r) => r.displayName ?? '无' },
  {
    key: 'category',
    header: '产品分类',
    width: 'w-28',
    accessor: (r) => categoryLabel(r.category) || '无',
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
/**
 * 产品分类显示名。
 *
 * 现存数据里有一批物料的 category 存的不是分类主数据的 `PCAT-*`，而是 `raw-material`
 * 这类**物料类型** slug（后端 `Sku.Create` 六参重载把 category 同时赋给了 materialType，
 * 种子又只传了物料类型，已作为后端种子缺陷单独移交）。分类目录里查不到时再用物料类型
 * 常量兜一层中文，避免把英文码值印到界面上；两个词表都没有才原样显示。
 */
function categoryLabel(value?: string | null) {
  if (!value) return ''
  const fromCatalog = productCategoryOptions.value.find((o) => o.value === value)?.label
  if (fromCatalog) return fromCatalog
  return MATERIAL_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}
function skuDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '物料编码', value: row.code ?? '' },
    { label: '物料名称', value: row.displayName ?? '' },
    { label: '产品分类', value: categoryLabel(row.category) },
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
function openForm(row?: BusinessConsoleResourceItem) {
  formEditing.value = row
  formSession.value += 1
  formOpen.value = true
}
function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  openForm(row)
}
function onCreated(item: DirectoryCreatedItem) {
  localSkus.value = [
    {
      resourceType: 'sku',
      code: item.code,
      displayName: item.name,
      active: true,
      snapshotVersion: '本次录入',
    },
    ...localSkus.value,
  ]
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
        <NvButton size="sm" type="button" @click="openForm()">
          <PlusIcon aria-hidden="true" />
          新建物料
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选物料编码、名称">
      <template #filters>
        <IncludeDisabledFilter v-model="includeDisabled" />
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
          @toggle="(row) => lifecycle.request(row, skuActions, '物料')"
          @edit="openEdit"
        />
      </template>
    </NvDataTable>
    <MasterDataLifecycleDialog :controller="lifecycle" />
    <SkuFormDialog
      v-if="formSession"
      :key="formSession"
      v-model:open="formOpen"
      :editing="formEditing"
      @created="onCreated"
    />
  </BusinessLayout>
</template>
