<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { ref } from 'vue'
import { PlusIcon } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

/**
 * Fab — a floating action button pinned to the app shell's corner (absolute, so
 * it stays inside the device frame). Single-action by default, or a speed-dial
 * when `actions` are provided: the main button rotates open, a scrim appears,
 * and labelled mini-actions stagger in above it. Place inside a positioned
 * (relative) container — e.g. AppShellMobile.
 */
export interface FabAction {
  key?: string
  icon?: Component
  text?: string
}

const props = withDefaults(
  defineProps<{
    icon?: Component
    /** Extended pill label (single-action mode only). */
    text?: string
    /** Speed-dial actions; when present the FAB toggles open on tap. */
    actions?: FabAction[]
    position?: 'bottom-right' | 'bottom-left' | 'bottom-center'
    tone?: 'brand' | 'default'
    class?: HTMLAttributes['class']
  }>(),
  { position: 'bottom-right', tone: 'brand' },
)

const emit = defineEmits<{ click: []; select: [action: FabAction, index: number] }>()

const open = ref(false)

function onMain() {
  if (props.actions?.length) open.value = !open.value
  else emit('click')
}
function onSelect(action: FabAction, index: number) {
  emit('select', action, index)
  open.value = false
}
</script>

<template>
  <div :class="cn('ds-fab-root', props.class)" :data-pos="position">
    <Transition name="ds-fab-scrim">
      <div v-if="open && actions?.length" class="ds-fab-scrim" @click="open = false" />
    </Transition>

    <div class="ds-fab-stack">
      <TransitionGroup name="ds-fab-act" tag="div" class="ds-fab-actions">
        <button
          v-for="(action, i) in open && actions ? actions : []"
          :key="action.key ?? i"
          type="button"
          class="ds-fab-act"
          :style="{ '--i': (actions?.length ?? 0) - 1 - i }"
          @click="onSelect(action, i)"
        >
          <span v-if="action.text" class="ds-fab-act-label">{{ action.text }}</span>
          <span class="ds-fab-act-btn"><component :is="action.icon" aria-hidden="true" /></span>
        </button>
      </TransitionGroup>

      <button
        type="button"
        class="ds-fab-main"
        :class="text && !actions?.length ? 'ds-fab-extended' : ''"
        :data-tone="tone"
        :aria-expanded="actions?.length ? open : undefined"
        @click="onMain"
      >
        <component
          :is="icon ?? PlusIcon"
          class="ds-fab-glyph"
          :data-open="open || undefined"
          aria-hidden="true"
        />
        <span v-if="text && !actions?.length" class="ds-fab-label">{{ text }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-fab-root {
    position: absolute;
    inset: 0;
    z-index: 40;
    pointer-events: none;
  }
  .ds-fab-scrim {
    position: absolute;
    inset: 0;
    background-color: color-mix(in oklch, black 32%, transparent);
    backdrop-filter: blur(2px);
    pointer-events: auto;
  }

  .ds-fab-stack {
    position: absolute;
    bottom: calc(1rem + env(safe-area-inset-bottom, 0px));
    display: flex;
    flex-direction: column;
    gap: 0.875rem;
    pointer-events: auto;
  }
  .ds-fab-root[data-pos='bottom-right'] .ds-fab-stack {
    right: 1rem;
    align-items: flex-end;
  }
  .ds-fab-root[data-pos='bottom-left'] .ds-fab-stack {
    left: 1rem;
    align-items: flex-start;
  }
  .ds-fab-root[data-pos='bottom-center'] .ds-fab-stack {
    left: 50%;
    transform: translateX(-50%);
    align-items: center;
  }

  .ds-fab-actions {
    display: flex;
    flex-direction: column;
    gap: 0.875rem;
    align-items: inherit;
  }
  .ds-fab-act {
    display: inline-flex;
    align-items: center;
    gap: 0.625rem;
    outline: none;
    -webkit-tap-highlight-color: transparent;
  }
  .ds-fab-act-label {
    padding: 0.25rem 0.625rem;
    border-radius: 7px;
    background-color: var(--card);
    border: 1px solid var(--border);
    font-size: 0.8125rem;
    color: var(--foreground);
    box-shadow: 0 2px 8px -2px color-mix(in oklch, black 30%, transparent);
    white-space: nowrap;
  }
  .ds-fab-act-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 2.5rem;
    height: 2.5rem;
    border-radius: 9999px;
    background-color: var(--card);
    border: 1px solid var(--border);
    color: var(--foreground);
    box-shadow: 0 2px 10px -2px color-mix(in oklch, black 35%, transparent);
  }
  .ds-fab-act-btn :deep(svg) {
    width: 1.125rem;
    height: 1.125rem;
  }
  .ds-fab-act:active .ds-fab-act-btn {
    background-color: var(--muted);
  }

  .ds-fab-main {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    width: 3.5rem;
    height: 3.5rem;
    border-radius: 9999px;
    color: var(--nv-brand-foreground);
    background-color: var(--nv-brand);
    box-shadow:
      inset 0 1px 0 0 color-mix(in oklch, white 18%, transparent),
      0 8px 24px -6px color-mix(in oklch, var(--nv-brand) 70%, black 20%);
    outline: none;
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
    transition:
      transform 0.18s var(--nv-ease-out-quart),
      box-shadow 0.18s ease,
      filter 0.15s ease;
  }
  .ds-fab-main[data-tone='default'] {
    background-color: var(--card);
    color: var(--foreground);
    border: 1px solid var(--border);
  }
  .ds-fab-main:active {
    transform: scale(0.92);
  }
  .ds-fab-main:hover {
    filter: brightness(1.05);
  }
  .ds-fab-extended {
    width: auto;
    padding-inline: 1.125rem 1.375rem;
    border-radius: 9999px;
  }
  .ds-fab-label {
    font-size: 0.9375rem;
    font-weight: 600;
  }
  .ds-fab-glyph {
    width: 1.5rem;
    height: 1.5rem;
    transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .ds-fab-glyph[data-open] {
    transform: rotate(135deg);
  }

  /* Scrim fade */
  .ds-fab-scrim-enter-active,
  .ds-fab-scrim-leave-active {
    transition: opacity 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .ds-fab-scrim-enter-from,
  .ds-fab-scrim-leave-to {
    opacity: 0;
  }

  /* Speed-dial actions: staggered rise. */
  .ds-fab-act-enter-active {
    transition:
      opacity 0.22s var(--nv-ease-out-quart, ease-out),
      transform 0.22s var(--nv-ease-out-quart);
    transition-delay: calc(var(--i) * 40ms);
  }
  .ds-fab-act-leave-active {
    transition:
      opacity 0.14s ease,
      transform 0.14s ease;
  }
  .ds-fab-act-enter-from,
  .ds-fab-act-leave-to {
    opacity: 0;
    transform: translateY(0.75rem) scale(0.9);
  }

  @media (prefers-reduced-motion: reduce) {
    .ds-fab-main,
    .ds-fab-glyph,
    .ds-fab-act-enter-active,
    .ds-fab-act-leave-active,
    .ds-fab-scrim-enter-active,
    .ds-fab-scrim-leave-active {
      transition: none;
    }
  }
}
</style>
