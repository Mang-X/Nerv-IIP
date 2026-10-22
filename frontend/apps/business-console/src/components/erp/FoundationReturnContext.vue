<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { queryText } from './costRecovery'
const route = useRoute()
const returnUrl = computed(() => {
  const query = new URLSearchParams()
  for (const field of ['siteCode', 'lineCode', 'workCenterCode', 'skuId', 'productionVersionId']) {
    const value = queryText(route.query, `foundation${field[0]!.toUpperCase()}${field.slice(1)}`)
    if (value) query.set(field, value)
  }
  return `/mes/foundation?${query}`
})
</script>
<template>
  <section v-if="route.query.fromFoundation === '1'" class="my-3 rounded-md border p-3 text-sm">
    <p>来自生产准备检查；维护完成后返回原范围，重新检查权威结果。</p>
    <a :href="returnUrl" class="text-brand hover:underline">返回原准备检查</a>
  </section>
</template>
