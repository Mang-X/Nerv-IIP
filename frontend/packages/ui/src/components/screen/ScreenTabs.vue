<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — big-board tabs. A row of labels over a hairline baseline; the active
 * tab glows cyan and a single underline ink slides beneath it (driven by index,
 * so the travel is smooth). Arrow keys move between tabs. Built on the
 * independent `--nv-scr-*` tokens via `v-model`.
 */
type Value = string | number

const model = defineModel<Value>()

const props = withDefaults(defineProps<{ items?: { label: string; value: Value }[] }>(), {
  items: () => [
    { label: '产量趋势', value: 'output' },
    { label: '质量分析', value: 'quality' },
    { label: '能耗监控', value: 'energy' },
    { label: '设备状态', value: 'device' },
  ],
})

if (model.value === undefined && props.items.length) {
  model.value = props.items[0].value
}

const activeIndex = computed(() =>
  Math.max(
    0,
    props.items.findIndex((t) => t.value === model.value),
  ),
)

function move(dir: 1 | -1) {
  const next = (activeIndex.value + dir + props.items.length) % props.items.length
  model.value = props.items[next].value
}
</script>

<template>
  <div class="nv-scr-tb" role="tablist">
    <button
      v-for="t in items"
      :key="String(t.value)"
      type="button"
      class="nv-scr-tb-item"
      role="tab"
      :class="{ on: t.value === model }"
      :aria-selected="t.value === model"
      :tabindex="t.value === model ? 0 : -1"
      @click="model = t.value"
      @keydown.right.prevent="move(1)"
      @keydown.left.prevent="move(-1)"
    >
      {{ t.label }}
    </button>
    <span
      class="nv-scr-tb-ink"
      :style="{
        width: `${100 / Math.max(1, items.length)}%`,
        transform: `translateX(${activeIndex * 100}%)`,
      }"
    />
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-tb {
    position: relative;
    display: flex;
    border-bottom: 1px solid var(--nv-scr-line);
  }
  .nv-scr-tb-item {
    flex: 1;
    padding: 9px 4px 11px;
    border: 0;
    background: transparent;
    color: var(--nv-scr-muted);
    font-size: 14px;
    font-weight: 500;
    white-space: nowrap;
    cursor: pointer;
    transition: color 0.2s var(--nv-scr-ease);
  }
  .nv-scr-tb-item:hover:not(.on) {
    color: var(--nv-scr-text-2);
  }
  .nv-scr-tb-item.on {
    color: var(--nv-scr-cyan);
    font-weight: 600;
  }
  .nv-scr-tb-item:focus-visible {
    outline: none;
    border-radius: 4px;
    box-shadow: inset 0 0 0 2px var(--nv-scr-cyan-dim);
  }
  /* sliding underline ink */
  .nv-scr-tb-ink {
    position: absolute;
    left: 0;
    bottom: -1px;
    height: 2px;
    border-radius: 2px;
    background: var(--nv-scr-cyan);
    box-shadow: 0 0 10px var(--nv-scr-cyan-dim);
    transition:
      transform 0.28s var(--nv-scr-ease-emphasized),
      width 0.28s var(--nv-scr-ease-emphasized);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-tb-item,
    .nv-scr-tb-ink {
      transition: none;
    }
  }
}
</style>
