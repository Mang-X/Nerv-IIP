<script setup lang="ts">
import {
  NvButton,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import {
  CalendarCogIcon,
  ClipboardCheckIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
  XCircleIcon,
} from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  canCancel: View['canCancel']
  canClose: View['canClose']
  canHold: View['canHold']
  canReadInspectionRecords: View['canReadInspectionRecords']
  canReadReceipts: View['canReadReceipts']
  canReadReports: View['canReadReports']
  canRecordEngineeringDecision: View['canRecordEngineeringDecision']
  canTransformWorkOrder: View['canTransformWorkOrder']
  cancelDisabledReason: View['cancelDisabledReason']
  detail: View['detail']
  detailError: View['detailError']
  detailPending: View['detailPending']
  materialReadinessPending: View['materialReadinessPending']
  openCancelDialog: View['openCancelDialog']
  openDecisionDialog: View['openDecisionDialog']
  openHoldDialog: View['openHoldDialog']
  openRoute: View['openRoute']
  openSplitDialog: View['openSplitDialog']
  refreshAll: View['refreshAll']
  scheduleDisabledReason: View['scheduleDisabledReason']
  splitPending: View['splitPending']
  workOrderManageScopeReady: View['workOrderManageScopeReady']
}>()

const closeOpen = defineModel<View['closeOpen']>('closeOpen', { required: true })

const scheduleOpen = defineModel<View['scheduleOpen']>('scheduleOpen', { required: true })
</script>

<template>
  <!-- 对该单排产：只生成一个含本工单的新方案（MAN-694 / #1262）。 -->
  <NvButton
    v-if="canRecordEngineeringDecision"
    size="sm"
    type="button"
    variant="outline"
    data-testid="record-engineering-change-decision"
    @click="openDecisionDialog"
  >
    记录工程变更决策
  </NvButton>
  <NvButton
    v-if="workOrderManageScopeReady"
    size="sm"
    type="button"
    variant="outline"
    :disabled="!canTransformWorkOrder || splitPending"
    :title="
      canTransformWorkOrder
        ? '将当前工单拆成多个新的子工单'
        : '只有已创建或已下达状态的工单可以拆分'
    "
    data-testid="open-split-work-order"
    @click="openSplitDialog"
  >
    拆分工单
  </NvButton>
  <NvButton
    v-if="canHold"
    size="sm"
    type="button"
    variant="outline"
    data-testid="open-hold-work-order"
    @click="openHoldDialog"
  >
    挂起工单
  </NvButton>
  <NvButton
    v-if="canClose"
    size="sm"
    type="button"
    variant="default"
    data-testid="close-work-order"
    @click="closeOpen = true"
  >
    关闭工单
  </NvButton>
  <NvButton
    v-if="!scheduleDisabledReason"
    size="sm"
    type="button"
    variant="outline"
    data-testid="work-order-schedule-single"
    @click="scheduleOpen = true"
  >
    <CalendarCogIcon aria-hidden="true" />
    对该单排产
  </NvButton>
  <NvTooltipProvider v-else>
    <NvTooltip>
      <NvTooltipTrigger as-child>
        <span tabindex="0">
          <NvButton size="sm" type="button" variant="outline" disabled>
            <CalendarCogIcon aria-hidden="true" />
            对该单排产
          </NvButton>
        </span>
      </NvTooltipTrigger>
      <NvTooltipContent>{{ scheduleDisabledReason }}</NvTooltipContent>
    </NvTooltip>
  </NvTooltipProvider>
  <NvButton
    v-if="canReadReports"
    size="sm"
    type="button"
    variant="outline"
    @click="openRoute('/mes/production-reports')"
  >
    <ClipboardCheckIcon aria-hidden="true" />
    报工记录
  </NvButton>
  <NvButton
    v-if="canReadReceipts"
    size="sm"
    type="button"
    variant="outline"
    :disabled="detailPending || !!detailError || !detail?.skuId"
    @click="openRoute('/mes/receipts')"
  >
    <PackageCheckIcon aria-hidden="true" />
    完工入库
  </NvButton>
  <NvButton
    v-if="canReadInspectionRecords"
    size="sm"
    type="button"
    variant="outline"
    @click="openRoute('/quality/inspections')"
  >
    <ShieldCheckIcon aria-hidden="true" />
    质量检验
  </NvButton>
  <NvButton
    size="sm"
    type="button"
    variant="outline"
    :disabled="detailPending || materialReadinessPending"
    @click="refreshAll"
  >
    <RefreshCwIcon aria-hidden="true" />
    刷新
  </NvButton>

  <!-- 取消工单：仅创建/已下达/暂停可用，其余禁用并 tooltip 说明原因 -->
  <NvButton
    v-if="canCancel"
    size="sm"
    type="button"
    variant="destructive"
    @click="openCancelDialog"
  >
    <XCircleIcon aria-hidden="true" />
    取消工单
  </NvButton>
  <NvTooltipProvider v-else>
    <NvTooltip>
      <NvTooltipTrigger as-child>
        <span tabindex="0">
          <NvButton size="sm" type="button" variant="destructive" disabled>
            <XCircleIcon aria-hidden="true" />
            取消工单
          </NvButton>
        </span>
      </NvTooltipTrigger>
      <NvTooltipContent>{{ cancelDisabledReason }}</NvTooltipContent>
    </NvTooltip>
  </NvTooltipProvider>
</template>
