<script setup lang="ts">
import type {
  BusinessConsoleManufacturingBomItem,
  BusinessConsoleReleaseManufacturingBomRequest,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessSkus, useBusinessUoms } from '@/composables/useBusinessMasterData'
import { useEngineeringMboms, usePublishedEboms } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  DatePicker,
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
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate } from '@/utils/format'
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

const columns: DataTableColumn<BusinessConsoleManufacturingBomItem>[] = [
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
    effectiveDate: null,
    materialLines: [blankMaterialLine()],
    recipeLines: [],
  }
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
// 配方行可选；若填了某行，则该行参数 + 目标值须齐。
function recipeLineComplete(line: RecipeLine) {
  return line.parameterCode.trim().length > 0 && line.targetValue.trim().length > 0
}
const recipeLinesValid = computed(() => form.recipeLines.every(recipeLineComplete))
const hasSelectorData = computed(() => ebomOptions.value.length > 0)
const canSubmit = computed(() =>
  ebomValid.value && skuValid.value && revisionValid.value && effectiveValid.value
  && materialLinesValid.value && recipeLinesValid.value,
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

// ── 查看物料行（list 含 MaterialLines）──────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleManufacturingBomItem | null>(null)
function openView(row: BusinessConsoleManufacturingBomItem) {
  viewTarget.value = row
  viewOpen.value = true
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function formatScrap(rate?: number | null) {
  if (rate == null) return '—'
  return `${(rate * 100).toFixed(1)}%`
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
        <Button size="sm" variant="outline" type="button" :disabled="mbomsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="formOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              发布新版本
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-3xl">
            <DialogHeader>
              <DialogTitle>发布制造 BOM 新版本</DialogTitle>
              <DialogDescription>
                制造 BOM 须引用一份已发布的设计 BOM。一经发布即不可变，修改请填新物料行 + 新修订号再发布。带 * 为必填项。
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
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
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !ebomValid">
                  <FieldLabel for="mbom-ebom">引用设计 BOM <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="form.ebomKey">
                    <SelectTrigger id="mbom-ebom"><SelectValue placeholder="选择已发布 EBOM" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in ebomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>仅可选择已发布的设计 BOM。</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !skuValid">
                  <FieldLabel for="mbom-sku">产出物料 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="form.skuCode">
                    <SelectTrigger id="mbom-sku"><SelectValue placeholder="选择产出物料" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>来自基础数据物料。</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !revisionValid">
                  <FieldLabel for="mbom-rev">修订号 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="mbom-rev" v-model="form.revision" placeholder="如 A、B、001" />
                </Field>
                <Field :data-invalid="showErrors && !effectiveValid">
                  <FieldLabel>生效日 <span class="text-destructive">*</span></FieldLabel>
                  <DatePicker v-model="form.effectiveDate" placeholder="选择生效日" class="w-full" />
                </Field>
              </FieldGroup>

              <div class="flex items-center justify-between">
                <FormSectionTitle>物料行</FormSectionTitle>
                <Button type="button" variant="outline" size="sm" @click="addMaterialLine">
                  <PlusIcon aria-hidden="true" />
                  增加物料
                </Button>
              </div>
              <div class="grid gap-2">
                <div
                  v-for="(line, index) in form.materialLines"
                  :key="index"
                  class="grid grid-cols-[1fr_5rem_7rem_6rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <Field :data-invalid="showErrors && !line.skuCode.trim()">
                    <FieldLabel :for="`mbom-mat-${index}`">物料 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="line.skuCode">
                      <SelectTrigger :id="`mbom-mat-${index}`"><SelectValue placeholder="选择物料" /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field :data-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0">
                    <FieldLabel :for="`mbom-qty-${index}`">数量 <span class="text-destructive">*</span></FieldLabel>
                    <Input :id="`mbom-qty-${index}`" v-model="line.quantity" type="number" min="0" step="any" />
                  </Field>
                  <Field :data-invalid="showErrors && !line.unitOfMeasureCode.trim()">
                    <FieldLabel :for="`mbom-uom-${index}`">单位 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="line.unitOfMeasureCode">
                      <SelectTrigger :id="`mbom-uom-${index}`"><SelectValue placeholder="单位" /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel :for="`mbom-scrap-${index}`">损耗率</FieldLabel>
                    <Input :id="`mbom-scrap-${index}`" v-model="line.scrapRate" type="number" min="0" max="1" step="any" placeholder="0~1" />
                  </Field>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="删除该物料行"
                    :disabled="form.materialLines.length <= 1"
                    @click="removeMaterialLine(index)"
                  >
                    <Trash2Icon aria-hidden="true" />
                  </Button>
                </div>
              </div>

              <div class="flex items-center justify-between">
                <FormSectionTitle>配方行（可选）</FormSectionTitle>
                <Button type="button" variant="outline" size="sm" @click="addRecipeLine">
                  <PlusIcon aria-hidden="true" />
                  增加配方
                </Button>
              </div>
              <p class="text-xs text-muted-foreground">
                配方参数（如温度、压力、时长）按需登记。发布后配方明细暂不在列表回显（待后端）。
              </p>
              <div v-if="form.recipeLines.length" class="grid gap-2">
                <div
                  v-for="(line, index) in form.recipeLines"
                  :key="index"
                  class="grid grid-cols-[1fr_1fr_6rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <Field :data-invalid="showErrors && !line.parameterCode.trim()">
                    <FieldLabel :for="`mbom-param-${index}`">参数 <span class="text-destructive">*</span></FieldLabel>
                    <Input :id="`mbom-param-${index}`" v-model="line.parameterCode" placeholder="如 温度" />
                  </Field>
                  <Field :data-invalid="showErrors && !line.targetValue.trim()">
                    <FieldLabel :for="`mbom-target-${index}`">目标值 <span class="text-destructive">*</span></FieldLabel>
                    <Input :id="`mbom-target-${index}`" v-model="line.targetValue" placeholder="如 180" />
                  </Field>
                  <Field>
                    <FieldLabel :for="`mbom-runit-${index}`">单位</FieldLabel>
                    <Input :id="`mbom-runit-${index}`" v-model="line.unitOfMeasureCode" placeholder="如 ℃" />
                  </Field>
                  <Button type="button" variant="ghost" size="icon" aria-label="删除该配方行" @click="removeRecipeLine(index)">
                    <Trash2Icon aria-hidden="true" />
                  </Button>
                </div>
              </div>

              <DialogFooter>
                <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
                <Button type="submit" :disabled="releasePending || !canSubmit">
                  <Spinner v-if="releasePending" aria-hidden="true" />
                  发布版本
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="已发布 MBOM" :value="publishedCount" hint="可被生产版本绑定的制造 BOM" />
      <SectionCard description="草稿 MBOM" :value="draftCount" hint="尚未发布、不可被绑定的版本" />
    </SectionCards>

    <Toolbar v-model:search="skuSearch" search-placeholder="按产出物料编码筛选">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="状态筛选"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in STATUS_FILTER_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="mboms"
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
        <StatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{ row.effectiveDate ? formatDate(row.effectiveDate) : '长期' }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <Button type="button" variant="ghost" size="sm" @click="openView(row)">查看物料</Button>
        </div>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="mbomsTotal" />

    <Sheet v-model:open="viewOpen">
      <SheetContent class="sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>制造 BOM · 物料行</SheetTitle>
          <SheetDescription>
            {{ viewTarget ? `${viewTarget.bomCode} · 修订 ${viewTarget.revision} · ${skuLabel(viewTarget.skuCode)}` : '' }}
          </SheetDescription>
        </SheetHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2">
          <div v-if="viewTarget.materialLines?.length" class="overflow-hidden rounded-md border">
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
                <tr v-for="(line, i) in viewTarget.materialLines" :key="i" class="border-t">
                  <td class="px-3 py-2">
                    <div class="flex flex-col gap-0.5">
                      <span>{{ skuLabel(line.skuCode) }}</span>
                      <span class="text-xs text-muted-foreground">{{ line.skuCode }}</span>
                    </div>
                  </td>
                  <td class="px-3 py-2 text-right tabular-nums">{{ line.quantity ?? '—' }}</td>
                  <td class="px-3 py-2">{{ line.unitOfMeasureCode || '—' }}</td>
                  <td class="px-3 py-2 text-right tabular-nums">{{ formatScrap(line.scrapRate) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该版本没有物料行。
          </p>
          <p class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning">
            配方明细暂不在列表回显（待后端）。当前接口只回物料行，配方行发布后无法在此查看。
          </p>
        </div>
      </SheetContent>
    </Sheet>
  </BusinessLayout>
</template>
