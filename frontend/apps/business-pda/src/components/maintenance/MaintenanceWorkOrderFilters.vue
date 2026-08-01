<script setup lang="ts">
import {
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvSearchBar,
} from '@nerv-iip/ui-mobile'

defineProps<{ deviceLabel?: string }>()
const emit = defineEmits<{ chooseDevice: [] }>()
const status = defineModel<string>('status', { required: true })
const deviceAssetId = defineModel<string>('deviceAssetId', { required: true })
const keyword = defineModel<string>('keyword', { required: true })

const statusOptions = [
  { label: '全部状态', value: '' },
  { label: '待处理', value: 'open' },
  { label: '已接单', value: 'accepted' },
  { label: '处理中', value: 'inProgress' },
  { label: '已暂停', value: 'paused' },
  { label: '等待备件', value: 'waitingForParts' },
  { label: '已完成', value: 'completed' },
  { label: '已验证', value: 'verified' },
  { label: '已关闭', value: 'closed' },
  { label: '已取消', value: 'cancelled' },
]
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
        :subtitle="deviceAssetId ? deviceLabel || deviceAssetId : '全部设备'"
        @select="emit('chooseDevice')"
      />
    </div>
    <NvMobileButton
      v-if="deviceAssetId"
      data-testid="maintenance-device-clear"
      variant="text"
      size="sm"
      block
      @click="deviceAssetId = ''"
    >
      清除设备筛选
    </NvMobileButton>
  </div>
</template>
