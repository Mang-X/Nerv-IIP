<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.availability',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="Inventory"
      route-name="Availability"
      summary="Inventory availability query entry point for #167."
    />
  </BusinessLayout>
</template>
