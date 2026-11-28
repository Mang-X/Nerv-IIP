<script setup lang="ts">
import { useFirstInvalidField } from '@/components/masterData/useFirstInvalidField'
import type {
  BusinessConsoleManufacturingBomItem,
  BusinessConsoleReleaseManufacturingBomRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment, StatusTone } from '@nerv-iip/ui'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { pagedBreakdownSegments } from '@/composables/shared/metricSegments'
import { useBusinessSkus, useBusinessUoms } from '@/composables/master-data'
import {
  useBomRevisionSuggestions,
  useEngineeringMboms,
  usePublishedEboms,
} from '@/composables/engineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogBody,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldError,
  NvEntityPicker,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: 'MBOM 制造BOM',
    requiredPermissions: ['business.engineering.boms.read'],
  },
})

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

const {
  eboms: publishedEboms,
  ebomsPending: publishedEbomsPending,
  ebomsError: publishedEbomsError,
  refreshEboms,
  fetchEbomDetail,
} = usePublishedEboms()
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
  set: (value: string) => {
    filters.skuCode = value.trim() ? value : undefined
  },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch(
  [page, pageSize],
  () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  },
  { immediate: true },
)

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

const uomOptions = computed(() =>
  uoms.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: u.displayName ?? (u.code as string) })),
)
// 物料编码 → 基本单位，选物料后自动带出行单位（仍可手动覆盖）。
const baseUomByCode = computed(
  () =>
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

function engStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(
  () => mboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'published').length,
)
const draftCount = computed(
  () => mboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'draft').length,
)
// 一张构成卡表达「制造 BOM 里有多少已发布、多少还是草稿」；未取回的行补齐，分母守恒。
const mbomSegments = computed(() => {
  const others = mboms.value.length - publishedCount.value - draftCount.value
  const segments: NvMetricSegment[] = [
    { key: 'published', label: '已发布', value: publishedCount.value, tone: 'success' },
    { key: 'draft', label: '草稿', value: draftCount.value, tone: 'warning' },
  ]
  if (others > 0)
    segments.push({ key: 'others', label: '已归档等', value: others, tone: 'neutral' })
  return pagedBreakdownSegments(mbomsTotal.value, segments)
})

const listErrorMessage = computed(() => inlineErrorMessage(mbomsError.value))

const columns: NvDataTableColumn<BusinessConsoleManufacturingBomItem>[] = [
  { key: 'bomCode', header: 'BOM 编号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'skuCode', header: '产出物料' },
  {
    key: 'materialCount',
    header: '物料行',
    width: 'w-20',
    align: 'end',
    accessor: (r) => r.materialLines?.length ?? 0,
  },
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
  line.unitOfMeasureCode = matchedUom(code) ?? ''
  if (!code || line.unitOfMeasureCode) return
  const stop = watch(
    () =>
      [
        line.skuCode,
        line.unitOfMeasureCode,
        matchedUom(code),
        formOpen.value,
        form.materialLines.includes(line),
      ] as const,
    ([sku, unit, next, open, active]) => {
      if (sku !== code || unit || !open || !active) stop()
      else if (next) {
        stop()
        line.unitOfMeasureCode = next
      }
    },
  )
}
function matchedUom(code: string) {
  const base = baseUomByCode.value.get(code)
  if (!base) return undefined
  return uomOptions.value.find((o) => o.value.toLowerCase() === base.toLowerCase())?.value
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const { formElement, focusFirstInvalidField } = useFirstInvalidField()
const form = reactive<MbomForm>(blankForm())

const sourcePending = ref(false)
const sourceFailed = ref(false)
const importedCount = ref(0)
const unmappedCount = ref(0)
const importedSnapshot = ref('')
const pendingSource = ref<string>()
let sourceRequest = 0

function selectSource(key: string) {
  if (key === form.ebomKey) {
    pendingSource.value = undefined
    return
  }
  if (JSON.stringify(form.materialLines) !== importedSnapshot.value) {
    pendingSource.value = key
    return
  }
  void loadSource(key)
}

async function loadSource(key: string) {
  pendingSource.value = undefined
  const request = ++sourceRequest
  form.ebomKey = key
  form.skuCode = ''
  form.materialLines = [blankMaterialLine()]
  importedSnapshot.value = JSON.stringify(form.materialLines)
  importedCount.value = 0
  unmappedCount.value = 0
  sourceFailed.value = false
  sourcePending.value = !!key
  if (!key) return

  const source = publishedEboms.value.find((row) => `${row.bomCode}::${row.revision}` === key)
  try {
    if (!source?.bomCode || !source.revision) throw new Error('找不到所选设计 BOM')
    const detail = await fetchEbomDetail(source.bomCode, source.revision)
    if (request !== sourceRequest || !formOpen.value) return
    if (!detail?.parentItemCode) throw new Error('来源缺少父项物料')
    form.skuCode = detail.parentItemCode
    const lines = (detail.lines ?? []).flatMap((line): MaterialLine[] =>
      line.childItemCode && line.quantity != null && line.unitOfMeasureCode
        ? [
            {
              skuCode: line.childItemCode,
              quantity: line.quantity,
              unitOfMeasureCode:
                uomOptions.value.find(
                  (option) => option.value.toLowerCase() === line.unitOfMeasureCode?.toLowerCase(),
                )?.value ?? line.unitOfMeasureCode,
              scrapRate: '0',
            },
          ]
        : [],
    )
    importedCount.value = lines.length
    unmappedCount.value = (detail.lines?.length ?? 0) - lines.length
    form.materialLines = lines.length ? lines : [blankMaterialLine()]
    importedSnapshot.value = JSON.stringify(form.materialLines)
  } catch (error) {
    if (request !== sourceRequest || !formOpen.value) return
    sourceFailed.value = true
    notifyError(error, '读取来源设计 BOM 失败，请重试。')
  } finally {
    if (request === sourceRequest) sourcePending.value = false
  }
}

function confirmSourceChange() {
  if (pendingSource.value !== undefined) void loadSource(pendingSource.value)
}

watch([ebomOptions, formOpen], ([options, open]) => {
  if (!open) {
    sourceRequest += 1
    pendingSource.value = undefined
    sourcePending.value = false
  } else if (!form.ebomKey && options.length === 1) {
    selectSource(options[0]!.value)
  }
})

function parseNumber(value: string | number | null | undefined): number | undefined {
  if (value === null || value === undefined) return undefined
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

// 修订号是本次新建的值，不能做成只读选择器；改为「已占用修订」建议 + 重复校验。
const { takenRevisions, takenRevisionsPending, isTaken } = useBomRevisionSuggestions(
  'manufacturing',
  () => form.skuCode,
)
const revisionTaken = computed(() => isTaken(form.revision))
// 换了产出物料，已填修订号对新物料未必可用 —— 清空下游。
watch(
  () => form.skuCode,
  () => {
    form.revision = ''
  },
)

const ebomValid = computed(() => !!form.ebomKey && !sourceFailed.value && !sourcePending.value)
const skuValid = computed(() => form.skuCode.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0 && !revisionTaken.value)
const effectiveValid = computed(() => !!form.effectiveDate)
function materialLineValid(line: MaterialLine) {
  return (
    line.skuCode.trim().length > 0 &&
    (parseNumber(line.quantity) ?? 0) > 0 &&
    line.unitOfMeasureCode.trim().length > 0
  )
}
const materialLinesValid = computed(
  () => form.materialLines.length > 0 && form.materialLines.every(materialLineValid),
)
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
const canSubmit = computed(
  () =>
    ebomValid.value &&
    skuValid.value &&
    revisionValid.value &&
    effectiveValid.value &&
    materialLinesValid.value &&
    recipeLinesValid.value &&
    !duplicateMaterial.value &&
    !selfReferenceMaterial.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  form.materialLines = [blankMaterialLine()]
  form.recipeLines = []
  importedSnapshot.value = JSON.stringify(form.materialLines)
  importedCount.value = 0
  unmappedCount.value = 0
  sourceFailed.value = false
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
    await focusFirstInvalidField()
    return
  }
  const source = publishedEboms.value.find(
    (row) => `${row.bomCode}::${row.revision}` === form.ebomKey,
  )
  if (!source?.bomCode || !source.revision) {
    sourceFailed.value = true
    showErrors.value = true
    await focusFirstInvalidField()
    return
  }
  const body: BusinessConsoleReleaseManufacturingBomRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    revision: form.revision.trim(),
    skuCode: form.skuCode.trim(),
    engineeringBomCode: source.bomCode,
    engineeringBomRevision: source.revision,
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
  } catch (error) {
    notifyOperationFailure('发布制造 BOM 失败', error, '发布制造 BOM 失败，请稍后重试。')
  }
}

// ── 查看物料行 + 配方行（get-by-id 拉真实明细）─────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleManufacturingBomItem | null>(null)
const detailPending = ref(false)
const viewMaterialLines = computed(() => viewTarget.value?.materialLines ?? [])
const viewRecipeLines = computed(() => viewTarget.value?.recipeLines ?? [])
async function openView(row: BusinessConsoleManufacturingBomItem) {
  // list 已带物料行，先用它即时显示；再 get-by-id 补齐配方行并刷新物料行。
  viewTarget.value = row
  viewOpen.value = true
  if (!row.bomCode || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchMbomDetail(row.bomCode, row.revision)
    if (detail) viewTarget.value = detail
  } catch (error) {
    // 结果一律 toast；不在抽屉里留常驻错误条。
    notifyError(error, '加载配方行失败，请稍后重试。')
  } finally {
    detailPending.value = false
  }
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
    <NvPageHeader
      title="MBOM 制造BOM"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${mbomsTotal} 个版本`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="mbomsPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="formOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              发布新版本
            </NvButton>
          </NvDialogTrigger>
          <!-- 组件行数不定，弹框高度会超出视口：内部滚动，别让「发布」按钮被挤到屏幕外（GH#1292 第 7 项）。 -->
          <NvDialogContent scrollable class="sm:max-w-3xl">
            <NvDialogHeader>
              <NvDialogTitle>发布制造 BOM 新版本</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only">
                引用一份已发布的设计 BOM，确认带入的产出物料和物料行，再填写修订号。
              </NvDialogDescription>
            </NvDialogHeader>
            <form ref="formElement" novalidate @submit.prevent="submitForm">
              <NvDialogBody>
                <div class="grid gap-5">
                  <p
                    v-if="showErrors && selfReferenceMaterial"
                    class="text-sm text-destructive"
                    role="alert"
                  >
                    物料不能与产出物料「{{
                      skuLabel(selfReferenceMaterial)
                    }}」相同——产出物料不能把自己当原料，请改选别的物料。
                  </p>
                  <p
                    v-else-if="showErrors && duplicateMaterial"
                    class="text-sm text-destructive"
                    role="alert"
                  >
                    物料「{{
                      skuLabel(duplicateMaterial)
                    }}」重复了——同一物料只能有一行，请合并数量或删除重复行。
                  </p>
                  <p
                    v-else-if="showErrors && !canSubmit"
                    class="text-sm text-destructive"
                    role="alert"
                  >
                    请完整填写带 * 的必填项，并确保至少一行物料填好物料、数量（大于
                    0）与单位；填写了的配方行须含参数与目标值。
                  </p>
                  <p
                    v-if="!publishedEbomsPending && !publishedEbomsError && !hasSelectorData"
                    class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
                    role="alert"
                  >
                    当前没有已发布的设计 BOM 可引用。请先在 EBOM 页发布版本，再回来发布制造 BOM。
                  </p>

                  <FormSectionTitle>版本头</FormSectionTitle>
                  <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                    <NvField :data-invalid="showErrors && !ebomValid">
                      <NvFieldLabel for="mbom-ebom"
                        >引用设计 BOM <span class="text-destructive">*</span></NvFieldLabel
                      >
                      <NvEntityPicker
                        id="mbom-ebom"
                        :model-value="form.ebomKey"
                        :options="ebomOptions"
                        :loading="publishedEbomsPending"
                        :invalid="showErrors && !ebomValid"
                        :aria-describedby="showErrors && !ebomValid ? 'mbom-ebom-error' : undefined"
                        title="选择已发布设计 BOM"
                        placeholder="选择已发布 EBOM"
                        :empty-text="hasSelectorData ? '没有匹配的设计 BOM' : '暂无已发布设计 BOM'"
                        :show-code="false"
                        @update:model-value="selectSource"
                      />
                      <NvFieldError
                        v-if="showErrors && !ebomValid"
                        id="mbom-ebom-error"
                        :errors="[
                          sourceFailed ? '请重试读取来源。' : '请选择并加载已发布设计 BOM。',
                        ]"
                      />
                      <NvFieldDescription v-if="sourcePending" role="status"
                        >正在读取来源明细…</NvFieldDescription
                      >
                      <NvButton
                        v-else-if="sourceFailed"
                        type="button"
                        variant="outline"
                        size="sm"
                        @click="loadSource(form.ebomKey)"
                        >重试读取来源</NvButton
                      >
                      <NvButton
                        v-if="publishedEbomsError"
                        type="button"
                        variant="outline"
                        size="sm"
                        @click="refreshEboms"
                        >重试加载设计 BOM</NvButton
                      >
                      <NvFieldDescription v-if="form.skuCode" role="status">
                        来源 {{ form.ebomKey.replace('::', ' · 修订 ') }}，已带入
                        {{ importedCount }} 行。
                        <template v-if="unmappedCount"
                          >{{ unmappedCount }} 行缺少物料、数量或单位，请手动补录。</template
                        >
                      </NvFieldDescription>
                    </NvField>
                    <NvField>
                      <NvFieldLabel for="mbom-sku">产出物料</NvFieldLabel>
                      <NvInput
                        id="mbom-sku"
                        :model-value="form.skuCode ? skuLabel(form.skuCode) : ''"
                        placeholder="由来源父项带入"
                        readonly
                      />
                      <NvFieldDescription>更换产出物料时请选择其他来源。</NvFieldDescription>
                    </NvField>
                    <div
                      v-if="pendingSource !== undefined"
                      class="space-y-2 rounded-md border p-3 sm:col-span-2"
                    >
                      <p role="alert">更换来源会替换已修改的物料行，配方行会保留。</p>
                      <div class="flex gap-2">
                        <NvButton
                          type="button"
                          variant="outline"
                          size="sm"
                          @click="pendingSource = undefined"
                          >保留当前来源</NvButton
                        >
                        <NvButton type="button" size="sm" @click="confirmSourceChange"
                          >更换来源</NvButton
                        >
                      </div>
                    </div>
                    <NvField :data-invalid="showErrors && !revisionValid">
                      <NvFieldLabel for="mbom-rev"
                        >修订号 <span class="text-destructive">*</span></NvFieldLabel
                      >
                      <NvCombobox
                        id="mbom-rev"
                        :aria-invalid="showErrors && !revisionValid"
                        :aria-describedby="
                          showErrors && !revisionValid ? 'mbom-rev-error' : undefined
                        "
                        v-model="form.revision"
                        :suggestions="takenRevisions"
                        :disabled="!form.skuCode || takenRevisionsPending"
                        :placeholder="form.skuCode ? '填写新修订号' : '请先选产出物料'"
                        empty-text="该物料还没有历史修订"
                      />
                      <NvFieldDescription>例如 A、B、001。</NvFieldDescription>
                      <p v-if="revisionTaken" class="text-sm text-destructive" role="alert">
                        修订号「{{ form.revision.trim() }}」已存在，请换一个新号。
                      </p>
                      <p v-else-if="takenRevisions.length" class="text-xs text-muted-foreground">
                        已占用：{{ takenRevisions.map((r) => r.value).join('、') }}
                      </p>
                      <NvFieldError
                        v-if="showErrors && !revisionValid"
                        id="mbom-rev-error"
                        :errors="['请填写未占用的修订号。']"
                      />
                    </NvField>
                    <NvField :data-invalid="showErrors && !effectiveValid">
                      <NvFieldLabel>生效日 <span class="text-destructive">*</span></NvFieldLabel>
                      <NvDatePicker
                        v-model="form.effectiveDate"
                        :aria-invalid="showErrors && !effectiveValid"
                        :aria-describedby="
                          showErrors && !effectiveValid ? 'mbom-date-error' : undefined
                        "
                        placeholder="选择生效日"
                        class="w-full"
                      />
                      <NvFieldError
                        v-if="showErrors && !effectiveValid"
                        id="mbom-date-error"
                        :errors="['请选择生效日。']"
                      />
                    </NvField>
                  </NvFieldGroup>

                  <div class="flex items-center justify-between">
                    <FormSectionTitle>物料行</FormSectionTitle>
                    <NvButton
                      type="button"
                      variant="outline"
                      size="sm"
                      :disabled="sourcePending"
                      @click="addMaterialLine"
                    >
                      <PlusIcon aria-hidden="true" />
                      增加物料
                    </NvButton>
                  </div>
                  <div class="grid gap-2">
                    <div
                      v-for="(line, index) in sourcePending ? [] : form.materialLines"
                      :key="index"
                      class="grid grid-cols-1 items-end gap-2 rounded-md border p-2 sm:grid-cols-[1fr_5rem_7rem_6rem_auto]"
                    >
                      <NvField :data-invalid="showErrors && !line.skuCode.trim()">
                        <NvFieldLabel :for="`mbom-mat-${index}`"
                          >物料 <span class="text-destructive">*</span></NvFieldLabel
                        >
                        <DirectoryPicker
                          :id="`mbom-mat-${index}`"
                          :invalid="
                            showErrors &&
                            (!line.skuCode.trim() ||
                              line.skuCode === duplicateMaterial ||
                              line.skuCode === selfReferenceMaterial)
                          "
                          :aria-describedby="
                            showErrors &&
                            (!line.skuCode.trim() ||
                              line.skuCode === duplicateMaterial ||
                              line.skuCode === selfReferenceMaterial)
                              ? `mbom-mat-${index}-error`
                              : undefined
                          "
                          v-model="line.skuCode"
                          directory-type="material"
                          creatable
                          placeholder="选择物料"
                          @update:model-value="(v: string) => applyMaterialUom(line, v)"
                        />
                        <NvFieldError
                          v-if="
                            showErrors &&
                            (!line.skuCode.trim() ||
                              line.skuCode === duplicateMaterial ||
                              line.skuCode === selfReferenceMaterial)
                          "
                          :id="`mbom-mat-${index}-error`"
                          :errors="['请选择有效且不重复的物料。']"
                        />
                      </NvField>
                      <NvField :data-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0">
                        <NvFieldLabel :for="`mbom-qty-${index}`"
                          >数量 <span class="text-destructive">*</span></NvFieldLabel
                        >
                        <NvInput
                          :id="`mbom-qty-${index}`"
                          :aria-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0"
                          :aria-describedby="
                            showErrors && (parseNumber(line.quantity) ?? 0) <= 0
                              ? `mbom-qty-${index}-error`
                              : undefined
                          "
                          v-model="line.quantity"
                          type="number"
                          min="0"
                          step="any"
                        />
                        <NvFieldError
                          v-if="showErrors && (parseNumber(line.quantity) ?? 0) <= 0"
                          :id="`mbom-qty-${index}-error`"
                          :errors="['数量必须大于 0。']"
                        />
                      </NvField>
                      <NvField :data-invalid="showErrors && !line.unitOfMeasureCode.trim()">
                        <NvFieldLabel :for="`mbom-uom-${index}`"
                          >单位 <span class="text-destructive">*</span></NvFieldLabel
                        >
                        <NvSelect v-model="line.unitOfMeasureCode">
                          <NvSelectTrigger
                            :id="`mbom-uom-${index}`"
                            :aria-invalid="showErrors && !line.unitOfMeasureCode.trim()"
                            :aria-describedby="
                              showErrors && !line.unitOfMeasureCode.trim()
                                ? `mbom-uom-${index}-error`
                                : undefined
                            "
                            ><NvSelectValue placeholder="单位"
                          /></NvSelectTrigger>
                          <NvSelectContent>
                            <NvSelectItem
                              v-if="
                                line.unitOfMeasureCode &&
                                !uomOptions.some(
                                  (option) => option.value === line.unitOfMeasureCode,
                                )
                              "
                              :value="line.unitOfMeasureCode"
                            >
                              {{ line.unitOfMeasureCode }}
                            </NvSelectItem>
                            <NvSelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{
                              o.label
                            }}</NvSelectItem>
                          </NvSelectContent>
                        </NvSelect>
                        <NvFieldError
                          v-if="showErrors && !line.unitOfMeasureCode.trim()"
                          :id="`mbom-uom-${index}-error`"
                          :errors="['请选择单位。']"
                        />
                      </NvField>
                      <NvField>
                        <NvFieldLabel :for="`mbom-scrap-${index}`">损耗率</NvFieldLabel>
                        <NvInput
                          :id="`mbom-scrap-${index}`"
                          v-model="line.scrapRate"
                          type="number"
                          min="0"
                          max="1"
                          step="any"
                          placeholder="0~1"
                        />
                      </NvField>
                      <NvButton
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label="删除该物料行"
                        :disabled="form.materialLines.length <= 1"
                        @click="removeMaterialLine(index)"
                      >
                        <Trash2Icon aria-hidden="true" />
                      </NvButton>
                    </div>
                  </div>

                  <div class="flex items-center justify-between">
                    <FormSectionTitle>配方行（可选）</FormSectionTitle>
                    <NvButton type="button" variant="outline" size="sm" @click="addRecipeLine">
                      <PlusIcon aria-hidden="true" />
                      增加配方
                    </NvButton>
                  </div>
                  <div v-if="form.recipeLines.length" class="grid gap-2">
                    <div
                      v-for="(line, index) in form.recipeLines"
                      :key="index"
                      class="grid grid-cols-1 items-end gap-2 rounded-md border p-2 sm:grid-cols-[1fr_1fr_6rem_auto]"
                    >
                      <NvField :data-invalid="showErrors && !line.parameterCode.trim()">
                        <NvFieldLabel :for="`mbom-param-${index}`"
                          >参数 <span class="text-destructive">*</span></NvFieldLabel
                        >
                        <NvInput
                          :id="`mbom-param-${index}`"
                          :aria-invalid="showErrors && !line.parameterCode.trim()"
                          :aria-describedby="
                            showErrors && !line.parameterCode.trim()
                              ? `mbom-param-${index}-error`
                              : undefined
                          "
                          v-model="line.parameterCode"
                          placeholder="如 温度"
                        />
                        <NvFieldError
                          v-if="showErrors && !line.parameterCode.trim()"
                          :id="`mbom-param-${index}-error`"
                          :errors="['请填写配方参数。']"
                        />
                      </NvField>
                      <NvField :data-invalid="showErrors && !line.targetValue.trim()">
                        <NvFieldLabel :for="`mbom-target-${index}`"
                          >目标值 <span class="text-destructive">*</span></NvFieldLabel
                        >
                        <NvInput
                          :id="`mbom-target-${index}`"
                          :aria-invalid="showErrors && !line.targetValue.trim()"
                          :aria-describedby="
                            showErrors && !line.targetValue.trim()
                              ? `mbom-target-${index}-error`
                              : undefined
                          "
                          v-model="line.targetValue"
                          placeholder="如 180"
                        />
                        <NvFieldError
                          v-if="showErrors && !line.targetValue.trim()"
                          :id="`mbom-target-${index}-error`"
                          :errors="['请填写目标值。']"
                        />
                      </NvField>
                      <NvField>
                        <NvFieldLabel :for="`mbom-runit-${index}`">单位</NvFieldLabel>
                        <NvSearchSelect
                          :id="`mbom-runit-${index}`"
                          v-model="line.unitOfMeasureCode"
                          :options="uomOptions"
                          placeholder="选择单位"
                          empty-text="暂无计量单位，请先在基础数据维护"
                          aria-label="工艺参数单位"
                        />
                      </NvField>
                      <NvButton
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label="删除该配方行"
                        @click="removeRecipeLine(index)"
                      >
                        <Trash2Icon aria-hidden="true" />
                      </NvButton>
                    </div>
                  </div>
                </div>
              </NvDialogBody>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
                <NvButton
                  variant="brand"
                  type="submit"
                  :disabled="releasePending || sourcePending || pendingSource !== undefined"
                >
                  <NvSpinner v-if="releasePending" aria-hidden="true" />
                  发布版本
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2">
      <NvMetricCard
        variant="breakdown"
        label="制造 BOM 版本"
        :value="mbomsTotal"
        unit="个"
        :segments="mbomSegments"
      />
      <NvMetricCard
        variant="alert"
        label="草稿待发布"
        :value="draftCount"
        unit="个"
        :tone="draftCount > 0 ? 'warning' : 'neutral'"
        :status="
          draftCount > 0
            ? { label: '待评审', tone: 'warning' }
            : { label: '无待办', tone: 'success' }
        "
        :foot-start="
          draftCount > 0 ? '确认用料与损耗后发布，供生产版本引用。' : '当前没有待发布的制造 BOM。'
        "
      />
    </div>

    <NvToolbar v-model:search="skuSearch" search-placeholder="按产出物料编码筛选">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="状态筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in STATUS_FILTER_OPTIONS" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="mbomsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
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
        <NvStatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{
        row.effectiveDate ? formatDate(row.effectiveDate) : '长期'
      }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openView(row)"
            >查看物料</NvButton
          >
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="viewOpen">
      <NvSheetContent class="sm:max-w-lg">
        <NvSheetHeader>
          <NvSheetTitle>制造 BOM · 物料行</NvSheetTitle>
          <NvSheetDescription>
            {{
              viewTarget
                ? `${viewTarget.bomCode} · 修订 ${viewTarget.revision} · ${skuLabel(viewTarget.skuCode)}`
                : ''
            }}
          </NvSheetDescription>
        </NvSheetHeader>
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
                    <td class="px-3 py-2 text-right tabular-nums">
                      {{ formatScrap(line.scrapRate) }}
                    </td>
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
            <div
              v-if="detailPending"
              class="flex items-center gap-2 py-2 text-sm text-muted-foreground"
            >
              <NvSpinner aria-hidden="true" />
              加载配方行…
            </div>
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
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
