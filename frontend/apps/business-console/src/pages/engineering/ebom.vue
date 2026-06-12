<script setup lang="ts">
import type {
  BusinessConsoleEngineeringBomItem,
  BusinessConsoleReleaseEngineeringBomRequest,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useBusinessSkus, useBusinessUoms } from '@/composables/useBusinessMasterData'
import { useEngineeringEboms } from '@/composables/useProductEngineering'
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
import { formatDate, today } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: 'EBOM 设计BOM' } })

const {
  eboms,
  ebomsError,
  ebomsPending,
  ebomsTotal,
  filters,
  refresh,
  releaseEbom,
  releasePending,
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
  set: (value: string) => { filters.parentItemCode = value.trim() ? value : undefined },
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
// 物料编码 → 基本单位，选物料后自动带出行单位（仍可手动覆盖）。
const baseUomByCode = computed(() =>
  new Map(skus.value.filter((s) => s.code).map((s) => [s.code as string, s.baseUomCode ?? ''])),
)
const uomOptions = computed(() =>
  uoms.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: u.displayName ?? u.code as string })),
)

function engStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(() => eboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'published').length)
const draftCount = computed(() => eboms.value.filter((b) => (b.status ?? '').toLowerCase() === 'draft').length)

const listErrorMessage = computed(() => formatError(ebomsError.value))

const columns: DataTableColumn<BusinessConsoleEngineeringBomItem>[] = [
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

const parentValid = computed(() => form.parentItemCode.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0)
const effectiveValid = computed(() => !!form.effectiveDate)
function lineValid(line: ComponentLine) {
  return line.componentCode.trim().length > 0
    && (parseNumber(line.quantity) ?? 0) > 0
    && line.unitOfMeasureCode.trim().length > 0
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
const canSubmit = computed(() => parentValid.value && revisionValid.value && effectiveValid.value && linesValid.value && !duplicateComponent.value && !selfReferenceComponent.value)

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
    notifySuccess(`已发布设计 BOM「${skuLabel(form.parentItemCode)}」修订 ${form.revision.trim()}。`)
    showErrors.value = false
    formOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 查看版本头（list 无行 → 明细待后端 #389）─────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleEngineeringBomItem | null>(null)
function openView(row: BusinessConsoleEngineeringBomItem) {
  viewTarget.value = row
  viewOpen.value = true
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="EBOM 设计BOM"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${ebomsTotal} 个版本`"
    >
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="ebomsPending" @click="refresh">
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
              <DialogTitle>发布设计 BOM 新版本</DialogTitle>
              <DialogDescription>
                设计 BOM 一经发布即不可变。修改请填一套新的组件行 + 新修订号，发布出新版本。带 * 为必填项。
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && selfReferenceComponent" class="text-sm text-destructive" role="alert">
                组件不能与父项「{{ skuLabel(selfReferenceComponent) }}」相同——一个物料不能把自己当组件，请改选别的组件。
              </p>
              <p v-else-if="showErrors && duplicateComponent" class="text-sm text-destructive" role="alert">
                组件「{{ skuLabel(duplicateComponent) }}」重复了——同一组件只能有一行，请合并数量或删除重复行。
              </p>
              <p v-else-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 * 的必填项，并确保至少一行组件填好编码、数量（大于 0）与单位。
              </p>

              <FormSectionTitle>版本头</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-3">
                <Field :data-invalid="showErrors && !parentValid">
                  <FieldLabel for="ebom-parent">父项物料 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="form.parentItemCode">
                    <SelectTrigger id="ebom-parent"><SelectValue placeholder="选择父项" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>来自基础数据物料。</FieldDescription>
                </Field>
                <Field :data-invalid="showErrors && !revisionValid">
                  <FieldLabel for="ebom-rev">修订号 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="ebom-rev" v-model="form.revision" placeholder="如 A、B、001" />
                </Field>
                <Field :data-invalid="showErrors && !effectiveValid">
                  <FieldLabel>生效日 <span class="text-destructive">*</span></FieldLabel>
                  <DatePicker v-model="form.effectiveDate" placeholder="选择生效日" class="w-full" />
                </Field>
              </FieldGroup>

              <div class="flex items-center justify-between">
                <FormSectionTitle>组件行</FormSectionTitle>
                <Button type="button" variant="outline" size="sm" @click="addLine">
                  <PlusIcon aria-hidden="true" />
                  增加组件
                </Button>
              </div>
              <div class="grid gap-2">
                <div
                  v-for="(line, index) in form.lines"
                  :key="index"
                  class="grid grid-cols-[1fr_6rem_8rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <Field :data-invalid="showErrors && !line.componentCode.trim()">
                    <FieldLabel :for="`ebom-comp-${index}`">组件物料 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="line.componentCode" @update:model-value="(v) => applyComponentUom(line, String(v ?? ''))">
                      <SelectTrigger :id="`ebom-comp-${index}`"><SelectValue placeholder="选择组件" /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field :data-invalid="showErrors && (parseNumber(line.quantity) ?? 0) <= 0">
                    <FieldLabel :for="`ebom-qty-${index}`">数量 <span class="text-destructive">*</span></FieldLabel>
                    <Input :id="`ebom-qty-${index}`" v-model="line.quantity" type="number" min="0" step="any" />
                  </Field>
                  <Field :data-invalid="showErrors && !line.unitOfMeasureCode.trim()">
                    <FieldLabel :for="`ebom-uom-${index}`">单位 <span class="text-destructive">*</span></FieldLabel>
                    <Select v-model="line.unitOfMeasureCode">
                      <SelectTrigger :id="`ebom-uom-${index}`"><SelectValue placeholder="单位" /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="删除该组件行"
                    :disabled="form.lines.length <= 1"
                    @click="removeLine(index)"
                  >
                    <Trash2Icon aria-hidden="true" />
                  </Button>
                </div>
              </div>

              <DialogFooter>
                <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
                <Button type="submit" :disabled="releasePending">
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
      <SectionCard description="已发布 EBOM" :value="publishedCount" hint="可供 MBOM 引用的设计 BOM 版本" />
      <SectionCard description="草稿 EBOM" :value="draftCount" hint="尚未发布、不可被引用的版本" />
    </SectionCards>

    <Toolbar v-model:search="parentSearch" search-placeholder="按父项物料编码筛选">
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
      :rows="eboms"
      :row-key="(r) => `${r.bomCode}:${r.revision}`"
      :loading="ebomsPending"
      empty-message="当前范围没有设计 BOM。可发布新版本，把父项物料与其组件行登记为一个不可变版本。"
    >
      <template #cell-parentItemCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuLabel(row.parentItemCode) }}</span>
          <span class="text-xs text-muted-foreground">{{ row.parentItemCode }}</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
      </template>
      <template #cell-effectiveDate="{ row }">{{ row.effectiveDate ? formatDate(row.effectiveDate) : '长期' }}</template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <Button type="button" variant="ghost" size="sm" @click="openView(row)">查看</Button>
        </div>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="ebomsTotal" />

    <Sheet v-model:open="viewOpen">
      <SheetContent class="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>设计 BOM 版本</SheetTitle>
          <SheetDescription>查看该版本的版本头信息。</SheetDescription>
        </SheetHeader>
        <div v-if="viewTarget" class="grid gap-3 px-4 py-2 text-sm">
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">BOM 编号</span>
            <span class="font-medium">{{ viewTarget.bomCode || '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">父项物料</span>
            <span class="font-medium">{{ skuLabel(viewTarget.parentItemCode) }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">修订</span>
            <span class="font-medium">{{ viewTarget.revision || '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">状态</span>
            <StatusBadge :label="engStatus(viewTarget.status).label" :tone="engStatus(viewTarget.status).tone" />
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">生效日</span>
            <span class="font-medium">{{ viewTarget.effectiveDate ? formatDate(viewTarget.effectiveDate) : '长期' }}</span>
          </div>
          <p class="mt-2 rounded-md border border-warning/30 bg-warning/10 p-3 text-warning">
            组件行明细待后端提供（#389）。当前列表接口只回版本头，发布后无法在此逐行查看；如需改动请发布新修订。
          </p>
        </div>
      </SheetContent>
    </Sheet>
  </BusinessLayout>
</template>
