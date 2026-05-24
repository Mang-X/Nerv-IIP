<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.skus',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="MasterData"
      route-name="SKUs"
      summary="SKU list, create, edit, and resource selection entry point for #166."
    />
  </BusinessLayout>
</template>
