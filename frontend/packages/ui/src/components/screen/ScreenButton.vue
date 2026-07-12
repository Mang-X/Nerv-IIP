<script setup lang="ts">
/**
 * Screen — big-board button. The same premium craft as the PC `NvButton` — a
 * layered surface with a white top highlight, a defined contact shadow (NOT an
 * outer glow), and a sheen that wipes across on hover — re-skinned to the dark
 * board palette. `primary` is a cyan gradient; `secondary` an indigo hairline;
 * `ghost` a bare hairline. Press is a pure scale, no shift. Built on `--nv-scr-*`.
 */
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost'
    /** `sm` is the compact height for table action cells / dense toolbars. */
    size?: 'default' | 'sm'
    disabled?: boolean
    /** Forwarded to the native button so it can submit / reset a form. */
    type?: 'button' | 'submit' | 'reset'
  }>(),
  {
    variant: 'primary',
    size: 'default',
    disabled: false,
    type: 'button',
  },
)
</script>

<template>
  <button
    class="nv-scr-btn"
    :class="[variant, size]"
    :type="type"
    :disabled="disabled"
    :aria-disabled="disabled || undefined"
  >
    <span class="nv-scr-btn-label"><slot>确定</slot></span>
    <span v-if="variant === 'primary'" class="nv-scr-btn-sheen" aria-hidden="true" />
  </button>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-btn {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    height: 38px;
    padding: 0 20px;
    border: 1px solid transparent;
    border-radius: var(--nv-scr-radius);
    font-size: 14px;
    font-weight: 600;
    letter-spacing: 0.01em;
    color: var(--nv-scr-text);
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
    overflow: hidden;
    transition:
      transform 0.12s var(--nv-scr-ease),
      box-shadow 0.18s var(--nv-scr-ease),
      border-color 0.18s var(--nv-scr-ease),
      background 0.18s var(--nv-scr-ease);
  }
  .nv-scr-btn.sm {
    height: 30px;
    padding: 0 12px;
    font-size: 13px;
    gap: 6px;
  }
  .nv-scr-btn-label {
    position: relative;
    z-index: 1;
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
  /* press — pure scale, no vertical nudge */
  .nv-scr-btn:active:not(:disabled) {
    transform: scale(0.985);
  }
  .nv-scr-btn:focus-visible {
    outline: none;
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }
  .nv-scr-btn:disabled {
    opacity: 0.45;
    cursor: not-allowed;
  }

  /* primary — cyan gradient, white top highlight + a defined contact shadow.
   Not the banned outer glow — shadow is dark and ≤4px. */
  .nv-scr-btn.primary {
    background: var(--nv-scr-accent-fill);
    color: #04203a;
    border-color: var(--nv-scr-accent-edge);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.4),
      0 1px 3px rgba(0, 0, 0, 0.5);
    text-shadow: 0 1px 0 rgba(255, 255, 255, 0.15);
  }
  .nv-scr-btn.primary:hover:not(:disabled) {
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.5),
      0 2px 7px rgba(0, 0, 0, 0.55);
  }
  /* a faint sheen that wipes across the solid fill on hover (same as NvButton) */
  .nv-scr-btn-sheen {
    position: absolute;
    inset: 0;
    border-radius: inherit;
    pointer-events: none;
    opacity: 0;
    background: linear-gradient(
      100deg,
      transparent 30%,
      rgba(255, 255, 255, 0.28) 50%,
      transparent 70%
    );
    background-size: 220% 100%;
    background-position: 120% 0;
    transition:
      opacity 0.2s ease,
      background-position 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-btn.primary:hover:not(:disabled) .nv-scr-btn-sheen {
    opacity: 1;
    background-position: -40% 0;
  }

  /* secondary — indigo hairline over a faint tint */
  .nv-scr-btn.secondary {
    background: rgba(139, 155, 230, 0.08);
    border-color: rgba(139, 155, 230, 0.4);
    color: var(--nv-scr-indigo);
    box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.05);
  }
  .nv-scr-btn.secondary:hover:not(:disabled) {
    background: rgba(139, 155, 230, 0.14);
    border-color: rgba(139, 155, 230, 0.65);
  }

  /* ghost — bare hairline */
  .nv-scr-btn.ghost {
    background: transparent;
    border-color: var(--nv-scr-line-2);
    color: var(--nv-scr-text-2);
  }
  .nv-scr-btn.ghost:hover:not(:disabled) {
    background: rgba(255, 255, 255, 0.04);
    border-color: var(--nv-scr-line-2);
    color: var(--nv-scr-text);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-btn {
      transition: none;
    }
    .nv-scr-btn:active:not(:disabled) {
      transform: none;
    }
    .nv-scr-btn-sheen {
      display: none;
    }
  }
}
</style>
