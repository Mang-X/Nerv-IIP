<script setup lang="ts">
import type {
  BusinessConsoleCreateUnitOfMeasureRequest,
  BusinessConsoleCreateUomConversionRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import {
  useBusinessMasterDataResources,
  useBusinessUoms,
  useMasterDataResourceActions,
  useUomConversions,
} from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  TabsPro,
  TabsProContent,
  TabsProList,
  TabsProTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { ArrowRightLeftIcon, PlusIcon, RulerIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'
import { mergeReferenceOptions } from '@/data/masterDataReference'

definePage({ meta: { requiresAuth: true, title: '计量单位' } })

interface Option { value: string, label: string }

// 通用 resources 列表项只含 5 个稳定字段；UoM 的量纲 / 精度 / 取整由后端按需附带，
// 但未进 ResourceItem 静态类型。读这些可选附带字段时收窄到本地形状（缺失即 undefined）。
interface UomRow extends BusinessConsoleResourceItem {
  dimensionType?: string | null
  precision?: number | null
  roundingMode?: string | null
}
function asUom(row: BusinessConsoleResourceItem): UomRow {
  return row as UomRow
}

// 换算关系列表项的可选 typed 字段（与 UoM 同理，收窄到本地形状读取）。
interface ConversionRow extends BusinessConsoleResourceItem {
  fromUomCode?: string | null
  toUomCode?: string | null
  factor?: number | null
  offset?: number | null
  effectiveFrom?: string | null
}
function asConversion(row: BusinessConsoleResourceItem): ConversionRow {
  return row as ConversionRow
}

// 量纲常量兜底（数据字典 `uom-dimension` 实时为空时回退）。
const DIMENSION_OPTIONS: Option[] = [
  { value: 'count', label: '计数' },
  { value: 'length', label: '长度' },
  { value: 'area', label: '面积' },
  { value: 'volume', label: '体积' },
  { value: 'weight', label: '重量' },
  { value: 'time', label: '时间' },
]
// 取整方式（系统枚举常量）。
const ROUNDING_OPTIONS: Option[] = [
  { value: 'half-up', label: '四舍五入' },
  { value: 'half-down', label: '五舍六入' },
  { value: 'up', label: '向上取整' },
  { value: 'down', label: '向下取整' },
]
const UOM_DEFAULTS = {
  dimensionType: 'count',
  roundingMode: 'half-up',
}

const {
  createUom,
  createUomPending,
  filters,
  refreshUoms,
  uoms,
  uomsError,
  uomsPending,
  uomsTotal,
} = useBusinessUoms()
const uomActions = useMasterDataResourceActions('unit-of-measure')

const {
  conversions,
  conversionsError,
  conversionsPending,
  conversionsTotal,
  createUomConversion,
  createUomConversionPending,
  filters: conversionFilters,
  refreshConversions,
} = useUomConversions()
const conversionActions = useMasterDataResourceActions('uom-conversion')

// 量纲下拉「实时拉取 + 常量兜底」：取数据字典 uom-dimension，实时为空回退常量。
const { resources: dimensionResources } = useBusinessMasterDataResources('reference-data', { codeSet: 'uom-dimension' })
const dimensionOptions = computed<Option[]>(() => mergeReferenceOptions(dimensionResources.value, DIMENSION_OPTIONS))

const keyword = ref('')
const page = ref(1)
const pageSize = ref('10')
const createOpen = ref(false)
const createShowErrors = ref(false)
// 编辑态：null=新建，否则=正在编辑的单位编码（编码不可改）。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)
const createForm = reactive({
  code: '',
  name: '',
  dimensionType: UOM_DEFAULTS.dimensionType,
  precision: '' as string,
  roundingMode: UOM_DEFAULTS.roundingMode,
})

// 换算关系：列表筛选 + 新建表单。
const conversionKeyword = ref('')
const conversionPage = ref(1)
const conversionPageSize = ref('10')
const conversionOpen = ref(false)
const conversionShowErrors = ref(false)
const conversionForm = reactive({
  fromUom: '',
  toUom: '',
  factor: '' as string,
  offset: '' as string,
  precision: '' as string,
  roundingMode: UOM_DEFAULTS.roundingMode,
})

const columns: DataTableProColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'dimensionType', header: '量纲', width: 'w-28', accessor: (r) => labelOf(dimensionOptions.value, asUom(r).dimensionType) || '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

// 单位编码 → 名称：用本页已取的单位列表解析，列表里没有则回退编码本身。
const uomNameByCode = computed<Map<string, string>>(() => {
  const map = new Map<string, string>()
  for (const u of uoms.value) {
    if (u.code) map.set(u.code, u.displayName ?? u.code)
  }
  return map
})
function uomName(code?: string | null): string {
  if (!code) return ''
  return uomNameByCode.value.get(code) ?? code
}
// 单位下拉选项（换算表单选 from/to 单位用，显示名称、值为编码）。
const uomSelectOptions = computed<Option[]>(() =>
  uoms.value
    .filter((u) => !!u.code)
    .map((u) => ({ value: u.code as string, label: u.displayName ?? (u.code as string) })),
)

// 换算公式文案：1 {from} = factor {to} (+offset)，单位显示名称。
function conversionFormula(row: BusinessConsoleResourceItem): string {
  const c = asConversion(row)
  const from = uomName(c.fromUomCode)
  const to = uomName(c.toUomCode)
  const factor = c.factor != null ? c.factor : 1
  const base = `1 ${from || '?'} = ${factor} ${to || '?'}`
  return c.offset != null && c.offset !== 0 ? `${base} ${c.offset > 0 ? '+' : '-'} ${Math.abs(c.offset)}` : base
}

const conversionColumns: DataTableProColumn<BusinessConsoleResourceItem>[] = [
  { key: 'formula', header: '换算关系', cellClass: 'font-medium', accessor: (r) => conversionFormula(r) },
  { key: 'fromUom', header: '源单位', width: 'w-28', accessor: (r) => uomName(asConversion(r).fromUomCode) || '无' },
  { key: 'toUom', header: '目标单位', width: 'w-28', accessor: (r) => uomName(asConversion(r).toUomCode) || '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]
function conversionDetailFields(row: BusinessConsoleResourceItem) {
  const c = asConversion(row)
  return [
    { label: '源单位', value: uomName(c.fromUomCode) },
    { label: '目标单位', value: uomName(c.toUomCode) },
    { label: '换算系数', value: c.factor != null ? String(c.factor) : '' },
    { label: '偏移量', value: c.offset != null ? String(c.offset) : '' },
    { label: '换算公式', value: conversionFormula(row) },
  ]
}

function labelOf(options: ReadonlyArray<Option>, value?: string | null) {
  if (!value) return ''
  return options.find((o) => o.value === value)?.label ?? value
}
function uomDetailFields(row: BusinessConsoleResourceItem) {
  const u = asUom(row)
  return [
    { label: '编码', value: row.code ?? '' },
    { label: '名称', value: row.displayName ?? '' },
    { label: '量纲', value: labelOf(dimensionOptions.value, u.dimensionType) },
    { label: '小数精度', value: u.precision != null ? String(u.precision) : '' },
    { label: '取整方式', value: labelOf(ROUNDING_OPTIONS, u.roundingMode) },
  ]
}

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return uoms.value
  return uoms.value.filter((row) =>
    [row.code, row.displayName].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
})
const canCreateUom = computed(() =>
  isNonEmpty(createForm.name)
  && inOptions(dimensionOptions.value, createForm.dimensionType)
  && inOptions(ROUNDING_OPTIONS, createForm.roundingMode)
  && isPrecisionValid(createForm.precision),
)
const listErrorMessage = computed(() => formatError(uomsError.value))

const conversionRows = computed(() => {
  const kw = conversionKeyword.value.trim().toLowerCase()
  if (!kw) return conversions.value
  return conversions.value.filter((row) => {
    const c = asConversion(row)
    return [uomName(c.fromUomCode), uomName(c.toUomCode), c.fromUomCode, c.toUomCode]
      .some((value) => (value ?? '').toLowerCase().includes(kw))
  })
})
// fromUom≠toUom 且 factor>0；factor 必填且为正数。
const canCreateConversion = computed(() =>
  inOptions(uomSelectOptions.value, conversionForm.fromUom)
  && inOptions(uomSelectOptions.value, conversionForm.toUom)
  && conversionForm.fromUom !== conversionForm.toUom
  && isFactorValid(conversionForm.factor)
  && inOptions(ROUNDING_OPTIONS, conversionForm.roundingMode)
  && isOffsetValid(conversionForm.offset)
  && isPrecisionValid(conversionForm.precision),
)
const conversionListError = computed(() => formatError(conversionsError.value))

watch(createOpen, (open) => { if (open) createShowErrors.value = false })
watch([keyword, pageSize], () => { page.value = 1 })
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * (Number(pageSize.value) || 10)
  filters.take = Number(pageSize.value) || 10
}, { immediate: true })

watch(conversionOpen, (open) => { if (open) conversionShowErrors.value = false })
watch([conversionKeyword, conversionPageSize], () => { conversionPage.value = 1 })
watch([conversionPage, conversionPageSize], () => {
  conversionFilters.skip = (conversionPage.value - 1) * (Number(conversionPageSize.value) || 10)
  conversionFilters.take = Number(conversionPageSize.value) || 10
}, { immediate: true })

function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? 'unit-of-measure'}:${item.code || item.displayName || ''}`
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function inOptions(options: readonly Option[], value: string) {
  return options.some((option) => option.value === value)
}
// 数字输入框的 v-model 可能回传 number；统一收成可 trim 的字符串。
function asText(value: string | number): string {
  return value == null ? '' : String(value)
}
// 精度可空；填了则必须是 >= 0 的整数。
function isPrecisionValid(value: string | number) {
  if (asText(value).trim() === '') return true
  const n = Number(value)
  return Number.isInteger(n) && n >= 0
}
function precisionNumber(value: string | number): number | undefined {
  return asText(value).trim() === '' ? undefined : Number(value)
}
// 换算系数必填，必须是 > 0 的有限数。
function isFactorValid(value: string | number) {
  if (asText(value).trim() === '') return false
  const n = Number(value)
  return Number.isFinite(n) && n > 0
}
// 偏移量可空；填了则必须是有限数（可负）。
function isOffsetValid(value: string | number) {
  if (asText(value).trim() === '') return true
  return Number.isFinite(Number(value))
}
function offsetNumber(value: string | number): number | undefined {
  return asText(value).trim() === '' ? undefined : Number(value)
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function resetCreateForm() {
  Object.assign(createForm, {
    code: '',
    name: '',
    dimensionType: UOM_DEFAULTS.dimensionType,
    precision: '',
    roundingMode: UOM_DEFAULTS.roundingMode,
  })
}
function openCreate() {
  editingCode.value = null
  resetCreateForm()
  createShowErrors.value = false
  createOpen.value = true
}
async function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  editingCode.value = row.code
  createShowErrors.value = false
  editLoading.value = true
  createOpen.value = true
  const u = asUom(row)
  try {
    const d = await uomActions.fetchDetail(row.code)
    Object.assign(createForm, {
      code: row.code,
      name: d?.name ?? row.displayName ?? '',
      dimensionType: d?.dimensionType ?? u.dimensionType ?? UOM_DEFAULTS.dimensionType,
      precision: d?.precision != null ? String(d.precision) : (u.precision != null ? String(u.precision) : ''),
      roundingMode: d?.roundingMode ?? u.roundingMode ?? UOM_DEFAULTS.roundingMode,
    })
  }
  finally {
    editLoading.value = false
  }
}
async function submitUom() {
  if (!canCreateUom.value) {
    createShowErrors.value = true
    return
  }
  try {
    if (editingCode.value) {
      await uomActions.update(editingCode.value, {
        name: createForm.name.trim(),
        dimensionType: createForm.dimensionType,
        precision: precisionNumber(createForm.precision),
        roundingMode: createForm.roundingMode,
      })
      notifySuccess(`计量单位「${createForm.name.trim()}」已更新。`)
    }
    else {
      const body: BusinessConsoleCreateUnitOfMeasureRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        name: createForm.name.trim(),
        dimensionType: createForm.dimensionType,
        precision: precisionNumber(createForm.precision),
        roundingMode: createForm.roundingMode,
      }
      await createUom(body)
      notifySuccess(`计量单位「${body.name}」已创建。`)
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

function refreshAll() {
  void refreshUoms()
  void refreshConversions()
}
function resetConversionForm() {
  Object.assign(conversionForm, {
    fromUom: '',
    toUom: '',
    factor: '',
    offset: '',
    precision: '',
    roundingMode: UOM_DEFAULTS.roundingMode,
  })
}
function openCreateConversion() {
  resetConversionForm()
  conversionShowErrors.value = false
  conversionOpen.value = true
}
async function submitConversion() {
  if (!canCreateConversion.value) {
    conversionShowErrors.value = true
    return
  }
  try {
    const body: BusinessConsoleCreateUomConversionRequest = {
      organizationId: conversionFilters.organizationId,
      environmentId: conversionFilters.environmentId,
      fromUomCode: conversionForm.fromUom,
      toUomCode: conversionForm.toUom,
      factor: Number(conversionForm.factor),
      offset: offsetNumber(conversionForm.offset),
      precision: precisionNumber(conversionForm.precision),
      roundingMode: conversionForm.roundingMode,
    }
    await createUomConversion(body)
    notifySuccess(`换算关系「${uomName(body.fromUomCode)} → ${uomName(body.toUomCode)}」已创建。`)
    resetConversionForm()
    conversionShowErrors.value = false
    conversionOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="计量单位" :breadcrumbs="[{ label: '基础数据' }]" :count="`${uomsTotal} 个单位`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="uomsPending || conversionsPending" @click="refreshAll">
          <RulerIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">计量单位是物料基本单位与单位换算的取值来源；物料表单的「基本单位」实时取自这里，换算关系定义单位间的折算系数。</p>

    <TabsPro default-value="units">
      <TabsProList>
        <TabsProTrigger value="units">计量单位 ({{ uomsTotal }})</TabsProTrigger>
        <TabsProTrigger value="conversions">换算关系 ({{ conversionsTotal }})</TabsProTrigger>
      </TabsProList>

      <!-- 计量单位 -->
      <TabsProContent value="units" class="grid gap-3">
        <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选编码、名称">
          <template #actions>
            <DialogPro v-model:open="createOpen">
              <DialogProTrigger as-child>
                <ButtonPro size="sm" type="button" @click="openCreate">
                  <PlusIcon aria-hidden="true" />
                  新建计量单位
                </ButtonPro>
              </DialogProTrigger>
              <DialogProContent class="sm:max-w-2xl">
                <DialogProHeader>
                  <DialogProTitle>{{ editingCode ? `编辑计量单位 · ${editingCode}` : '新建计量单位' }}</DialogProTitle>
                  <DialogProDescription>{{ editingCode ? '修改计量单位（编码不可修改）。带 * 为必填项。' : '为库存、核算与单位换算建立统一的计量单位。带 * 为必填项。' }}</DialogProDescription>
                </DialogProHeader>
                <form class="grid gap-4" @submit.prevent="submitUom">
                  <p v-if="createShowErrors && !canCreateUom" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                    <FieldPro v-if="editingCode">
                      <FieldProLabel for="uom-code">编码</FieldProLabel>
                      <InputPro id="uom-code" :model-value="createForm.code" disabled />
                      <FieldProDescription>系统分配，不可修改。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                      <FieldProLabel for="uom-name">名称 <span class="text-destructive">*</span></FieldProLabel>
                      <InputPro id="uom-name" v-model="createForm.name" autocomplete="off" required />
                      <FieldProDescription v-if="!editingCode">编码由系统自动生成（如 EA、pcs、kg）。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="createShowErrors && !inOptions(dimensionOptions, createForm.dimensionType)">
                      <FieldProLabel for="uom-dimension">量纲 <span class="text-destructive">*</span></FieldProLabel>
                      <SelectPro v-model="createForm.dimensionType">
                        <SelectProTrigger id="uom-dimension"><SelectProValue placeholder="请选择量纲" /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem v-for="o in dimensionOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                      <FieldProDescription>同一量纲内的单位才可互相换算。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="createShowErrors && !inOptions(ROUNDING_OPTIONS, createForm.roundingMode)">
                      <FieldProLabel for="uom-rounding">取整方式 <span class="text-destructive">*</span></FieldProLabel>
                      <SelectPro v-model="createForm.roundingMode">
                        <SelectProTrigger id="uom-rounding"><SelectProValue /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem v-for="o in ROUNDING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                    </FieldPro>
                    <FieldPro :data-invalid="createShowErrors && !isPrecisionValid(createForm.precision)">
                      <FieldProLabel for="uom-precision">小数精度</FieldProLabel>
                      <InputPro id="uom-precision" v-model="createForm.precision" type="number" min="0" step="1" autocomplete="off" placeholder="可留空" />
                      <FieldProDescription>保留的小数位数，可留空。</FieldProDescription>
                    </FieldPro>
                  </FieldProGroup>
                  <DialogProFooter>
                    <ButtonPro type="button" variant="outline" @click="createOpen = false">取消</ButtonPro>
                    <ButtonPro type="submit" :disabled="createUomPending || uomActions.updatePending.value || editLoading">
                      <Spinner v-if="createUomPending || uomActions.updatePending.value" aria-hidden="true" />
                      {{ editingCode ? '保存修改' : '保存计量单位' }}
                    </ButtonPro>
                  </DialogProFooter>
                </form>
              </DialogProContent>
            </DialogPro>
          </template>
        </Toolbar>

        <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

        <DataTablePro :pagination="false"
          :columns="columns"
          :rows="listRows"
          :row-key="rowKey"
          :loading="uomsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="还没有计量单位，点击「新建计量单位」创建第一条。"
        >
          <template #cell-active="{ row }">
            <StatusBadgePro :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="计量单位" :detail-fields="uomDetailFields(row)" :actions="uomActions" @edit="openEdit" />
          </template>
        </DataTablePro>

        <DataTablePaginationPro v-model:page="page" :page-size="pageSize" :total-items="uomsTotal" @update:page-size="(v) => (pageSize = String(v))" />
      </TabsProContent>

      <!-- 换算关系 -->
      <TabsProContent value="conversions" class="grid gap-3">
        <p class="text-sm text-muted-foreground">换算关系定义两个单位间的折算：1 源单位 = 系数 × 目标单位（可带偏移量）。源单位与目标单位显示其单位名称。</p>
        <Toolbar v-model:search="conversionKeyword" search-placeholder="在当前页内筛选源 / 目标单位">
          <template #actions>
            <DialogPro v-model:open="conversionOpen">
              <DialogProTrigger as-child>
                <ButtonPro size="sm" type="button" @click="openCreateConversion">
                  <ArrowRightLeftIcon aria-hidden="true" />
                  新建换算关系
                </ButtonPro>
              </DialogProTrigger>
              <DialogProContent class="sm:max-w-2xl">
                <DialogProHeader>
                  <DialogProTitle>新建换算关系</DialogProTitle>
                  <DialogProDescription>定义一组单位换算：1 源单位 = 系数 × 目标单位（可带偏移量）。带 * 为必填项。</DialogProDescription>
                </DialogProHeader>
                <form class="grid gap-4" @submit.prevent="submitConversion">
                  <p v-if="conversionShowErrors && !canCreateConversion" class="text-sm text-destructive" role="alert">请检查带 * 的必填项：源单位与目标单位不能相同，换算系数须大于 0（已标红）。</p>
                  <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                    <FieldPro :data-invalid="conversionShowErrors && (!inOptions(uomSelectOptions, conversionForm.fromUom) || conversionForm.fromUom === conversionForm.toUom)">
                      <FieldProLabel for="conv-from">源单位 <span class="text-destructive">*</span></FieldProLabel>
                      <SelectPro v-model="conversionForm.fromUom">
                        <SelectProTrigger id="conv-from"><SelectProValue placeholder="请选择源单位" /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem v-for="o in uomSelectOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                    </FieldPro>
                    <FieldPro :data-invalid="conversionShowErrors && (!inOptions(uomSelectOptions, conversionForm.toUom) || conversionForm.fromUom === conversionForm.toUom)">
                      <FieldProLabel for="conv-to">目标单位 <span class="text-destructive">*</span></FieldProLabel>
                      <SelectPro v-model="conversionForm.toUom">
                        <SelectProTrigger id="conv-to"><SelectProValue placeholder="请选择目标单位" /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem v-for="o in uomSelectOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                      <FieldProDescription>目标单位需与源单位不同。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="conversionShowErrors && !isFactorValid(conversionForm.factor)">
                      <FieldProLabel for="conv-factor">换算系数 <span class="text-destructive">*</span></FieldProLabel>
                      <InputPro id="conv-factor" v-model="conversionForm.factor" type="number" min="0" step="any" autocomplete="off" placeholder="如 1000" />
                      <FieldProDescription>1 源单位等于多少目标单位，须大于 0。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="conversionShowErrors && !isOffsetValid(conversionForm.offset)">
                      <FieldProLabel for="conv-offset">偏移量</FieldProLabel>
                      <InputPro id="conv-offset" v-model="conversionForm.offset" type="number" step="any" autocomplete="off" placeholder="可留空" />
                      <FieldProDescription>线性换算的常数项（如温标换算），可留空。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="conversionShowErrors && !inOptions(ROUNDING_OPTIONS, conversionForm.roundingMode)">
                      <FieldProLabel for="conv-rounding">取整方式 <span class="text-destructive">*</span></FieldProLabel>
                      <SelectPro v-model="conversionForm.roundingMode">
                        <SelectProTrigger id="conv-rounding"><SelectProValue /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem v-for="o in ROUNDING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                    </FieldPro>
                    <FieldPro :data-invalid="conversionShowErrors && !isPrecisionValid(conversionForm.precision)">
                      <FieldProLabel for="conv-precision">小数精度</FieldProLabel>
                      <InputPro id="conv-precision" v-model="conversionForm.precision" type="number" min="0" step="1" autocomplete="off" placeholder="可留空" />
                      <FieldProDescription>换算结果保留的小数位数，可留空。</FieldProDescription>
                    </FieldPro>
                  </FieldProGroup>
                  <DialogProFooter>
                    <ButtonPro type="button" variant="outline" @click="conversionOpen = false">取消</ButtonPro>
                    <ButtonPro type="submit" :disabled="createUomConversionPending">
                      <Spinner v-if="createUomConversionPending" aria-hidden="true" />
                      保存换算关系
                    </ButtonPro>
                  </DialogProFooter>
                </form>
              </DialogProContent>
            </DialogPro>
          </template>
        </Toolbar>

        <p v-if="conversionListError" class="text-sm text-destructive" role="alert">{{ conversionListError }}</p>

        <DataTablePro :pagination="false"
          :columns="conversionColumns"
          :rows="conversionRows"
          :row-key="rowKey"
          :loading="conversionsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="还没有换算关系，点击「新建换算关系」创建第一条。"
        >
          <template #cell-active="{ row }">
            <StatusBadgePro :value="row.active === false ? 'disabled' : 'active'" />
          </template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="换算关系" :detail-fields="conversionDetailFields(row)" :actions="conversionActions" />
          </template>
        </DataTablePro>

        <DataTablePaginationPro v-model:page="conversionPage" :page-size="conversionPageSize" :total-items="conversionsTotal" @update:page-size="(v) => (conversionPageSize = String(v))" />
      </TabsProContent>
    </TabsPro>
  </BusinessLayout>
</template>
