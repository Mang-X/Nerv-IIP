<script setup lang="ts">
import NvMobileButton from '../button/MobileButton.vue'

/**
 * Pure-presentation list-load error panel — an already-classified message plus a retry.
 *
 * Deliberately free of any error-classification / request-layer coupling so it can live
 * in the shared mobile kit and be reused across WMS / equipment / MES read pages. The
 * caller passes the display `message` (classified upstream in the request/composable
 * layer) and owns the `retry` action. A read (GET) is idempotent, so retry is always
 * safe — the panel therefore always offers it.
 */
defineProps<{
  /** Display copy, already classified upstream (e.g. via the request layer). */
  message: string
  /** Disable retry while a refetch is in flight. */
  pending?: boolean
  /** Root data-testid so a page can keep its own anchor (defaults to 'list-error'). */
  testId?: string
}>()

const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    :data-testid="testId ?? 'list-error'"
    role="alert"
    class="space-y-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
  >
    <p class="text-destructive">{{ message }}</p>
    <NvMobileButton
      variant="outline"
      size="lg"
      block
      data-testid="retry-list"
      :disabled="pending"
      @click="emit('retry')"
    >
      重试
    </NvMobileButton>
  </div>
</template>
