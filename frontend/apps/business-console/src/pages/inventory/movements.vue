<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.movements',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="Inventory"
      route-name="Movements"
      summary="Inventory movement posting entry point for #167."
    />
  </BusinessLayout>
</template>
