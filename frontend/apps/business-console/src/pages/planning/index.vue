<script setup lang="ts">
import PlanningWorkbench from '@/components/planning/PlanningWorkbench.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '需求与计划',
    requiredPermissions: [
      'business.planning.demands.read',
      'business.planning.mrp.read',
      'business.planning.suggestions.manage',
    ],
  },
})
</script>

<template>
  <BusinessLayout>
    <PlanningWorkbench />
  </BusinessLayout>
</template>
