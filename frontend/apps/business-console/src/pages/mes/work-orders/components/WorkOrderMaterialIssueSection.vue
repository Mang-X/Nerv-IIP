<script setup lang="ts">
import { readFaceText } from '@/utils/readFace'
import { NvButton, NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  canReceive: View['canReceive']
  canReturn: View['canReturn']
  formatQuantity: View['formatQuantity']
  materialIssueColumns: View['materialIssueColumns']
  materialIssueReadForbidden: View['materialIssueReadForbidden']
  materialIssueRequestsError: View['materialIssueRequestsError']
  materialIssueRequestsErrorMessage: View['materialIssueRequestsErrorMessage']
  materialIssueRequestsPending: View['materialIssueRequestsPending']
  materialIssueRows: View['materialIssueRows']
  materialReadPermissionMessage: View['materialReadPermissionMessage']
  openReceiveDialog: View['openReceiveDialog']
  openReturnDialog: View['openReturnDialog']
  refreshMaterialIssueRequests: View['refreshMaterialIssueRequests']
  statusLabel: View['statusLabel']
}>()
</script>

<template>
  <!-- 领料与收料：本工单已发起的领料单、仓库出库单回写与线边收料入口（#1324）。 -->
  <div class="grid gap-2">
    <div class="flex items-center justify-between gap-2">
      <span class="text-sm font-semibold text-foreground">领料与收料</span>
      <NvButton
        type="button"
        variant="ghost"
        size="sm"
        :disabled="materialIssueRequestsPending || materialIssueReadForbidden"
        @click="refreshMaterialIssueRequests"
      >
        <RefreshCwIcon aria-hidden="true" />
        刷新
      </NvButton>
    </div>
    <p v-if="materialIssueReadForbidden" class="text-sm text-muted-foreground" role="status">
      {{ materialReadPermissionMessage }}
    </p>
    <NvDataTable
      v-else
      :columns="materialIssueColumns"
      :rows="materialIssueRows"
      :row-key="(r) => r.requestId ?? ''"
      :loading="materialIssueRequestsPending"
      :error="materialIssueRequestsError"
      :error-message="materialIssueRequestsErrorMessage"
      empty-message="本工单暂无领料申请。可按需发起领料，在这里查看申请量、已收量和关联出库单。"
      :searchable="false"
      :column-settings="false"
      data-testid="material-issue-requests"
      @retry="refreshMaterialIssueRequests"
    >
      <template #cell-requestedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.requestedQuantity) }}</span></template
      >
      <template #cell-receivedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</span></template
      >
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-wmsRequestId="{ row }">
        <span v-if="row.wmsRequestId" class="font-medium">{{
          readFaceText(row.wmsRequestId)
        }}</span>
        <span v-else class="text-xs text-muted-foreground">仓库尚未接单</span>
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canReceive(row)"
          type="button"
          variant="ghost"
          size="sm"
          :data-testid="`receive-material-${row.requestId}`"
          @click="openReceiveDialog(row)"
        >
          线边收料
        </NvButton>
        <NvButton
          v-if="canReturn(row)"
          type="button"
          variant="ghost"
          size="sm"
          :data-testid="`return-material-${row.requestId}`"
          @click="openReturnDialog(row)"
        >
          线边退料
        </NvButton>
        <span v-if="!canReceive(row) && !canReturn(row)" class="text-xs text-muted-foreground"
          >无需操作</span
        >
      </template>
    </NvDataTable>
  </div>
</template>
