<script setup lang="ts">
import type {
  BusinessConsoleEngineeringBomItem,
  BusinessConsoleReleaseEngineeringBomRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { useBusinessSkus, useBusinessUoms } from '@/composables/useBusinessMasterData'
import { useBomRevisionSuggestions, useEngineeringEboms } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
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
  Spinner,
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
    title: 'EBOM 设计BOM',
    requiredPermissions: ['business.engineering.boms.read'],
  },
})

const {
  eboms,
  ebomsError,
  ebomsPending,
  ebomsTotal,
  filters,
  refresh,
  releaseEbom,
  releasePending,
  fetchEbomDetail,
} = useEngineeringEboms()

const { skus } = useBusinessSkus()
const { uoms } = useBusinessUoms()

// 状态枚举用后端真值 Published/Draft/Archived（不要用 Released）。
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

const parentSearch = computed({
  get: () => filters.parentItemCode ?? '',
  set: (value: string) => {
    filters.parentItemCode = value.trim() ? value : undefined
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

const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
// 物料编码 → 基本单位，选物料后自动带出行单位（仍可手动覆盖）。
const baseUomByCode = computed(
  () =>
    new Map(skus.value.filter((s) => s.code).map((s) => [s.code as string, s.baseUomCode ?? ''])),
)
const uomOptions = computed(() =>
  uoms.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: u.displayName ?? (u.code as string) })),
)

function engStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(
  () => eboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'published').length,
)
const draftCount = computed(
  () => eboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'draft').length,
)
// 已发布/草稿是同一批版本的两种状态，用一张构成卡表达；已归档等其余状态单列，
// 未取回的行由 pagedBreakdownSegments 补齐，分段之和恒等于版本总数。
const ebomSegments = computed(() => {
  const others = eboms.value.length - publishedCount.value - draftCount.value
  const segments: NvMetricSegment[] = [
    { key: 'published', label: '已发布', value: publishedCount.value, tone: 'success' },
    { key: 'draft', label: '草稿', value: draftCount.value, tone: 'warning' },
  ]
  if (others > 0)
    segments.push({ key: 'others', label: '已归档等', value: others, tone: 'neutral' })
  return pagedBreakdownSegments(ebomsTotal.value, segments)
})

const listErrorMessage = computed(() => formatError(ebomsError.value))

const columns: NvDataTableColumn<BusinessConsoleEngineeringBomItem>[] = [
  { key: 'bomCode', header: 'BOM 编号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'parentItemCode', header: '父项' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 发布新版本向导 ──────────────────────────────────────────────
interface ComponentLine {
  componentCode: string
  quantity: string | number
  unitOfMeasureCode: string
}
interface EbomForm {
  parentItemCode: string
  revision: string
  effectiveDate: string | null
  lines: ComponentLine[]
}
function blankLine(): ComponentLine {
  return { componentCode: '', quantity: '1', unitOfMeasureCode: '' }
}
function blankForm(): EbomForm {
  return { parentItemCode: '', revision: '', effectiveDate: today(), lines: [blankLine()] }
}

// 选物料后把该行单位自动设为其基本单位（按单位选项大小写不敏感匹配真实 code——
// SKU 的基本单位可能与单位表大小写不一致，如 'PCS' vs 'pcs'；匹配不到则不填，避免落到无效值/占位符）。
function applyComponentUom(line: ComponentLine, code: string) {
  const base = baseUomByCode.value.get(code)
  if (!base) return
  const match = uomOptions.value.find((o) => o.value.toLowerCase() === base.toLowerCase())
  if (match) line.unitOfMeasureCode = match.value
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<EbomForm>(blankForm())

function parseNumber(value: string | number | null | undefined): number | undefined {
  if (value === null || value === undefined) return undefined
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

// 修订号是本次新建的值，不能做成只读选择器；改为「已占用修订」建议 + 重复校验，
// 让用户看得到哪些号已经用掉，又不会被目录挡住新号。
const { takenRevisions, takenRevisionsPending, isTaken } = useBomRevisionSuggestions(
  'engineering',
  () => form.parentItemCode,
)
const revisionTaken = computed(() => isTaken(form.revision))
// 换了父项物料，已填修订号对新物料未必可用 —— 清空下游，重新按新目录填。
watch(
  () => form.parentItemCode,
  () => {
    form.revision = ''
  },
)

const parentValid = computed(() => form.parentItemCode.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0 && !revisionTaken.value)
const effectiveValid = computed(() => !!form.effectiveDate)
function lineValid(line: ComponentLine) {
  return (
    line.componentCode.trim().length > 0 &&
    (parseNumber(line.quantity) ?? 0) > 0 &&
    line.unitOfMeasureCode.trim().length > 0
  )
}
const linesValid = computed(() => form.lines.length > 0 && form.lines.every(lineValid))
// 同一组件不能重复（后端 AddLine 拒绝重复子件，否则 500）。返回第一个重复的组件编码。
const duplicateComponent = computed(() => {
  const seen = new Set<string>()
  for (const l of form.lines) {
    const c = l.componentCode.trim()
    if (!c) continue
    if (seen.has(c)) return c
    seen.add(c)
  }
  return ''
})
// 组件不能等于父项（自引用会成环，后端拒绝）。返回第一个等于父项的组件编码。
const selfReferenceComponent = computed(() => {
  const parent = form.parentItemCode.trim()
  if (!parent) return ''
  for (const l of form.lines) {
    if (l.componentCode.trim() === parent) return parent
  }
  return ''
})
const canSubmit = computed(
  () =>
    parentValid.value &&
    revisionValid.value &&
    effectiveValid.value &&
    linesValid.value &&
    !duplicateComponent.value &&
    !selfReferenceComponent.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function addLine() {
  form.lines.push(blankLine())
}
function removeLine(index: number) {
  if (form.lines.length <= 1) return
  form.lines.splice(index, 1)
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleReleaseEngineeringBomRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    revision: form.revision.trim(),
    parentItemCode: form.parentItemCode.trim(),
    effectiveDate: form.effectiveDate ?? undefined,
    lines: form.lines.map((line) => ({
      componentCode: line.componentCode.trim(),
      quantity: parseNumber(line.quantity) ?? 0,
      unitOfMeasureCode: line.unitOfMeasureCode.trim(),
    })),
  }
  try {
    await releaseEbom(body)
    notifySuccess(
      `已发布设计 BOM「${skuLabel(form.parentItemCode)}」修订 ${form.revision.trim()}。`,
    )
    showErrors.value = false
    formOpen.value = false
  } catch (error) {
    notifyOperationFailure('发布设计 BOM 失败', error, '发布设计 BOM 失败，请稍后重试。')
  }
}

// ── 查看版本明细（get-by-id 拉真实组件行）────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringBomItem | null>(null)
const detailPending = ref(false)
const viewLines = computed(() => viewTarget.value?.lines ?? [])
async function openView(row: BusinessConsoleEngineeringBomItem) {
  viewTarget.value = row
  viewOpen.value = true
  if (!row.bomCode || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchEbomDetail(row.bomCode, row.revision)
    if (detail) viewTarget.value = detail
  } catch (error) {
    // 结果一律 toast；不在抽屉里留常驻错误条。
    notifyError(error, '加载组件行失败，请稍后重试。')
  } finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
function uomLabel(code?: string | null) {
  if (!code) return '—'
  return uomOptions.value.find((o) => o.value === code)?.label ?? code
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="EBOM 设计BOM"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${ebomsTotal} 个版本`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="ebomsPending"
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
          <NvDialogContent class="sm:max-w-3xl">
            <NvDialogHeader>
              <NvDialogTitle>发布设计 BOM 新版本</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only">
                填写父项物料、修订号与组件行，发布为不可变版本。
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p
                v-if="showErrors && selfReferenceComponent"
                class="text-sm text-destructive"
                role="alert"
              >
                组件不能与父项「{{
                  skuLabel(selfReferenceComponent)
                }}」相同——一个物料不能把自己当组件，请改选别的组件。
              </p>
              <p
                v-else-if="showErrors && duplicateComponent"
                class="text-sm text-destructive"
                role="alert"
              >
                组件「{{
                  skuLabel(duplicateComponent)
                }}」重复了——同一组件只能有一行，请合并数量或删除重复行。
              </p>
              <p v-else-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项，并确保至少一行组件填好编码、数量（大于 0）与单位。
              </p>

              <FormSectionTitle>版本头</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-3">
                <NvField :data-invalid="showErrors && !parentValid">
                  <NvFieldLabel for="ebom-parent"
                    >父项物料 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="form.parentItemCode">
                    <NvSelectTrigger id="ebom-parent"
                      ><NvSelectValue placeholder="选择父项"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{
                        o.label
                      }}</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="showErrors && !revisionValid">
                  <NvFieldLabel for="ebom-rev"
                    >修订号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvCombobox
                    id="ebom-rev"
                    v-model="form.revision"
                    :suggestions="takenRevisions"
                    :disabled="!form.parentItemCode || takenRevisionsPending"
                    :placeholder="
                      form.parentItemCode ? '填写新修订号，如 A、B、001' : '请先选父项物料'
                    "
                    empty-text="该物料还没有历史修订"
                  />
                  <p v-if="revisionTaken" class="text-sm text-destructive" role="alert">
                    修订号「{{ form.revision.trim() }}」已存在，请换一个新号。
                  </p>
                  <p v-else-if="takenRevisions.length" class="text-xs text-muted-foreground">
                    已占用：{{ takenRevisions.map((r) => r.value).join('、') }}
                  </p>
                </NvField>
                <NvField :data-invalid="showErrors && !effectiveValid">
                  <NvFieldLabel>生效日 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvDatePicker
                    v-model="form.effectiveDate"
                    placeholder="选择生效日"
                    class="w-full"
                  />
                </NvField>
              </NvFieldGroup>

              <div class="flex items-center justify-between">
                <FormSectionTitle>组件行</FormSectionTitle>
                <NvButton type="button" variant="outline" size="sm" @click="addLine">
                  <PlusIcon aria-hidden="true" />
                  增加组件
                </NvButton>
              </div>
              <div class="grid gap-2">
                <div
                  v-for="(line, index) in form.lines"
                  :key="index"
                  class="grid grid-cols-[1fr_6rem_8rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <NvField :data-invalid="showErrors && !line.componentCode.trim()">
                    <NvFieldLabel :for="`ebom-comp-${index}`"
                      >组件物料 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvSelect
                      v-model="line.componentCode"
                      @update:model-value="(v) => applyComponentUom(line, String(v ?? ''))"
                    >
                      <NvSelectTrigger :id="`ebom-comp-${index}`"
                        ><NvSelectValue placeholder="选择组件"
                      /></NvSelectTrigger>
                      <NvSelectContent>
                        <NvSelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{
                          o.label
                        }}</NvSelectItem>
                      </NvSelectContent>
                    </NvSelect>
                  </NvField>
                  <NvField :data-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0">
                    <NvFieldLabel :for="`ebom-qty-${index}`"
                      >数量 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvInput
                      :id="`ebom-qty-${index}`"
                      v-model="line.quantity"
                      type="number"
                      min="0"
                      step="any"
                    />
                  </NvField>
                  <NvField :data-invalid="showErrors && !line.unitOfMeasureCode.trim()">
                    <NvFieldLabel :for="`ebom-uom-${index}`"
                      >单位 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvSelect v-model="line.unitOfMeasureCode">
                      <NvSelectTrigger :id="`ebom-uom-${index}`"
                        ><NvSelectValue placeholder="单位"
                      /></NvSelectTrigger>
                      <NvSelectContent>
                        <NvSelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{
                          o.label
                        }}</NvSelectItem>
                      </NvSelectContent>
                    </NvSelect>
                  </NvField>
                  <NvButton
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="删除该组件行"
                    :disabled="form.lines.length <= 1"
                    @click="removeLine(index)"
                  >
                    <Trash2Icon aria-hidden="true" />
                  </NvButton>
                </div>
              </div>

              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
                <NvButton type="submit" :disabled="releasePending">
                  <Spinner v-if="releasePending" aria-hidden="true" />
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
        label="设计 BOM 版本"
        :value="ebomsTotal"
        unit="个"
        :segments="ebomSegments"
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
          draftCount > 0 ? '确认组件行后发布，供生产版本引用。' : '当前没有待发布的设计 BOM。'
        "
      />
    </div>

    <NvToolbar v-model:search="parentSearch" search-placeholder="按父项物料编码筛选">
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
      :total-items="ebomsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="eboms"
      :row-key="(r) => `${r.bomCode}:${r.revision}`"
      :loading="ebomsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有设计 BOM。可发布新版本，把父项物料与其组件行登记为一个不可变版本。"
    >
      <template #cell-parentItemCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuLabel(row.parentItemCode) }}</span>
          <span class="text-xs text-muted-foreground">{{ row.parentItemCode }}</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{
        row.effectiveDate ? formatDate(row.effectiveDate) : '长期'
      }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openView(row)">查看</NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="viewOpen">
      <NvSheetContent class="sm:max-w-lg">
        <NvSheetHeader>
          <NvSheetTitle>设计 BOM · 组件行</NvSheetTitle>
          <NvSheetDescription>
            {{
              viewTarget
                ? `${viewTarget.bomCode} · 修订 ${viewTarget.revision} · ${skuLabel(viewTarget.parentItemCode)}`
                : ''
            }}
          </NvSheetDescription>
        </NvSheetHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2">
          <div class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <NvStatusBadge
                :label="engStatus(viewTarget.status).label"
                :tone="engStatus(viewTarget.status).tone"
              />
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">生效日</span>
              <span class="font-medium">{{
                viewTarget.effectiveDate ? formatDate(viewTarget.effectiveDate) : '长期'
              }}</span>
            </div>
          </div>

          <div
            v-if="detailPending"
            class="flex items-center gap-2 py-4 text-sm text-muted-foreground"
          >
            <Spinner aria-hidden="true" />
            加载组件行…
          </div>
          <div v-else-if="viewLines.length" class="overflow-hidden rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-left font-medium">组件</th>
                  <th class="px-3 py-2 text-right font-medium">数量</th>
                  <th class="px-3 py-2 text-left font-medium">单位</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(line, i) in viewLines" :key="i" class="border-t">
                  <td class="px-3 py-2">
                    <div class="flex flex-col gap-0.5">
                      <span>{{ skuLabel(line.childItemCode) }}</span>
                      <span class="text-xs text-muted-foreground">{{ line.childItemCode }}</span>
                    </div>
                  </td>
                  <td class="px-3 py-2 text-right tabular-nums">{{ line.quantity ?? '—' }}</td>
                  <td class="px-3 py-2">{{ uomLabel(line.unitOfMeasureCode) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该版本没有组件行。
          </p>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
