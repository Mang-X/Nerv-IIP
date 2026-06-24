<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

/**
 * Screen — big-board segmented control (今日 / 近7天 / 近30天). A hairline-framed
 * track with a sliding cyan thumb that glides under the active label — the same
 * sliding-indicator language as the PC layer, not a hard background swap. The
 * thumb is measured from the active button so labels of any width line up.
 * Keyboard-driven (← / →) and exposed as a radio group via `v-model`.
 */
type Value = string | number

const model = defineModel<Value>()

const props = withDefaults(
  defineProps<{ options?: { label: string; value: Value }[] }>(),
  {
    options: () => [
      { label: '今日', value: 'today' },
      { label: '近7天', value: '7d' },
      { label: '近30天', value: '30d' },
    ],
  },
)

// default to the first option when unbound so a zero-prop render still highlights
if (model.value === undefined && props.options.length) {
  model.value = props.options[0].value
}

const root = ref<HTMLElement>()
const thumb = ref({ x: 0, w: 0, ready: false })

/** Measure the active segment and park the thumb under it. */
function sync() {
  const el = root.value?.querySelector<HTMLElement>('.sb-sg-opt.on')
  if (el) thumb.value = { x: el.offsetLeft, w: el.offsetWidth, ready: true }
}

onMounted(() => {
  nextTick(sync)
  window.addEventListener('resize', sync)
})
onBeforeUnmount(() => window.removeEventListener('resize', sync))
watch(() => model.value, () => nextTick(sync))

function move(dir: 1 | -1) {
  const i = props.options.findIndex(o => o.value === model.value)
  const next = (i + dir + props.options.length) % props.options.length
  model.value = props.options[next].value
}
</script>

<template>
  <div ref="root" class="sb-sg" role="radiogroup">
    <span
      class="sb-sg-thumb"
      :class="{ ready: thumb.ready }"
      :style="{ transform: `translateX(${thumb.x}px)`, width: `${thumb.w}px` }"
    />
    <button
      v-for="o in options"
      :key="String(o.value)"
      type="button"
      class="sb-sg-opt"
      role="radio"
      :class="{ on: o.value === model }"
      :aria-checked="o.value === model"
      :tabindex="o.value === model ? 0 : -1"
      @click="model = o.value"
      @keydown.right.prevent="move(1)"
      @keydown.left.prevent="move(-1)"
    >
      {{ o.label }}
    </button>
  </div>
</template>

<style scoped>
.sb-sg {
  position: relative;
  display: inline-flex;
  padding: 3px;
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.35);
}
/* sliding cyan thumb — the active fill glides between segments */
.sb-sg-thumb {
  position: absolute;
  top: 3px;
  bottom: 3px;
  left: 0;
  border-radius: calc(var(--sb-radius) - 2px);
  background: linear-gradient(180deg, #19ecff, #00b8d4);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.35),
    0 0 10px rgba(0, 229, 255, 0.3);
  opacity: 0;
  pointer-events: none;
  transition: transform 0.28s var(--sb-ease-emphasized), width 0.28s var(--sb-ease-emphasized);
}
.sb-sg-thumb.ready {
  opacity: 1;
}
.sb-sg-opt {
  position: relative;
  z-index: 1;
  padding: 6px 16px;
  border: 0;
  background: transparent;
  color: var(--sb-muted);
  font-size: 13px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  cursor: pointer;
  transition: color 0.28s var(--sb-ease);
}
.sb-sg-opt:hover:not(.on) {
  color: var(--sb-text-2);
}
.sb-sg-opt.on {
  color: #04141a;
  font-weight: 600;
}
.sb-sg-opt:focus-visible {
  outline: none;
  border-radius: calc(var(--sb-radius) - 2px);
  box-shadow: 0 0 0 2px var(--sb-bg), 0 0 0 4px var(--sb-cyan-dim);
}

@media (prefers-reduced-motion: reduce) {
  .sb-sg-thumb,
  .sb-sg-opt {
    transition: none;
  }
}
</style>
