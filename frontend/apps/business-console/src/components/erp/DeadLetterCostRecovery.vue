<script setup lang="ts">
import type { IntegrationEventDeadLetterDetailResponse } from '@nerv-iip/api-client'
import { computed } from 'vue'
import { readCostRecoveryContext, recoveryUrl } from './costRecovery'

const props = defineProps<{
  detail: IntegrationEventDeadLetterDetailResponse
  service: string
  receiptNo: string
  receiptWorkOrderId: string
}>()
const context = computed(() => readCostRecoveryContext(props.detail))
const receiptNo = computed(() =>
  context.value?.workOrderId === props.receiptWorkOrderId ? props.receiptNo : '',
)
const machineFactMissing = computed(
  () => props.detail.failureCode === 'unavailable-machine-time-fact',
)
const maintenanceLink = computed(() => {
  const value = context.value
  if (!value?.workCenterId || machineFactMissing.value) return undefined
  const path =
    props.detail.failureCode === 'missing-work-center-cost-rate'
      ? '/erp/finance/work-center-cost-rates'
      : '/erp/finance/machine-overhead-rates'
  return recoveryUrl(path, {
    ...value,
    service: props.service,
    deadLetterId: props.detail.id ?? '',
    receiptNo: receiptNo.value,
  })
})
</script>

<template>
  <section class="grid gap-2 rounded-md border p-3" aria-label="成本修复前提">
    <h3 class="font-semibold">修复前提与业务核实</h3>
    <template v-if="context">
      <p>工单 {{ context.workOrderId }} · 工序 {{ context.operationTaskId || '未提供' }}</p>
      <p v-if="context.workCenterId">工作中心 {{ context.workCenterId }}</p>
      <p v-if="context.atUtc">计价时点 {{ context.atUtc }}</p>
      <p v-if="machineFactMissing">
        机器事实已在工序完工时冻结；不能通过事后派工或补录改变原事件。请核对原工单的设备与工时记录，缺失事实需由业务负责人核实处置；重放同一事件不能补齐事实。
      </p>
      <template v-else>
        <p v-if="detail.failureCode === 'missing-work-center-cost-rate'">
          费率生效期间须覆盖原计价时点；只补今天的费率不能修复历史结算。
        </p>
        <p v-else>
          须有唯一覆盖完工日期的会计期间，并维护该期间的机器制造费用率；期间不能从月份编码推断，请以财务期间为准。
        </p>
        <a v-if="maintenanceLink" :href="maintenanceLink" class="text-brand hover:underline">{{
          detail.failureCode === 'missing-work-center-cost-rate'
            ? '去补充工作中心费率'
            : '去补充机器制造费用率'
        }}</a>
        <p v-else>原事件未提供工作中心，无法自动定位费率维护对象。</p>
      </template>
      <RouterLink
        :to="`/mes/work-orders/${encodeURIComponent(context.workOrderId)}`"
        class="text-brand hover:underline"
        >核实原工单与工序记录</RouterLink
      >
      <a
        :href="recoveryUrl('/mes/receipts', { workOrderId: context.workOrderId, receiptNo })"
        class="text-brand hover:underline"
        >查看原完工登记与入库终态</a
      >
      <p class="text-muted-foreground">
        补齐前提后返回本事件重放。重放回执不代表成本归集或入库完成，请刷新原登记核对成本回传及入库状态。
      </p>
    </template>
    <p v-else>原事件未提供可恢复的工单上下文，请依据事件内容核实对象，不能直接补录或派工。</p>
  </section>
</template>
