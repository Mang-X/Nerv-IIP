<script setup lang="ts">
import { Check, ChevronDown } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * Screen — big-board select. A dark trigger that drops a glowing panel of
 * options; the chosen row reads cyan with a check, and the open panel lifts on a
 * cyan-tinted shadow. Keyboard-driven (↑ / ↓ / Enter / Esc) and wired with
 * listbox semantics. Closes on outside click. Built on the independent `--sb-*`
 * tokens via `v-model`.
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
const active = ref(-1)

const selected = computed(() => props.options.find(o => o.value === model.value))

function toggle() {
  if (props.disabled) return
  open.value = !open.value
  if (open.value) {
    active.value = props.options.findIndex(o => o.value === model.value)
  }
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
function onDocClick(e: MouseEvent) {
  if (root.value && !root.value.contains(e.target as Node)) open.value = false
}

onMounted(() => document.addEventListener('click', onDocClick))
onBeforeUnmount(() => document.removeEventListener('click', onDocClick))
</script>

<template>
  <div ref="root" class="sb-sel" :class="{ disabled, open }">
    <button
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

    <ul v-if="open" class="sb-sel-panel" role="listbox">
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
    0 0 0 3px var(--sb-cyan-dim),
    0 0 12px rgba(0, 229, 255, 0.3);
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

.sb-sel-panel {
  position: absolute;
  top: calc(100% + 6px);
  left: 0;
  right: 0;
  z-index: 30;
  margin: 0;
  padding: 5px;
  list-style: none;
  max-height: 264px;
  overflow-y: auto;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid rgba(0, 229, 255, 0.25);
  box-shadow:
    0 12px 32px -8px rgba(0, 0, 0, 0.7),
    0 0 18px rgba(0, 229, 255, 0.12),
    inset 0 1px 0 rgba(255, 255, 255, 0.045);
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
