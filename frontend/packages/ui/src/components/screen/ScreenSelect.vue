<script setup lang="ts">
import { Check, ChevronDown } from 'lucide-vue-next'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

/**
 * Screen — big-board select. A dark trigger that drops a glowing panel of options
 * **teleported to <body>** so it's never clipped by a panel's `overflow:hidden`;
 * the panel is fixed-positioned under the trigger and re-anchors on scroll/resize.
 * The chosen row reads cyan with a check. Keyboard-driven (↑/↓/Enter/Esc), listbox
 * semantics, closes on outside click. Built on the independent `--sb-*` tokens.
 */
type Value = string | number

const model = defineModel<Value>()

const props = withDefaults(
  defineProps<{
    options?: { label: string; value: Value }[]
    placeholder?: string
    disabled?: boolean
  }>(),
  {
    placeholder: '请选择产线',
    disabled: false,
    options: () => [
      { label: '焊接线 A', value: 'line-a' },
      { label: '装配线 B', value: 'line-b' },
      { label: 'CNC 线 C', value: 'line-c' },
      { label: '涂装线 D', value: 'line-d' },
    ],
  },
)

const open = ref(false)
const root = ref<HTMLElement>()
const trigger = ref<HTMLElement>()
const panel = ref<HTMLElement>()
const active = ref(-1)
const pos = ref({ left: 0, top: 0, width: 0 })

const selected = computed(() => props.options.find(o => o.value === model.value))

/** Anchor the teleported panel just under the trigger, in viewport coords. */
function place() {
  const el = trigger.value
  if (!el) return
  const r = el.getBoundingClientRect()
  pos.value = { left: r.left, top: r.bottom + 6, width: r.width }
}
function toggle() {
  if (props.disabled) return
  open.value = !open.value
}
function pick(v: Value) {
  model.value = v
  open.value = false
}
function onKey(e: KeyboardEvent) {
  if (props.disabled) return
  if (e.key === 'Escape') {
    open.value = false
    return
  }
  if (!open.value && (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown')) {
    e.preventDefault()
    toggle()
    return
  }
  if (!open.value) return
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    active.value = Math.min(props.options.length - 1, active.value + 1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    active.value = Math.max(0, active.value - 1)
  } else if (e.key === 'Enter') {
    e.preventDefault()
    if (active.value >= 0) pick(props.options[active.value].value)
  }
}
function onDocPointer(e: MouseEvent) {
  const t = e.target as Node
  if (root.value?.contains(t) || panel.value?.contains(t)) return
  open.value = false
}

watch(open, (v) => {
  if (v) {
    place()
    active.value = props.options.findIndex(o => o.value === model.value)
    document.addEventListener('pointerdown', onDocPointer, true)
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
  } else {
    document.removeEventListener('pointerdown', onDocPointer, true)
    window.removeEventListener('scroll', place, true)
    window.removeEventListener('resize', place)
  }
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocPointer, true)
  window.removeEventListener('scroll', place, true)
  window.removeEventListener('resize', place)
})
</script>

<template>
  <div ref="root" class="sb-sel" :class="{ disabled, open }">
    <button
      ref="trigger"
      type="button"
      class="sb-sel-trigger"
      role="combobox"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :disabled="disabled"
      @click="toggle"
      @keydown="onKey"
    >
      <span class="sb-sel-value" :class="{ placeholder: !selected }">
        {{ selected ? selected.label : placeholder }}
      </span>
      <ChevronDown class="sb-sel-caret" :size="16" aria-hidden="true" />
    </button>

    <Teleport to="body">
      <ul
        v-if="open"
        ref="panel"
        class="sb-sel-panel sb-scroll"
        role="listbox"
        :style="{ left: `${pos.left}px`, top: `${pos.top}px`, width: `${pos.width}px` }"
      >
        <li
          v-for="(o, i) in options"
          :key="String(o.value)"
          class="sb-sel-opt"
          role="option"
          :class="{ on: o.value === model, active: i === active }"
          :aria-selected="o.value === model"
          @click="pick(o.value)"
          @mousemove="active = i"
        >
          <span>{{ o.label }}</span>
          <Check v-if="o.value === model" class="sb-sel-check" :size="15" />
        </li>
      </ul>
    </Teleport>
  </div>
</template>

<style scoped>
.sb-sel {
  position: relative;
  display: inline-block;
  width: 100%;
}
.sb-sel-trigger {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  width: 100%;
  height: 38px;
  padding: 0 12px;
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.4);
  color: var(--sb-text);
  font-size: 14px;
  font-family: inherit;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.18s var(--sb-ease), box-shadow 0.18s var(--sb-ease);
}
.sb-sel.open .sb-sel-trigger,
.sb-sel-trigger:focus-visible {
  outline: none;
  border-color: var(--sb-cyan);
  box-shadow:
    inset 0 1px 2px rgba(0, 0, 0, 0.4),
    0 0 0 3px var(--sb-cyan-dim);
}
.sb-sel.disabled {
  opacity: 0.5;
}
.sb-sel.disabled .sb-sel-trigger {
  cursor: not-allowed;
}
.sb-sel-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.sb-sel-value.placeholder {
  color: var(--sb-faint);
}
.sb-sel-caret {
  flex: none;
  color: var(--sb-muted);
  transition: transform 0.2s var(--sb-ease);
}
.sb-sel.open .sb-sel-caret {
  transform: rotate(180deg);
  color: var(--sb-cyan);
}

/* teleported to <body> — fixed so no ancestor overflow can clip it */
.sb-sel-panel {
  position: fixed;
  z-index: 1000;
  margin: 0;
  padding: 5px;
  list-style: none;
  max-height: 264px;
  overflow-y: auto;
  border-radius: var(--sb-radius);
  /* glass — translucent dark + blur, white highlight edge, no colored bloom */
  background: rgba(11, 15, 24, 0.72);
  backdrop-filter: blur(16px) saturate(1.3);
  -webkit-backdrop-filter: blur(16px) saturate(1.3);
  border: 1px solid rgba(255, 255, 255, 0.12);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.1),
    0 16px 40px -12px rgba(0, 0, 0, 0.85);
}
.sb-sel-opt {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 9px 11px;
  border-radius: calc(var(--sb-radius) - 3px);
  font-size: 14px;
  color: var(--sb-text-2);
  cursor: pointer;
}
.sb-sel-opt.active {
  background: rgba(255, 255, 255, 0.05);
  color: var(--sb-text);
}
.sb-sel-opt.on {
  color: var(--sb-cyan);
}
.sb-sel-opt.on.active {
  background: rgba(0, 229, 255, 0.1);
}
.sb-sel-check {
  flex: none;
  color: var(--sb-cyan);
  filter: drop-shadow(0 0 4px var(--sb-cyan-dim));
}

@media (prefers-reduced-motion: reduce) {
  .sb-sel-trigger,
  .sb-sel-caret {
    transition: none;
  }
}
</style>
