<script setup lang="ts">
import type { UrgencyDisplayMode } from '@/composables/useUrgencyDisplayMode'
import { URGENCY_DISPLAY_MODES } from '@/composables/useUrgencyDisplayMode'
import {
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'

// MAN-590 / #1061: one shared urgency display-mode selector reused by the ERP
// sales order, DemandPlanning demand, MES work order and Scheduling assignment
// tables. Switching modes only changes presentation; it never triggers a backend
// recompute — the parent keeps the same urgency result and only re-labels badges.
const model = defineModel<UrgencyDisplayMode>({ required: true })

defineProps<{ label?: string }>()
</script>

<template>
  <div class="flex items-center gap-2">
    <span class="whitespace-nowrap text-sm text-muted-foreground">{{ label ?? '紧急度显示' }}</span>
    <NvSelect v-model="model">
      <NvSelectTrigger class="h-9 w-40" aria-label="紧急度显示模式">
        <NvSelectValue />
      </NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem v-for="mode in URGENCY_DISPLAY_MODES" :key="mode.value" :value="mode.value">
          {{ mode.label }}
        </NvSelectItem>
      </NvSelectContent>
    </NvSelect>
  </div>
</template>
