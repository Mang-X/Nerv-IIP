<script setup lang="ts">
import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleRecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMaintenanceInspections } from '@/composables/useBusinessMaintenance'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  FieldPro,
  FieldProError,
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
  toast,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '点检记录', requiredPermissions: ['business.maintenance.plans.read'] } })

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
const recordErrorMessage = computed(() => recordError.value || formatError(recordInspectionError.value))

type InspectionRow = BusinessConsoleMaintenanceInspectionItem
const columns: DataTableProColumn<InspectionRow>[] = [
  { key: 'inspectionId', header: '点检记录', cellClass: 'font-medium', accessor: (r) => inspectionNo(r) },
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
  return row.inspectionId ?? `${row.planId ?? ''}-${row.workOrderId ?? ''}-${row.inspectedAtUtc ?? ''}`
}
function resultLabel(value?: string | null) {
  return resultOptions.find((o) => o.value === (value ?? '').toLowerCase())?.label ?? value ?? '未知'
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
    <PageHeader title="点检记录" :breadcrumbs="[{ label: '设备监控' }]" :count="`${inspectionsTotal} 条点检记录`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="inspectionsPending" @click="refreshInspections">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openRecord">
          <PlusIcon aria-hidden="true" />
          记录点检
        </ButtonPro>
      </template>
    </PageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
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
      <template #cell-result="{ row }"><StatusBadgePro :value="resultLabel(row.result)" /></template>
    </DataTablePro>

    <DialogPro v-model:open="recordOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>记录点检</DialogProTitle>
          <DialogProDescription>点检可关联保养计划或维修工单，用于释放设备维护上下文。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitRecord">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="insp-plan">保养计划</FieldProLabel>
              <InputPro id="insp-plan" v-model="recordForm.planId" autocomplete="off" placeholder="可选" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="insp-work-order">维修工单</FieldProLabel>
              <InputPro id="insp-work-order" v-model="recordForm.workOrderId" autocomplete="off" placeholder="可选" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="insp-inspector">点检人</FieldProLabel>
              <InputPro id="insp-inspector" v-model="recordForm.inspector" autocomplete="off" placeholder="如 设备保全班" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="insp-result">点检结果</FieldProLabel>
              <SelectPro v-model="recordForm.result">
                <SelectProTrigger id="insp-result" aria-label="点检结果"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="o in resultOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </FieldPro>
            <FieldPro class="sm:col-span-2">
              <FieldProLabel for="insp-time">点检时间</FieldProLabel>
              <InputPro id="insp-time" v-model="recordForm.inspectedAtUtc" type="datetime-local" />
            </FieldPro>
          </FieldProGroup>

          <FieldProError v-if="recordErrorMessage" :errors="[recordErrorMessage]" />

          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="recordInspectionPending">
              <Spinner v-if="recordInspectionPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交点检
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
