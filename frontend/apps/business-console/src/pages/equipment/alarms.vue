<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvRowActions,
  NvSectionCards,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import {
  BellOffIcon,
  CheckCircle2Icon,
  EyeIcon,
  LineChartIcon,
  RefreshCwIcon,
  Settings2Icon,
  TriangleAlertIcon,
  Undo2Icon,
  WrenchIcon,
} from '@lucide/vue'
import { RouterLink } from 'vue-router'
import AlarmShelveDialog from './components/AlarmShelveDialog.vue'
import AlarmBatchAckDialog from './components/AlarmBatchAckDialog.vue'
import { useAlarmPage } from './useAlarmPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备报警',
    requiredPermissions: ['business.iiot.alarms.read'],
  },
})

const {
  DURATION_PRESETS,
  MAX_SHELVE_MINUTES,
  VIEW_TABS,
  abandonShelve,
  ackTargets,
  activeView,
  alarmKey,
  alarmNo,
  alarms,
  alarmsPending,
  batchAck,
  canAcknowledge,
  canCreateMaintenanceWorkOrder,
  canManageAlarms,
  canShelve,
  canUnshelve,
  columns,
  confirmBatchAck,
  confirmShelve,
  customMinutesError,
  deviceLabel,
  dispositionFacets,
  errorMessage,
  escalationTooltip,
  formatDateTime,
  handleAcknowledge,
  handleUnshelve,
  historyQuery,
  lifecycleLabel,
  onShelveOpenChange,
  openBatchAck,
  openShelve,
  page,
  pageSize,
  pagedAlarms,
  reasonError,
  reasonRequired,
  recordDowntime,
  refreshAlarms,
  resolveDevice,
  rowClass,
  selectView,
  selectedKeys,
  severityLabel,
  severitySegments,
  severityVariant,
  shelve,
  shelveContextItems,
  shelveInvalid,
  shelveIsBatch,
  shelveTargets,
  statusLabel,
  statusVariant,
  totalItems,
  updatePageSize,
  viewCounts,
} = useAlarmPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备报警"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${alarms.length} 条未解除`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment">设备看板</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/alarm-rules"
            ><Settings2Icon aria-hidden="true" />报警规则</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="alarmsPending"
          @click="refreshAlarms"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvMetricRing
        label="未解除报警级别构成"
        :value="alarms.length"
        center-caption="条未解除"
        :segments="severitySegments"
      />
      <NvMetricCard
        variant="facets"
        label="处置进展"
        :value="alarms.length"
        unit=" 条"
        :facets="dispositionFacets"
      />
    </NvSectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <!-- 快捷筛选:升级是与处置正交的独立视图(可与已确认/已搁置重叠) -->
    <div class="flex flex-wrap items-center gap-1.5" role="tablist" aria-label="报警筛选">
      <NvButton
        v-for="view in VIEW_TABS"
        :key="view.value"
        size="sm"
        type="button"
        role="tab"
        :variant="activeView === view.value ? 'default' : 'ghost'"
        :aria-selected="activeView === view.value"
        @click="selectView(view.value)"
      >
        {{ view.label }}
        <span class="ms-1 text-xs tabular-nums opacity-70">{{ viewCounts[view.value] }}</span>
      </NvButton>
    </div>

    <NvDataTable
      v-model:selected="selectedKeys"
      :columns="columns"
      :rows="pagedAlarms"
      :row-key="alarmKey"
      :row-class="rowClass"
      :loading="alarmsPending"
      :searchable="false"
      :column-settings="false"
      :selectable="canManageAlarms"
      manual
      :page="page"
      :total-items="totalItems"
      :page-size="pageSize"
      empty-message="当前没有未解除设备报警。"
      @update:page="page = $event"
      @update:page-size="updatePageSize"
    >
      <template #bulk-actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!ackTargets.length"
          @click="openBatchAck"
        >
          <CheckCircle2Icon aria-hidden="true" />
          批量确认{{ ackTargets.length ? ` (${ackTargets.length})` : '' }}
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!shelveTargets.length"
          @click="openShelve(shelveTargets)"
        >
          <BellOffIcon aria-hidden="true" />
          批量搁置{{ shelveTargets.length ? ` (${shelveTargets.length})` : '' }}
        </NvButton>
      </template>
      <template #cell-alarmEventId="{ row }">
        <div class="flex items-center gap-1.5">
          <NvTooltipProvider v-if="row.escalatedAtUtc">
            <NvTooltip>
              <NvTooltipTrigger as-child>
                <TriangleAlertIcon
                  class="size-4 shrink-0 text-destructive"
                  aria-label="报警已升级"
                />
              </NvTooltipTrigger>
              <NvTooltipContent>
                <span class="whitespace-pre-line">{{ escalationTooltip(row) }}</span>
              </NvTooltipContent>
            </NvTooltip>
          </NvTooltipProvider>
          <span>{{ alarmNo(row) }}</span>
        </div>
      </template>
      <template #cell-deviceAssetId="{ row }">
        <RouterLink
          :to="`/equipment/${row.deviceAssetId}`"
          class="grid leading-tight text-brand underline-offset-4 hover:underline"
        >
          <span>{{ deviceLabel(row.deviceAssetId) }}</span>
          <span v-if="resolveDevice(row.deviceAssetId)" class="text-xs text-muted-foreground">{{
            row.deviceAssetId
          }}</span>
        </RouterLink>
      </template>
      <template #cell-severity="{ row }">
        <NvBadge class="rounded-sm" :variant="severityVariant(row.severity)">{{
          severityLabel(row.severity)
        }}</NvBadge>
      </template>
      <template #cell-status="{ row }">
        <NvBadge class="rounded-sm" :variant="statusVariant(row.status)">{{
          statusLabel(row.status)
        }}</NvBadge>
      </template>
      <template #cell-lifecycle="{ row }">
        <span class="text-sm text-muted-foreground">{{ lifecycleLabel(row) }}</span>
      </template>
      <template #cell-raisedAtUtc="{ row }">{{ formatDateTime(row.raisedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`报警操作 ${row.alarmEventId ?? ''}`">
          <NvDropdownMenuItem v-if="canAcknowledge(row)" @click="handleAcknowledge(row)">
            <CheckCircle2Icon aria-hidden="true" />
            确认报警
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canShelve(row)" @click="openShelve([row])">
            <BellOffIcon aria-hidden="true" />
            搁置报警…
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canUnshelve(row)" @click="handleUnshelve(row)">
            <Undo2Icon aria-hidden="true" />
            解除搁置
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink :to="`/equipment/${row.deviceAssetId}`"
              ><EyeIcon aria-hidden="true" />设备详情</RouterLink
            >
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/equipment/telemetry/history',
                query: historyQuery(row),
              }"
            >
              <LineChartIcon aria-hidden="true" />
              历史趋势
            </RouterLink>
          </NvDropdownMenuItem>
          <NvDropdownMenuItem @click="recordDowntime(row.deviceAssetId)">
            <WrenchIcon aria-hidden="true" />
            记录停机
          </NvDropdownMenuItem>
          <NvDropdownMenuItem v-if="canCreateMaintenanceWorkOrder" as-child>
            <RouterLink
              :to="{
                path: '/maintenance/work-orders',
                query: { deviceAssetId: row.deviceAssetId, sourceAlarmId: row.alarmEventId },
              }"
            >
              <WrenchIcon aria-hidden="true" />
              创建维修工单
            </RouterLink>
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <!-- 搁置对话框:时长选项组(30m/2h/8h/自定义) + 原因(批量必填);失败重试时锁定字段+禁关闭 -->
    <AlarmShelveDialog
      :-d-u-r-a-t-i-o-n_-p-r-e-s-e-t-s="DURATION_PRESETS"
      :-m-a-x_-s-h-e-l-v-e_-m-i-n-u-t-e-s="MAX_SHELVE_MINUTES"
      :abandon-shelve="abandonShelve"
      :confirm-shelve="confirmShelve"
      :custom-minutes-error="customMinutesError"
      :on-shelve-open-change="onShelveOpenChange"
      :reason-error="reasonError"
      :reason-required="reasonRequired"
      :shelve="shelve"
      :shelve-context-items="shelveContextItems"
      :shelve-invalid="shelveInvalid"
      :shelve-is-batch="shelveIsBatch"
    />

    <!-- 批量确认:AlertDialog + 条数复述 -->
    <AlarmBatchAckDialog
      :ack-targets="ackTargets"
      :batch-ack="batchAck"
      :confirm-batch-ack="confirmBatchAck"
    />
  </BusinessLayout>
</template>
