<script setup lang="ts">
/**
 * Screen — big-board text field. A sunken dark well that lights a cyan ring on
 * focus; an `error` state swaps the hairline and ring to red (and flags
 * `aria-invalid`) so the state never rides on color alone. An optional `suffix`
 * pins a unit (件 / kWh / %) to the right. Built on the independent `--nv-scr-*`
 * tokens via `v-model` (string).
 */
const model = defineModel<string>({ default: '' })

withDefaults(
  defineProps<{
    error?: boolean
    placeholder?: string
    suffix?: string
    disabled?: boolean
    /** Accessible name when there's no visible <label>. */
    ariaLabel?: string
  }>(),
  {
    error: false,
    placeholder: '请输入',
    disabled: false,
  },
)
</script>

<template>
  <div class="nv-scr-in" :class="{ error, disabled }">
    <input
      v-model="model"
      class="nv-scr-in-field"
      type="text"
      :placeholder="placeholder"
      :disabled="disabled"
      :aria-label="ariaLabel"
      :aria-invalid="error || undefined"
    />
    <span v-if="suffix" class="nv-scr-in-suffix">{{ suffix }}</span>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-in {
    position: relative;
    display: inline-flex;
    align-items: center;
    width: 100%;
    height: 38px;
    padding: 0 12px;
    border-radius: var(--nv-scr-radius);
    background: rgba(255, 255, 255, 0.025);
    isolation: isolate;
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      inset 0 1px 2px rgba(0, 0, 0, 0.4);
    font-variant-numeric: tabular-nums;
    transition: box-shadow 0.18s var(--nv-scr-ease);
  }
  /* gradient hairline — brighter down the two sides + white top highlight (like the panels) */
  .nv-scr-in::before {
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
  .nv-scr-in > * {
    position: relative;
    z-index: 1;
  }
  .nv-scr-in:focus-within {
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      inset 0 1px 2px rgba(0, 0, 0, 0.4),
      0 0 0 3px var(--nv-scr-cyan-dim);
  }
  .nv-scr-in:focus-within::before {
    background: linear-gradient(
      90deg,
      var(--nv-scr-cyan),
      rgba(70, 150, 230, 0.55) 30%,
      rgba(70, 150, 230, 0.55) 70%,
      var(--nv-scr-cyan)
    );
  }
  .nv-scr-in.error::before {
    background: linear-gradient(
      90deg,
      var(--nv-scr-red),
      rgba(239, 90, 99, 0.5) 30%,
      rgba(239, 90, 99, 0.5) 70%,
      var(--nv-scr-red)
    );
  }
  .nv-scr-in.error:focus-within {
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      inset 0 1px 2px rgba(0, 0, 0, 0.4),
      0 0 0 3px rgba(255, 23, 68, 0.4);
  }
  .nv-scr-in.disabled {
    opacity: 0.5;
  }
  .nv-scr-in-field {
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
  .nv-scr-in-field::placeholder {
    color: var(--nv-scr-faint);
  }
  .nv-scr-in-field:disabled {
    cursor: not-allowed;
  }
  .nv-scr-in-suffix {
    flex: none;
    margin-left: 8px;
    padding-left: 10px;
    border-left: 1px solid var(--nv-scr-line);
    font-size: 13px;
    color: var(--nv-scr-muted);
    white-space: nowrap;
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-in {
      transition: none;
    }
  }
}
</style>
