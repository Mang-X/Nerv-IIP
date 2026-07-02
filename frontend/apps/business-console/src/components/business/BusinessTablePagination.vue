<script setup lang="ts">
import {
  Pagination,
  PaginationContent,
  PaginationNext,
  PaginationPrevious,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
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
const currentPage = computed(() => Math.min(Math.max(1, props.page), totalPages.value))
const summary = computed(() => {
  if (props.totalItems <= 0) return '0 条'
  const start = (currentPage.value - 1) * pageSizeNumber.value + 1
  const end = Math.min(currentPage.value * pageSizeNumber.value, props.totalItems)
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
  <div class="flex min-w-0 flex-wrap items-center justify-between gap-3 border-t pt-3">
    <p class="min-w-0 truncate text-sm text-muted-foreground" aria-live="polite">
      显示 {{ summary }}
    </p>
    <div class="flex flex-wrap items-center gap-3">
      <div class="flex items-center gap-2">
        <span class="shrink-0 text-sm text-muted-foreground">每页</span>
        <SelectPro :model-value="pageSize" @update:model-value="updatePageSize">
          <SelectProTrigger class="h-8 w-24" aria-label="每页条数">
            <SelectProValue />
          </SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in pageSizeOptions" :key="option" :value="option">
              {{ option }} 条
            </SelectProItem>
          </SelectProContent>
        </SelectPro>
      </div>
      <Pagination
        :items-per-page="pageSizeNumber"
        :page="currentPage"
        :sibling-count="1"
        :total="totalItems"
        show-edges
        @update:page="updatePage"
      >
        <PaginationContent>
          <PaginationPrevious size="sm">上一页</PaginationPrevious>
          <span class="min-w-14 px-2 text-center text-sm text-muted-foreground" aria-label="当前页">
            {{ currentPage }} / {{ totalPages }}
          </span>
          <PaginationNext size="sm">下一页</PaginationNext>
        </PaginationContent>
      </Pagination>
    </div>
  </div>
</template>
