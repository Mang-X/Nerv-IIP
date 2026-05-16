<script setup lang="ts">
withDefaults(defineProps<{
  disabled?: boolean
  type?: 'button' | 'submit' | 'reset'
  variant?: 'primary' | 'secondary' | 'danger'
}>(), {
  disabled: false,
  type: 'button',
  variant: 'primary'
})
</script>

<template>
  <button
    class="ui-button"
    :class="`ui-button--${variant}`"
    :disabled="disabled"
    :type="type"
  >
    <slot />
  </button>
</template>

<style scoped>
.ui-button {
  align-items: center;
  border: 1px solid transparent;
  border-radius: 0.5rem;
  cursor: pointer;
  display: inline-flex;
  font: inherit;
  font-size: 0.875rem;
  font-weight: 700;
  gap: 0.45rem;
  justify-content: center;
  letter-spacing: 0;
  line-height: 1.2;
  min-height: 2.25rem;
  padding: 0.55rem 0.85rem;
  transition:
    background-color 150ms ease,
    border-color 150ms ease,
    box-shadow 150ms ease,
    color 150ms ease,
    transform 150ms ease;
  user-select: none;
  white-space: nowrap;
}

.ui-button:hover:not(:disabled) {
  transform: translateY(-1px);
}

.ui-button:focus-visible {
  box-shadow: 0 0 0 3px rgb(37 99 235 / 0.25);
  outline: none;
}

.ui-button:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.ui-button--primary {
  background: #2563eb;
  color: #ffffff;
}

.ui-button--primary:hover:not(:disabled) {
  background: #1d4ed8;
}

.ui-button--secondary {
  background: #ffffff;
  border-color: #cbd5e1;
  color: #334155;
}

.ui-button--secondary:hover:not(:disabled) {
  background: #f8fafc;
  border-color: #94a3b8;
}

.ui-button--danger {
  background: #dc2626;
  color: #ffffff;
}

.ui-button--danger:hover:not(:disabled) {
  background: #b91c1c;
}
</style>
