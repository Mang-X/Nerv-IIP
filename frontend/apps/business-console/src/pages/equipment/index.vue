<script setup lang="ts">
import type { NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentOverview,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvInput,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvRowActions,
  NvSectionCards,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  ActivityIcon,
  BellRingIcon,
  EyeIcon,
  GaugeIcon,
  RefreshCwIcon,
  WrenchIcon,
} from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备运行看板',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const router = useRouter()
const { activeBlocks, devices, filters, overviewError, overviewPending, refreshOverview } =
  useBusinessEquipmentOverview()

const errorMessage = computed(() => formatError(overviewError.value))
const runningCount = computed(
  () => devices.value.filter((d) => equipmentStatusTone(d.currentState) === 'success').length,
)
const faultCount = computed(
  () => devices.value.filter((d) => equipmentStatusTone(d.currentState) === 'danger').length,
)
const alarmCount = computed(() =>
  devices.value.reduce((total, d) => total + (d.activeAlarmCount ?? 0), 0),
)
// 设备状态构成：三段互斥且相加等于在册设备数，满足 NvMetricRing 的「部分之和 = 整体」前提。
// 报警数 / 阻塞窗口数与设备台数不同量纲，另用独立的告警卡表达，不混入同一个环。
const otherStateCount = computed(() =>
  Math.max(0, devices.value.length - runningCount.value - faultCount.value),
)
const stateSegments = computed<NvMetricSegment[]>(() => [
  { key: 'running', label: '运行就绪', value: runningCount.value, tone: 'success' },
  { key: 'fault', label: '异常停机', value: faultCount.value, tone: 'danger' },
  { key: 'other', label: '其他状态', value: otherStateCount.value, tone: 'neutral' },
])

type Device = (typeof devices)['value'][number]
const columns: NvDataTableColumn<Device>[] = [
  {
    key: 'deviceAssetId',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) => r.deviceAssetId ?? '无编号',
  },
  { key: 'currentState', header: '状态', width: 'w-32' },
  { key: 'isSourceFresh', header: '实时数据', width: 'w-32' },
  { key: 'activeAlarmCount', header: '报警', align: 'end', width: 'w-20' },
  { key: 'activeBlockCount', header: '阻塞', align: 'end', width: 'w-20' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'danger'
  return 'neutral'
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
  // 设备没上报状态不是"未知状态"，而是这台设备当前没有实时数据可读——照实说。
  return status ? (labels[status.toLowerCase()] ?? status) : '暂无实时数据'
}
function recordDowntime(deviceAssetId?: string | null) {
  void router.push({ path: '/mes/downtime', query: { deviceAssetId: deviceAssetId ?? undefined } })
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
      title="设备运行看板"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${devices.length} 台设备`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/alarms"
            ><BellRingIcon aria-hidden="true" />查看报警</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/oee"
            ><GaugeIcon aria-hidden="true" />OEE 与可用性</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="overviewPending"
          @click="refreshOverview"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="3">
      <NvMetricRing
        label="设备状态构成"
        :value="devices.length"
        center-caption="台设备"
        :segments="stateSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未解除报警"
        :value="alarmCount"
        :tone="alarmCount > 0 ? 'danger' : 'neutral'"
        :status="alarmCount > 0 ? { label: '需处理', tone: 'danger' } : undefined"
        :action="{ label: '查看报警', href: '/equipment/alarms' }"
      />
      <NvMetricCard
        variant="alert"
        label="阻塞中"
        :value="activeBlocks.length"
        :tone="activeBlocks.length > 0 ? 'warning' : 'neutral'"
        :status="activeBlocks.length > 0 ? { label: '影响排程', tone: 'warning' } : undefined"
        foot-start="阻塞窗口会占用排程与执行时段"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.deviceAssetIds"
          class="h-9 w-72"
          placeholder="默认全部设备；逗号分隔设备号可缩小范围"
          aria-label="设备范围（留空显示全部）"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.75fr)]">
      <NvDataTable
        :columns="columns"
        :rows="devices"
        :row-key="(r) => r.deviceAssetId ?? '无'"
        :loading="overviewPending"
        :searchable="false"
        :column-settings="false"
        empty-message="暂无设备运行记录。请先在基础数据登记设备资产，或调整上方设备范围后再试。"
      >
        <template #cell-deviceAssetId="{ row }">
          <RouterLink
            :to="`/equipment/${row.deviceAssetId}`"
            class="font-medium text-brand underline-offset-4 hover:underline"
          >
            {{ row.deviceAssetId ?? '无编号' }}
          </RouterLink>
        </template>
        <template #cell-currentState="{ row }">
          <NvBadge
            class="rounded-sm"
            :variant="badgeVariant(equipmentStatusTone(row.currentState))"
            >{{ statusLabel(row.currentState) }}</NvBadge
          >
        </template>
        <!-- 「过期」是采集口径，现场关心的是"这行数还能不能信"：不新鲜＝没有实时数据。 -->
        <template #cell-isSourceFresh="{ row }">
          <NvBadge class="rounded-sm" :variant="row.isSourceFresh ? 'success' : 'warning'">{{
            row.isSourceFresh ? '实时' : '暂无实时数据'
          }}</NvBadge>
        </template>
        <template #cell-activeAlarmCount="{ row }"
          ><span class="tabular-nums">{{ row.activeAlarmCount ?? 0 }}</span></template
        >
        <template #cell-activeBlockCount="{ row }"
          ><span class="tabular-nums">{{ row.activeBlockCount ?? 0 }}</span></template
        >
        <template #cell-actions="{ row }">
          <NvRowActions :label="`设备操作 ${row.deviceAssetId ?? ''}`">
            <NvDropdownMenuItem as-child>
              <RouterLink :to="`/equipment/${row.deviceAssetId}`"
                ><EyeIcon aria-hidden="true" />查看详情</RouterLink
              >
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/equipment/telemetry/oee',
                  query: { deviceAssetId: row.deviceAssetId },
                }"
              >
                <GaugeIcon aria-hidden="true" />
                OEE 与可用性
              </RouterLink>
            </NvDropdownMenuItem>
            <NvDropdownMenuItem @click="recordDowntime(row.deviceAssetId)">
              <WrenchIcon aria-hidden="true" />
              记录停机
            </NvDropdownMenuItem>
            <NvDropdownMenuItem as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/work-orders',
                  query: { deviceAssetId: row.deviceAssetId },
                }"
              >
                <WrenchIcon aria-hidden="true" />
                创建维修工单
              </RouterLink>
            </NvDropdownMenuItem>
          </NvRowActions>
        </template>
      </NvDataTable>

      <div class="rounded-lg border bg-card">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">当前阻塞</h2>
          <NvBadge class="rounded-sm" variant="neutral">{{ activeBlocks.length }}</NvBadge>
        </div>
        <div class="grid gap-3 p-4">
          <div
            v-for="block in activeBlocks"
            :key="`${block.deviceAssetId}-${block.reasonCode}-${block.startUtc}`"
            class="grid gap-2 rounded-lg border p-3"
          >
            <div class="flex min-w-0 items-center justify-between gap-2">
              <div class="min-w-0">
                <p class="truncate text-sm font-semibold text-foreground">
                  {{ block.deviceAssetId ?? '无设备' }}
                </p>
                <p class="truncate text-xs text-muted-foreground">
                  {{ block.workCenterId ?? '未绑定工作中心' }}
                </p>
              </div>
              <NvBadge class="rounded-sm" variant="danger">{{
                describeEquipmentReason(block.reasonCode ?? '').label
              }}</NvBadge>
            </div>
            <p class="text-sm leading-6 text-muted-foreground">
              {{ describeEquipmentReason(block.reasonCode ?? '').nextStep }}
            </p>
            <div class="flex flex-wrap gap-2 text-xs text-muted-foreground">
              <span
                ><ActivityIcon class="inline size-3" /> {{ formatDateTime(block.startUtc) }}</span
              >
              <span>{{ formatDateTime(block.endUtc) }}</span>
              <span v-if="block.sourceReferenceId">关联单据 {{ block.sourceReferenceId }}</span>
              <span v-if="block.substituteDeviceAssetIds?.length"
                >替代设备 {{ block.substituteDeviceAssetIds.join(', ') }}</span
              >
            </div>
            <NvButton
              size="sm"
              type="button"
              variant="outline"
              class="justify-self-start"
              @click="recordDowntime(block.deviceAssetId)"
            >
              <WrenchIcon aria-hidden="true" />
              记录停机
            </NvButton>
            <NvButton size="sm" type="button" variant="outline" class="justify-self-start" as-child>
              <RouterLink
                :to="{
                  path: '/maintenance/work-orders',
                  query: { deviceAssetId: block.deviceAssetId },
                }"
              >
                <WrenchIcon aria-hidden="true" />
                创建维修工单
              </RouterLink>
            </NvButton>
          </div>
          <div
            v-if="!activeBlocks.length"
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            当前没有设备阻塞窗口。
          </div>
        </div>
      </div>
    </div>
  </BusinessLayout>
</template>
