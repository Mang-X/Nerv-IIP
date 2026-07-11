<script setup lang="ts">
import { describeRequestError } from '@/api/request-timeout'
import { NvMobileErrorRetry } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

/**
 * App-level classification adapter for the shared presentation panel
 * `NvMobileErrorRetry` (in `@nerv-iip/ui-mobile`).
 *
 * The panel UI (border/tone, `NvMobileButton`, touch sizing) lives in the mobile kit;
 * this adapter's ONLY job is the app/request-layer concern of turning a raw query
 * `error` into display copy via `describeRequestError`. Pages pass their raw `error` +
 * a page-specific fallback and never touch the classifier or the presentation markup.
 */
const props = defineProps<{
  error: unknown
  pending?: boolean
  fallback?: string
  testId?: string
}>()

const emit = defineEmits<{ retry: [] }>()

const message = computed(() => describeRequestError(props.error, props.fallback).message)
</script>

<template>
  <NvMobileErrorRetry
    :message="message"
    :pending="pending"
    :test-id="testId"
    @retry="emit('retry')"
  />
</template>
