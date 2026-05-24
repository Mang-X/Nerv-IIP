<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.workOrders',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="MES"
      route-name="Work orders"
      summary="Work order list and rush-order entry point for #169."
    />
  </BusinessLayout>
</template>
