<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentDevice,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge, Button, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { ArrowLeftIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '设备详情' } })

const route = useRoute()
const routeDeviceAssetId = computed(() => {
  const params = route.params as Record<string, string | string[] | undefined>
  const value = params.deviceAssetId
  return Array.isArray(value) ? value[0] : value
})

const { activeAlarms, availabilityWindows, device, deviceError, devicePending, filters, refreshDevice } =
  useBusinessEquipmentDevice()

const currentState = computed(() => device.value?.currentState)
const errorMessage = computed(() => formatError(deviceError.value))
const blockCount = computed(() =>
  availabilityWindows.value.filter((window) => window.availabilityStatus !== 'available').length,
)

watch(
  routeDeviceAssetId,
  (deviceAssetId) => {
    if (!deviceAssetId || deviceAssetId === filters.deviceAssetId) return
    filters.deviceAssetId = deviceAssetId
    void refreshDevice()
  },
  { immediate: true },
)

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'destructive'
  return 'secondary'
}

function statusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    down: '停机',
    faulted: '故障',
    idle: '空闲',
    offline: '离线',
    ready: '就绪',
    running: '运行中',
    stopped: '停止',
  }
  return status ? (labels[status.toLowerCase()] ?? status) : '未知'
}

function severityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    blocked: '阻塞',
    critical: '严重',
    info: '信息',
    warning: '预警',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}

function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'destructive'
  if (severity === 'warning') return 'warning'
  return 'secondary'
}

function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    available: '可用',
    unavailable: '不可用',
    unknown: '未知',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}

function availabilityVariant(value?: string | null) {
  if (value === 'available') return 'success'
  if (value === 'unavailable') return 'destructive'
  return 'secondary'
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="设备异常"
        :title="`设备详情：${filters.deviceAssetId}`"
        summary="查看设备当前状态、未解除报警、可用性窗口和关联业务单据。"
        badge="设备详情"
      >
        <template #actions>
          <Button as-child size="sm" type="button" variant="outline">
            <RouterLink to="/equipment">
              <ArrowLeftIcon data-icon="inline-start" />
              返回看板
            </RouterLink>
          </Button>
          <Button size="sm" type="button" variant="outline" :disabled="devicePending" @click="refreshDevice">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessFormStatus :error="errorMessage" />

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="当前状态" :value="statusLabel(currentState?.currentState)" detail="设备运行事实" />
        <BusinessMetricCell
          label="数据状态"
          :value="currentState?.isSourceFresh ? '正常' : '过期'"
          :detail="formatDateTime(currentState?.stateOccurredAtUtc)"
        />
        <BusinessMetricCell label="未解除报警" :value="activeAlarms.length" detail="当前设备报警" />
        <BusinessMetricCell label="占用窗口" :value="blockCount" detail="影响排程或执行" />
      </div>

      <div class="grid gap-4 lg:grid-cols-[360px_minmax(0,1fr)]">
        <div class="grid gap-4">
          <div class="rounded-lg border bg-background p-4">
            <p class="text-xs font-bold uppercase text-primary">当前状态</p>
            <div class="mt-3 flex items-center justify-between gap-3">
              <div class="min-w-0">
                <p class="truncate text-lg font-semibold text-foreground">{{ currentState?.deviceAssetId ?? filters.deviceAssetId }}</p>
                <p class="mt-1 text-sm text-muted-foreground">状态时间 {{ formatDateTime(currentState?.stateOccurredAtUtc) }}</p>
              </div>
              <Badge class="rounded-sm" :variant="badgeVariant(equipmentStatusTone(currentState?.currentState))">
                {{ statusLabel(currentState?.currentState) }}
              </Badge>
            </div>
            <div class="mt-3">
              <Badge class="rounded-sm" :variant="currentState?.isSourceFresh ? 'success' : 'warning'">
                {{ currentState?.isSourceFresh ? '采集正常' : '采集过期' }}
              </Badge>
            </div>
          </div>

          <div class="rounded-lg border bg-background">
            <div class="border-b px-4 py-3">
              <h2 class="text-sm font-semibold text-foreground">当前报警</h2>
            </div>
            <div class="grid gap-3 p-4">
              <div v-for="alarm in activeAlarms" :key="alarm.alarmEventId ?? alarm.alarmCode" class="grid gap-2 rounded-lg border p-3">
                <div class="flex items-center justify-between gap-2">
                  <p class="truncate text-sm font-semibold text-foreground">{{ alarm.alarmCode ?? '无代码' }}</p>
                  <Badge class="rounded-sm" :variant="severityVariant(alarm.severity)">
                    {{ severityLabel(alarm.severity) }}
                  </Badge>
                </div>
                <p class="text-xs text-muted-foreground">{{ formatDateTime(alarm.raisedAtUtc) }}</p>
              </div>
              <div v-if="!activeAlarms.length" class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
                当前设备没有未解除报警。
              </div>
            </div>
          </div>
        </div>

        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">可用性窗口</h2>
            <span class="text-sm text-muted-foreground">排程与维修占用</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>状态</TableHead>
                  <TableHead>原因</TableHead>
                  <TableHead>工作中心</TableHead>
                  <TableHead>开始</TableHead>
                  <TableHead>结束</TableHead>
                  <TableHead>关联业务</TableHead>
                  <TableHead>替代设备</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="window in availabilityWindows" :key="`${window.deviceAssetId}-${window.reasonCode}-${window.startUtc}`">
                  <TableCell>
                    <Badge class="rounded-sm" :variant="availabilityVariant(window.availabilityStatus)">
                      {{ availabilityLabel(window.availabilityStatus) }}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <div class="grid gap-1">
                      <span class="font-medium text-foreground">{{ describeEquipmentReason(window.reasonCode ?? '').label }}</span>
                      <span class="text-xs text-muted-foreground">{{ describeEquipmentReason(window.reasonCode ?? '').nextStep }}</span>
                    </div>
                  </TableCell>
                  <TableCell>{{ window.workCenterId ?? '未绑定' }}</TableCell>
                  <TableCell>{{ formatDateTime(window.startUtc) }}</TableCell>
                  <TableCell>{{ formatDateTime(window.endUtc) }}</TableCell>
                  <TableCell>{{ window.sourceReferenceId ?? '无' }}</TableCell>
                  <TableCell>{{ window.substituteDeviceAssetIds?.length ? window.substituteDeviceAssetIds.join(', ') : '无' }}</TableCell>
                </TableRow>
                <TableEmpty v-if="devicePending" :colspan="7">正在加载设备详情...</TableEmpty>
                <TableEmpty v-if="!availabilityWindows.length && !devicePending" :colspan="7">
                  当前设备没有可用性窗口。
                </TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
