<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

defineProps<{
  code: string
}>()
</script>

<template>
  <Badge variant="secondary" class="font-mono">
    {{ code }}
  </Badge>
</template>
