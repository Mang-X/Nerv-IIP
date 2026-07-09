<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { nextTick, onMounted, ref, watch } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Tabs — top content tabs with a brand underline that SLIDES between
 * tabs (Vant / tdesign-mobile style). Horizontally scrollable; v-model selects.
 */
export interface MobileTabItem {
  value: string
  label: string
}

const props = defineProps<{
  items: MobileTabItem[]
  class?: HTMLAttributes['class']
}>()
const model = defineModel<string>({ required: true })

const navEl = ref<HTMLElement>()
const indicator = ref({ left: 0, width: 0, ready: false })

function measure() {
  const active = navEl.value?.querySelector<HTMLElement>('[aria-selected=true]')
  if (!active) return
  const w = 24
  indicator.value = {
    left: active.offsetLeft + (active.offsetWidth - w) / 2,
    width: w,
    ready: true,
  }
}
watch([() => model.value, () => props.items], () => nextTick(measure), { deep: true })
onMounted(() => nextTick(measure))
</script>

<template>
  <div
    ref="navEl"
    data-slot="mobile-tabs"
    role="tablist"
    :class="
      cn(
        'relative flex overflow-x-auto border-b border-border [&::-webkit-scrollbar]:hidden [scrollbar-width:none]',
        $props.class,
      )
    "
  >
    <button
      v-for="item in items"
      :key="item.value"
      type="button"
      role="tab"
      :aria-selected="model === item.value"
      :class="
        cn(
          'ds-mtab relative h-11 shrink-0 px-4 text-[15px] whitespace-nowrap transition-colors',
          model === item.value ? 'font-medium text-foreground' : 'text-muted-foreground',
        )
      "
      @click="model = item.value"
    >
      {{ item.label }}
    </button>
    <span
      v-show="indicator.ready"
      class="ds-mtab-bar"
      :style="{ left: `${indicator.left}px`, width: `${indicator.width}px` }"
      aria-hidden="true"
    />
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-mtab {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .ds-mtab-bar {
    position: absolute;
    bottom: 0;
    height: 2px;
    border-radius: 9999px;
    background: var(--nv-brand);
    transition:
      left 0.28s var(--nv-ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1)),
      width 0.28s var(--nv-ease-out-expo, cubic-bezier(0.16, 1, 0.3, 1));
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-mtab-bar {
      transition: none;
    }
  }
}
</style>
