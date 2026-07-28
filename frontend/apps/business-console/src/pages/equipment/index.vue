<script setup lang="ts">
import type { NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
import DeviceQuickViewSheet from '@/components/equipment/DeviceQuickViewSheet.vue'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentOverview,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import { useEquipmentScopeSelection } from '@/composables/useEquipmentScopeSelection'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { equipmentStateLabel } from '@nerv-iip/business-core'
import {
  NvBadge,
  NvButton,
  NvCascadePicker,
  NvDataTable,
  NvDropdownMenuItem,
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
import { computed, ref, watch } from 'vue'
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

// 车间 → 产线 → 设备 级联范围：选择结果映射为 overview 接口吃的设备编号集合。
const { scope, levels, devicesInScope, scopePending } = useEquipmentScopeSelection()
const scopeNarrowed = computed(() =>
  Boolean(scope.value.workshop || scope.value.line || scope.value.device),
)
const scopedDeviceCodes = computed(() =>
  devicesInScope.value.map((d) => (d.code ?? '').trim()).filter((code) => code.length > 0),
)
watch(
  [scopeNarrowed, scopedDeviceCodes],
  ([narrowed, codes]) => {
    // 全厂（未收窄）留空串，composable 会回退到全部设备；收窄后按范围编号查询（后端上限 50 台）。
    filters.deviceAssetIds = narrowed ? codes.slice(0, 50).join(',') : ''
  },
  { immediate: true },
)
// 收窄后的范围里一台设备都没有时，编号集合为空串会被误读成「全部」，
// 此处直接以空列表呈现，不让查询悄悄回退到全厂。
const scopeEmpty = computed(() => scopeNarrowed.value && scopedDeviceCodes.value.length === 0)
const visibleDevices = computed(() => (scopeEmpty.value ? [] : devices.value))
const visibleBlocks = computed(() => (scopeEmpty.value ? [] : activeBlocks.value))

const errorMessage = computed(() => formatError(overviewError.value))
const runningCount = computed(
  () =>
    visibleDevices.value.filter((d) => equipmentStatusTone(d.currentState) === 'success').length,
)
const faultCount = computed(
  () => visibleDevices.value.filter((d) => equipmentStatusTone(d.currentState) === 'danger').length,
)
const alarmCount = computed(() =>
  visibleDevices.value.reduce((total, d) => total + (d.activeAlarmCount ?? 0), 0),
)
// 设备状态构成：三段互斥且相加等于在册设备数，满足 NvMetricRing 的「部分之和 = 整体」前提。
// 报警数 / 阻塞窗口数与设备台数不同量纲，另用独立的告警卡表达，不混入同一个环。
const otherStateCount = computed(() =>
  Math.max(0, visibleDevices.value.length - runningCount.value - faultCount.value),
)
const stateSegments = computed<NvMetricSegment[]>(() => [
  { key: 'running', label: '运行就绪', value: runningCount.value, tone: 'success' },
  { key: 'fault', label: '异常停机', value: faultCount.value, tone: 'danger' },
  { key: 'other', label: '其他状态', value: otherStateCount.value, tone: 'neutral' },
])

// 遥测读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice, resolveWorkCenter } = useMasterDataDisplayNames({
  devices: true,
  workCenters: true,
})
/** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
function deviceLabel(code?: string | null, fallback = '无编号') {
  if (!code) return fallback
  return resolveDevice(code) ?? code
}

type Device = (typeof devices)['value'][number]
const columns: NvDataTableColumn<Device>[] = [
  {
    key: 'deviceAssetId',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) =>
      resolveDevice(r.deviceAssetId)
        ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
        : (r.deviceAssetId ?? '无编号'),
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
function stateText(status?: string | null) {
  // 设备没上报状态不是"未知状态"，而是这台设备当前没有实时数据可读——照实说；
  // 非空的未知码走 business-core 共用映射（与 PDA 同一口径），兜底「未知状态」。
  return status?.trim() ? equipmentStateLabel(status) : '暂无实时数据'
}

// 行内速览抽屉：不离开看板即可核对设备状态 / 报警 / 可用性窗口。
const quickViewDeviceId = ref('')
const quickViewOpen = ref(false)
function openQuickView(deviceAssetId?: string | null) {
  if (!deviceAssetId) return
  quickViewDeviceId.value = deviceAssetId
  quickViewOpen.value = true
}

// 「当前阻塞」默认收敛为前几条，高度受控；其余折叠在「展开全部」后面。
const BLOCK_PREVIEW_COUNT = 4
const blocksExpanded = ref(false)
const visibleBlockCards = computed(() =>
  blocksExpanded.value ? visibleBlocks.value : visibleBlocks.value.slice(0, BLOCK_PREVIEW_COUNT),
)
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
      :count="`${visibleDevices.length} 台设备`"
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
        :value="visibleDevices.length"
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
        :value="visibleBlocks.length"
        :tone="visibleBlocks.length > 0 ? 'warning' : 'neutral'"
        :status="visibleBlocks.length > 0 ? { label: '影响排程', tone: 'warning' } : undefined"
        foot-start="阻塞窗口会占用排程与执行时段"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvCascadePicker
          v-model="scope"
          :levels="levels"
          class="min-w-0 flex-1"
          :aria-busy="scopePending"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.75fr)]">
      <NvDataTable
        :columns="columns"
        :rows="visibleDevices"
        :row-key="(r) => r.deviceAssetId ?? '无'"
        :loading="overviewPending"
        :searchable="false"
        :column-settings="false"
        :empty-message="
          scopeEmpty
            ? '当前范围内暂无设备主数据。请调整上方范围，或先在基础数据登记设备资产。'
            : '暂无设备运行记录。请先在基础数据登记设备资产，或调整上方设备范围后再试。'
        "
      >
        <template #cell-deviceAssetId="{ row }">
          <RouterLink
            :to="`/equipment/${row.deviceAssetId}`"
            class="grid leading-tight text-brand underline-offset-4 hover:underline"
          >
            <span class="font-medium">{{ deviceLabel(row.deviceAssetId) }}</span>
            <span v-if="resolveDevice(row.deviceAssetId)" class="text-xs text-muted-foreground">{{
              row.deviceAssetId
            }}</span>
          </RouterLink>
        </template>
        <template #cell-currentState="{ row }">
          <NvBadge
            class="rounded-sm"
            :variant="badgeVariant(equipmentStatusTone(row.currentState))"
            >{{ stateText(row.currentState) }}</NvBadge
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
            <NvDropdownMenuItem @click="openQuickView(row.deviceAssetId)">
              <EyeIcon aria-hidden="true" />
              查看详情
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

      <div class="self-start rounded-lg border bg-card">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">当前阻塞</h2>
          <NvBadge class="rounded-sm" variant="neutral">{{ visibleBlocks.length }}</NvBadge>
        </div>
        <div class="grid gap-3 p-4">
          <div
            v-for="block in visibleBlockCards"
            :key="`${block.deviceAssetId}-${block.reasonCode}-${block.startUtc}`"
            class="grid gap-2 rounded-lg border p-3"
          >
            <div class="flex min-w-0 items-center justify-between gap-2">
              <div class="min-w-0">
                <p class="truncate text-sm font-semibold text-foreground">
                  {{ deviceLabel(block.deviceAssetId, '无设备') }}
                </p>
                <p class="truncate text-xs text-muted-foreground">
                  {{
                    block.workCenterId
                      ? (resolveWorkCenter(block.workCenterId) ?? block.workCenterId)
                      : '未绑定工作中心'
                  }}
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
                >替代设备
                {{ block.substituteDeviceAssetIds.map((id) => deviceLabel(id)).join('、') }}</span
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
            v-if="!visibleBlocks.length"
            class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          >
            当前没有设备阻塞窗口。
          </div>
          <NvButton
            v-if="visibleBlocks.length > BLOCK_PREVIEW_COUNT"
            size="sm"
            type="button"
            variant="ghost"
            class="justify-self-center"
            @click="blocksExpanded = !blocksExpanded"
          >
            {{ blocksExpanded ? '收起' : `展开全部 (${visibleBlocks.length})` }}
          </NvButton>
        </div>
      </div>
    </div>

    <DeviceQuickViewSheet
      v-if="quickViewDeviceId"
      v-model:open="quickViewOpen"
      :device-asset-id="quickViewDeviceId"
    />
  </BusinessLayout>
</template>
