<script setup lang="ts">
/**
 * Screen — big-board toggle. Off is a sunken hairline track; on fills cyan with
 * a soft glow and slides the handle across. The press squashes the handle a hair
 * (no bounce). State is carried by both the fill and the handle position — never
 * color alone — and `aria-checked` keeps it honest for assistive tech. Built on
 * the independent `--sb-*` tokens via `v-model` (boolean).
 */
const model = defineModel<boolean>({ default: false })

withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false })

function toggle() {
  model.value = !model.value
}
function onKey(e: KeyboardEvent) {
  if (e.key === ' ' || e.key === 'Enter') {
    e.preventDefault()
    toggle()
  }
}
</script>

<template>
  <button
    type="button"
    class="sb-sw"
    role="switch"
    :class="{ on: model }"
    :aria-checked="model"
    :disabled="disabled"
    @click="toggle"
    @keydown="onKey"
  >
    <span class="sb-sw-track">
      <span class="sb-sw-handle" />
    </span>
  </button>
</template>

<style scoped>
.sb-sw {
  display: inline-flex;
  padding: 0;
  border: 0;
  background: none;
  cursor: pointer;
  border-radius: 999px;
}
.sb-sw:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
.sb-sw-track {
  position: relative;
  display: block;
  width: 46px;
  height: 24px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.5), inset 0 0 0 1px var(--sb-line-2);
  transition: background 0.22s var(--sb-ease), box-shadow 0.22s var(--sb-ease);
}
.sb-sw-handle {
  position: absolute;
  top: 3px;
  left: 3px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: linear-gradient(180deg, #f4f8fb, #c6d2dc);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.55), inset 0 1px 0 rgba(255, 255, 255, 0.7);
  transition: transform 0.22s var(--sb-ease);
}
.sb-sw.on .sb-sw-track {
  background: linear-gradient(180deg, #00c9e6, #009fbd);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.35),
    0 0 12px rgba(0, 229, 255, 0.45),
    inset 0 0 0 1px rgba(0, 229, 255, 0.55);
}
.sb-sw.on .sb-sw-handle {
  transform: translateX(22px);
  background: linear-gradient(180deg, #ffffff, #d8eef4);
}
/* press — shrink the handle a hair, no bounce (unified with SwitchPro's scale 0.86) */
.sb-sw:active:not(:disabled) .sb-sw-handle {
  transform: scale(0.86);
}
.sb-sw.on:active:not(:disabled) .sb-sw-handle {
  transform: translateX(22px) scale(0.86);
}
.sb-sw:focus-visible {
  outline: none;
}
.sb-sw:focus-visible .sb-sw-track {
  box-shadow:
    inset 0 0 0 1px var(--sb-line-2),
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim);
}
.sb-sw.on:focus-visible .sb-sw-track {
  box-shadow:
    inset 0 0 0 1px rgba(0, 229, 255, 0.55),
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim);
}

@media (prefers-reduced-motion: reduce) {
  .sb-sw-track,
  .sb-sw-handle {
    transition: none;
  }
}
</style>
