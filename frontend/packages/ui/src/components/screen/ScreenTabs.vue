<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — big-board tabs. A row of labels over a hairline baseline; the active
 * tab glows cyan and a single underline ink slides beneath it (driven by index,
 * so the travel is smooth). Arrow keys move between tabs. Built on the
 * independent `--sb-*` tokens via `v-model`.
 */
type Value = string | number

const model = defineModel<Value>()

const props = withDefaults(
  defineProps<{ items?: { label: string; value: Value }[] }>(),
  {
    items: () => [
      { label: '产量趋势', value: 'output' },
      { label: '质量分析', value: 'quality' },
      { label: '能耗监控', value: 'energy' },
      { label: '设备状态', value: 'device' },
    ],
  },
)

if (model.value === undefined && props.items.length) {
  model.value = props.items[0].value
}

const activeIndex = computed(() =>
  Math.max(0, props.items.findIndex(t => t.value === model.value)),
)

function move(dir: 1 | -1) {
  const next = (activeIndex.value + dir + props.items.length) % props.items.length
  model.value = props.items[next].value
}
</script>

<template>
  <div class="sb-tb" role="tablist">
    <button
      v-for="t in items"
      :key="String(t.value)"
      type="button"
      class="sb-tb-item"
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
      class="sb-tb-ink"
      :style="{
        width: `${100 / Math.max(1, items.length)}%`,
        transform: `translateX(${activeIndex * 100}%)`,
      }"
    />
  </div>
</template>

<style scoped>
.sb-tb {
  position: relative;
  display: flex;
  border-bottom: 1px solid var(--sb-line);
}
.sb-tb-item {
  flex: 1;
  padding: 9px 4px 11px;
  border: 0;
  background: transparent;
  color: var(--sb-muted);
  font-size: 14px;
  font-weight: 500;
  white-space: nowrap;
  cursor: pointer;
  transition: color 0.2s var(--sb-ease);
}
.sb-tb-item:hover:not(.on) {
  color: var(--sb-text-2);
}
.sb-tb-item.on {
  color: var(--sb-cyan);
  font-weight: 600;
}
.sb-tb-item:focus-visible {
  outline: none;
  border-radius: 4px;
  box-shadow: inset 0 0 0 2px var(--sb-cyan-dim);
}
/* sliding underline ink */
.sb-tb-ink {
  position: absolute;
  left: 0;
  bottom: -1px;
  height: 2px;
  border-radius: 2px;
  background: var(--sb-cyan);
  box-shadow: 0 0 10px var(--sb-cyan-dim);
  transition: transform 0.28s var(--sb-ease-emphasized), width 0.28s var(--sb-ease-emphasized);
}

@media (prefers-reduced-motion: reduce) {
  .sb-tb-item,
  .sb-tb-ink {
    transition: none;
  }
}
</style>
