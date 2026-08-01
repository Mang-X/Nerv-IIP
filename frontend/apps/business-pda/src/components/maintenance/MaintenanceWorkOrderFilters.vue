<script setup lang="ts">
import {
  maintenanceWorkOrderStatusOptions,
  type MaintenanceWorkOrderStatusCode,
} from '@nerv-iip/business-core'
import {
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvSearchBar,
} from '@nerv-iip/ui-mobile'

defineProps<{ deviceLabel?: string }>()
const emit = defineEmits<{ chooseDevice: [] }>()
const status = defineModel<'' | MaintenanceWorkOrderStatusCode>('status', { required: true })
const deviceAssetId = defineModel<string>('deviceAssetId', { required: true })
const keyword = defineModel<string>('keyword', { required: true })

const statusOptions = [{ label: '全部状态', value: '' }, ...maintenanceWorkOrderStatusOptions]
</script>

<template>
  <div class="space-y-2 p-3">
    <NvSearchBar
      v-model="keyword"
      data-testid="maintenance-keyword"
      aria-label="维修工单关键字"
      placeholder="搜索工单号、设备或指派"
    />
    <NvMobileDropdownMenu>
      <NvMobileDropdownMenuItem
        v-model="status"
        data-testid="maintenance-status"
        title="工单状态"
        :options="statusOptions"
      />
    </NvMobileDropdownMenu>
    <div class="overflow-hidden rounded-lg border border-border">
      <NvListRow
        data-testid="maintenance-device-filter"
        title="设备"
        :subtitle="deviceAssetId ? deviceLabel || '已选择设备' : '全部设备'"
        @select="emit('chooseDevice')"
      />
    </div>
    <NvMobileButton
      v-if="deviceAssetId"
      data-testid="maintenance-device-clear"
      variant="text"
      size="sm"
      block
      class="min-h-touch"
      @click="deviceAssetId = ''"
    >
      清除设备筛选
    </NvMobileButton>
  </div>
</template>
