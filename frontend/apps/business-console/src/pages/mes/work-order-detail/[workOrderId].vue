<script setup lang="ts">
import { onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单详情',
  },
})

const route = useRoute()
const router = useRouter()

onMounted(() => {
  const value = (route.params as Record<string, string | string[] | undefined>).workOrderId
  const workOrderId = Array.isArray(value) ? value[0] : value
  void router.replace({
    path: `/mes/work-orders/${encodeURIComponent(workOrderId ?? 'WO-001')}`,
    query: route.query,
  })
})
</script>

<template>
  <div />
</template>
