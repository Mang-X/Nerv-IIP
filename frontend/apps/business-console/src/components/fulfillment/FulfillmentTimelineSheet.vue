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
        <NvSheetDescription>
          从销售订单沿计划 / 排程 / 生产 / 质量 / 库存 / 发货 /
          应收全链路追踪当前卡点。仅串接已建立稳定关联的节点，其余显式标注「尚未建立关联」。
        </NvSheetDescription>
      </NvSheetHeader>

      <FulfillmentTimelineBody v-if="open" :order="order" />
    </NvSheetContent>
  </NvSheet>
</template>
