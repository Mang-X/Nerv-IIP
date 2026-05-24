<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.schedules',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="MES"
      route-name="Schedules"
      summary="Rule schedule run and result-status entry point for #169, without Gantt."
    />
  </BusinessLayout>
</template>
