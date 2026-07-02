<script setup lang="ts">
import type { EquipmentRuntimeAvailabilityWindow } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMaintenanceAvailabilityWindows } from '@/composables/useBusinessMaintenance'
import { describeEquipmentReason } from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  BadgePro,
  ButtonPro,
  DataTablePro,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
} from '@nerv-iip/ui'
import { RefreshCwIcon, WrenchIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '可用窗口', requiredPermissions: ['business.maintenance.work-orders.read'] } })

const route = useRoute()
const initialDeviceAssetIds = typeof route.query.deviceAssetId === 'string' ? route.query.deviceAssetId : ''
const { availabilityError, availabilityPending, availabilityWindows, filters, refreshAvailability } = useMaintenanceAvailabilityWindows({
  deviceAssetIds: initialDeviceAssetIds,
})

const hasDeviceScope = computed(() => filters.deviceAssetIds.trim().length > 0)
const unavailableCount = computed(() =>
  availabilityWindows.value.filter((window) => (window.availabilityStatus ?? '').toLowerCase() === 'unavailable').length,
)
const inspectionCount = computed(() =>
  availabilityWindows.value.filter((window) => (window.reasonCode ?? '').trim().toLowerCase() === 'equipment.inspectionrequired').length,
)
const errorMessage = computed(() => formatError(availabilityError.value))

const windowStartLocal = computed({
  get: () => toLocalDateTime(filters.windowStartUtc),
  set: (value: string) => {
    filters.windowStartUtc = toIsoDateTime(value)
  },
})
const windowEndLocal = computed({
  get: () => toLocalDateTime(filters.windowEndUtc),
  set: (value: string) => {
    filters.windowEndUtc = toIsoDateTime(value)
  },
})

const columns: DataTableProColumn<EquipmentRuntimeAvailabilityWindow>[] = [
  { key: 'deviceAssetId', header: '设备', cellClass: 'font-medium', accessor: (r) => r.deviceAssetId ?? '未记录' },
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  { key: 'reasonCode', header: '原因' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '未绑定' },
  { key: 'startUtc', header: '开始', accessor: (r) => formatDateTime(r.startUtc) },
  { key: 'endUtc', header: '结束', accessor: (r) => formatDateTime(r.endUtc) },
  { key: 'sourceReferenceId', header: '关联业务', accessor: (r) => r.sourceReferenceId ?? '无' },
]

function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = { available: '可用', unavailable: '不可用', unknown: '未知' }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function availabilityVariant(value?: string | null) {
  if ((value ?? '').toLowerCase() === 'available') return 'success'
  if ((value ?? '').toLowerCase() === 'unavailable') return 'danger'
  return 'neutral'
}
function rowKey(row: EquipmentRuntimeAvailabilityWindow) {
  return `${row.deviceAssetId ?? ''}-${row.reasonCode ?? ''}-${row.startUtc ?? ''}-${row.endUtc ?? ''}`
}
function toLocalDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString()
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
    <PageHeader title="可用窗口" :breadcrumbs="[{ label: '设备监控' }]" :count="hasDeviceScope ? `${availabilityWindows.length} 个窗口` : '选择设备后查询'">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/maintenance/work-orders"><WrenchIcon aria-hidden="true" />维护工单</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="!hasDeviceScope || availabilityPending" @click="refreshAvailability">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <FieldProGroup class="grid gap-3 rounded-lg border bg-card p-4 lg:grid-cols-[minmax(220px,1fr)_220px_220px_minmax(180px,0.8fr)]">
      <FieldPro>
        <FieldProLabel for="avail-devices">设备范围</FieldProLabel>
        <InputPro id="avail-devices" v-model="filters.deviceAssetIds" autocomplete="off" placeholder="逗号分隔设备编号" />
      </FieldPro>
      <FieldPro>
        <FieldProLabel for="avail-start">窗口开始</FieldProLabel>
        <InputPro id="avail-start" v-model="windowStartLocal" type="datetime-local" />
      </FieldPro>
      <FieldPro>
        <FieldProLabel for="avail-end">窗口结束</FieldProLabel>
        <InputPro id="avail-end" v-model="windowEndLocal" type="datetime-local" />
      </FieldPro>
      <FieldPro>
        <FieldProLabel for="avail-work-centers">工作中心</FieldProLabel>
        <InputPro id="avail-work-centers" v-model="filters.workCenterIds" autocomplete="off" placeholder="可选，逗号分隔" />
      </FieldPro>
    </FieldProGroup>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div v-if="!hasDeviceScope" class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground">
      请输入设备编号后查看维护占用、点检阻塞和其他可用性窗口。
    </div>

    <template v-else>
      <SectionCards :columns="3">
        <SectionCard description="窗口数量" :value="availabilityWindows.length" hint="当前查询范围" />
        <SectionCard description="不可用窗口" :value="unavailableCount" hint="影响排程或执行" />
        <SectionCard description="点检相关" :value="inspectionCount" hint="点检未通过或待处理" />
      </SectionCards>

      <DataTablePro
        :columns="columns"
        :rows="availabilityWindows"
        :row-key="rowKey"
        :loading="availabilityPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围没有维护可用性窗口。"
      >
        <template #cell-deviceAssetId="{ row }">
          <RouterLink :to="`/equipment/${row.deviceAssetId}`" class="text-brand underline-offset-4 hover:underline">
            {{ row.deviceAssetId ?? '未记录' }}
          </RouterLink>
        </template>
        <template #cell-availabilityStatus="{ row }">
          <BadgePro class="rounded-sm" :variant="availabilityVariant(row.availabilityStatus)">{{ availabilityLabel(row.availabilityStatus) }}</BadgePro>
        </template>
        <template #cell-reasonCode="{ row }">
          <div class="grid gap-1">
            <span class="font-medium text-foreground">{{ describeEquipmentReason(row.reasonCode ?? '').label }}</span>
            <span class="text-xs text-muted-foreground">{{ describeEquipmentReason(row.reasonCode ?? '').nextStep }}</span>
          </div>
        </template>
      </DataTablePro>
    </template>
  </BusinessLayout>
</template>
