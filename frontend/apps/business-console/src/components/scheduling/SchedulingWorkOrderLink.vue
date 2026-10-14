<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'

const props = defineProps<{
  workOrderId?: string | null
  operationTaskId?: string | null
}>()
const auth = useAuthStore()
const target = computed(() => {
  if (!props.workOrderId || !auth.principal?.permissionCodes?.includes(P.mesWorkOrdersRead))
    return undefined
  return {
    path: `/mes/work-orders/${encodeURIComponent(props.workOrderId)}`,
    query: props.operationTaskId ? { operationTaskId: props.operationTaskId } : {},
  }
})
</script>

<template>
  <RouterLink v-if="target" :to="target" class="text-primary hover:underline" @click.stop>
    <slot>{{ workOrderId }}</slot>
  </RouterLink>
  <span v-else
    ><slot>{{ workOrderId }}</slot></span
  >
</template>
