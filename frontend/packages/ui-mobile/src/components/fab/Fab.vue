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
  <div :class="cn('nv-m-fab-root', props.class)" :data-pos="position">
    <Transition name="nv-m-fab-scrim">
      <div v-if="open && actions?.length" class="nv-m-fab-scrim" @click="open = false" />
    </Transition>

    <div class="nv-m-fab-stack">
      <TransitionGroup name="nv-m-fab-act" tag="div" class="nv-m-fab-actions">
        <button
          v-for="(action, i) in open && actions ? actions : []"
          :key="action.key ?? i"
          type="button"
          class="nv-m-fab-act"
          :style="{ '--i': (actions?.length ?? 0) - 1 - i }"
          @click="onSelect(action, i)"
        >
          <span v-if="action.text" class="nv-m-fab-act-label">{{ action.text }}</span>
          <span class="nv-m-fab-act-btn"><component :is="action.icon" aria-hidden="true" /></span>
        </button>
      </TransitionGroup>

      <button
        type="button"
        class="nv-m-fab-main"
        :class="text && !actions?.length ? 'nv-m-fab-extended' : ''"
        :data-tone="tone"
        :aria-expanded="actions?.length ? open : undefined"
        @click="onMain"
      >
        <component
          :is="icon ?? PlusIcon"
          class="nv-m-fab-glyph"
          :data-open="open || undefined"
          aria-hidden="true"
        />
        <span v-if="text && !actions?.length" class="nv-m-fab-label">{{ text }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-m-fab-root {
    position: absolute;
    inset: 0;
    z-index: 40;
    pointer-events: none;
  }
  .nv-m-fab-scrim {
    position: absolute;
    inset: 0;
    background-color: color-mix(in oklch, black 32%, transparent);
    backdrop-filter: blur(2px);
    pointer-events: auto;
  }

  .nv-m-fab-stack {
    position: absolute;
    bottom: calc(1rem + env(safe-area-inset-bottom, 0px));
    display: flex;
    flex-direction: column;
    gap: 0.875rem;
    pointer-events: auto;
  }
  .nv-m-fab-root[data-pos='bottom-right'] .nv-m-fab-stack {
    right: 1rem;
    align-items: flex-end;
  }
  .nv-m-fab-root[data-pos='bottom-left'] .nv-m-fab-stack {
    left: 1rem;
    align-items: flex-start;
  }
  .nv-m-fab-root[data-pos='bottom-center'] .nv-m-fab-stack {
    left: 50%;
    transform: translateX(-50%);
    align-items: center;
  }

  .nv-m-fab-actions {
    display: flex;
    flex-direction: column;
    gap: 0.875rem;
    align-items: inherit;
  }
  .nv-m-fab-act {
    display: inline-flex;
    align-items: center;
    gap: 0.625rem;
    outline: none;
    -webkit-tap-highlight-color: transparent;
  }
  .nv-m-fab-act-label {
    padding: 0.25rem 0.625rem;
    border-radius: 7px;
    background-color: var(--card);
    border: 1px solid var(--border);
    font-size: 0.8125rem;
    color: var(--foreground);
    box-shadow: 0 2px 8px -2px color-mix(in oklch, black 30%, transparent);
    white-space: nowrap;
  }
  .nv-m-fab-act-btn {
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
  .nv-m-fab-act-btn :deep(svg) {
    width: 1.125rem;
    height: 1.125rem;
  }
  .nv-m-fab-act:active .nv-m-fab-act-btn {
    background-color: var(--muted);
  }

  .nv-m-fab-main {
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
  .nv-m-fab-main[data-tone='default'] {
    background-color: var(--card);
    color: var(--foreground);
    border: 1px solid var(--border);
  }
  .nv-m-fab-main:active {
    transform: scale(0.92);
  }
  .nv-m-fab-main:hover {
    filter: brightness(1.05);
  }
  .nv-m-fab-extended {
    width: auto;
    padding-inline: 1.125rem 1.375rem;
    border-radius: 9999px;
  }
  .nv-m-fab-label {
    font-size: 0.9375rem;
    font-weight: 600;
  }
  .nv-m-fab-glyph {
    width: 1.5rem;
    height: 1.5rem;
    transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-fab-glyph[data-open] {
    transform: rotate(135deg);
  }

  /* Scrim fade */
  .nv-m-fab-scrim-enter-active,
  .nv-m-fab-scrim-leave-active {
    transition: opacity 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-fab-scrim-enter-from,
  .nv-m-fab-scrim-leave-to {
    opacity: 0;
  }

  /* Speed-dial actions: staggered rise. */
  .nv-m-fab-act-enter-active {
    transition:
      opacity 0.22s var(--nv-ease-out-quart, ease-out),
      transform 0.22s var(--nv-ease-out-quart);
    transition-delay: calc(var(--i) * 40ms);
  }
  .nv-m-fab-act-leave-active {
    transition:
      opacity 0.14s ease,
      transform 0.14s ease;
  }
  .nv-m-fab-act-enter-from,
  .nv-m-fab-act-leave-to {
    opacity: 0;
    transform: translateY(0.75rem) scale(0.9);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-m-fab-main,
    .nv-m-fab-glyph,
    .nv-m-fab-act-enter-active,
    .nv-m-fab-act-leave-active,
    .nv-m-fab-scrim-enter-active,
    .nv-m-fab-scrim-leave-active {
      transition: none;
    }
  }
}
</style>
