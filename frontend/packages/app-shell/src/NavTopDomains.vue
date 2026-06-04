<script setup lang="ts">
import type { NavDomain } from './types'
import { ChevronDownIcon } from 'lucide-vue-next'
import {
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  cn,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = withDefaults(
  defineProps<{
    domains: NavDomain[]
    currentDomainId?: string
    /** Top domains shown before the rest collapse into "更多". */
    maxVisible?: number
  }>(),
  { maxVisible: 7 },
)

const visible = computed(() => props.domains.slice(0, props.maxVisible))
const overflow = computed(() => props.domains.slice(props.maxVisible))
const overflowActive = computed(() => overflow.value.some((d) => d.id === props.currentDomainId))

const tabClass = (active: boolean) =>
  cn(
    'inline-flex h-8 shrink-0 items-center gap-1.5 rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors',
    active
      ? 'bg-accent text-accent-foreground'
      : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
  )
</script>

<template>
  <nav class="flex min-w-0 items-center gap-1 overflow-hidden" aria-label="一级能力区">
    <RouterLink
      v-for="domain in visible"
      :key="domain.id"
      :to="domain.to ?? ''"
      :class="tabClass(domain.id === currentDomainId)"
      :aria-current="domain.id === currentDomainId ? 'page' : undefined"
    >
      <component :is="domain.icon" v-if="domain.icon" class="size-4" aria-hidden="true" />
      <span>{{ domain.title }}</span>
    </RouterLink>

    <DropdownMenu v-if="overflow.length">
      <DropdownMenuTrigger as-child>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          :class="cn('h-8 gap-1', overflowActive ? 'text-foreground' : 'text-muted-foreground')"
        >
          更多
          <ChevronDownIcon class="size-4" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" class="w-52">
        <DropdownMenuItem
          v-for="domain in overflow"
          :key="domain.id"
          as-child
          :class="domain.id === currentDomainId ? 'bg-accent' : ''"
        >
          <RouterLink :to="domain.to ?? ''">
            <component :is="domain.icon" v-if="domain.icon" class="size-4" aria-hidden="true" />
            {{ domain.title }}
          </RouterLink>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </nav>
</template>
