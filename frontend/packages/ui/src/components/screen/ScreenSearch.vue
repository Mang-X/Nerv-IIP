<script setup lang="ts">
import { Search, X } from '@lucide/vue'

/**
 * Screen — big-board search box. A lucide search glyph anchors the left, the
 * well lights a cyan ring on focus, and once there's text a clear button (×)
 * appears to wipe it. Built on the independent `--nv-scr-*` tokens via `v-model`
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
  <div class="nv-scr-se" :class="{ disabled }">
    <Search class="nv-scr-se-icon" :size="16" aria-hidden="true" />
    <input
      v-model="model"
      class="nv-scr-se-field"
      type="search"
      :aria-label="ariaLabel"
      :placeholder="placeholder"
      :disabled="disabled"
    />
    <button v-if="model" type="button" class="nv-scr-se-clear" aria-label="清除" @click="clear">
      <X :size="14" />
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-se {
    position: relative;
    display: inline-flex;
    align-items: center;
    gap: 8px;
    width: 100%;
    height: 38px;
    padding: 0 10px;
    border-radius: var(--nv-scr-radius);
    background: rgba(255, 255, 255, 0.025);
    isolation: isolate;
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      inset 0 1px 2px rgba(0, 0, 0, 0.4);
    transition: box-shadow 0.18s var(--nv-scr-ease);
  }
  .nv-scr-se::before {
    content: '';
    position: absolute;
    inset: 0;
    border-radius: inherit;
    padding: 1px;
    background: var(--nv-scr-edge-gradient);
    -webkit-mask:
      linear-gradient(#000 0 0) content-box,
      linear-gradient(#000 0 0);
    -webkit-mask-composite: xor;
    mask-composite: exclude;
    pointer-events: none;
    z-index: 0;
    transition: background 0.18s var(--nv-scr-ease);
  }
  .nv-scr-se > * {
    position: relative;
    z-index: 1;
  }
  .nv-scr-se:focus-within {
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      inset 0 1px 2px rgba(0, 0, 0, 0.4),
      0 0 0 3px var(--nv-scr-cyan-dim);
  }
  .nv-scr-se:focus-within::before {
    background: linear-gradient(
      90deg,
      var(--nv-scr-cyan),
      rgba(70, 150, 230, 0.55) 30%,
      rgba(70, 150, 230, 0.55) 70%,
      var(--nv-scr-cyan)
    );
  }
  .nv-scr-se:focus-within .nv-scr-se-icon {
    color: var(--nv-scr-cyan);
  }
  .nv-scr-se.disabled {
    opacity: 0.5;
  }
  .nv-scr-se-icon {
    flex: none;
    color: var(--nv-scr-muted);
    transition: color 0.18s var(--nv-scr-ease);
  }
  .nv-scr-se-field {
    flex: 1;
    min-width: 0;
    height: 100%;
    border: 0;
    outline: none;
    background: transparent;
    color: var(--nv-scr-text);
    font-size: 14px;
    font-family: inherit;
  }
  .nv-scr-se-field::placeholder {
    color: var(--nv-scr-faint);
  }
  .nv-scr-se-field:disabled {
    cursor: not-allowed;
  }
  /* strip the native search clear so ours is the only one */
  .nv-scr-se-field::-webkit-search-decoration,
  .nv-scr-se-field::-webkit-search-cancel-button {
    appearance: none;
  }
  .nv-scr-se-clear {
    flex: none;
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    padding: 0;
    border: 0;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.06);
    color: var(--nv-scr-muted);
    cursor: pointer;
    transition:
      background 0.15s var(--nv-scr-ease),
      color 0.15s var(--nv-scr-ease),
      transform 0.15s var(--nv-scr-ease);
  }
  .nv-scr-se-clear:active {
    transform: scale(0.88);
  }
  .nv-scr-se-clear:hover {
    background: rgba(255, 255, 255, 0.12);
    color: var(--nv-scr-text);
  }
  .nv-scr-se-clear:focus-visible {
    outline: none;
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-se,
    .nv-scr-se-icon,
    .nv-scr-se-clear {
      transition: none;
    }
  }
}
</style>
