<script setup lang="ts">
import type {
  BusinessConsoleCreateUnitOfMeasureRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import {
  useBusinessMasterDataResources,
  useBusinessUoms,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
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
import { PlusIcon, RulerIcon } from 'lucide-vue-next'
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

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'dimensionType', header: '量纲', width: 'w-28', accessor: (r) => labelOf(dimensionOptions.value, asUom(r).dimensionType) || '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

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
const activeCount = computed(() => uoms.value.filter((u) => u.active !== false).length)
const canCreateUom = computed(() =>
  isNonEmpty(createForm.code)
  && isNonEmpty(createForm.name)
  && inOptions(dimensionOptions.value, createForm.dimensionType)
  && inOptions(ROUNDING_OPTIONS, createForm.roundingMode)
  && isPrecisionValid(createForm.precision),
)
const listErrorMessage = computed(() => formatError(uomsError.value))

watch(createOpen, (open) => { if (open) createShowErrors.value = false })
watch([keyword, pageSize], () => { page.value = 1 })
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * (Number(pageSize.value) || 10)
  filters.take = Number(pageSize.value) || 10
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
// 精度可空；填了则必须是 >= 0 的整数。
function isPrecisionValid(value: string) {
  if (value.trim() === '') return true
  const n = Number(value)
  return Number.isInteger(n) && n >= 0
}
function precisionNumber(value: string): number | undefined {
  return value.trim() === '' ? undefined : Number(value)
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
        code: createForm.code.trim(),
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
</script>

<template>
  <BusinessLayout>
    <PageHeader title="计量单位" :breadcrumbs="[{ label: '基础数据' }]" :count="`${uomsTotal} 个单位`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="uomsPending" @click="refreshUoms">
          <RulerIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="createOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建计量单位
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? `编辑计量单位 · ${editingCode}` : '新建计量单位' }}</DialogTitle>
              <DialogDescription>{{ editingCode ? '修改计量单位（编码不可修改）。带 * 为必填项。' : '为库存、核算与单位换算建立统一的计量单位。带 * 为必填项。' }}</DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitUom">
              <p v-if="createShowErrors && !canCreateUom" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.code)">
                  <FieldLabel for="uom-code">编码 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="uom-code" v-model="createForm.code" autocomplete="off" :disabled="!!editingCode" required />
                  <FieldDescription>如 EA、pcs、kg。保存后不可修改。</FieldDescription>
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.name)">
                  <FieldLabel for="uom-name">名称 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="uom-name" v-model="createForm.name" autocomplete="off" required />
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(dimensionOptions, createForm.dimensionType)">
                  <FieldLabel for="uom-dimension">量纲 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.dimensionType">
                    <SelectTrigger id="uom-dimension"><SelectValue placeholder="请选择量纲" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in dimensionOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>同一量纲内的单位才可互相换算。</FieldDescription>
                </Field>
                <Field :data-invalid="createShowErrors && !inOptions(ROUNDING_OPTIONS, createForm.roundingMode)">
                  <FieldLabel for="uom-rounding">取整方式 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.roundingMode">
                    <SelectTrigger id="uom-rounding"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in ROUNDING_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field :data-invalid="createShowErrors && !isPrecisionValid(createForm.precision)">
                  <FieldLabel for="uom-precision">小数精度</FieldLabel>
                  <Input id="uom-precision" v-model="createForm.precision" type="number" min="0" step="1" autocomplete="off" placeholder="可留空" />
                  <FieldDescription>保留的小数位数，可留空。</FieldDescription>
                </Field>
              </FieldGroup>
              <DialogFooter>
                <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                <Button type="submit" :disabled="createUomPending || uomActions.updatePending.value || editLoading">
                  <Spinner v-if="createUomPending || uomActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存计量单位' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">计量单位是物料基本单位与单位换算的取值来源；物料表单的「基本单位」实时取自这里。</p>

    <SectionCards :columns="2">
      <SectionCard description="单位总数" :value="uomsTotal" hint="后端分页总数" />
      <SectionCard description="本页启用" :value="activeCount" hint="可用于物料与换算" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选编码、名称" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="listRows"
      :row-key="rowKey"
      :loading="uomsPending"
      empty-message="暂无计量单位。可清空筛选或新建计量单位。"
    >
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="计量单位" :detail-fields="uomDetailFields(row)" :actions="uomActions" @edit="openEdit" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="uomsTotal" />
  </BusinessLayout>
</template>
