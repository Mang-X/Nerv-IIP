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
  <Teleport :to="overlayTarget">
    <Transition name="ds-toast">
      <div v-if="show" class="ds-toast-layer" :class="overlay && 'ds-toast-blocking'">
        <div
          class="ds-toast"
          :class="type === 'text' && 'ds-toast-compact'"
          role="status"
          aria-live="polite"
        >
          <span v-if="type !== 'text'" class="ds-toast-icon" :data-type="type">
            <Loader2Icon v-if="type === 'loading'" class="ds-toast-spin" aria-hidden="true" />
            <CheckIcon v-else-if="type === 'success'" aria-hidden="true" />
            <XIcon v-else aria-hidden="true" />
          </span>
          <span v-if="message" class="ds-toast-msg">{{ message }}</span>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.ds-toast-layer {
  position: fixed;
  inset: 0;
  z-index: 60;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  pointer-events: none;
}
.ds-toast-blocking {
  pointer-events: auto;
}
.ds-toast {
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
.ds-toast-compact {
  padding: 0.625rem 0.875rem;
  border-radius: 10px;
}
.ds-toast-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.ds-toast-icon :deep(svg) {
  width: 1.75rem;
  height: 1.75rem;
}
.ds-toast-icon[data-type='success'] {
  color: var(--success);
}
.ds-toast-icon[data-type='error'] {
  color: var(--destructive);
}
.ds-toast-msg {
  font-size: 0.875rem;
  line-height: 1.4;
}
.ds-toast-spin {
  animation: ds-toast-spin 0.8s linear infinite;
}
@keyframes ds-toast-spin {
  to {
    transform: rotate(360deg);
  }
}

.ds-toast-enter-active {
  transition:
    opacity 0.2s var(--ease-out-quart, ease-out),
    transform 0.2s var(--ease-out-quart, ease-out);
}
.ds-toast-leave-active {
  transition:
    opacity 0.15s ease,
    transform 0.15s ease;
}
.ds-toast-enter-from,
.ds-toast-leave-to {
  opacity: 0;
  transform: scale(0.9);
}

@media (prefers-reduced-motion: reduce) {
  .ds-toast-spin {
    animation-duration: 1.4s;
  }
  .ds-toast-enter-active,
  .ds-toast-leave-active {
    transition: opacity 0.15s linear;
  }
  .ds-toast-enter-from,
  .ds-toast-leave-to {
    transform: none;
  }
}
</style>
