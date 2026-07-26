<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import {
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import FulfillmentTimelineBody from './FulfillmentTimelineBody.vue'

const props = defineProps<{
  open: boolean
  order: BusinessConsoleErpSalesOrderItem | null | undefined
}>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()

// 履约追踪时间线只在 Sheet 打开时实例化（延迟每节点独立 query，空态不发请求）。
const openModel = computed({
  get: () => props.open,
  set: (value) => emit('update:open', value),
})
</script>

<template>
  <NvSheet v-model:open="openModel">
    <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>
          履约追踪
          <span v-if="order?.salesOrderNo" class="text-primary">· {{ order.salesOrderNo }}</span>
        </NvSheetTitle>
        <!-- 各节点状态由下方时间线自己说清楚；此处仅供读屏播报，不在界面上再写一遍说明。 -->
        <NvSheetDescription class="sr-only">
          销售订单 {{ order?.salesOrderNo ?? '' }} 的履约节点时间线。
        </NvSheetDescription>
      </NvSheetHeader>

      <FulfillmentTimelineBody v-if="open" :order="order" />
    </NvSheetContent>
  </NvSheet>
</template>
