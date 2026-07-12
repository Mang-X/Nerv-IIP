<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * MobileGrid — a function-entry grid menu (宫格) for home / workbench screens.
 * Icon + label cells in an N-column grid, optional hairline borders + square
 * cells, and a corner badge (count or dot). Emits `select` on tap.
 */
export interface GridItem {
  key?: string
  icon?: Component
  text?: string
  /** Corner badge: a count, or `true` for a dot. */
  badge?: string | number | boolean
}

const props = withDefaults(
  defineProps<{
    items: GridItem[]
    columns?: number
    bordered?: boolean
    square?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { columns: 4, bordered: false, square: false },
)

const emit = defineEmits<{ select: [item: GridItem, index: number] }>()
</script>

<template>
  <div
    :class="cn('nv-m-grid', bordered && 'nv-m-grid-bordered', props.class)"
    :style="{ '--nv-m-grid-cols': String(columns) }"
  >
    <button
      v-for="(item, i) in items"
      :key="item.key ?? i"
      type="button"
      class="nv-m-grid-item"
      :class="square && 'nv-m-grid-square'"
      @click="emit('select', item, i)"
    >
      <span class="nv-m-grid-icon">
        <slot :name="`icon-${item.key}`" :item="item">
          <component :is="item.icon" v-if="item.icon" aria-hidden="true" />
        </slot>
        <span
          v-if="item.badge !== undefined && item.badge !== false"
          class="nv-m-grid-badge"
          :data-dot="item.badge === true || undefined"
        >
          <template v-if="item.badge !== true">{{ item.badge }}</template>
        </span>
      </span>
      <span v-if="item.text" class="nv-m-grid-text">{{ item.text }}</span>
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-m-grid {
    display: grid;
    grid-template-columns: repeat(var(--nv-m-grid-cols), minmax(0, 1fr));
  }
  .nv-m-grid-bordered {
    gap: 1px;
    background-color: var(--border);
    border: 1px solid var(--border);
    border-radius: 12px;
    overflow: hidden;
  }

  .nv-m-grid-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    padding: 0.875rem 0.25rem;
    background-color: var(--card);
    outline: none;
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
    /* Background-only feedback (no scale → no layout/icon shift); the tinted cell
     also makes the tap target obvious. Instant press-in, eased fade-out. */
    transition: background-color 0.22s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-grid:not(.nv-m-grid-bordered) .nv-m-grid-item {
    background-color: transparent;
  }
  .nv-m-grid-item:active {
    background-color: var(--muted);
    transition-duration: 0s;
  }
  .nv-m-grid-item:active .nv-m-grid-icon {
    color: var(--nv-brand-strong);
  }
  .nv-m-grid-square {
    aspect-ratio: 1;
  }

  .nv-m-grid-icon {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    color: var(--foreground);
  }
  .nv-m-grid-icon :deep(svg) {
    width: 1.625rem;
    height: 1.625rem;
  }
  .nv-m-grid-text {
    font-size: 0.75rem;
    line-height: 1;
    color: var(--muted-foreground);
  }

  .nv-m-grid-badge {
    position: absolute;
    top: -0.375rem;
    right: -0.5rem;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 1.0625rem;
    height: 1.0625rem;
    padding-inline: 0.25rem;
    border-radius: 9999px;
    background-color: var(--destructive);
    color: #fff;
    font-size: 0.625rem;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
    box-shadow: 0 0 0 2px var(--card);
  }
  .nv-m-grid-badge[data-dot] {
    top: -0.125rem;
    right: -0.125rem;
    min-width: 0.5rem;
    width: 0.5rem;
    height: 0.5rem;
    padding: 0;
  }
}
</style>
