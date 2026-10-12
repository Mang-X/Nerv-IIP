<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { queryText, recoveryUrl } from './costRecovery'
const route = useRoute()
const context = computed(() => ({
  workOrderId: queryText(route.query, 'workOrderId'),
  operationTaskId: queryText(route.query, 'operationTaskId'),
  atUtc: queryText(route.query, 'atUtc'),
  receiptNo: queryText(route.query, 'receiptNo'),
  service: queryText(route.query, 'service'),
  deadLetterId: queryText(route.query, 'deadLetterId'),
}))
</script>
<template>
  <section
    v-if="context.workOrderId"
    class="my-3 grid gap-2 rounded-md border p-3 text-sm"
    aria-label="原完工等待项"
  >
    <p>原工单 {{ context.workOrderId }} · 工序 {{ context.operationTaskId || '未提供' }}</p>
    <p v-if="context.atUtc">原计价时点 {{ context.atUtc }}；请确保费率覆盖该时点。</p>
    <p v-if="context.receiptNo">完工登记 {{ context.receiptNo }}</p>
    <a
      v-if="context.service && context.deadLetterId"
      :href="recoveryUrl('/ops/dead-letters', context)"
      class="text-brand hover:underline"
      >返回等待事件</a
    >
    <a
      :href="
        recoveryUrl('/mes/receipts', {
          workOrderId: context.workOrderId,
          receiptNo: context.receiptNo,
        })
      "
      class="text-brand hover:underline"
      >返回原完工登记</a
    >
  </section>
</template>
