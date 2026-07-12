<script setup lang="ts">
import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleMaintenanceInspectionMeasurementItem,
  BusinessConsoleRecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenanceInspections } from '@/composables/useBusinessMaintenance'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { usePagedList } from '@/composables/usePagedList'
import { useAuthStore } from '@/stores/auth'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  COMMON_INSPECTION_CHARACTERISTICS,
  createMeasurementDraft,
  measurementOutOfTolerance,
  measurementRowsValid,
  toMeasurementPayload,
  type MeasurementDraftRow,
} from '@nerv-iip/business-core'
import {
  NvButton,
  NvDataTable,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
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
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvStatusBadge,
  toast,
} from '@nerv-iip/ui'
import {
  AlertTriangleIcon,
  ClipboardCheckIcon,
  PlusIcon,
  RefreshCwIcon,
  Trash2Icon,
} from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '点检记录',
    requiredPermissions: ['business.maintenance.plans.read'],
  },
})

const {
  filters,
  inspections,
  inspectionsError,
  inspectionsPending,
  inspectionsTotal,
  refreshInspections,
  recordInspection,
  recordInspectionPending,
  recordInspectionError,
} = useMaintenanceInspections()
const { page, pageSize } = usePagedList(filters)

// 人员目录 + 当前登录用户（点检人默认当前用户，可改选他人，不自由输入）。
const { workers } = useBusinessWorkers()
const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const currentUserId = computed(() => principal.value?.principalId ?? '')
const workerOptions = computed(() =>
  workers.value
    .map((w) => ({
      value: w.userId ?? '',
      label: w.displayName ?? w.userId ?? '',
      hint: w.employeeNo ?? undefined,
    }))
    .filter((o) => o.value.length > 0),
)
function personLabel(userId: string) {
  return (
    workers.value.find((w) => w.userId === userId)?.displayName ??
    principal.value?.loginName ??
    userId
  )
}

const resultOptions = [
  { label: '通过', value: 'passed' },
  { label: '异常', value: 'failed' },
  { label: '需复检', value: 'requires-review' },
]

interface MeasurementFormRow extends MeasurementDraftRow {
  id: number
}

let nextMeasurementRowId = 1
function createMeasurementRow(): MeasurementFormRow {
  return { id: nextMeasurementRowId++, ...createMeasurementDraft() }
}

const recordOpen = shallowRef(false)
const recordForm = reactive({
  planId: '',
  workOrderId: '',
  inspectorUserId: '',
  result: 'passed',
  inspectedAtUtc: '',
})
const measurementRows = reactive<MeasurementFormRow[]>([createMeasurementRow()])
const recordError = shallowRef('')

// 详情抽屉（轻详情：只读回看某条点检的测量值与超差标记）。
const detailOpen = shallowRef(false)
const detailTarget = shallowRef<InspectionRow>()

const measurementsValid = computed(() => measurementRowsValid(measurementRows))

// 测量特性下拉候选：常用特性 + 已加载点检记录里的历史特性，去重。让点检人从已知项里选，不用猜。
const characteristicOptions = computed(() => {
  const seen = new Set<string>()
  const out: { value: string; label: string }[] = []
  const add = (code: string) => {
    const c = code.trim()
    if (c && !seen.has(c)) {
      seen.add(c)
      out.push({ value: c, label: c })
    }
  }
  for (const code of COMMON_INSPECTION_CHARACTERISTICS) add(code)
  for (const inspection of inspections.value) {
    for (const measurement of inspection.measurements ?? [])
      add(measurement.characteristicCode ?? '')
  }
  return out
})

const listErrorMessage = computed(() => formatError(inspectionsError.value))
const recordErrorMessage = computed(
  () => recordError.value || formatError(recordInspectionError.value),
)

type InspectionRow = BusinessConsoleMaintenanceInspectionItem
type MeasurementItem = BusinessConsoleMaintenanceInspectionMeasurementItem
const columns: NvDataTableColumn<InspectionRow>[] = [
  {
    key: 'inspectionId',
    header: '点检记录',
    cellClass: 'font-medium',
    accessor: (r) => inspectionNo(r),
  },
  { key: 'planId', header: '保养计划', accessor: (r) => r.planId ?? '未关联' },
  { key: 'workOrderId', header: '维修工单', accessor: (r) => r.workOrderId ?? '未关联' },
  { key: 'inspector', header: '点检人', accessor: (r) => r.inspector ?? '未记录' },
  { key: 'result', header: '结果', width: 'w-24' },
  { key: 'measurements', header: '测量值', width: 'w-40' },
  { key: 'inspectedAtUtc', header: '点检时间', accessor: (r) => formatDateTime(r.inspectedAtUtc) },
]

function inspectionNo(row: InspectionRow) {
  const id = row.inspectionId ?? ''
  return id ? `INSP-${id.slice(-8).toUpperCase()}` : '点检记录'
}
function rowKey(row: InspectionRow) {
  return (
    row.inspectionId ?? `${row.planId ?? ''}-${row.workOrderId ?? ''}-${row.inspectedAtUtc ?? ''}`
  )
}
function resultLabel(value?: string | null) {
  return (
    resultOptions.find((o) => o.value === (value ?? '').toLowerCase())?.label ?? value ?? '未知'
  )
}
// 后端已算 isWithinSpec：超差 = 明确 false（未判定的 undefined 不当作超差）。
function measurementOutOfSpec(m: MeasurementItem) {
  return m.isWithinSpec === false
}
function outOfSpecCount(row: InspectionRow) {
  return (row.measurements ?? []).filter(measurementOutOfSpec).length
}
function measurementRange(m: MeasurementItem) {
  const lower = m.lowerSpecLimit ?? null
  const upper = m.upperSpecLimit ?? null
  if (lower === null && upper === null) return '无规格限'
  return `${lower ?? '−∞'} ~ ${upper ?? '+∞'}`
}
function nowLocal() {
  const date = new Date()
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString()
}

function openRecord() {
  recordForm.planId = ''
  recordForm.workOrderId = ''
  recordForm.inspectorUserId = currentUserId.value
  recordForm.result = 'passed'
  recordForm.inspectedAtUtc = nowLocal()
  measurementRows.splice(0, measurementRows.length, createMeasurementRow())
  recordError.value = ''
  recordOpen.value = true
}
function addMeasurementRow() {
  measurementRows.push(createMeasurementRow())
}
function removeMeasurementRow(rowId: number) {
  if (measurementRows.length === 1) {
    Object.assign(measurementRows[0], createMeasurementDraft())
    return
  }
  const index = measurementRows.findIndex((row) => row.id === rowId)
  if (index >= 0) measurementRows.splice(index, 1)
}
function openDetail(row: InspectionRow) {
  detailTarget.value = row
  detailOpen.value = true
}

async function submitRecord() {
  if (!recordForm.planId.trim() && !recordForm.workOrderId.trim()) {
    recordError.value = '请至少关联保养计划或维修工单。'
    return
  }
  if (!recordForm.inspectorUserId) {
    recordError.value = '请选择点检人。'
    return
  }
  if (!measurementsValid.value) {
    recordError.value = '请完整填写测量值（特性/数值/单位），且下限不得大于上限。'
    return
  }

  const measurements = toMeasurementPayload(measurementRows)
  const body: BusinessConsoleRecordMaintenanceInspectionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    planId: recordForm.planId.trim() || undefined,
    workOrderId: recordForm.workOrderId.trim() || undefined,
    inspector: personLabel(recordForm.inspectorUserId),
    result: recordForm.result,
    inspectedAtUtc: toIsoDateTime(recordForm.inspectedAtUtc),
    // 只送有效行：空行被过滤，无测量值时不带该字段。
    ...(measurements.length > 0 ? { measurements } : {}),
  }

  try {
    await recordInspection(body)
    recordOpen.value = false
    toast.success('点检记录已提交')
  } catch {
    // 失败信息由抽屉错误区呈现。
  }
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="点检记录"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${inspectionsTotal} 条点检记录`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="inspectionsPending"
          @click="refreshInspections"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openRecord">
          <PlusIcon aria-hidden="true" />
          记录点检
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="inspectionsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="inspections"
      :row-key="rowKey"
      :loading="inspectionsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无点检记录。可从保养计划或维修工单补录点检结果。"
    >
      <template #cell-result="{ row }"><NvStatusBadge :value="resultLabel(row.result)" /></template>
      <template #cell-measurements="{ row }">
        <button
          v-if="(row.measurements?.length ?? 0) > 0"
          type="button"
          class="inline-flex items-center gap-1.5 rounded-md px-1.5 py-0.5 text-sm hover:bg-muted"
          :data-testid="`measurements-${rowKey(row)}`"
          @click="openDetail(row)"
        >
          <span class="tabular-nums text-muted-foreground">{{ row.measurements?.length }} 项</span>
          <span
            v-if="outOfSpecCount(row) > 0"
            class="inline-flex items-center gap-1 rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-medium text-destructive"
          >
            <AlertTriangleIcon class="size-3" aria-hidden="true" />
            {{ outOfSpecCount(row) }} 项超差
          </span>
          <span v-else class="text-xs text-muted-foreground">全部合格</span>
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
    </NvDataTable>

    <!-- 记录点检：含测量值动态行 → 侧滑 Sheet（A1 §1）。 -->
    <NvSheet v-model:open="recordOpen">
      <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>记录点检</NvSheetTitle>
          <NvSheetDescription
            >点检可关联保养计划或维修工单，用于释放设备维护上下文。可记录测量值，超出上下限的行会即时红色警示。</NvSheetDescription
          >
        </NvSheetHeader>
        <form class="grid gap-5 px-4 pb-4" @submit.prevent="submitRecord">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="insp-plan">保养计划</NvFieldLabel>
              <NvInput
                id="insp-plan"
                v-model="recordForm.planId"
                autocomplete="off"
                placeholder="可选"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="insp-work-order">维修工单</NvFieldLabel>
              <NvInput
                id="insp-work-order"
                v-model="recordForm.workOrderId"
                autocomplete="off"
                placeholder="可选"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="insp-inspector">点检人</NvFieldLabel>
              <NvSearchSelect
                id="insp-inspector"
                v-model="recordForm.inspectorUserId"
                :options="workerOptions"
                aria-label="点检人"
                placeholder="选择点检人"
                search-placeholder="搜索姓名 / 工号…"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="insp-result">点检结果</NvFieldLabel>
              <NvSelect v-model="recordForm.result">
                <NvSelectTrigger id="insp-result" aria-label="点检结果"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in resultOptions" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="insp-time">点检时间</NvFieldLabel>
              <NvInput id="insp-time" v-model="recordForm.inspectedAtUtc" type="datetime-local" />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium"
                >测量值 <span class="text-muted-foreground">（可选，填了就须完整）</span></span
              >
              <NvButton type="button" variant="outline" size="sm" @click="addMeasurementRow">
                <PlusIcon aria-hidden="true" />
                添加一行
              </NvButton>
            </div>

            <div
              v-for="row in measurementRows"
              :key="row.id"
              :data-testid="`measurement-row-${row.id}`"
              :data-out-of-tolerance="measurementOutOfTolerance(row) ? 'true' : undefined"
              class="grid gap-2 rounded-md border p-3"
              :class="
                measurementOutOfTolerance(row)
                  ? 'border-destructive bg-destructive/5'
                  : 'border-border'
              "
            >
              <div class="grid gap-2 sm:grid-cols-3">
                <NvField>
                  <NvFieldLabel :for="`m-char-${row.id}`">特性</NvFieldLabel>
                  <NvSearchSelect
                    :id="`m-char-${row.id}`"
                    v-model="row.characteristicCode"
                    :options="characteristicOptions"
                    aria-label="测量特性"
                    placeholder="选择特性"
                    search-placeholder="搜索特性…"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`m-value-${row.id}`">数值</NvFieldLabel>
                  <NvInput
                    :id="`m-value-${row.id}`"
                    v-model="row.measuredValue"
                    type="number"
                    step="any"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`m-uom-${row.id}`">单位</NvFieldLabel>
                  <NvInput
                    :id="`m-uom-${row.id}`"
                    v-model="row.uomCode"
                    autocomplete="off"
                    placeholder="如 ℃"
                  />
                </NvField>
              </div>
              <div class="grid items-end gap-2 sm:grid-cols-[1fr_1fr_auto]">
                <NvField>
                  <NvFieldLabel :for="`m-lower-${row.id}`">下限</NvFieldLabel>
                  <NvInput
                    :id="`m-lower-${row.id}`"
                    v-model="row.lowerSpecLimit"
                    type="number"
                    step="any"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`m-upper-${row.id}`">上限</NvFieldLabel>
                  <NvInput
                    :id="`m-upper-${row.id}`"
                    v-model="row.upperSpecLimit"
                    type="number"
                    step="any"
                  />
                </NvField>
                <NvButton
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label="移除该测量值"
                  @click="removeMeasurementRow(row.id)"
                >
                  <Trash2Icon aria-hidden="true" />
                </NvButton>
              </div>
              <p
                v-if="measurementOutOfTolerance(row)"
                class="flex items-center gap-1.5 text-sm font-medium text-destructive"
                role="alert"
              >
                <AlertTriangleIcon class="size-4" aria-hidden="true" />
                测量值超出规格上下限。
              </p>
            </div>
          </div>

          <NvFieldError v-if="recordErrorMessage" :errors="[recordErrorMessage]" />

          <NvSheetFooter class="px-0">
            <NvButton type="button" variant="outline" @click="recordOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="recordInspectionPending">
              <Spinner v-if="recordInspectionPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交点检
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>

    <!-- 点检详情：只读回看测量值与超差标记（验收①：提交后详情可见超差标记）。 -->
    <NvSheet v-model:open="detailOpen">
      <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>{{ detailTarget ? inspectionNo(detailTarget) : '点检详情' }}</NvSheetTitle>
          <NvSheetDescription>
            {{ resultLabel(detailTarget?.result) }} · {{ detailTarget?.inspector ?? '未记录' }} ·
            {{ formatDateTime(detailTarget?.inspectedAtUtc) }}
          </NvSheetDescription>
        </NvSheetHeader>

        <div class="grid gap-2 px-4 pb-4">
          <p
            v-if="detailTarget && outOfSpecCount(detailTarget) > 0"
            class="flex items-center gap-1.5 rounded-md border border-destructive bg-destructive/5 px-3 py-2 text-sm font-medium text-destructive"
            role="alert"
          >
            <AlertTriangleIcon class="size-4" aria-hidden="true" />
            共 {{ outOfSpecCount(detailTarget) }} 项测量值超差。
          </p>

          <div
            v-for="(m, i) in detailTarget?.measurements ?? []"
            :key="i"
            :data-testid="`detail-measurement-${i}`"
            :data-out-of-spec="measurementOutOfSpec(m) ? 'true' : undefined"
            class="grid gap-1 rounded-md border p-3"
            :class="
              measurementOutOfSpec(m) ? 'border-destructive bg-destructive/5' : 'border-border'
            "
          >
            <div class="flex items-center justify-between gap-2">
              <span class="font-medium">{{ m.characteristicCode ?? '测量值' }}</span>
              <span
                v-if="measurementOutOfSpec(m)"
                class="inline-flex items-center gap-1 rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-medium text-destructive"
              >
                <AlertTriangleIcon class="size-3" aria-hidden="true" />
                超差
              </span>
              <span v-else class="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground"
                >合格</span
              >
            </div>
            <div class="text-sm text-muted-foreground">
              测量 <span class="tabular-nums text-foreground">{{ m.measuredValue ?? '—' }}</span>
              {{ m.uomCode ?? '' }} · 规格 {{ measurementRange(m) }}
            </div>
          </div>

          <p
            v-if="(detailTarget?.measurements?.length ?? 0) === 0"
            class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground"
          >
            本次点检未记录测量值。
          </p>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
