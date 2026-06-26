<script setup lang="ts">
import type {
  BusinessConsoleManufacturingBomItem,
  BusinessConsoleReleaseManufacturingBomRequest,
} from '@nerv-iip/api-client'
import type { DataTableProColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessSkus, useBusinessUoms } from '@/composables/useBusinessMasterData'
import { useEngineeringMboms, usePublishedEboms } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  DatePickerPro,
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
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  SheetPro,
  SheetProContent,
  SheetProDescription,
  SheetProHeader,
  SheetProTitle,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: 'MBOM 制造BOM' } })

const {
  mboms,
  mbomsError,
  mbomsPending,
  mbomsTotal,
  filters,
  refresh,
  releaseMbom,
  releasePending,
  fetchMbomDetail,
} = useEngineeringMboms()

const { eboms: publishedEboms, ebomsPending: publishedEbomsPending } = usePublishedEboms()
const { skus } = useBusinessSkus()
const { uoms } = useBusinessUoms()

const STATUS_FILTER_OPTIONS = [
  { label: '全部状态', value: 'all' },
  { label: '已发布', value: 'Published' },
  { label: '草稿', value: 'Draft' },
  { label: '已归档', value: 'Archived' },
]
const statusFilter = ref('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const skuSearch = computed({
  get: () => filters.skuCode ?? '',
  set: (value: string) => { filters.skuCode = value.trim() ? value : undefined },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

const skuNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const sku of skus.value) {
    if (sku.code) map.set(sku.code, sku.displayName ?? sku.code)
  }
  return map
})
function skuLabel(code?: string | null) {
  if (!code) return '无'
  return skuNameByCode.value.get(code) ?? code
}

const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const uomOptions = computed(() =>
  uoms.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: u.displayName ?? u.code as string })),
)
// 物料编码 → 基本单位，选物料后自动带出行单位（仍可手动覆盖）。
const baseUomByCode = computed(() =>
  new Map(skus.value.filter((s) => s.code).map((s) => [s.code as string, s.baseUomCode ?? ''])),
)
// 已发布 EBOM 选择器：值用 `code::rev`，便于拆出 engineeringBomCode + engineeringBomRevision。
const ebomOptions = computed(() =>
  publishedEboms.value
    .filter((b) => b.bomCode && b.revision)
    .map((b) => ({
      value: `${b.bomCode}::${b.revision}`,
      label: `${b.bomCode} · ${b.revision}${b.parentItemCode ? ` · ${skuLabel(b.parentItemCode)}` : ''}`,
    })),
)

function engStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(() => mboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'published').length)
const draftCount = computed(() => mboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'draft').length)

const listErrorMessage = computed(() => formatError(mbomsError.value))

const columns: DataTableProColumn<BusinessConsoleManufacturingBomItem>[] = [
  { key: 'bomCode', header: 'BOM 编号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'skuCode', header: '产出物料' },
  { key: 'materialCount', header: '物料行', width: 'w-20', align: 'end', accessor: (r) => r.materialLines?.length ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 发布新版本向导 ──────────────────────────────────────────────
interface MaterialLine {
  skuCode: string
  quantity: string | number
  unitOfMeasureCode: string
  scrapRate: string | number
}
interface RecipeLine {
  parameterCode: string
  targetValue: string
  unitOfMeasureCode: string
}
interface MbomForm {
  ebomKey: string
  skuCode: string
  revision: string
  effectiveDate: string | null
  materialLines: MaterialLine[]
  recipeLines: RecipeLine[]
}
function blankMaterialLine(): MaterialLine {
  return { skuCode: '', quantity: '1', unitOfMeasureCode: '', scrapRate: '0' }
}
function blankRecipeLine(): RecipeLine {
  return { parameterCode: '', targetValue: '', unitOfMeasureCode: '' }
}
function blankForm(): MbomForm {
  return {
    ebomKey: '',
    skuCode: '',
    revision: '',
    effectiveDate: today(),
    materialLines: [blankMaterialLine()],
    recipeLines: [],
  }
}

// 选物料后把该行单位自动设为其基本单位（按单位选项大小写不敏感匹配真实 code——
// SKU 的基本单位可能与单位表大小写不一致，如 'PCS' vs 'pcs'；匹配不到则不填，避免落到无效值/占位符）。
function applyMaterialUom(line: MaterialLine, code: string) {
  const base = baseUomByCode.value.get(code)
  if (!base) return
  const match = uomOptions.value.find((o) => o.value.toLowerCase() === base.toLowerCase())
  if (match) line.unitOfMeasureCode = match.value
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<MbomForm>(blankForm())

function parseNumber(value: string | number | null | undefined): number | undefined {
  if (value === null || value === undefined) return undefined
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

const ebomValid = computed(() => form.ebomKey.trim().length > 0)
const skuValid = computed(() => form.skuCode.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0)
const effectiveValid = computed(() => !!form.effectiveDate)
function materialLineValid(line: MaterialLine) {
  return line.skuCode.trim().length > 0
    && (parseNumber(line.quantity) ?? 0) > 0
    && line.unitOfMeasureCode.trim().length > 0
}
const materialLinesValid = computed(() => form.materialLines.length > 0 && form.materialLines.every(materialLineValid))
// 同一物料不能重复（后端拒绝重复物料行，否则 500）。返回第一个重复的物料编码。
const duplicateMaterial = computed(() => {
  const seen = new Set<string>()
  for (const l of form.materialLines) {
    const c = l.skuCode.trim()
    if (!c) continue
    if (seen.has(c)) return c
    seen.add(c)
  }
  return ''
})
// 配方行可选；若填了某行，则该行参数 + 目标值须齐。
function recipeLineComplete(line: RecipeLine) {
  return line.parameterCode.trim().length > 0 && line.targetValue.trim().length > 0
}
const recipeLinesValid = computed(() => form.recipeLines.every(recipeLineComplete))
// 物料不能等于产出物料（自引用，后端拒绝）。返回第一个等于产出物料的物料编码。
const selfReferenceMaterial = computed(() => {
  const output = form.skuCode.trim()
  if (!output) return ''
  for (const l of form.materialLines) {
    if (l.skuCode.trim() === output) return output
  }
  return ''
})
const hasSelectorData = computed(() => ebomOptions.value.length > 0)
const canSubmit = computed(() =>
  ebomValid.value && skuValid.value && revisionValid.value && effectiveValid.value
  && materialLinesValid.value && recipeLinesValid.value && !duplicateMaterial.value && !selfReferenceMaterial.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  form.materialLines = [blankMaterialLine()]
  form.recipeLines = []
  showErrors.value = false
  formOpen.value = true
}
function addMaterialLine() {
  form.materialLines.push(blankMaterialLine())
}
function removeMaterialLine(index: number) {
  if (form.materialLines.length <= 1) return
  form.materialLines.splice(index, 1)
}
function addRecipeLine() {
  form.recipeLines.push(blankRecipeLine())
}
function removeRecipeLine(index: number) {
  form.recipeLines.splice(index, 1)
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const [engineeringBomCode, engineeringBomRevision] = form.ebomKey.split('::')
  const body: BusinessConsoleReleaseManufacturingBomRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    revision: form.revision.trim(),
    skuCode: form.skuCode.trim(),
    engineeringBomCode: engineeringBomCode ?? '',
    engineeringBomRevision: engineeringBomRevision ?? '',
    effectiveDate: form.effectiveDate ?? undefined,
    materialLines: form.materialLines.map((line) => ({
      skuCode: line.skuCode.trim(),
      quantity: parseNumber(line.quantity) ?? 0,
      unitOfMeasureCode: line.unitOfMeasureCode.trim(),
      scrapRate: parseNumber(line.scrapRate) ?? 0,
    })),
    recipeLines: form.recipeLines.length
      ? form.recipeLines.map((line) => ({
          parameterCode: line.parameterCode.trim(),
          targetValue: line.targetValue.trim(),
          unitOfMeasureCode: line.unitOfMeasureCode.trim() || undefined,
        }))
      : undefined,
  }
  try {
    await releaseMbom(body)
    notifySuccess(`已发布制造 BOM「${skuLabel(form.skuCode)}」修订 ${form.revision.trim()}。`)
    showErrors.value = false
    formOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 查看物料行 + 配方行（get-by-id 拉真实明细）─────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleManufacturingBomItem | null>(null)
const detailPending = ref(false)
const detailError = ref('')
const viewMaterialLines = computed(() => viewTarget.value?.materialLines ?? [])
const viewRecipeLines = computed(() => viewTarget.value?.recipeLines ?? [])
async function openView(row: BusinessConsoleManufacturingBomItem) {
  // list 已带物料行，先用它即时显示；再 get-by-id 补齐配方行并刷新物料行。
  viewTarget.value = row
  viewOpen.value = true
  detailError.value = ''
  if (!row.bomCode || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchMbomDetail(row.bomCode, row.revision)
    if (detail) viewTarget.value = detail
  }
  catch (error) {
    detailError.value = formatError(error) || '加载配方行失败，请稍后重试。'
  }
  finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function formatScrap(rate?: number | null) {
  if (rate == null) return '—'
  return `${(rate * 100).toFixed(1)}%`
}
function uomLabel(code?: string | null) {
  if (!code) return '—'
  return uomOptions.value.find((o) => o.value === code)?.label ?? code
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="MBOM 制造BOM"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${mbomsTotal} 个版本`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="mbomsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              发布新版本
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-3xl">
            <DialogProHeader>
              <DialogProTitle>发布制造 BOM 新版本</DialogProTitle>
              <DialogProDescription>
                制造 BOM 须引用一份已发布的设计 BOM。一经发布即不可变，修改请填新物料行 + 新修订号再发布。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && selfReferenceMaterial" class="text-sm text-destructive" role="alert">
                物料不能与产出物料「{{ skuLabel(selfReferenceMaterial) }}」相同——产出物料不能把自己当原料，请改选别的物料。
              </p>
              <p v-else-if="showErrors && duplicateMaterial" class="text-sm text-destructive" role="alert">
                物料「{{ skuLabel(duplicateMaterial) }}」重复了——同一物料只能有一行，请合并数量或删除重复行。
              </p>
              <p v-else-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项，并确保至少一行物料填好物料、数量（大于 0）与单位；填写了的配方行须含参数与目标值。
              </p>
              <p
                v-if="!publishedEbomsPending && !hasSelectorData"
                class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
                role="alert"
              >
                当前没有已发布的设计 BOM 可引用。请先在 EBOM 页发布版本，再回来发布制造 BOM。
              </p>

              <FormSectionTitle>版本头</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !ebomValid">
                  <FieldProLabel for="mbom-ebom">引用设计 BOM <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.ebomKey">
                    <SelectProTrigger id="mbom-ebom"><SelectProValue placeholder="选择已发布 EBOM" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in ebomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>仅可选择已发布的设计 BOM。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !skuValid">
                  <FieldProLabel for="mbom-sku">产出物料 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.skuCode">
                    <SelectProTrigger id="mbom-sku"><SelectProValue placeholder="选择产出物料" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>来自基础数据物料。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !revisionValid">
                  <FieldProLabel for="mbom-rev">修订号 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="mbom-rev" v-model="form.revision" placeholder="如 A、B、001" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !effectiveValid">
                  <FieldProLabel>生效日 <span class="text-destructive">*</span></FieldProLabel>
                  <DatePickerPro v-model="form.effectiveDate" placeholder="选择生效日" class="w-full" />
                </FieldPro>
              </FieldProGroup>

              <div class="flex items-center justify-between">
                <FormSectionTitle>物料行</FormSectionTitle>
                <ButtonPro type="button" variant="outline" size="sm" @click="addMaterialLine">
                  <PlusIcon aria-hidden="true" />
                  增加物料
                </ButtonPro>
              </div>
              <div class="grid gap-2">
                <div
                  v-for="(line, index) in form.materialLines"
                  :key="index"
                  class="grid grid-cols-[1fr_5rem_7rem_6rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <FieldPro :data-invalid="showErrors && !line.skuCode.trim()">
                    <FieldProLabel :for="`mbom-mat-${index}`">物料 <span class="text-destructive">*</span></FieldProLabel>
                    <SelectPro v-model="line.skuCode" @update:model-value="(v) => applyMaterialUom(line, String(v ?? ''))">
                      <SelectProTrigger :id="`mbom-mat-${index}`"><SelectProValue placeholder="选择物料" /></SelectProTrigger>
                      <SelectProContent>
                        <SelectProItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                      </SelectProContent>
                    </SelectPro>
                  </FieldPro>
                  <FieldPro :data-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0">
                    <FieldProLabel :for="`mbom-qty-${index}`">数量 <span class="text-destructive">*</span></FieldProLabel>
                    <InputPro :id="`mbom-qty-${index}`" v-model="line.quantity" type="number" min="0" step="any" />
                  </FieldPro>
                  <FieldPro :data-invalid="showErrors && !line.unitOfMeasureCode.trim()">
                    <FieldProLabel :for="`mbom-uom-${index}`">单位 <span class="text-destructive">*</span></FieldProLabel>
                    <SelectPro v-model="line.unitOfMeasureCode">
                      <SelectProTrigger :id="`mbom-uom-${index}`"><SelectProValue placeholder="单位" /></SelectProTrigger>
                      <SelectProContent>
                        <SelectProItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                      </SelectProContent>
                    </SelectPro>
                  </FieldPro>
                  <FieldPro>
                    <FieldProLabel :for="`mbom-scrap-${index}`">损耗率</FieldProLabel>
                    <InputPro :id="`mbom-scrap-${index}`" v-model="line.scrapRate" type="number" min="0" max="1" step="any" placeholder="0~1" />
                  </FieldPro>
                  <ButtonPro
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="删除该物料行"
                    :disabled="form.materialLines.length <= 1"
                    @click="removeMaterialLine(index)"
                  >
                    <Trash2Icon aria-hidden="true" />
                  </ButtonPro>
                </div>
              </div>

              <div class="flex items-center justify-between">
                <FormSectionTitle>配方行（可选）</FormSectionTitle>
                <ButtonPro type="button" variant="outline" size="sm" @click="addRecipeLine">
                  <PlusIcon aria-hidden="true" />
                  增加配方
                </ButtonPro>
              </div>
              <p class="text-xs text-muted-foreground">
                配方参数（如温度、压力、时长）按需登记。发布后可在「查看物料」里查看配方行。
              </p>
              <div v-if="form.recipeLines.length" class="grid gap-2">
                <div
                  v-for="(line, index) in form.recipeLines"
                  :key="index"
                  class="grid grid-cols-[1fr_1fr_6rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <FieldPro :data-invalid="showErrors && !line.parameterCode.trim()">
                    <FieldProLabel :for="`mbom-param-${index}`">参数 <span class="text-destructive">*</span></FieldProLabel>
                    <InputPro :id="`mbom-param-${index}`" v-model="line.parameterCode" placeholder="如 温度" />
                  </FieldPro>
                  <FieldPro :data-invalid="showErrors && !line.targetValue.trim()">
                    <FieldProLabel :for="`mbom-target-${index}`">目标值 <span class="text-destructive">*</span></FieldProLabel>
                    <InputPro :id="`mbom-target-${index}`" v-model="line.targetValue" placeholder="如 180" />
                  </FieldPro>
                  <FieldPro>
                    <FieldProLabel :for="`mbom-runit-${index}`">单位</FieldProLabel>
                    <InputPro :id="`mbom-runit-${index}`" v-model="line.unitOfMeasureCode" placeholder="如 ℃" />
                  </FieldPro>
                  <ButtonPro type="button" variant="ghost" size="icon" aria-label="删除该配方行" @click="removeRecipeLine(index)">
                    <Trash2Icon aria-hidden="true" />
                  </ButtonPro>
                </div>
              </div>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="releasePending">
                  <Spinner v-if="releasePending" aria-hidden="true" />
                  发布版本
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="已发布 MBOM" :value="publishedCount" hint="可被生产版本绑定的制造 BOM" />
      <SectionCard description="草稿 MBOM" :value="draftCount" hint="尚未发布、不可被绑定的版本" />
    </SectionCards>

    <Toolbar v-model:search="skuSearch" search-placeholder="按产出物料编码筛选">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="状态筛选"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="o in STATUS_FILTER_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro :pagination="false"
      :columns="columns"
      :rows="mboms"
      :searchable="false"
      :column-settings="false"
      :row-key="(r) => `${r.bomCode}:${r.revision}`"
      :loading="mbomsPending"
      empty-message="当前范围没有制造 BOM。可发布新版本，引用已发布的设计 BOM 并登记物料行。"
    >
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuLabel(row.skuCode) }}</span>
          <span class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-materialCount="{ row }">
        <span class="tabular-nums">{{ row.materialLines?.length ?? 0 }}</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{ row.effectiveDate ? formatDate(row.effectiveDate) : '长期' }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <ButtonPro type="button" variant="ghost" size="sm" @click="openView(row)">查看物料</ButtonPro>
        </div>
      </template>
    </DataTablePro>

    <DataTablePaginationPro
      v-model:page="page"
      :page-size="pageSize"
      :total-items="mbomsTotal"
      @update:page-size="(v) => (pageSize = String(v))"
    />

    <SheetPro v-model:open="viewOpen">
      <SheetProContent class="sm:max-w-lg">
        <SheetProHeader>
          <SheetProTitle>制造 BOM · 物料行</SheetProTitle>
          <SheetProDescription>
            {{ viewTarget ? `${viewTarget.bomCode} · 修订 ${viewTarget.revision} · ${skuLabel(viewTarget.skuCode)}` : '' }}
          </SheetProDescription>
        </SheetProHeader>
        <div v-if="viewTarget" class="grid gap-4 px-4 py-2">
          <section class="grid gap-2">
            <h3 class="text-sm font-medium text-muted-foreground">物料行</h3>
            <div v-if="viewMaterialLines.length" class="overflow-hidden rounded-md border">
              <table class="w-full text-sm">
                <thead class="bg-muted/40 text-muted-foreground">
                  <tr>
                    <th class="px-3 py-2 text-left font-medium">物料</th>
                    <th class="px-3 py-2 text-right font-medium">数量</th>
                    <th class="px-3 py-2 text-left font-medium">单位</th>
                    <th class="px-3 py-2 text-right font-medium">损耗率</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(line, i) in viewMaterialLines" :key="i" class="border-t">
                    <td class="px-3 py-2">
                      <div class="flex flex-col gap-0.5">
                        <span>{{ skuLabel(line.skuCode) }}</span>
                        <span class="text-xs text-muted-foreground">{{ line.skuCode }}</span>
                      </div>
                    </td>
                    <td class="px-3 py-2 text-right tabular-nums">{{ line.quantity ?? '—' }}</td>
                    <td class="px-3 py-2">{{ uomLabel(line.unitOfMeasureCode) }}</td>
                    <td class="px-3 py-2 text-right tabular-nums">{{ formatScrap(line.scrapRate) }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              该版本没有物料行。
            </p>
          </section>

          <section class="grid gap-2">
            <h3 class="text-sm font-medium text-muted-foreground">配方行</h3>
            <div v-if="detailPending" class="flex items-center gap-2 py-2 text-sm text-muted-foreground">
              <Spinner aria-hidden="true" />
              加载配方行…
            </div>
            <p v-else-if="detailError" class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
              {{ detailError }}
            </p>
            <div v-else-if="viewRecipeLines.length" class="overflow-hidden rounded-md border">
              <table class="w-full text-sm">
                <thead class="bg-muted/40 text-muted-foreground">
                  <tr>
                    <th class="px-3 py-2 text-left font-medium">参数</th>
                    <th class="px-3 py-2 text-left font-medium">目标值</th>
                    <th class="px-3 py-2 text-left font-medium">单位</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(line, i) in viewRecipeLines" :key="i" class="border-t">
                    <td class="px-3 py-2">{{ line.parameterCode || '—' }}</td>
                    <td class="px-3 py-2">{{ line.targetValue || '—' }}</td>
                    <td class="px-3 py-2">{{ line.unitOfMeasureCode || '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              该版本没有配方行。
            </p>
          </section>
        </div>
      </SheetProContent>
    </SheetPro>
  </BusinessLayout>
</template>
