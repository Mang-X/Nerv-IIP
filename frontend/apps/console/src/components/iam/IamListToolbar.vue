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
          class="iam-list-toolbar__search pl-8"
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

    <Button type="button" class="iam-list-toolbar__primary-action" @click="emit('action')">
      {{ props.actionLabel }}
    </Button>
  </div>
</template>

<style scoped>
.iam-list-toolbar__primary-action {
  background: #0048b8;
  border-color: #0048b8;
  color: #fff;
}

.iam-list-toolbar__search:focus-visible {
  border-color: #2f6fd6;
  box-shadow: 0 0 0 3px rgb(47 111 214 / 35%);
}

@supports (color: oklch(0.49 0.17 255)) {
  .iam-list-toolbar__primary-action {
    background: var(--primary);
    border-color: var(--primary);
    color: var(--primary-foreground);
  }

  .iam-list-toolbar__search:focus-visible {
    border-color: var(--ring);
    box-shadow: 0 0 0 3px rgb(47 111 214 / 35%);
  }
}
</style>
