<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesDowntimeEvents } from '@/composables/useBusinessMes'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import {
  mesDowntimeStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES } from '@/permissions'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备与停机',
    requiredPermissions: ['business.mes.downtime.read'],
  },
})

const {
  downtimeEvents,
  downtimeEventsError,
  downtimeEventsPending,
  downtimeEventsTotal,
  filters,
  recoverDowntimeEvent,
  recoverDowntimeEventPending,
  refreshDowntimeEvents,
} = useMesDowntimeEvents()
const { keyword } = useMesKeywordFilter(filters)
const { statusLabel } = useMesReferenceLabels()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})
const statusFilter = shallowRef('all')

const openCount = computed(
  () => downtimeEvents.value.filter((x) => x.status?.toLowerCase() === 'open').length,
)
// 停机的决策点是「还有多少台没恢复」——一张构成卡把总量与恢复进度放在一起。
const downtimeSegments = computed(() =>
  pagedBreakdownSegments(downtimeEventsTotal.value, [
    { key: 'open', label: '未恢复', value: openCount.value, tone: 'danger' },
    {
      key: 'recovered',
      label: '已恢复',
      value: downtimeEvents.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)
const errorMessage = computed(() => formatError(downtimeEventsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

// 停机读面只回设备编码，中文设备名在设备台账里，按编码 join 出来。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })

type DowntimeRow = (typeof downtimeEvents)['value'][number]

function deviceCode(row: DowntimeRow) {
  return row.deviceAssetCode ?? row.deviceAssetId ?? ''
}
function deviceName(row: DowntimeRow) {
  return row.deviceAssetName ?? resolveDevice(deviceCode(row))
}
/** 「名称 编码」纯文本，供排序 / 导出用；名录查不到就只有编码，不编名字。 */
function deviceText(row: DowntimeRow) {
  const code = deviceCode(row)
  if (!code) return '未指定'
  const name = deviceName(row)
  return name ? `${name} ${code}` : code
}

const columns: NvDataTableColumn<DowntimeRow>[] = [
  {
    key: 'downtimeEventId',
    header: '停机事件',
    cellClass: 'font-medium',
    accessor: (r) => r.downtimeEventId ?? '无',
  },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) => r.workCenterId ?? '未指定',
  },
  {
    key: 'workOrderId',
    header: '工单',
    accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '未关联',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => deviceText(r),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'startedAtUtc', header: '开始', width: 'w-44' },
  { key: 'recoveredAtUtc', header: '恢复', width: 'w-44' },
  { key: 'actions', header: '操作', width: 'w-24', sortable: false, accessor: () => '' },
]

// ── 恢复停机（#1323：恢复通道从只读页断掉，这里补权威入口）──────────────
const auth = useAuthStore()
const canRecover = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(BUSINESS_PERMISSION_CODES.mesDowntimeManage),
)
const recoverTarget = ref<DowntimeRow | null>(null)
const recoverOpen = computed({
  get: () => recoverTarget.value !== null,
  set: (open: boolean) => {
    if (!open) recoverTarget.value = null
  },
})

function isOpenRow(row: DowntimeRow) {
  return row.status?.toLowerCase() === 'open'
}

async function confirmRecover() {
  const row = recoverTarget.value
  if (!row?.downtimeEventId) return
  try {
    await recoverDowntimeEvent(row.downtimeEventId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      recoveredAtUtc: new Date().toISOString(),
      // #1219 稳定幂等键：同一停机事件的恢复是同一业务意图，键不掺时间戳，
      // 重复点击/重试由后端幂等或 KnownException 兜住。
      idempotencyKey: `downtime-recover-${row.downtimeEventId}`,
    })
    notifySuccess('停机已恢复，该工作中心的开工拦截已解除。')
    recoverTarget.value = null
    void refreshDowntimeEvents()
  } catch (error) {
    notifyOperationFailure('恢复停机失败', error, '恢复停机失败，请稍后重试。')
  }
}

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备与停机"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${downtimeEventsTotal} 条停机事件`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="downtimeEventsPending"
          @click="refreshDowntimeEvents"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2">
      <NvMetricCard
        variant="breakdown"
        label="停机事件"
        :value="downtimeEventsTotal"
        unit="起"
        :segments="downtimeSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未恢复停机"
        :value="openCount"
        unit="起"
        :tone="openCount > 0 ? 'danger' : 'neutral'"
        :status="
          openCount > 0
            ? { label: '需跟进恢复', tone: 'danger' }
            : { label: '全部恢复', tone: 'success' }
        "
        :foot-start="
          openCount > 0 ? '优先处理仍在影响工序执行的停机事件。' : '当前没有未恢复的停机事件。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="工单 / 工序 / 设备"
          aria-label="搜索停机事件"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="停机状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesDowntimeStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="downtimeEventsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="downtimeEvents"
      row-key="downtimeEventId"
      :loading="downtimeEventsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无停机事件。先在工序执行登记设备异常，再回到这里跟进恢复与影响范围。"
    >
      <template #cell-deviceAssetId="{ row }">
        <CodeWithNameCell :code="deviceCode(row)" :name="deviceName(row)" fallback="未指定" />
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-startedAtUtc="{ row }">{{ formatDateTime(row.startedAtUtc) }}</template>
      <template #cell-recoveredAtUtc="{ row }">{{ formatDateTime(row.recoveredAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canRecover && isOpenRow(row)"
          size="sm"
          type="button"
          variant="outline"
          :disabled="recoverDowntimeEventPending"
          @click="recoverTarget = row"
        >
          恢复
        </NvButton>
        <span v-else class="text-muted-foreground">—</span>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="recoverOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认恢复停机</NvDialogTitle>
          <NvDialogDescription>
            确认后停机事件立即关闭，工作中心解除停机拦截，受影响工序可重新开工。
          </NvDialogDescription>
        </NvDialogHeader>
        <dl v-if="recoverTarget" class="grid gap-2 text-sm">
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">停机事件</dt>
            <dd class="font-medium">{{ recoverTarget.downtimeEventId }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">工作中心</dt>
            <dd>{{ recoverTarget.workCenterId ?? '未指定' }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">设备</dt>
            <dd>{{ deviceText(recoverTarget) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">停机开始</dt>
            <dd>{{ formatDateTime(recoverTarget.startedAtUtc) }}</dd>
          </div>
          <div class="flex justify-between gap-4">
            <dt class="text-muted-foreground">恢复人</dt>
            <dd>{{ auth.displayName }}</dd>
          </div>
        </dl>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="recoverOpen = false">取消</NvButton>
          <NvButton type="button" :disabled="recoverDowntimeEventPending" @click="confirmRecover">
            {{ recoverDowntimeEventPending ? '恢复中…' : '确认恢复' }}
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
