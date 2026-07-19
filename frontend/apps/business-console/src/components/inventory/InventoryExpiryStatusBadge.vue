<script setup lang="ts">
import type { ExpiryAlertLike } from '@nerv-iip/business-core'
import { expiryToneFromAlert, expiryToneLabel } from '@nerv-iip/business-core'
import { NvStatusBadge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{ line: ExpiryAlertLike }>()
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
  <NvStatusBadge :value="expiryTone" :label="label" :tone="statusTone" />
</template>
