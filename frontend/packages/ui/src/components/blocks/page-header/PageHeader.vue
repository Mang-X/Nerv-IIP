<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '../../ui/breadcrumb'
import { cn } from '../../../lib/utils'

export interface PageHeaderCrumb {
  label: string
  /** Optional link target. Omit for a non-navigable ancestor. */
  href?: string
}

defineProps<{
  /** Current page title — rendered as the last (current) breadcrumb. */
  title: string
  /** Ancestor breadcrumbs, in order. For SPA links use the #breadcrumbs slot instead. */
  breadcrumbs?: PageHeaderCrumb[]
  /** Optional count shown next to the title (e.g. result total). */
  count?: number | string
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <header
    :class="cn('flex min-w-0 flex-wrap items-center justify-between gap-x-4 gap-y-2', $props.class)"
  >
    <div class="flex min-w-0 items-center gap-2">
      <Breadcrumb>
        <BreadcrumbList>
          <slot name="breadcrumbs">
            <template v-for="crumb in breadcrumbs" :key="crumb.label">
              <BreadcrumbItem>
                <BreadcrumbLink v-if="crumb.href" :href="crumb.href">{{
                  crumb.label
                }}</BreadcrumbLink>
                <span v-else class="text-muted-foreground">{{ crumb.label }}</span>
              </BreadcrumbItem>
              <BreadcrumbSeparator />
            </template>
          </slot>
          <BreadcrumbItem>
            <BreadcrumbPage class="text-sm font-semibold text-foreground">{{
              title
            }}</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
      <span v-if="count !== undefined" class="shrink-0 text-sm tabular-nums text-muted-foreground">
        {{ count }}
      </span>
    </div>
    <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2">
      <slot name="actions" />
    </div>
  </header>
</template>
