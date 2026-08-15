<script setup lang="ts">
import {
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  scopeKey?: string
  status?: string
  scopeOptions: DropdownOption[]
  statusOptions: DropdownOption[]
}>()

const emit = defineEmits<{
  'update:scopeKey': [value: string | undefined]
  'update:status': [value: string | undefined]
}>()

const scopeModel = computed<string | number | undefined>({
  get: () => props.scopeKey,
  set: (value) => emit('update:scopeKey', value ? String(value) : undefined),
})
const statusModel = computed<string | number | undefined>({
  get: () => props.status,
  set: (value) => emit('update:status', value ? String(value) : undefined),
})
</script>

<template>
  <NvMobileDropdownMenu>
    <NvMobileDropdownMenuItem
      v-if="scopeOptions.length"
      v-model="scopeModel"
      title="作业范围"
      :options="scopeOptions"
    />
    <NvMobileDropdownMenuItem v-model="statusModel" title="任务状态" :options="statusOptions" />
  </NvMobileDropdownMenu>
</template>
