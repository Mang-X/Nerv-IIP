<script setup lang="ts">
import PlanningWorkbench from '@/components/planning/PlanningWorkbench.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.planning',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlanningWorkbench />
  </BusinessLayout>
</template>
