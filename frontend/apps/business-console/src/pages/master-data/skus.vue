<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { useBusinessSkus } from '@/composables/useBusinessMasterData'
import { demoSkus, mergeByKey } from '@/data/shockAbsorberDemo'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import type { BusinessConsoleCreateSkuRequest, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import {
  Badge,
  Button,
  Checkbox,
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '物料与产品',
  },
})

type SortColumn = 'code' | 'displayName' | 'resourceType' | 'active' | 'snapshotVersion'

const {
  createSku,
  createSkuError,
  createSkuPending,
  filters,
  refreshSkus,
  skus,
  skusError,
  skusPending,
} = useBusinessSkus()

const createOpen = shallowRef(false)
const createSuccess = shallowRef('')
const localSkus = shallowRef<BusinessConsoleResourceItem[]>([])
const filterDraft = reactive({
  keyword: '',
  includeDisabled: false,
})
const appliedFilter = reactive({
  keyword: '',
  includeDisabled: false,
})
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'code' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const createForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  name: '',
  baseUomCode: 'EA',
  category: '减振器总成',
  materialType: 'finished-good',
  batchTrackingPolicy: 'required',
  serialTrackingPolicy: 'optional',
  shelfLifePolicyCode: '无保质期',
  storageConditionCode: '常温干燥',
  defaultBarcodeRuleCode: '减振器箱标',
  qualityRequired: true,
  complianceTags: 'IATF16949',
  idempotencyKey: newSkuIdempotencyKey(),
})
const materialTypeOptions = [
  { label: '成品', value: 'finished-good', prefix: 'FG-SAD' },
  { label: '半成品', value: 'semi-finished', prefix: 'SF-SAD' },
  { label: '原材料', value: 'raw-material', prefix: 'RM-SAD' },
  { label: '包材', value: 'packaging', prefix: 'PK-SAD' },
  { label: '服务', value: 'service', prefix: 'SV-SAD' },
]
const trackingOptions = [
  { label: '不追踪', value: 'none' },
  { label: '按需记录', value: 'optional' },
  { label: '必须记录', value: 'required' },
]

const sourceSkus = computed(() => {
  return mergeByKey([...localSkus.value, ...skus.value, ...demoSkus], (row) => row.code)
})
const listRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()

  return sourceSkus.value.filter((sku) => {
    const activeMatched = appliedFilter.includeDisabled || sku.active !== false
    const keywordMatched =
      !keyword ||
      [sku.code, sku.displayName, sku.resourceType, sku.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))

    return activeMatched && keywordMatched
  })
})
const sortedRows = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1

  return [...listRows.value].sort((left, right) =>
    String(left[tableState.sortBy] ?? '').localeCompare(String(right[tableState.sortBy] ?? ''), 'zh-Hans-CN') * direction,
  )
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const pagedRows = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})
const createErrorMessage = computed(() => formatError(createSkuError.value))
const listErrorMessage = computed(() => formatError(skusError.value))
const canCreateSku = computed(
  () =>
    isNonEmpty(createForm.name) &&
    isNonEmpty(createForm.baseUomCode) &&
    isNonEmpty(createForm.category) &&
    isNonEmpty(createForm.materialType) &&
    isNonEmpty(createForm.batchTrackingPolicy) &&
    isNonEmpty(createForm.serialTrackingPolicy) &&
    isNonEmpty(createForm.shelfLifePolicyCode) &&
    isNonEmpty(createForm.storageConditionCode) &&
    isNonEmpty(createForm.defaultBarcodeRuleCode),
)

watch(
  () => [appliedFilter.keyword, appliedFilter.includeDisabled, tableState.pageSize, sourceSkus.value.length],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  appliedFilter.keyword = filterDraft.keyword
  appliedFilter.includeDisabled = filterDraft.includeDisabled
  filters.includeDisabled = filterDraft.includeDisabled
}

function clearFilters() {
  filterDraft.keyword = ''
  filterDraft.includeDisabled = false
  applyFilters()
}

function splitTags(value: string) {
  const tags = value
    .split(',')
    .map((tag) => tag.trim())
    .filter(Boolean)

  return tags.length ? tags : undefined
}

function resetCreateForm() {
  createForm.name = ''
  createForm.baseUomCode = 'EA'
  createForm.category = '减振器总成'
  createForm.materialType = 'finished-good'
  createForm.batchTrackingPolicy = 'required'
  createForm.serialTrackingPolicy = 'optional'
  createForm.shelfLifePolicyCode = '无保质期'
  createForm.storageConditionCode = '常温干燥'
  createForm.defaultBarcodeRuleCode = '减振器箱标'
  createForm.qualityRequired = true
  createForm.complianceTags = 'IATF16949'
  createForm.idempotencyKey = newSkuIdempotencyKey()
}

async function submitSku() {
  if (!canCreateSku.value) return

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
    {
      resourceType: 'sku',
      code: createdCode,
      displayName: body.name,
      active: true,
      snapshotVersion: '本次录入',
    },
    ...localSkus.value,
  ]
  createSuccess.value = `物料 ${createdCode} 已提交。`
  resetCreateForm()
  createOpen.value = false
}

function newSkuIdempotencyKey() {
  return `sku-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

function syncContextFromFilters() {
  createForm.organizationId = filters.organizationId
  createForm.environmentId = filters.environmentId
}

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }

  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function rowKey(item: BusinessConsoleResourceItem, index: number) {
  return `${item.resourceType ?? 'sku'}:${item.code ?? index}`
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="主数据"
        title="物料与产品"
        summary="维护成品、半成品、原材料和包材，确保计划、采购、检验、生产和库存使用一致的物料档案。"
      >
        <template #actions>
          <Button size="sm" variant="outline" type="button" :disabled="skusPending" @click="refreshSkus">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>

          <Dialog v-model:open="createOpen" @update:open="syncContextFromFilters">
            <DialogTrigger as-child>
              <Button size="sm" type="button">
                <PlusIcon data-icon="inline-start" />
                新建物料
              </Button>
            </DialogTrigger>
            <DialogContent class="sm:max-w-3xl">
              <DialogHeader>
                <DialogTitle>新建物料</DialogTitle>
                <DialogDescription>
                  用于减振器成品、零部件和原材料建档。带星号字段为必填。
                </DialogDescription>
              </DialogHeader>

              <form class="grid gap-4" @submit.prevent="submitSku">
                <BusinessFormStatus :error="createErrorMessage" />

                <FieldGroup class="grid gap-3 sm:grid-cols-2">
                  <Field>
                    <FieldLabel>物料编号</FieldLabel>
                    <div class="rounded-md border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">
                      保存后由系统分配
                    </div>
                    <FieldDescription>普通建档不需要填写系统编号。</FieldDescription>
                  </Field>
                  <Field :data-invalid="!isNonEmpty(createForm.name)">
                    <FieldLabel for="sku-name">物料名称 <span class="text-destructive">*</span></FieldLabel>
                    <Input id="sku-name" v-model="createForm.name" autocomplete="off" aria-required="true" required />
                  </Field>
                  <Field :data-invalid="!isNonEmpty(createForm.baseUomCode)">
                    <FieldLabel for="sku-uom">基本单位 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="createForm.baseUomCode">
                      <SelectTrigger id="sku-uom">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="EA">件</SelectItem>
                        <SelectItem value="PCS">只</SelectItem>
                        <SelectItem value="KG">千克</SelectItem>
                        <SelectItem value="L">升</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field :data-invalid="!isNonEmpty(createForm.category)">
                    <FieldLabel for="sku-category">产品分类 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="createForm.category">
                      <SelectTrigger id="sku-category">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="减振器总成">减振器总成</SelectItem>
                        <SelectItem value="活塞杆组件">活塞杆组件</SelectItem>
                        <SelectItem value="筒体组件">筒体组件</SelectItem>
                        <SelectItem value="油封与橡胶件">油封与橡胶件</SelectItem>
                        <SelectItem value="工艺辅料">工艺辅料</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>物料类型 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="createForm.materialType">
                      <SelectTrigger aria-label="物料类型">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="option in materialTypeOptions" :key="option.value" :value="option.value">
                          {{ option.label }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>批次追踪 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="createForm.batchTrackingPolicy">
                      <SelectTrigger aria-label="批次追踪">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="option in trackingOptions" :key="option.value" :value="option.value">
                          {{ option.label }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel>序列号追踪 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="createForm.serialTrackingPolicy">
                      <SelectTrigger aria-label="序列号追踪">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="option in trackingOptions" :key="option.value" :value="option.value">
                          {{ option.label }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="sku-shelf">有效期策略 <span class="text-destructive">*</span></FieldLabel>
                    <Input id="sku-shelf" v-model="createForm.shelfLifePolicyCode" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-storage">存储要求 <span class="text-destructive">*</span></FieldLabel>
                    <Input id="sku-storage" v-model="createForm.storageConditionCode" required />
                  </Field>
                  <Field>
                    <FieldLabel for="sku-barcode">条码规则 <span class="text-destructive">*</span></FieldLabel>
                    <Input id="sku-barcode" v-model="createForm.defaultBarcodeRuleCode" required />
                  </Field>
                  <Field class="sm:col-span-2">
                    <FieldLabel for="sku-tags">质量/合规标签</FieldLabel>
                    <Input id="sku-tags" v-model="createForm.complianceTags" placeholder="IATF16949, 客户特殊特性" />
                  </Field>
                  <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3 sm:col-span-2">
                    <FieldLabel for="sku-quality">投产前需要质量检验</FieldLabel>
                    <Checkbox id="sku-quality" v-model:checked="createForm.qualityRequired" />
                  </Field>
                </FieldGroup>

                <DialogFooter>
                  <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                  <Button type="submit" :disabled="createSkuPending || !canCreateSku">
                    <Spinner v-if="createSkuPending" data-icon="inline-start" />
                    保存物料
                  </Button>
                </DialogFooter>
              </form>
            </DialogContent>
          </Dialog>
        </template>
      </BusinessPageHeader>

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]">
            <Field>
              <FieldLabel for="sku-keyword">关键字</FieldLabel>
              <Input id="sku-keyword" v-model="filterDraft.keyword" placeholder="物料编码、名称、版本" @keydown.enter="applyFilters" />
            </Field>
            <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
              <FieldLabel for="sku-include-disabled">包含停用数据</FieldLabel>
              <Checkbox id="sku-include-disabled" v-model:checked="filterDraft.includeDisabled" />
            </Field>
            <div class="flex items-end gap-2">
              <Button type="button" @click="applyFilters">查询</Button>
              <Button type="button" variant="outline" @click="clearFilters">清空</Button>
            </div>
          </FieldGroup>
          <BusinessFormStatus :error="listErrorMessage" :success="createSuccess" />
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">物料列表</h2>
          <span class="text-sm text-muted-foreground">物料档案</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('code')">
                    物料编码
                    <component :is="sortIcon('code')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('displayName')">
                    物料名称
                    <component :is="sortIcon('displayName')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('resourceType')">
                    类型
                    <component :is="sortIcon('resourceType')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('active')">
                    状态
                    <component :is="sortIcon('active')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('snapshotVersion')">
                    版本
                    <component :is="sortIcon('snapshotVersion')" data-icon="inline-end" />
                  </Button>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="(sku, index) in pagedRows" :key="rowKey(sku, index)">
                <TableCell class="font-medium">{{ sku.code ?? '无' }}</TableCell>
                <TableCell>{{ sku.displayName ?? '无' }}</TableCell>
                <TableCell>{{ sku.resourceType === 'sku' ? '物料' : sku.resourceType }}</TableCell>
                <TableCell>
                  <Badge :variant="sku.active === false ? 'secondary' : 'success'">
                    {{ sku.active === false ? '停用' : '启用' }}
                  </Badge>
                </TableCell>
                <TableCell class="tabular-nums">{{ sku.snapshotVersion ?? '无' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!listRows.length && !skusPending" :colspan="5">
                未找到物料。可以清空筛选或新建物料。
              </TableEmpty>
              <TableEmpty v-if="skusPending" :colspan="5">正在加载物料...</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <BusinessTablePagination
            v-model:page="tableState.page"
            v-model:page-size="tableState.pageSize"
            :total-items="sortedRows.length"
          />
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
