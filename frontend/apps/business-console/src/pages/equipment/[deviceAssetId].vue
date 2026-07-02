<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentDevice,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  BadgePro,
  ButtonPro,
  DataTablePro,
  PageHeader,
  SectionCard,
  SectionCards,
} from '@nerv-iip/ui'
import { ArrowLeftIcon, CalendarRangeIcon, RefreshCwIcon, TrendingUpIcon, WrenchIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '设备详情', requiredPermissions: ['business.iiot.telemetry.read'] } })

const route = useRoute()
const router = useRouter()
const routeDeviceAssetId = computed(() => {
  const params = route.params as Record<string, string | string[] | undefined>
  const value = params.deviceAssetId
  return Array.isArray(value) ? value[0] : value
})

const { activeAlarms, availabilityWindows, device, deviceError, devicePending, filters, refreshDevice } = useBusinessEquipmentDevice()

const currentState = computed(() => device.value?.currentState)
const errorMessage = computed(() => formatError(deviceError.value))
const blockCount = computed(() => availabilityWindows.value.filter((w) => w.availabilityStatus !== 'available').length)

watch(
  routeDeviceAssetId,
  (deviceAssetId) => {
    if (!deviceAssetId || deviceAssetId === filters.deviceAssetId) return
    filters.deviceAssetId = deviceAssetId
    void refreshDevice()
  },
  { immediate: true },
)

type Window = (typeof availabilityWindows)['value'][number]
const columns: DataTableProColumn<Window>[] = [
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  { key: 'reason', header: '原因', accessor: (r) => describeEquipmentReason(r.reasonCode ?? '').label },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '未绑定' },
  { key: 'startUtc', header: '开始', width: 'w-44' },
  { key: 'endUtc', header: '结束', width: 'w-44' },
  { key: 'sourceReferenceId', header: '关联业务', accessor: (r) => r.sourceReferenceId ?? '无' },
  { key: 'substituteDeviceAssetIds', header: '替代设备', accessor: (r) => (r.substituteDeviceAssetIds?.length ? r.substituteDeviceAssetIds.join(', ') : '无') },
]

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'danger'
  return 'neutral'
}
function statusLabel(status?: string | null) {
  const labels: Record<string, string> = { down: '停机', faulted: '故障', idle: '空闲', offline: '离线', ready: '就绪', running: '运行中', stopped: '停止' }
  return status ? (labels[status.toLowerCase()] ?? status) : '未知'
}
function severityLabel(value?: string | null) {
  const labels: Record<string, string> = { blocked: '阻塞', critical: '严重', info: '信息', warning: '预警' }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'danger'
  if (severity === 'warning') return 'warning'
  return 'neutral'
}
function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = { available: '可用', unavailable: '不可用', unknown: '未知' }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function availabilityVariant(value?: string | null) {
  if (value === 'available') return 'success'
  if (value === 'unavailable') return 'danger'
  return 'neutral'
}
function recordDowntime() {
  void router.push({ path: '/mes/downtime', query: { deviceAssetId: filters.deviceAssetId } })
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
    <PageHeader :title="filters.deviceAssetId ? `设备详情：${filters.deviceAssetId}` : '设备详情'" :breadcrumbs="[{ label: '设备监控（IoT）' }]">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment"><ArrowLeftIcon aria-hidden="true" />返回看板</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" @click="recordDowntime">
          <WrenchIcon aria-hidden="true" />
          记录停机
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="{ path: '/maintenance/work-orders', query: { deviceAssetId: filters.deviceAssetId } }">
            <WrenchIcon aria-hidden="true" />
            创建维修工单
          </RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="{ path: '/maintenance/reliability', query: { deviceAssetId: filters.deviceAssetId } }">
            <TrendingUpIcon aria-hidden="true" />
            可靠性
          </RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="{ path: '/maintenance/availability', query: { deviceAssetId: filters.deviceAssetId } }">
            <CalendarRangeIcon aria-hidden="true" />
            可用窗口
          </RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="devicePending" @click="refreshDevice">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div v-if="!filters.deviceAssetId" class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground">
      未指定设备。请从设备运行看板选择具体设备查看详情。
    </div>

    <template v-else>
    <SectionCards :columns="4">
      <SectionCard description="当前状态" :value="statusLabel(currentState?.currentState)" hint="设备运行事实" />
      <SectionCard description="数据状态" :value="currentState?.isSourceFresh ? '正常' : '过期'" :hint="formatDateTime(currentState?.stateOccurredAtUtc)" />
      <SectionCard description="未解除报警" :value="activeAlarms.length" hint="当前设备报警" />
      <SectionCard description="占用窗口" :value="blockCount" hint="影响排程或执行" />
    </SectionCards>

    <div class="grid gap-4 lg:grid-cols-[360px_minmax(0,1fr)]">
      <div class="grid gap-4">
        <div class="rounded-lg border bg-card p-4">
          <p class="text-xs font-bold uppercase text-primary">当前状态</p>
          <div class="mt-3 flex items-center justify-between gap-3">
            <div class="min-w-0">
              <p class="truncate text-lg font-semibold text-foreground">{{ currentState?.deviceAssetId ?? filters.deviceAssetId }}</p>
              <p class="mt-1 text-sm text-muted-foreground">状态时间 {{ formatDateTime(currentState?.stateOccurredAtUtc) }}</p>
            </div>
            <BadgePro class="rounded-sm" :variant="badgeVariant(equipmentStatusTone(currentState?.currentState))">{{ statusLabel(currentState?.currentState) }}</BadgePro>
          </div>
          <div class="mt-3">
            <BadgePro class="rounded-sm" :variant="currentState?.isSourceFresh ? 'success' : 'warning'">{{ currentState?.isSourceFresh ? '采集正常' : '采集过期' }}</BadgePro>
          </div>
        </div>

        <div class="rounded-lg border bg-card">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">当前报警</h2>
          </div>
          <div class="grid gap-3 p-4">
            <div v-for="alarm in activeAlarms" :key="alarm.alarmEventId ?? alarm.alarmCode" class="grid gap-2 rounded-lg border p-3">
              <div class="flex items-center justify-between gap-2">
                <p class="truncate text-sm font-semibold text-foreground">{{ alarm.alarmCode ?? '无代码' }}</p>
                <BadgePro class="rounded-sm" :variant="severityVariant(alarm.severity)">{{ severityLabel(alarm.severity) }}</BadgePro>
              </div>
              <p class="text-xs text-muted-foreground">{{ formatDateTime(alarm.raisedAtUtc) }}</p>
            </div>
            <div v-if="!activeAlarms.length" class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              当前设备没有未解除报警。
            </div>
          </div>
        </div>
      </div>

      <div class="grid gap-2">
        <span class="text-sm font-semibold text-foreground">可用性窗口（排程与维修占用）</span>
        <DataTablePro
          :columns="columns"
          :rows="availabilityWindows"
          :row-key="(r) => `${r.deviceAssetId}-${r.reasonCode}-${r.startUtc}`"
          :loading="devicePending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前设备没有可用性窗口。"
        >
          <template #cell-availabilityStatus="{ row }">
            <BadgePro class="rounded-sm" :variant="availabilityVariant(row.availabilityStatus)">{{ availabilityLabel(row.availabilityStatus) }}</BadgePro>
          </template>
          <template #cell-reason="{ row }">
            <div class="grid gap-1">
              <span class="font-medium text-foreground">{{ describeEquipmentReason(row.reasonCode ?? '').label }}</span>
              <span class="text-xs text-muted-foreground">{{ describeEquipmentReason(row.reasonCode ?? '').nextStep }}</span>
            </div>
          </template>
          <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
          <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
        </DataTablePro>
      </div>
    </div>
    </template>
  </BusinessLayout>
</template>
