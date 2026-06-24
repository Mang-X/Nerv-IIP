<script setup lang="ts">
/**
 * Screen — big-board button. Three weights on the dark surface: `primary` is a
 * cyan gradient with a soft outer glow and an inset top highlight; `secondary`
 * is an indigo hairline over a faint tint; `ghost` is a bare hairline. Focus
 * lands a high-contrast cyan ring, and a press nudges the face down a hair (no
 * bounce — restraint over flourish). Built on the independent `--sb-*` tokens.
 */
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost'
    disabled?: boolean
    /** Forwarded to the native button so it can submit / reset a form. */
    type?: 'button' | 'submit' | 'reset'
  }>(),
  {
    variant: 'primary',
    disabled: false,
    type: 'button',
  },
)
</script>

<template>
  <button
    class="sb-btn"
    :class="variant"
    :type="type"
    :disabled="disabled"
    :aria-disabled="disabled || undefined"
  >
    <span class="sb-btn-label"><slot>确定</slot></span>
  </button>
</template>

<style scoped>
.sb-btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  height: 38px;
  padding: 0 20px;
  border: 1px solid transparent;
  border-radius: var(--sb-radius);
  font-size: 14px;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--sb-text);
  cursor: pointer;
  user-select: none;
  white-space: nowrap;
  transition:
    transform 0.12s var(--sb-ease),
    box-shadow 0.18s var(--sb-ease),
    border-color 0.18s var(--sb-ease),
    background 0.18s var(--sb-ease);
}
.sb-btn-label {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.sb-btn:active:not(:disabled) {
  transform: translateY(1px);
}
.sb-btn:focus-visible {
  outline: none;
  border-color: var(--sb-cyan);
  box-shadow:
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim),
    0 0 12px rgba(0, 229, 255, 0.4);
}
.sb-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

/* primary — cyan gradient, outer glow + inset top highlight */
.sb-btn.primary {
  background: linear-gradient(180deg, #19ecff, #00b8d4);
  color: #04141a;
  border-color: rgba(0, 229, 255, 0.5);
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.4),
    0 0 14px rgba(0, 229, 255, 0.35);
  text-shadow: 0 1px 0 rgba(255, 255, 255, 0.18);
}
.sb-btn.primary:hover:not(:disabled) {
  box-shadow:
    inset 0 1px 0 rgba(255, 255, 255, 0.45),
    0 0 20px rgba(0, 229, 255, 0.5);
}

/* secondary — indigo hairline over a faint tint */
.sb-btn.secondary {
  background: rgba(167, 139, 250, 0.08);
  border-color: rgba(167, 139, 250, 0.45);
  color: var(--sb-indigo);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
}
.sb-btn.secondary:hover:not(:disabled) {
  background: rgba(167, 139, 250, 0.14);
  border-color: rgba(167, 139, 250, 0.7);
}

/* ghost — bare hairline */
.sb-btn.ghost {
  background: transparent;
  border-color: var(--sb-line-2);
  color: var(--sb-text-2);
}
.sb-btn.ghost:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.04);
  border-color: var(--sb-cyan-dim);
  color: var(--sb-text);
}

@media (prefers-reduced-motion: reduce) {
  .sb-btn {
    transition: none;
  }
  .sb-btn:active:not(:disabled) {
    transform: none;
  }
}
</style>
