<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import SingleOrderSchedulingDialog from '@/components/scheduling/SingleOrderSchedulingDialog.vue'
import { useCanScheduleSingleOrder } from '@/composables/useSingleOrderScheduling'
import {
  NvButton,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
} from '@nerv-iip/ui'
import { CalendarCogIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'
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

// 「对该单排产」（MAN-694 / #1262）。排程的最小单位是 MES 工单，而契约里还没有
// 销售订单 → 工单 的稳定关联键（见 useFulfillmentTimeline 的 mes-work-order 节点），
// 所以这里把销售单号当**检索起点**交给弹窗，由排产员确认工单——绝不按相似编号自动认定。
const scheduleOpen = shallowRef(false)
const canSchedule = useCanScheduleSingleOrder()
const salesOrderNo = computed(() => props.order?.salesOrderNo?.trim() ?? '')
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

      <div v-if="salesOrderNo" class="flex flex-wrap items-center justify-between gap-2 px-4 pt-2">
        <p class="text-sm text-muted-foreground">
          排程以 MES 工单为最小单位；本入口只生成一个只含该单的新方案。
        </p>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          data-testid="sales-order-schedule-single"
          :disabled="!canSchedule"
          :title="canSchedule ? '对该销售订单对应的工单排产' : '当前账号没有排产管理权限'"
          @click="scheduleOpen = true"
        >
          <CalendarCogIcon aria-hidden="true" />
          对该单排产
        </NvButton>
      </div>

      <SingleOrderSchedulingDialog
        v-if="scheduleOpen"
        v-model:open="scheduleOpen"
        :context-label="`销售订单 ${salesOrderNo}`"
        :initial-keyword="salesOrderNo"
      />

      <FulfillmentTimelineBody v-if="open" :order="order" />
    </NvSheetContent>
  </NvSheet>
</template>
