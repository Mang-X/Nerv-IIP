<script setup lang="ts">
import { Alert, AlertDescription } from '@nerv-iip/ui'

withDefaults(
  defineProps<{
    error?: string
    success?: string
  }>(),
  {
    error: '',
    success: '',
  },
)
</script>

<template>
  <Alert v-if="error" role="alert" variant="destructive">
    <AlertDescription>{{ error }}</AlertDescription>
  </Alert>
  <Alert v-else-if="success" role="status">
    <AlertDescription>{{ success }}</AlertDescription>
  </Alert>
</template>
