<script setup lang="ts">
import { Search, X } from 'lucide-vue-next'

/**
 * Screen — big-board search box. A lucide search glyph anchors the left, the
 * well lights a cyan ring on focus, and once there's text a clear button (×)
 * appears to wipe it. Built on the independent `--sb-*` tokens via `v-model`
 * (string).
 */
const model = defineModel<string>({ default: '' })

withDefaults(defineProps<{ placeholder?: string; disabled?: boolean; ariaLabel?: string }>(), {
  placeholder: '搜索工单号 / 产线 / 设备',
  disabled: false,
  ariaLabel: '搜索',
})

function clear() {
  model.value = ''
}
</script>

<template>
  <div class="sb-se" :class="{ disabled }">
    <Search class="sb-se-icon" :size="16" aria-hidden="true" />
    <input
      v-model="model"
      class="sb-se-field"
      type="search"
      :aria-label="ariaLabel"
      :placeholder="placeholder"
      :disabled="disabled"
    >
    <button
      v-if="model"
      type="button"
      class="sb-se-clear"
      aria-label="清除"
      @click="clear"
    >
      <X :size="14" />
    </button>
  </div>
</template>

<style scoped>
.sb-se {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 38px;
  padding: 0 10px;
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.4);
  transition: border-color 0.18s var(--sb-ease), box-shadow 0.18s var(--sb-ease);
}
.sb-se:focus-within {
  border-color: var(--sb-cyan);
  box-shadow:
    inset 0 1px 2px rgba(0, 0, 0, 0.4),
    0 0 0 3px var(--sb-cyan-dim),
    0 0 12px rgba(0, 229, 255, 0.3);
}
.sb-se:focus-within .sb-se-icon {
  color: var(--sb-cyan);
}
.sb-se.disabled {
  opacity: 0.5;
}
.sb-se-icon {
  flex: none;
  color: var(--sb-muted);
  transition: color 0.18s var(--sb-ease);
}
.sb-se-field {
  flex: 1;
  min-width: 0;
  height: 100%;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--sb-text);
  font-size: 14px;
  font-family: inherit;
}
.sb-se-field::placeholder {
  color: var(--sb-faint);
}
.sb-se-field:disabled {
  cursor: not-allowed;
}
/* strip the native search clear so ours is the only one */
.sb-se-field::-webkit-search-decoration,
.sb-se-field::-webkit-search-cancel-button {
  appearance: none;
}
.sb-se-clear {
  flex: none;
  display: grid;
  place-items: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 0;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.06);
  color: var(--sb-muted);
  cursor: pointer;
  transition: background 0.15s var(--sb-ease), color 0.15s var(--sb-ease), transform 0.15s var(--sb-ease);
}
.sb-se-clear:active {
  transform: scale(0.88);
}
.sb-se-clear:hover {
  background: rgba(255, 255, 255, 0.12);
  color: var(--sb-text);
}
.sb-se-clear:focus-visible {
  outline: none;
  box-shadow: 0 0 0 2px var(--sb-bg), 0 0 0 4px var(--sb-cyan-dim);
}

@media (prefers-reduced-motion: reduce) {
  .sb-se,
  .sb-se-icon,
  .sb-se-clear {
    transition: none;
  }
}
</style>
