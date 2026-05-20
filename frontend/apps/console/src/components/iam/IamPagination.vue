<script setup lang="ts">
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  pageIndex: number
  pageSize: number
  totalCount: number
}>()

const emit = defineEmits<{
  pageChange: [pageIndex: number]
}>()

const pageCount = computed(() => Math.max(1, Math.ceil(props.totalCount / props.pageSize)))
const firstItem = computed(() =>
  props.totalCount === 0 ? 0 : (props.pageIndex - 1) * props.pageSize + 1,
)
const lastItem = computed(() => Math.min(props.pageIndex * props.pageSize, props.totalCount))

function changePage(page: number) {
  if (page === props.pageIndex || page < 1 || page > pageCount.value) {
    return
  }

  emit('pageChange', page)
}
</script>

<template>
  <div
    v-if="props.totalCount > props.pageSize"
    class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
  >
    <p class="text-sm text-muted-foreground">
      Showing {{ firstItem }}-{{ lastItem }} of {{ props.totalCount }}
    </p>

    <Pagination
      :page="props.pageIndex"
      :items-per-page="props.pageSize"
      :total="props.totalCount"
      class="mx-0 w-auto justify-start sm:justify-end"
      @update:page="changePage"
    >
      <PaginationContent v-slot="{ items }">
        <PaginationPrevious />
        <template
          v-for="(item, index) in items"
          :key="item.type === 'page' ? `page-${item.value}` : `ellipsis-${index}`"
        >
          <PaginationItem
            v-if="item.type === 'page'"
            :value="item.value"
            :is-active="item.value === props.pageIndex"
            @click="changePage(item.value)"
          >
            {{ item.value }}
          </PaginationItem>
          <PaginationEllipsis v-else />
        </template>
        <PaginationNext />
      </PaginationContent>
    </Pagination>
  </div>
</template>
