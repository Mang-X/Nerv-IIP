<script setup lang="ts">
import { ButtonPro, Empty, EmptyDescription, EmptyTitle, ScrollArea, StatusBadge } from '@nerv-iip/ui'
import { conflictReasonLabel } from '../../model/labels'
import type { UnscheduledItem } from '../../model/types'

defineProps<{ items: UnscheduledItem[] }>()
defineEmits<{ fix: [orderId: string, operationId: string] }>()
</script>

<template>
  <section class="flex h-full flex-col" aria-label="未排产">
    <header class="flex items-center justify-between px-3 py-2">
      <h3 class="text-sm font-semibold text-foreground">未排产工序</h3>
      <span class="text-xs text-muted-foreground">{{ items.length }} 项</span>
    </header>
    <Empty v-if="!items.length" class="py-8">
      <EmptyTitle>全部已排产</EmptyTitle>
      <EmptyDescription>所有工序均已安排到资源与时间。</EmptyDescription>
    </Empty>
    <ScrollArea v-else class="flex-1">
      <ul class="flex flex-col gap-1 p-2">
        <li
          v-for="item in items"
          :key="`${item.orderId}:${item.operationId}`"
          class="flex flex-col gap-1 rounded-md border border-border bg-card px-3 py-2"
        >
          <span class="flex items-center gap-2">
            <StatusBadge tone="warning" :label="conflictReasonLabel[item.reason]" />
            <span class="truncate text-xs text-muted-foreground">{{ item.orderId }}</span>
          </span>
          <span class="text-sm text-foreground">{{ item.message }}</span>
          <ButtonPro size="sm" variant="outline" class="mt-1 self-start" @click="$emit('fix', item.orderId, item.operationId)">
            去处理
          </ButtonPro>
        </li>
      </ul>
    </ScrollArea>
  </section>
</template>
