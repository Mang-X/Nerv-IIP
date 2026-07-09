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

const props = withDefaults(defineProps<{ options?: { label: string; value: Value }[] }>(), {
  options: () => [
    { label: '今日', value: 'today' },
    { label: '近7天', value: '7d' },
    { label: '近30天', value: '30d' },
  ],
})

// default to the first option when unbound so a zero-prop render still highlights
if (model.value === undefined && props.options.length) {
  model.value = props.options[0].value
}

const root = ref<HTMLElement>()
const thumb = ref({ x: 0, w: 0, ready: false })

/** Measure the active segment and park the thumb under it. */
function sync() {
  const el = root.value?.querySelector<HTMLElement>('.nv-scr-sg-opt.on')
  if (el) thumb.value = { x: el.offsetLeft, w: el.offsetWidth, ready: true }
}

onMounted(() => {
  nextTick(sync)
  window.addEventListener('resize', sync)
})
onBeforeUnmount(() => window.removeEventListener('resize', sync))
watch(
  () => model.value,
  () => nextTick(sync),
)

function move(dir: 1 | -1) {
  const i = props.options.findIndex((o) => o.value === model.value)
  const next = (i + dir + props.options.length) % props.options.length
  model.value = props.options[next].value
}
</script>

<template>
  <div ref="root" class="nv-scr-sg" role="radiogroup">
    <span
      class="nv-scr-sg-thumb"
      :class="{ ready: thumb.ready }"
      :style="{ transform: `translateX(${thumb.x}px)`, width: `${thumb.w}px` }"
    />
    <button
      v-for="o in options"
      :key="String(o.value)"
      type="button"
      class="nv-scr-sg-opt"
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
@layer nv-components {
  .nv-scr-sg {
    position: relative;
    display: inline-flex;
    padding: 3px;
    border-radius: var(--nv-scr-radius);
    background: rgba(255, 255, 255, 0.03);
    border: 1px solid var(--nv-scr-line-2);
    box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.35);
  }
  /* sliding cyan thumb — the active fill glides between segments */
  .nv-scr-sg-thumb {
    position: absolute;
    top: 3px;
    bottom: 3px;
    left: 0;
    border-radius: calc(var(--nv-scr-radius) - 2px);
    background: var(--nv-scr-accent-fill);
    box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.3);
    opacity: 0;
    pointer-events: none;
    transition:
      transform 0.28s var(--nv-scr-ease-emphasized),
      width 0.28s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-sg-thumb.ready {
    opacity: 1;
  }
  .nv-scr-sg-opt {
    position: relative;
    z-index: 1;
    padding: 6px 16px;
    border: 0;
    background: transparent;
    color: var(--nv-scr-muted);
    font-size: 13px;
    font-weight: 500;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
    cursor: pointer;
    transition: color 0.28s var(--nv-scr-ease);
  }
  .nv-scr-sg-opt:hover:not(.on) {
    color: var(--nv-scr-text-2);
  }
  .nv-scr-sg-opt.on {
    color: #04141a;
    font-weight: 600;
  }
  .nv-scr-sg-opt:focus-visible {
    outline: none;
    border-radius: calc(var(--nv-scr-radius) - 2px);
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-sg-thumb,
    .nv-scr-sg-opt {
      transition: none;
    }
  }
}
</style>
