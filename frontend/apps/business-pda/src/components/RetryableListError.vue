<script setup lang="ts">
import { describeRequestError } from '@/api/request-timeout'
import { computed } from 'vue'

/**
 * Shared list-load error panel for every PDA read page (WMS / equipment / MES lists).
 *
 * A read (GET) is idempotent, so a retry is always safe — this panel therefore ALWAYS
 * offers a retry. It classifies the error via `describeRequestError`, so timeout /
 * offline / network drops surface actionable copy ("网络超时…" / "当前离线…") instead of
 * one generic string. Centralising the markup here stops the panel (and its error
 * classification, a11y and retry semantics) from drifting across pages — the reason it
 * was extracted from the six pages that had copied it.
 */
const props = defineProps<{
  /** The raw query error (from a composable's `error` ref). */
  error: unknown
  /** Disable the retry button while a refetch is in flight. */
  pending?: boolean
  /** Page-specific copy for a business/unknown error without a usable server message. */
  fallback?: string
  /** Override the root `data-testid` so existing page tests keep their anchor. */
  testId?: string
}>()

const emit = defineEmits<{ retry: [] }>()

const message = computed(() => describeRequestError(props.error, props.fallback).message)
</script>

<template>
  <div
    :data-testid="testId ?? 'list-error'"
    role="alert"
    class="space-y-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
  >
    <p class="text-destructive">{{ message }}</p>
    <button
      type="button"
      data-testid="retry-list"
      :disabled="pending"
      class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
      @click="emit('retry')"
    >
      重试
    </button>
  </div>
</template>
