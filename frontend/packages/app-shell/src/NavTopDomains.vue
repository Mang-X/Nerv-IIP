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
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

const props = withDefaults(
  defineProps<{
    domains: NavDomain[]
    currentDomainId?: string
    /** Optional hard cap on visible tabs. Unset = purely width-driven. */
    maxVisible?: number
  }>(),
  {},
)

const GAP = 4 // matches gap-1

const containerRef = ref<HTMLElement>()
const measureRef = ref<HTMLElement>()
/** How many tabs fit by width (before applying the optional maxVisible cap). */
const fitCount = ref(props.domains.length)

const visibleCount = computed(() =>
  Math.max(0, Math.min(fitCount.value, props.maxVisible ?? Number.POSITIVE_INFINITY)),
)
const visible = computed(() => props.domains.slice(0, visibleCount.value))
const overflow = computed(() => props.domains.slice(visibleCount.value))
const overflowActive = computed(() => overflow.value.some((d) => d.id === props.currentDomainId))

function recompute() {
  const container = containerRef.value
  const measure = measureRef.value
  if (!container || !measure) return
  const available = container.clientWidth
  const children = Array.from(measure.children) as HTMLElement[]
  if (children.length <= 1) return
  const moreWidth = children[children.length - 1].offsetWidth
  const itemEls = children.slice(0, props.domains.length)

  // In jsdom/SSR widths are 0 → available 0 → show everything (cap still applies).
  let total = 0
  for (let i = 0; i < itemEls.length; i++) total += itemEls[i].offsetWidth + (i > 0 ? GAP : 0)
  if (total <= available) {
    fitCount.value = props.domains.length
    return
  }

  // Overflow needed → reserve room for the "更多" button.
  let used = 0
  let count = 0
  for (let i = 0; i < itemEls.length; i++) {
    const w = itemEls[i].offsetWidth + (i > 0 ? GAP : 0)
    if (used + w + GAP + moreWidth <= available) {
      used += w
      count++
    }
    else {
      break
    }
  }
  fitCount.value = count
}

let observer: ResizeObserver | undefined
onMounted(() => {
  recompute()
  if (typeof ResizeObserver !== 'undefined' && containerRef.value) {
    observer = new ResizeObserver(() => recompute())
    observer.observe(containerRef.value)
  }
  // Tab widths change once the web font loads.
  if (typeof document !== 'undefined' && document.fonts?.ready) {
    document.fonts.ready.then(recompute).catch(() => {})
  }
})
onBeforeUnmount(() => observer?.disconnect())
watch(() => props.domains, () => { fitCount.value = props.domains.length; recompute() }, { flush: 'post' })

const tabClass = (active: boolean) =>
  cn(
    'inline-flex h-8 shrink-0 items-center gap-1.5 rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors',
    active
      ? 'bg-accent text-accent-foreground'
      : 'text-muted-foreground hover:bg-accent/60 hover:text-foreground',
  )
</script>

<template>
  <div ref="containerRef" class="relative flex min-w-0 items-center gap-1 overflow-hidden">
    <!-- Off-flow measurement layer: every tab + the "更多" button at intrinsic width.
         Hidden via inline style so it never depends on a Tailwind class being generated. -->
    <div
      ref="measureRef"
      class="flex items-center gap-1"
      :style="{ position: 'absolute', top: '0', left: '0', visibility: 'hidden', pointerEvents: 'none', whiteSpace: 'nowrap' }"
      aria-hidden="true"
    >
      <span v-for="domain in domains" :key="domain.id" :class="tabClass(false)">
        <component :is="domain.icon" v-if="domain.icon" class="size-4" />
        <span>{{ domain.title }}</span>
      </span>
      <span class="inline-flex h-8 shrink-0 items-center gap-1 px-3 text-sm font-medium">
        更多<ChevronDownIcon class="size-4" />
      </span>
    </div>

    <nav class="flex min-w-0 items-center gap-1" aria-label="一级能力区">
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
            :class="cn('h-8 shrink-0 gap-1', overflowActive ? 'text-foreground' : 'text-muted-foreground')"
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
  </div>
</template>
