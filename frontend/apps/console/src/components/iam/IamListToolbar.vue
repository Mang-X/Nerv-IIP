<script setup lang="ts">
import {
  Button,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@nerv-iip/ui'
import { SearchIcon } from 'lucide-vue-next'
import { computed } from 'vue'

export interface IamListToolbarStatusOption {
  label: string
  value: string
}

const props = withDefaults(
  defineProps<{
    actionLabel: string
    searchLabel?: string
    searchPlaceholder?: string
    showStatusFilter?: boolean
    statusOptions?: IamListToolbarStatusOption[]
  }>(),
  {
    searchLabel: 'Search',
    searchPlaceholder: 'Search',
    showStatusFilter: false,
    statusOptions: () => [
      { label: 'Enabled', value: 'enabled' },
      { label: 'Disabled', value: 'disabled' },
    ],
  },
)

const emit = defineEmits<{
  action: []
}>()

const search = defineModel<string>('search', { default: '' })
const status = defineModel<string>('status', { default: '' })

const selectedStatus = computed({
  get: () => status.value || 'all',
  set: (value: string) => {
    status.value = value === 'all' ? '' : value
  },
})
</script>

<template>
  <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
    <div class="flex flex-1 flex-col gap-2 sm:max-w-xl sm:flex-row">
      <div class="relative min-w-0 flex-1">
        <SearchIcon
          class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          v-model="search"
          class="pl-8"
          :aria-label="props.searchLabel"
          :placeholder="props.searchPlaceholder"
          type="search"
        />
      </div>

      <Select v-if="props.showStatusFilter" v-model="selectedStatus">
        <SelectTrigger class="w-full sm:w-36" aria-label="Filter by status">
          <SelectValue placeholder="Status" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all"> All statuses </SelectItem>
          <SelectItem
            v-for="option in props.statusOptions"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </SelectItem>
        </SelectContent>
      </Select>
    </div>

    <Button type="button" @click="emit('action')">
      {{ props.actionLabel }}
    </Button>
  </div>
</template>
