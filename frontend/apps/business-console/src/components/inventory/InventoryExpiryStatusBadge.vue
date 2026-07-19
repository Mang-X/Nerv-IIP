<script setup lang="ts">
import type { ExpiryAlertLike } from '@nerv-iip/business-core'
import { expiryToneFromAlert, expiryToneLabel } from '@nerv-iip/business-core'
import { NvStatusBadge } from '@nerv-iip/ui'
import { computed } from 'vue'

type ExpiryStatusLine = ExpiryAlertLike & { blockReason?: string | null }
const props = defineProps<{ line: ExpiryStatusLine }>()
const expiryTone = computed(() => expiryToneFromAlert(props.line))
const label = computed(() => expiryToneLabel(expiryTone.value))
const statusTone = computed(() => {
  if (expiryTone.value === 'fresh') return 'success'
  if (expiryTone.value === 'near') return 'warning'
  if (expiryTone.value) return 'danger'
  return 'neutral'
})
</script>

<template>
  <div class="flex min-w-32 flex-col items-start gap-1">
    <NvStatusBadge :value="expiryTone" :label="label" :tone="statusTone" />
    <span v-if="line.blockReason" class="text-xs leading-4 text-muted-foreground">
      {{ line.blockReason }}
    </span>
  </div>
</template>
