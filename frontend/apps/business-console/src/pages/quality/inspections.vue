<script setup lang="ts">
import PlannedPage from '@/components/business/PlannedPage.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.inspections',
  },
})
</script>

<template>
  <BusinessLayout>
    <PlannedPage
      domain="Quality"
      route-name="Inspections"
      summary="Inspection plan and inspection record entry point for #168."
    />
  </BusinessLayout>
</template>
