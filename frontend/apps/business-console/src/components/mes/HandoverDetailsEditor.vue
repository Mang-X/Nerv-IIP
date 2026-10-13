<script setup lang="ts">
import type {
  BusinessConsoleMesShiftHandoverWipItem,
  BusinessConsoleMesShiftHandoverUnfinishedWorkOrder,
} from '@nerv-iip/api-client'
import {
  NvButton,
  NvField,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvDataTable,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'
import { useMesWorkOrders, useMesWorkOrderDetail } from '@/composables/useBusinessMes'
import MesWorkScopeSelect from './MesWorkScopeSelect.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useMesReferenceLabels } from '@/composables/mes/useMesReferenceLabels'
const { statusLabel } = useMesReferenceLabels()
import { notifyError } from '@/utils/notify'

const props = defineProps<{ organizationId: string; environmentId: string; disabled: boolean }>()
const wipItems = defineModel<BusinessConsoleMesShiftHandoverWipItem[]>('wipItems', {
  required: true,
})
const unfinishedWorkOrders = defineModel<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder[]>(
  'unfinishedWorkOrders',
  { required: true },
)
const orders = useMesWorkOrders()
const order = useMesWorkOrderDetail()
const selectedWorkOrderId = shallowRef('')
const selectedOperationId = shallowRef('order')
const countedQuantity = shallowRef('')
const showQuantityError = shallowRef(false)
const keyword = computed({
  get: () => orders.filters.keyword ?? '',
  set: (value: string) => {
    orders.filters.keyword = value
    orders.filters.skip = 0
  },
})
const page = computed(() => Math.floor(orders.filters.skip / orders.filters.take) + 1)
const listReady = computed(
  () =>
    orders.workOrderReadScopeReady.value &&
    !orders.workOrdersPending.value &&
    !orders.workOrdersError.value,
)
const detail = computed(() =>
  listReady.value &&
  !order.detailPending.value &&
  !order.detailError.value &&
  order.detail.value?.workOrderId === selectedWorkOrderId.value
    ? order.detail.value
    : undefined,
)
const operations = computed(() => detail.value?.operationTasks ?? [])
const quantityValid = computed(
  () =>
    countedQuantity.value.trim() !== '' &&
    Number.isFinite(Number(countedQuantity.value)) &&
    Number(countedQuantity.value) >= 0,
)
const canConfirmUnfinished = computed(() => {
  const item = detail.value
  return (
    item &&
    ['created', 'released', 'started', 'hold'].includes((item.status ?? '').toLowerCase()) &&
    item.quantity != null &&
    item.quantity > 0 &&
    item.completedQuantity != null &&
    item.completedQuantity >= 0 &&
    item.completedQuantity < item.quantity
  )
})

function clearDetails() {
  selectedOperationId.value = 'order'
  countedQuantity.value = ''
  showQuantityError.value = false
  wipItems.value = []
  unfinishedWorkOrders.value = []
}
watch(
  () => [
    props.organizationId,
    props.environmentId,
    orders.workOrderReadScope.value?.kind,
    orders.workOrderReadScope.value?.id,
  ],
  () => {
    selectedWorkOrderId.value = ''
    clearDetails()
    orders.filters.organizationId = props.organizationId
    orders.filters.environmentId = props.environmentId
    order.filters.organizationId = props.organizationId
    order.filters.environmentId = props.environmentId
  },
  { immediate: true, flush: 'sync' },
)
watch(
  selectedWorkOrderId,
  (id) => {
    clearDetails()
    order.filters.workOrderId = id
  },
  { flush: 'sync' },
)

function addWip() {
  showQuantityError.value = true
  if (!detail.value || !quantityValid.value || props.disabled) return
  if (
    selectedOperationId.value !== 'order' &&
    !operations.value.some((item) => item.operationTaskId === selectedOperationId.value)
  )
    return
  wipItems.value = [
    ...wipItems.value,
    {
      workOrderId: selectedWorkOrderId.value,
      operationTaskId: selectedOperationId.value === 'order' ? null : selectedOperationId.value,
      quantity: Number(countedQuantity.value),
    },
  ]
  countedQuantity.value = ''
  showQuantityError.value = false
}
function confirmUnfinished() {
  const item = detail.value
  if (!item || !canConfirmUnfinished.value || props.disabled) return
  unfinishedWorkOrders.value = [
    {
      workOrderId: selectedWorkOrderId.value,
      plannedQuantity: item.quantity!,
      completedQuantity: item.completedQuantity!,
      workOrderStatus: item.status!,
    },
  ]
}
async function retryRead() {
  try {
    await Promise.all([orders.refreshWorkOrders(), order.refreshDetail()])
  } catch (error) {
    notifyError(error, '工单读取失败，请重试。')
  }
}
const wipColumns = [
  {
    key: 'operationTaskId',
    header: '工序',
    accessor: (row: BusinessConsoleMesShiftHandoverWipItem) => row.operationTaskId || '按工单登记',
  },
  { key: 'quantity', header: '清点数量' },
  { key: 'actions', header: '操作' },
]
</script>

<template>
  <section class="grid gap-3">
    <FormSectionTitle>交班明细</FormSectionTitle>
    <p class="text-xs text-muted-foreground">
      按需选择工单、人工清点在制数量并确认未完进度。切换工单或作业范围会清除已添加明细；未结问题不会自动带入。
    </p>
    <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
    <p v-if="!orders.workOrderReadScopeReady.value" class="text-sm text-muted-foreground">
      {{ orders.workOrderReadScopeMessage.value }}
    </p>
    <NvField>
      <NvFieldLabel for="handover-work-order-search">查找工单</NvFieldLabel>
      <NvInput
        id="handover-work-order-search"
        v-model="keyword"
        placeholder="输入工单号"
        :disabled="disabled"
      />
    </NvField>
    <p v-if="orders.workOrdersPending.value" class="text-sm text-muted-foreground">正在读取工单…</p>
    <div
      v-else-if="orders.workOrdersError.value || order.detailError.value"
      class="flex items-center gap-2"
    >
      <span class="text-sm text-muted-foreground">工单读取失败，请重试。</span>
      <NvButton
        type="button"
        variant="outline"
        data-testid="handover-retry-work-orders"
        :disabled="disabled"
        @click="retryRead"
        >重试读取</NvButton
      >
    </div>
    <template v-else-if="listReady">
      <NvField>
        <NvFieldLabel for="handover-work-order-trigger">工单</NvFieldLabel>
        <NvSelect
          v-model="selectedWorkOrderId"
          data-testid="handover-work-order"
          :disabled="disabled"
        >
          <NvSelectTrigger id="handover-work-order-trigger" aria-label="选择交班工单"
            ><NvSelectValue placeholder="选择工单"
          /></NvSelectTrigger>
          <NvSelectContent
            ><NvSelectItem
              v-for="item in orders.workOrders.value"
              :key="item.workOrderId"
              :value="item.workOrderId!"
              >{{ item.workOrderId }}</NvSelectItem
            ></NvSelectContent
          >
        </NvSelect>
      </NvField>
      <p v-if="!orders.workOrders.value.length" class="text-sm text-muted-foreground">
        当前范围没有符合查询的工单，可调整查询后重试。
      </p>
      <div class="flex items-center justify-between text-sm">
        <span>第 {{ page }} 页 · 共 {{ orders.workOrdersTotal.value }} 条</span>
        <div class="flex gap-2">
          <NvButton
            type="button"
            variant="outline"
            :disabled="disabled || orders.filters.skip === 0"
            @click="orders.filters.skip = Math.max(0, orders.filters.skip - orders.filters.take)"
            >上一页</NvButton
          >
          <NvButton
            type="button"
            variant="outline"
            :disabled="
              disabled || orders.filters.skip + orders.filters.take >= orders.workOrdersTotal.value
            "
            @click="orders.filters.skip += orders.filters.take"
            >下一页</NvButton
          >
        </div>
      </div>
      <p v-if="order.detailPending.value" class="text-sm text-muted-foreground">
        正在读取工单工序及进度…
      </p>
      <template v-else-if="detail">
        <NvField>
          <NvFieldLabel for="handover-operation-trigger">在制工序</NvFieldLabel>
          <NvSelect
            v-model="selectedOperationId"
            data-testid="handover-operation"
            :disabled="disabled"
          >
            <NvSelectTrigger id="handover-operation-trigger" aria-label="选择在制工序"
              ><NvSelectValue placeholder="按工单登记"
            /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem value="order">按工单登记</NvSelectItem>
              <NvSelectItem
                v-for="item in operations"
                :key="item.operationTaskId"
                :value="item.operationTaskId!"
                >{{ item.operationCode || item.operationTaskNo || item.operationTaskId }} ·
                {{ item.workCenterName || item.workCenterCode }}</NvSelectItem
              >
            </NvSelectContent>
          </NvSelect>
        </NvField>
        <NvField>
          <NvFieldLabel for="handover-wip-quantity">在制清点数量</NvFieldLabel>
          <NvInput
            id="handover-wip-quantity"
            v-model="countedQuantity"
            data-testid="handover-wip-quantity"
            type="number"
            min="0"
            step="any"
            :disabled="disabled"
            :invalid="showQuantityError && !quantityValid"
          />
          <p v-if="showQuantityError && !quantityValid" class="text-xs text-destructive">
            请输入不小于零的人工清点数量。
          </p>
        </NvField>
        <NvButton
          type="button"
          variant="outline"
          data-testid="handover-add-wip"
          :disabled="disabled"
          @click="addWip"
          >添加在制明细</NvButton
        >
        <div class="grid gap-2 rounded-md border p-3 text-sm">
          <p>
            计划 {{ detail.quantity }} · 完成 {{ detail.completedQuantity }} ·
            {{ statusLabel(detail.status) }}
          </p>
          <NvButton
            type="button"
            variant="outline"
            data-testid="handover-confirm-unfinished"
            :disabled="disabled || !canConfirmUnfinished"
            @click="confirmUnfinished"
            >确认当前未完进度并添加</NvButton
          >
          <p v-if="!canConfirmUnfinished" class="text-xs text-muted-foreground">
            该工单当前不属于未完工单。
          </p>
          <p v-if="unfinishedWorkOrders.length" data-testid="handover-confirmed-progress">
            已确认：计划 {{ unfinishedWorkOrders[0]?.plannedQuantity }} · 完成
            {{ unfinishedWorkOrders[0]?.completedQuantity }} ·
            {{ statusLabel(unfinishedWorkOrders[0]?.workOrderStatus) }}
          </p>
          <NvButton
            v-if="unfinishedWorkOrders.length"
            type="button"
            variant="ghost"
            :disabled="disabled"
            @click="unfinishedWorkOrders = []"
            >移除未完明细</NvButton
          >
        </div>
      </template>
    </template>
    <NvDataTable
      v-if="wipItems.length"
      :columns="wipColumns"
      :rows="wipItems"
      :row-key="(row) => String(wipItems.indexOf(row))"
      data-testid="handover-added-wip"
    >
      <template #cell-actions="{ row }"
        ><NvButton
          type="button"
          variant="ghost"
          :disabled="disabled"
          @click="wipItems = wipItems.filter((item) => item !== row)"
          >移除</NvButton
        ></template
      >
    </NvDataTable>
  </section>
</template>
