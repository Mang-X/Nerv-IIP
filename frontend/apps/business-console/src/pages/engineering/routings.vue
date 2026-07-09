<script setup lang="ts">
import type {
  BusinessConsoleReleaseRoutingRequest,
  BusinessConsoleRoutingItem,
  BusinessConsoleStandardOperationItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import { useEngineeringRoutings, useStandardOperations } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
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
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
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
import { ArrowDownIcon, ArrowUpIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '工艺路线',
    requiredPermissions: ['business.engineering.routings.read'],
  },
})

const {
  routings,
  routingsError,
  routingsPending,
  routingsTotal,
  filters,
  refresh,
  releaseRouting,
  releasePending,
  fetchRoutingDetail,
} = useEngineeringRoutings()

const { skus } = useBusinessSkus()
const { resources: workCenters, resourcesPending: workCentersPending } =
  useBusinessMasterDataResources('work-center')
// 标准工序主数据（#397）：工序从「产品工程 › 标准工序」选，选中后自动带出其默认工作中心与标准工时。
const { standardOperations, standardOperationsPending: standardOpsPending } =
  useStandardOperations()

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
const wcNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const wc of workCenters.value) {
    if (wc.code) map.set(wc.code, wc.displayName ?? wc.code)
  }
  return map
})
function wcLabel(code?: string | null) {
  if (!code) return '—'
  return wcNameByCode.value.get(code) ?? code
}

const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const workCenterOptions = computed(() =>
  workCenters.value
    .filter((w) => w.code)
    .map((w) => ({ value: w.code as string, label: w.displayName ?? (w.code as string) })),
)
const hasWorkCenters = computed(() => workCenterOptions.value.length > 0)

// 标准工序：启用且有编码的工序可选；label 显工序名 + 编码，绑定值用 operationCode。
const operationOptions = computed(() =>
  standardOperations.value
    .filter((o) => o.enabled !== false && (o.operationCode ?? '').trim().length > 0)
    .map((o) => ({
      value: (o.operationCode ?? '').trim(),
      label: `${o.operationName ?? o.operationCode} · ${o.operationCode}`,
    })),
)
const hasOperations = computed(() => operationOptions.value.length > 0)
// 编码 → 工序全量（用于选中后带出默认工作中心/工时）。
const operationByCode = computed(() => {
  const map = new Map<string, BusinessConsoleStandardOperationItem>()
  for (const o of standardOperations.value) {
    if (o.operationCode) map.set(o.operationCode, o)
  }
  return map
})
const operationNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const o of standardOperations.value) {
    if (o.operationCode) map.set(o.operationCode, o.operationName ?? o.operationCode)
  }
  return map
})
function operationLabel(code?: string | null, fallbackName?: string | null) {
  if (!code) return fallbackName || '—'
  return operationNameByCode.value.get(code) ?? fallbackName ?? code
}
// 选标准工序后带出其默认工作中心与标准工时（用户仍可逐行覆盖）。
function applyOperationDefaults(op: Operation) {
  const def = operationByCode.value.get(op.operationCode.trim())
  if (!def) return
  if (def.defaultWorkCenterCode) op.workCenterCode = def.defaultWorkCenterCode
  op.standardMinutes =
    def.standardMinutes ?? (def.standardSetupMinutes ?? 0) + (def.standardRunMinutes ?? 0)
}

function engStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}

const publishedCount = computed(
  () => routings.value.filter((r) => (r.status ?? '').toLowerCase() === 'published').length,
)
const draftCount = computed(
  () => routings.value.filter((r) => (r.status ?? '').toLowerCase() === 'draft').length,
)

const listErrorMessage = computed(() => formatError(routingsError.value))

const columns: NvDataTableColumn<BusinessConsoleRoutingItem>[] = [
  { key: 'routingCode', header: '路线号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'skuCode', header: '产出物料' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 发布新版本向导（有序工序编辑器）─────────────────────────────
interface Operation {
  sequence: number
  workCenterCode: string
  operationCode: string
  standardMinutes: string | number
}
interface RoutingForm {
  skuCode: string
  revision: string
  effectiveDate: string | null
  operations: Operation[]
}
function blankOperation(sequence: number): Operation {
  return { sequence, workCenterCode: '', operationCode: '', standardMinutes: '' }
}
function blankForm(): RoutingForm {
  return { skuCode: '', revision: '', effectiveDate: today(), operations: [blankOperation(10)] }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const form = reactive<RoutingForm>(blankForm())

function parseNumber(value: string | number | null | undefined): number | undefined {
  if (value === null || value === undefined) return undefined
  if (typeof value === 'number') return Number.isFinite(value) ? value : undefined
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

const skuValid = computed(() => form.skuCode.trim().length > 0)
const revisionValid = computed(() => form.revision.trim().length > 0)
const effectiveValid = computed(() => !!form.effectiveDate)
function operationValid(op: Operation) {
  return (
    op.workCenterCode.trim().length > 0 &&
    op.operationCode.trim().length > 0 &&
    (parseNumber(op.standardMinutes) ?? -1) >= 0
  )
}
const operationsValid = computed(
  () => form.operations.length > 0 && form.operations.every(operationValid),
)
// 序号须为正且不重复。
const sequencesValid = computed(() => {
  const seqs = form.operations.map((o) => o.sequence)
  return seqs.every((s) => Number.isFinite(s) && s > 0) && new Set(seqs).size === seqs.length
})
const canSubmit = computed(
  () =>
    skuValid.value &&
    revisionValid.value &&
    effectiveValid.value &&
    operationsValid.value &&
    sequencesValid.value,
)

function openCreate() {
  Object.assign(form, blankForm())
  form.operations = [blankOperation(10)]
  showErrors.value = false
  formOpen.value = true
}
function nextSequence() {
  const max = form.operations.reduce((m, o) => Math.max(m, o.sequence || 0), 0)
  return max + 10
}
function addOperation() {
  form.operations.push(blankOperation(nextSequence()))
}
function removeOperation(index: number) {
  if (form.operations.length <= 1) return
  form.operations.splice(index, 1)
}
// 上移 / 下移：交换相邻两行，并同步交换两者序号，保持序号随显示顺序递增。
function swap(a: number, b: number) {
  const ops = form.operations
  const seqA = ops[a]!.sequence
  ops[a]!.sequence = ops[b]!.sequence
  ops[b]!.sequence = seqA
  const tmp = ops[a]!
  ops[a] = ops[b]!
  ops[b] = tmp
}
function moveUp(index: number) {
  if (index <= 0) return
  swap(index, index - 1)
}
function moveDown(index: number) {
  if (index >= form.operations.length - 1) return
  swap(index, index + 1)
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const body: BusinessConsoleReleaseRoutingRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    revision: form.revision.trim(),
    skuCode: form.skuCode.trim(),
    effectiveDate: form.effectiveDate ?? undefined,
    operations: form.operations.map((op) => ({
      sequence: op.sequence,
      workCenterCode: op.workCenterCode.trim(),
      // 工序从标准工序主数据选：operationCode 是受控标识，operationName 带出工序名（后端两个字段都收）。
      operationCode: op.operationCode.trim(),
      operationName:
        operationNameByCode.value.get(op.operationCode.trim()) ?? op.operationCode.trim(),
      standardMinutes: parseNumber(op.standardMinutes) ?? 0,
    })),
  }
  try {
    await releaseRouting(body)
    notifySuccess(`已发布工艺路线「${skuLabel(form.skuCode)}」修订 ${form.revision.trim()}。`)
    showErrors.value = false
    formOpen.value = false
  } catch (error) {
    notifyError(error)
  }
}

// ── 查看版本明细（get-by-id 拉真实工序行）────────────────────────
const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleRoutingItem | null>(null)
const detailPending = ref(false)
const detailError = ref('')
// 工序行（来自 get-by-id；按序号排好）。
const viewOperations = computed(() =>
  [...(viewTarget.value?.operations ?? [])].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)),
)
async function openView(row: BusinessConsoleRoutingItem) {
  viewTarget.value = row
  viewOpen.value = true
  detailError.value = ''
  if (!row.routingCode || !row.revision) return
  detailPending.value = true
  try {
    const detail = await fetchRoutingDetail(row.routingCode, row.revision)
    if (detail) viewTarget.value = detail
  } catch (error) {
    detailError.value = formatError(error) || '加载工序明细失败，请稍后重试。'
  } finally {
    detailPending.value = false
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工艺路线"
      :breadcrumbs="[{ label: '产品工程' }]"
      :count="`${routingsTotal} 个版本`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="routingsPending"
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
              <NvDialogTitle>发布工艺路线新版本</NvDialogTitle>
              <NvDialogDescription>
                按顺序编排工序，每道工序指派一个工作中心。一经发布即不可变，修改请填新工序 +
                新修订号再发布。带 * 为必填项。
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请完整填写带 *
                的必填项，确保每道工序有工作中心、标准工序与非负工时，且序号正整数且不重复。
              </p>
              <p
                v-if="!workCentersPending && !hasWorkCenters"
                class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
                role="alert"
              >
                还没有可指派的工作中心。
                <RouterLink to="/master-data/facilities" class="font-medium underline"
                  >去基础数据维护工作中心 →</RouterLink
                >
              </p>
              <p
                v-if="!standardOpsPending && !hasOperations"
                class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
                role="alert"
              >
                还没有可选的标准工序。
                <RouterLink to="/engineering/standard-operations" class="font-medium underline"
                  >去标准工序维护 →</RouterLink
                >
              </p>

              <FormSectionTitle>版本头</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-3">
                <NvField :data-invalid="showErrors && !skuValid">
                  <NvFieldLabel for="rt-sku"
                    >产出物料 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="form.skuCode">
                    <NvSelectTrigger id="rt-sku"
                      ><NvSelectValue placeholder="选择产出物料"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{
                        o.label
                      }}</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                  <NvFieldDescription>来自基础数据物料。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="showErrors && !revisionValid">
                  <NvFieldLabel for="rt-rev"
                    >修订号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="rt-rev" v-model="form.revision" placeholder="如 A、B、001" />
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
                <FormSectionTitle>工序（按顺序）</FormSectionTitle>
                <NvButton
                  type="button"
                  variant="outline"
                  size="sm"
                  :disabled="!hasWorkCenters || !hasOperations"
                  @click="addOperation"
                >
                  <PlusIcon aria-hidden="true" />
                  增加工序
                </NvButton>
              </div>
              <p v-if="showErrors && !sequencesValid" class="text-sm text-destructive" role="alert">
                工序序号须为正整数且互不相同。
              </p>
              <div class="grid gap-2">
                <div
                  v-for="(op, index) in form.operations"
                  :key="index"
                  class="grid grid-cols-[4.5rem_1fr_1fr_6rem_auto] items-end gap-2 rounded-md border p-2"
                >
                  <NvField :data-invalid="showErrors && !sequencesValid">
                    <NvFieldLabel :for="`rt-seq-${index}`"
                      >序号 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvInput
                      :id="`rt-seq-${index}`"
                      v-model.number="op.sequence"
                      type="number"
                      min="1"
                      step="1"
                    />
                  </NvField>
                  <NvField :data-invalid="showErrors && !op.workCenterCode.trim()">
                    <NvFieldLabel :for="`rt-wc-${index}`"
                      >工作中心 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvSelect v-model="op.workCenterCode">
                      <NvSelectTrigger :id="`rt-wc-${index}`"
                        ><NvSelectValue placeholder="选择工作中心"
                      /></NvSelectTrigger>
                      <NvSelectContent>
                        <NvSelectItem
                          v-for="o in workCenterOptions"
                          :key="o.value"
                          :value="o.value"
                          >{{ o.label }}</NvSelectItem
                        >
                      </NvSelectContent>
                    </NvSelect>
                  </NvField>
                  <NvField :data-invalid="showErrors && !op.operationCode.trim()">
                    <NvFieldLabel :for="`rt-name-${index}`"
                      >工序 <span class="text-destructive">*</span></NvFieldLabel
                    >
                    <NvSelect
                      v-model="op.operationCode"
                      @update:model-value="applyOperationDefaults(op)"
                    >
                      <NvSelectTrigger :id="`rt-name-${index}`"
                        ><NvSelectValue placeholder="选择标准工序"
                      /></NvSelectTrigger>
                      <NvSelectContent>
                        <NvSelectItem
                          v-for="o in operationOptions"
                          :key="o.value"
                          :value="o.value"
                          >{{ o.label }}</NvSelectItem
                        >
                      </NvSelectContent>
                    </NvSelect>
                  </NvField>
                  <NvField
                    :data-invalid="showErrors && (parseNumber(op.standardMinutes) ?? -1) < 0"
                  >
                    <NvFieldLabel :for="`rt-min-${index}`">工时(分)</NvFieldLabel>
                    <NvInput
                      :id="`rt-min-${index}`"
                      v-model="op.standardMinutes"
                      type="number"
                      min="0"
                      step="any"
                    />
                    <NvFieldDescription>选工序后自动带出，可改。</NvFieldDescription>
                  </NvField>
                  <div class="flex gap-1">
                    <NvButton
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label="上移工序"
                      :disabled="index === 0"
                      @click="moveUp(index)"
                    >
                      <ArrowUpIcon aria-hidden="true" />
                    </NvButton>
                    <NvButton
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label="下移工序"
                      :disabled="index === form.operations.length - 1"
                      @click="moveDown(index)"
                    >
                      <ArrowDownIcon aria-hidden="true" />
                    </NvButton>
                    <NvButton
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label="删除该工序"
                      :disabled="form.operations.length <= 1"
                      @click="removeOperation(index)"
                    >
                      <Trash2Icon aria-hidden="true" />
                    </NvButton>
                  </div>
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

    <NvSectionCards :columns="2">
      <NvSectionCard
        description="已发布工艺路线"
        :value="publishedCount"
        hint="可被生产版本绑定的路线版本"
      />
      <NvSectionCard
        description="草稿工艺路线"
        :value="draftCount"
        hint="尚未发布、不可被绑定的版本"
      />
    </NvSectionCards>

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
      :total-items="routingsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="routings"
      :row-key="(r) => `${r.routingCode}:${r.revision}`"
      :loading="routingsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有工艺路线。可发布新版本，按顺序编排工序并指派工作中心。"
    >
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuLabel(row.skuCode) }}</span>
          <span class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
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
          <NvSheetTitle>工艺路线 · 工序</NvSheetTitle>
          <NvSheetDescription>
            {{
              viewTarget
                ? `${viewTarget.routingCode} · 修订 ${viewTarget.revision} · ${skuLabel(viewTarget.skuCode)}`
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
            加载工序明细…
          </div>
          <p
            v-else-if="detailError"
            class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
            role="alert"
          >
            {{ detailError }}
          </p>
          <div v-else-if="viewOperations.length" class="overflow-hidden rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-right font-medium">序号</th>
                  <th class="px-3 py-2 text-left font-medium">工作中心</th>
                  <th class="px-3 py-2 text-left font-medium">工序</th>
                  <th class="px-3 py-2 text-right font-medium">工时(分)</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(op, i) in viewOperations" :key="i" class="border-t">
                  <td class="px-3 py-2 text-right tabular-nums">{{ op.sequence ?? '—' }}</td>
                  <td class="px-3 py-2">{{ wcLabel(op.workCenterCode) }}</td>
                  <td class="px-3 py-2">
                    {{ operationLabel(op.operationCode, op.operationName) }}
                  </td>
                  <td class="px-3 py-2 text-right tabular-nums">{{ op.standardMinutes ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该版本没有工序行。
          </p>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
