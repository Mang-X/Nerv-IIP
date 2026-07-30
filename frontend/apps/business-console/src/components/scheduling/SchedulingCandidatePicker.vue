<script setup lang="ts">
import { useMesWorkOrders } from '@/composables/useBusinessMes'
import {
  isSchedulableWorkbenchCandidate,
  SCHEDULABLE_WORK_ORDER_STATUSES,
} from '@/composables/useSchedulingWorkbench'
import {
  NvButton,
  NvField,
  NvFieldLabel,
  NvInput,
  NvRadioGroup,
  NvRadioGroupItem,
  Spinner,
} from '@nerv-iip/ui'
import { computed, watch } from 'vue'

/**
 * 候选工单选择器。
 *
 * **独立成组件就是为了让候选查询真正惰性**：`useMesWorkOrders` 一旦实例化就会发请求，
 * 而单单排产的两种入口里有一种（工单详情 / 计划建议行）已经知道目标工单、根本不需要候选。
 * 由调用方 `v-if` 挂载本组件，「需要挑单才查」这句话才成立。
 */
const props = withDefaults(
  defineProps<{
    /** 候选检索起点（例如销售单号）。**不是关联关系**，见下方界面说明。 */
    initialKeyword?: string
    disabled?: boolean
  }>(),
  { initialKeyword: '', disabled: false },
)

const selected = defineModel<string>({ required: true })

const mes = useMesWorkOrders({ initialTake: 50 })
mes.filters.statuses = SCHEDULABLE_WORK_ORDER_STATUSES.join(',')

watch(
  () => props.initialKeyword,
  (keyword) => {
    mes.filters.keyword = keyword?.trim() || undefined
  },
  { immediate: true },
)

const candidates = computed(() => mes.workOrders.value.filter(isSchedulableWorkbenchCandidate))
const failed = computed(() => !mes.workOrdersPending.value && mes.workOrdersError.value != null)

function candidateLabel(candidate: (typeof candidates.value)[number]) {
  return [
    candidate.workOrderNo ?? candidate.workOrderId,
    candidate.skuCode ?? candidate.skuId,
    candidate.quantity ?? 0,
  ]
    .filter((part) => part !== undefined && part !== null && part !== '')
    .join(' · ')
}
</script>

<template>
  <NvField>
    <NvFieldLabel for="single-order-scheduling-keyword">选择工单</NvFieldLabel>
    <NvInput
      id="single-order-scheduling-keyword"
      :model-value="mes.filters.keyword ?? ''"
      placeholder="工单号 / SKU / 生产版本"
      autocomplete="off"
      :disabled="props.disabled"
      @update:model-value="mes.filters.keyword = String($event).trim() || undefined"
    />
    <p class="text-sm text-muted-foreground">
      契约里还没有「销售订单 → MES 工单」的稳定关联键，检索词只是起点，请确认要排的工单。
    </p>

    <div
      v-if="mes.workOrdersPending.value"
      class="flex items-center gap-2 text-sm text-muted-foreground"
    >
      <Spinner aria-hidden="true" />
      正在读取候选工单
    </div>
    <p
      v-else-if="failed"
      class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm"
      role="alert"
    >
      候选工单读取失败，当前无法判断有哪些可排工单。
      <NvButton
        size="sm"
        variant="outline"
        type="button"
        class="ml-2"
        @click="mes.refreshWorkOrders()"
        >重试</NvButton
      >
    </p>
    <p
      v-else-if="candidates.length === 0"
      class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground"
      role="status"
    >
      没有匹配的可排工单。该单可能尚未下达到 MES，或检索词与工单号不一致。
    </p>
    <NvRadioGroup
      v-else
      v-model="selected"
      :disabled="props.disabled"
      class="grid max-h-56 gap-2 overflow-y-auto rounded-md border p-2"
      aria-label="候选工单"
    >
      <NvRadioGroupItem
        v-for="candidate in candidates"
        :key="candidate.workOrderId"
        :value="candidate.workOrderId"
      >
        {{ candidateLabel(candidate) }}
      </NvRadioGroupItem>
    </NvRadioGroup>
  </NvField>
</template>
