<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.ncrs',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="Quality"
      route-name="NCRs"
      summary="Nonconformance list, disposition, and close entry point for #168."
    />
  </BusinessLayout>
</template>
