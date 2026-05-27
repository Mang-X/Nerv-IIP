<script setup lang="ts">
import {
  Pagination,
  PaginationContent,
  PaginationNext,
  PaginationPrevious,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    page: number
    pageSize: string
    pageSizeOptions?: string[]
    totalItems: number
  }>(),
  {
    pageSizeOptions: () => ['10', '20', '50'],
  },
)

const emit = defineEmits<{
  'update:page': [value: number]
  'update:pageSize': [value: string]
}>()

const pageSizeNumber = computed(() => Number(props.pageSize) || 10)
const totalPages = computed(() => Math.max(1, Math.ceil(props.totalItems / pageSizeNumber.value)))
const summary = computed(() => {
  if (props.totalItems <= 0) return '0 条'
  const start = (props.page - 1) * pageSizeNumber.value + 1
  const end = Math.min(props.page * pageSizeNumber.value, props.totalItems)
  return `${start}-${end} / ${props.totalItems} 条`
})

function updatePage(value: number) {
  emit('update:page', Math.min(Math.max(1, value), totalPages.value))
}

function updatePageSize(value: unknown) {
  if (typeof value !== 'string') return
  emit('update:pageSize', value)
  emit('update:page', 1)
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3">
    <p class="text-sm text-muted-foreground">显示 {{ summary }}</p>
    <div class="flex flex-wrap items-center gap-3">
      <div class="flex items-center gap-2">
        <span class="text-sm text-muted-foreground">每页</span>
        <Select :model-value="pageSize" @update:model-value="updatePageSize">
          <SelectTrigger class="h-8 w-24">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in pageSizeOptions" :key="option" :value="option">
              {{ option }} 条
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <Pagination
        :items-per-page="pageSizeNumber"
        :page="page"
        :sibling-count="1"
        :total="totalItems"
        show-edges
        @update:page="updatePage"
      >
        <PaginationContent>
          <PaginationPrevious size="sm">上一页</PaginationPrevious>
          <span class="px-2 text-sm text-muted-foreground">
            {{ page }} / {{ totalPages }}
          </span>
          <PaginationNext size="sm">下一页</PaginationNext>
        </PaginationContent>
      </Pagination>
    </div>
  </div>
</template>
