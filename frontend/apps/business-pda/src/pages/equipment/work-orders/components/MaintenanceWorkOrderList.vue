<script setup lang="ts">
import type { BusinessConsoleMaintenanceWorkOrderItem } from '@nerv-iip/api-client'
import { maintenancePriorityLabel, maintenanceWorkOrderStatusLabel } from '@nerv-iip/business-core'
import { NvListRow, NvMobileTag } from '@nerv-iip/ui-mobile'

const props = defineProps<{
  items: BusinessConsoleMaintenanceWorkOrderItem[]
  principalDisplayName?: string
}>()
const emit = defineEmits<{ select: [workOrder: BusinessConsoleMaintenanceWorkOrderItem] }>()

function title(item: BusinessConsoleMaintenanceWorkOrderItem) {
  return item.sourceReferenceId?.trim() || '维修工单'
}

function subtitle(item: BusinessConsoleMaintenanceWorkOrderItem) {
  return [
    item.deviceAssetId?.trim() ? '设备已关联' : '设备未标识',
    `优先级 ${maintenancePriorityLabel(item.priority)}`,
    props.principalDisplayName ? `维修人员 ${props.principalDisplayName}` : '身份资料暂不可用',
  ].join(' · ')
}

function select(item: BusinessConsoleMaintenanceWorkOrderItem) {
  if (item.workOrderId) emit('select', item)
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
      @select="select(item)"
      @keydown.space.prevent="select(item)"
    >
      <template #trailing>
        <NvMobileTag variant="default">
          {{ maintenanceWorkOrderStatusLabel(item.status) }}
        </NvMobileTag>
      </template>
    </NvListRow>
  </div>
</template>
