<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import { ArrowDown, Loader2 } from 'lucide-vue-next'
import { cn, rubberband } from '../../lib/utils'

/**
 * Mobile PullRefresh — pull down at the top of its own scroll area to refresh
 * (Vant / tdesign-mobile style). Pointer-driven with resistance; releasing past
 * the threshold emits `refresh` and shows a spinner until `v-model` clears.
 */
const props = withDefaults(defineProps<{ threshold?: number; class?: HTMLAttributes['class'] }>(), {
  threshold: 56,
})
const emit = defineEmits<{ refresh: [] }>()
const loading = defineModel<boolean>({ default: false })

const scroller = ref<HTMLElement>()
const distance = ref(0)
const dragging = ref(false)
let startY = 0
let engaged = false

const status = computed<'pull' | 'release' | 'loading'>(() => {
  if (loading.value) return 'loading'
  return distance.value >= props.threshold ? 'release' : 'pull'
})
const text = computed(
  () => ({ pull: '下拉刷新', release: '释放立即刷新', loading: '刷新中…' })[status.value],
)

function onDown(e: PointerEvent) {
  if ((scroller.value?.scrollTop ?? 0) > 0 || loading.value) return
  startY = e.clientY
  engaged = false
}
function onMove(e: PointerEvent) {
  if (e.buttons === 0 || loading.value) return
  if ((scroller.value?.scrollTop ?? 0) > 0) return
  const dy = e.clientY - startY
  if (dy <= 0) return
  if (!engaged && dy < 4) return
  engaged = true
  dragging.value = true
  e.preventDefault()
  distance.value = rubberband(dy) // resistance (shared rubber-band curve)
}
function onUp() {
  if (!engaged) return
  dragging.value = false
  engaged = false
  if (distance.value >= props.threshold) {
    distance.value = props.threshold
    loading.value = true
    emit('refresh')
  } else {
    distance.value = 0
  }
}

watch(loading, (v) => {
  if (!v) distance.value = 0
})
</script>

<template>
  <div data-slot="pull-refresh" :class="cn('relative overflow-hidden', $props.class)">
    <div
      class="ds-pr-inner h-full"
      :class="!dragging && 'ds-pr-snap'"
      :style="{ transform: `translateY(${distance}px)` }"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointercancel="onUp"
    >
      <div
        class="pointer-events-none absolute inset-x-0 flex items-center justify-center gap-2 text-sm text-muted-foreground"
        :style="{ top: `-${threshold}px`, height: `${threshold}px` }"
      >
        <Loader2
          v-if="status === 'loading'"
          class="size-4 animate-spin text-brand"
          aria-hidden="true"
        />
        <ArrowDown
          v-else
          class="ds-pr-arrow size-4 transition-transform"
          :class="status === 'release' && 'rotate-180'"
          aria-hidden="true"
        />
        {{ text }}
      </div>
      <div ref="scroller" class="ds-pr-scroll h-full overflow-y-auto">
        <slot />
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-pr-scroll {
    touch-action: pan-y;
  }
  .ds-pr-snap {
    transition: transform 0.3s var(--nv-ease-out-expo);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-pr-snap {
      transition: none;
    }
  }
}
</style>
