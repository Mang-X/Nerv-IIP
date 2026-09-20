<script setup lang="ts">
import AndonWorkbench from '@/components/mes/AndonWorkbench.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '安灯响应',
    requiredPermissions: ['business.mes.operations.read'],
  },
})
</script>

<template>
  <BusinessLayout><AndonWorkbench /></BusinessLayout>
</template>
