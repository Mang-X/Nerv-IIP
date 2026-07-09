<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
} from 'reka-ui'
import { inject } from 'vue'
import { MOBILE_OVERLAY_TARGET } from '../../lib/overlay-target'
import { cn } from '../../lib/utils'

// Defaults to body (full-screen PDA); a host (e.g. docs phone sim) can scope it.
const overlayTarget = inject(MOBILE_OVERLAY_TARGET, undefined)

/**
 * MobileDialog — an iOS-style centered confirm dialog (确认 / 取消), distinct
 * from BottomSheet / ActionSheet. Native alert anatomy: a compact centered card
 * with a title + message and a hairline-split button row. Confirm-dialog default
 * is modal (overlay tap does not dismiss); set `closeOnOverlay` to allow it.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    title?: string
    description?: string
    confirmText?: string
    cancelText?: string
    showCancel?: boolean
    confirmTone?: 'brand' | 'danger'
    closeOnOverlay?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    confirmText: '确定',
    cancelText: '取消',
    showCancel: true,
    confirmTone: 'brand',
    closeOnOverlay: false,
  },
)

const emit = defineEmits<{ 'update:open': [value: boolean]; confirm: []; cancel: [] }>()

function onConfirm() {
  emit('confirm')
  emit('update:open', false)
}
function onCancel() {
  emit('cancel')
  emit('update:open', false)
}
function guardOutside(e: Event) {
  if (!props.closeOnOverlay) e.preventDefault()
}
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal :to="overlayTarget">
      <DialogOverlay
        class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-40 bg-black/45 backdrop-blur-[6px] duration-200"
      />
      <DialogContent
        data-slot="mobile-dialog-content"
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=open]:[animation-timing-function:var(--nv-ease-out-expo)] fixed top-1/2 left-1/2 z-50 flex w-[17.5rem] -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-[0_12px_48px_-12px_rgb(0_0_0/0.5)] duration-200 outline-none',
            $props.class,
          )
        "
        @pointer-down-outside="guardOutside"
        @interact-outside="guardOutside"
      >
        <div class="ds-md-body">
          <DialogTitle :class="title ? 'text-base font-semibold text-foreground' : 'sr-only'">
            {{ title ?? '提示' }}
          </DialogTitle>
          <DialogDescription
            :class="
              description ? 'mt-1.5 text-sm leading-relaxed text-muted-foreground' : 'sr-only'
            "
          >
            {{ description ?? title ?? '对话框' }}
          </DialogDescription>
          <div v-if="$slots.default" class="mt-2 text-sm text-foreground"><slot /></div>
        </div>

        <div class="ds-md-actions">
          <button v-if="showCancel" type="button" class="ds-md-btn" @click="onCancel">
            {{ cancelText }}
          </button>
          <button
            type="button"
            class="ds-md-btn ds-md-confirm"
            :data-tone="confirmTone"
            @click="onConfirm"
          >
            {{ confirmText }}
          </button>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
@layer nv-components {
  .ds-md-body {
    padding: 1.375rem 1.25rem 1.125rem;
    text-align: center;
  }
  .ds-md-actions {
    display: flex;
    border-top: 1px solid var(--border);
  }
  .ds-md-btn {
    flex: 1 1 0;
    min-width: 0;
    height: 2.875rem;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 1rem;
    color: var(--foreground);
    outline: none;
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
    transition: background-color 0.12s ease;
  }
  .ds-md-btn:active {
    background-color: var(--muted);
  }
  .ds-md-btn + .ds-md-btn {
    border-left: 1px solid var(--border);
  }
  .ds-md-confirm {
    font-weight: 600;
    color: var(--nv-brand-strong);
  }
  .ds-md-confirm[data-tone='danger'] {
    color: var(--nv-destructive-strong);
  }
  .ds-md-btn:focus-visible {
    box-shadow: inset 0 0 0 2px color-mix(in oklch, var(--ring) 50%, transparent);
  }

  @media (prefers-reduced-motion: reduce) {
    .ds-md-btn {
      transition: none;
    }
  }
}
</style>
