<script setup lang="ts">
/**
 * Screen — big-board segmented control (今日 / 近7天 / 近30天). A hairline-framed
 * track of options where the active one fills cyan with a faint glow; an inset
 * divider rides between segments. Selection is keyboard-driven (← / →) and
 * exposed as a radio group. Built on the independent `--sb-*` tokens via
 * `v-model`.
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

function move(dir: 1 | -1) {
  const i = props.options.findIndex(o => o.value === model.value)
  const next = (i + dir + props.options.length) % props.options.length
  model.value = props.options[next].value
}
</script>

<template>
  <div class="sb-sg" role="radiogroup">
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
  display: inline-flex;
  padding: 3px;
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.35);
}
.sb-sg-opt {
  position: relative;
  padding: 6px 16px;
  border: 1px solid transparent;
  border-radius: calc(var(--sb-radius) - 2px);
  background: transparent;
  color: var(--sb-muted);
  font-size: 13px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  cursor: pointer;
  transition: color 0.18s var(--sb-ease), background 0.18s var(--sb-ease);
}
/* inset divider between segments (hidden next to the active one) */
.sb-sg-opt + .sb-sg-opt::before {
  content: '';
  position: absolute;
  left: -1px;
  top: 5px;
  bottom: 5px;
  width: 1px;
  background: var(--sb-line);
}
.sb-sg-opt:hover:not(.on) {
  color: var(--sb-text-2);
}
.sb-sg-opt.on {
  color: #04141a;
  font-weight: 600;
  background: linear-gradient(180deg, #19ecff, #00b8d4);
  border-color: rgba(0, 229, 255, 0.5);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.35),
    0 0 10px rgba(0, 229, 255, 0.3);
}
.sb-sg-opt.on::before,
.sb-sg-opt.on + .sb-sg-opt::before {
  opacity: 0;
}
.sb-sg-opt:focus-visible {
  outline: none;
  box-shadow: 0 0 0 2px var(--sb-bg), 0 0 0 4px var(--sb-cyan-dim);
}

@media (prefers-reduced-motion: reduce) {
  .sb-sg-opt {
    transition: none;
  }
}
</style>
