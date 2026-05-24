<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.counts',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="Inventory"
      route-name="Counts"
      summary="Stock count task and adjustment entry point for #167."
    />
  </BusinessLayout>
</template>
