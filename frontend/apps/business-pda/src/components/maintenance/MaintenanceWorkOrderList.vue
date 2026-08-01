<script setup lang="ts">
import type { BusinessConsoleMaintenanceWorkOrderItem } from '@nerv-iip/api-client'
import { NvListRow, NvMobileTag } from '@nerv-iip/ui-mobile'

import { maintenancePriorityText, maintenanceStatusLabel } from './maintenanceWorkOrderPresentation'

defineProps<{ items: BusinessConsoleMaintenanceWorkOrderItem[] }>()
const emit = defineEmits<{ select: [workOrder: BusinessConsoleMaintenanceWorkOrderItem] }>()

function title(item: BusinessConsoleMaintenanceWorkOrderItem) {
  return item.sourceReferenceId?.trim() || '维修工单'
}

function subtitle(item: BusinessConsoleMaintenanceWorkOrderItem) {
  return [
    item.deviceAssetId?.trim() || '设备未标识',
    `优先级 ${maintenancePriorityText(item.priority)}`,
    item.assignedTechnicianUserId ? `已指派 ${item.assignedTechnicianUserId}` : '未指派维修人员',
  ].join(' · ')
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border border-border">
    <NvListRow
      v-for="item in items"
      :key="item.workOrderId"
      data-testid="maintenance-work-order-row"
      :data-work-order-id="item.workOrderId"
      :title="title(item)"
      :subtitle="subtitle(item)"
      :interactive="Boolean(item.workOrderId)"
      @select="item.workOrderId && emit('select', item)"
    >
      <template #trailing>
        <NvMobileTag variant="default">
          {{ maintenanceStatusLabel(item.status) }}
        </NvMobileTag>
      </template>
    </NvListRow>
  </div>
</template>
