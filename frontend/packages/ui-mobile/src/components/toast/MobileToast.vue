<script setup lang="ts">
import { inject, onBeforeUnmount, watch } from 'vue'
import { CheckIcon, Loader2Icon, XIcon } from 'lucide-vue-next'
import { MOBILE_OVERLAY_TARGET } from '../../lib/overlay-target'

// Defaults to body (full-screen PDA); a host (e.g. docs phone sim) can scope it.
const overlayTarget = inject(MOBILE_OVERLAY_TARGET, 'body')

/**
 * MobileToast — a centered HUD toast (居中提示), distinct from the top message
 * banner. Dark rounded card with an optional state glyph (loading spinner /
 * success / error) and a line of text. Auto-dismisses after `duration` (except
 * `loading`, which persists until closed); `overlay` blocks interaction while up.
 */
const props = withDefaults(
  defineProps<{
    show: boolean
    message?: string
    type?: 'text' | 'loading' | 'success' | 'error'
    /** Auto-hide delay in ms; ignored for `loading` (and when 0). */
    duration?: number
    /** Block taps behind the toast (use with `loading`). */
    overlay?: boolean
  }>(),
  { type: 'text', duration: 2000, overlay: false },
)

const emit = defineEmits<{ 'update:show': [value: boolean] }>()

let timer: ReturnType<typeof setTimeout> | undefined
function clearTimer() {
  if (timer) {
    clearTimeout(timer)
    timer = undefined
  }
}
watch(
  () => [props.show, props.type, props.duration] as const,
  ([show, type, duration]) => {
    clearTimer()
    if (show && type !== 'loading' && duration > 0) {
      timer = setTimeout(() => emit('update:show', false), duration)
    }
  },
  { immediate: true },
)
onBeforeUnmount(clearTimer)
</script>

<template>
  <!-- `defer` lets the target resolve after the tree mounts, so a scoped target
       that is an ancestor (docs phone sim) is found instead of erroring. -->
  <Teleport defer :to="overlayTarget">
    <Transition name="nv-m-toast">
      <div v-if="show" class="nv-m-toast-layer" :class="overlay && 'nv-m-toast-blocking'">
        <div
          class="nv-m-toast"
          :class="type === 'text' && 'nv-m-toast-compact'"
          role="status"
          aria-live="polite"
        >
          <span v-if="type !== 'text'" class="nv-m-toast-icon" :data-type="type">
            <Loader2Icon v-if="type === 'loading'" class="nv-m-toast-spin" aria-hidden="true" />
            <CheckIcon v-else-if="type === 'success'" aria-hidden="true" />
            <XIcon v-else aria-hidden="true" />
          </span>
          <span v-if="message" class="nv-m-toast-msg">{{ message }}</span>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
@layer nv-components {
  .nv-m-toast-layer {
    position: fixed;
    inset: 0;
    z-index: 60;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 1rem;
    pointer-events: none;
  }
  .nv-m-toast-blocking {
    pointer-events: auto;
  }
  .nv-m-toast {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.625rem;
    max-width: 16rem;
    padding: 0.875rem 1rem;
    border-radius: 14px;
    background-color: oklch(0.21 0 0 / 0.92);
    color: #fff;
    text-align: center;
    backdrop-filter: blur(4px);
    box-shadow: 0 12px 40px -12px rgb(0 0 0 / 0.6);
  }
  /* Text-only toasts are a compact single line. */
  .nv-m-toast-compact {
    padding: 0.625rem 0.875rem;
    border-radius: 10px;
  }
  .nv-m-toast-icon {
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }
  .nv-m-toast-icon :deep(svg) {
    width: 1.75rem;
    height: 1.75rem;
  }
  .nv-m-toast-icon[data-type='success'] {
    color: var(--nv-success);
  }
  .nv-m-toast-icon[data-type='error'] {
    color: var(--destructive);
  }
  .nv-m-toast-msg {
    font-size: 0.875rem;
    line-height: 1.4;
  }
  .nv-m-toast-spin {
    animation: nv-m-toast-spin 0.8s linear infinite;
  }
  @keyframes nv-m-toast-spin {
    to {
      transform: rotate(360deg);
    }
  }

  .nv-m-toast-enter-active {
    transition:
      opacity 0.2s var(--nv-ease-out-quart, ease-out),
      transform 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-toast-leave-active {
    transition:
      opacity 0.15s ease,
      transform 0.15s ease;
  }
  .nv-m-toast-enter-from,
  .nv-m-toast-leave-to {
    opacity: 0;
    transform: scale(0.9);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-m-toast-spin {
      animation-duration: 1.4s;
    }
    .nv-m-toast-enter-active,
    .nv-m-toast-leave-active {
      transition: opacity 0.15s linear;
    }
    .nv-m-toast-enter-from,
    .nv-m-toast-leave-to {
      transform: none;
    }
  }
}
</style>
