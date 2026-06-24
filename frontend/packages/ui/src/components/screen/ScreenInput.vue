<script setup lang="ts">
/**
 * Screen — big-board text field. A sunken dark well that lights a cyan ring on
 * focus; an `error` state swaps the hairline and ring to red (and flags
 * `aria-invalid`) so the state never rides on color alone. An optional `suffix`
 * pins a unit (件 / kWh / %) to the right. Built on the independent `--sb-*`
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
  <div class="sb-in" :class="{ error, disabled }">
    <input
      v-model="model"
      class="sb-in-field"
      type="text"
      :placeholder="placeholder"
      :disabled="disabled"
      :aria-label="ariaLabel"
      :aria-invalid="error || undefined"
    >
    <span v-if="suffix" class="sb-in-suffix">{{ suffix }}</span>
  </div>
</template>

<style scoped>
.sb-in {
  display: inline-flex;
  align-items: center;
  width: 100%;
  height: 38px;
  padding: 0 12px;
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.4);
  font-variant-numeric: tabular-nums;
  transition: border-color 0.18s var(--sb-ease), box-shadow 0.18s var(--sb-ease);
}
.sb-in:focus-within {
  border-color: var(--sb-cyan);
  box-shadow:
    inset 0 1px 2px rgba(0, 0, 0, 0.4),
    0 0 0 3px var(--sb-cyan-dim);
}
.sb-in.error {
  border-color: var(--sb-red);
}
.sb-in.error:focus-within {
  box-shadow:
    inset 0 1px 2px rgba(0, 0, 0, 0.4),
    0 0 0 3px rgba(255, 23, 68, 0.4);
}
.sb-in.disabled {
  opacity: 0.5;
}
.sb-in-field {
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
.sb-in-field::placeholder {
  color: var(--sb-faint);
}
.sb-in-field:disabled {
  cursor: not-allowed;
}
.sb-in-suffix {
  flex: none;
  margin-left: 8px;
  padding-left: 10px;
  border-left: 1px solid var(--sb-line);
  font-size: 13px;
  color: var(--sb-muted);
  white-space: nowrap;
}

@media (prefers-reduced-motion: reduce) {
  .sb-in {
    transition: none;
  }
}
</style>
