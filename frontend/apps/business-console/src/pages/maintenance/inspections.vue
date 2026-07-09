<script setup lang="ts">
import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleRecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenanceInspections } from '@/composables/useBusinessMaintenance'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  toast,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
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

const resultOptions = [
  { label: '通过', value: 'passed' },
  { label: '异常', value: 'failed' },
  { label: '需复检', value: 'requires-review' },
]

const recordOpen = shallowRef(false)
const recordForm = reactive({
  planId: '',
  workOrderId: '',
  inspector: '',
  result: 'passed',
  inspectedAtUtc: '',
})
const recordError = shallowRef('')

const listErrorMessage = computed(() => formatError(inspectionsError.value))
const recordErrorMessage = computed(
  () => recordError.value || formatError(recordInspectionError.value),
)

type InspectionRow = BusinessConsoleMaintenanceInspectionItem
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
  recordForm.inspector = ''
  recordForm.result = 'passed'
  recordForm.inspectedAtUtc = nowLocal()
  recordError.value = ''
  recordOpen.value = true
}
async function submitRecord() {
  if (!recordForm.planId.trim() && !recordForm.workOrderId.trim()) {
    recordError.value = '请至少关联保养计划或维修工单。'
    return
  }
  if (!recordForm.inspector.trim()) {
    recordError.value = '请填写点检人。'
    return
  }

  const body: BusinessConsoleRecordMaintenanceInspectionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    planId: recordForm.planId.trim() || undefined,
    workOrderId: recordForm.workOrderId.trim() || undefined,
    inspector: recordForm.inspector.trim(),
    result: recordForm.result,
    inspectedAtUtc: toIsoDateTime(recordForm.inspectedAtUtc),
  }

  try {
    await recordInspection(body)
    recordOpen.value = false
    toast.success('点检记录已提交')
  } catch {
    // 失败信息由对话框错误区呈现。
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
    </NvDataTable>

    <NvDialog v-model:open="recordOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>记录点检</NvDialogTitle>
          <NvDialogDescription
            >点检可关联保养计划或维修工单，用于释放设备维护上下文。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRecord">
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
              <NvInput
                id="insp-inspector"
                v-model="recordForm.inspector"
                autocomplete="off"
                placeholder="如 设备保全班"
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

          <NvFieldError v-if="recordErrorMessage" :errors="[recordErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="recordInspectionPending">
              <Spinner v-if="recordInspectionPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交点检
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
