<script setup lang="ts">
import { computed } from 'vue'
import {
  CircleAlertIcon,
  CircleCheckIcon,
  InfoIcon,
  TriangleAlertIcon,
  XIcon,
} from 'lucide-vue-next'
import type { NotifyKind } from './notify'
import { dismissNotify, useNotifyStore } from './notify'

/**
 * Pro — mount ONCE near the app root. Renders the message stack (top-center)
 * and the notification stack (top-right) from the shared notify store.
 */
const store = useNotifyStore()

const messages = computed(() => store.items.filter((i) => i.channel === 'message'))
const notifications = computed(() => store.items.filter((i) => i.channel === 'notification'))

const iconFor: Record<NotifyKind, typeof InfoIcon> = {
  info: InfoIcon,
  success: CircleCheckIcon,
  warning: TriangleAlertIcon,
  error: CircleAlertIcon,
}
const toneClass: Record<NotifyKind, string> = {
  info: 'text-brand-strong',
  success: 'text-success-strong',
  warning: 'text-warning-strong',
  error: 'text-destructive-strong',
}
</script>

<template>
  <Teleport to="body">
    <!-- Messages — top center, compact -->
    <div class="ds-msg-region pointer-events-none">
      <TransitionGroup name="ds-msg">
        <div
          v-for="item in messages"
          :key="item.id"
          class="ds-msg pointer-events-auto flex max-w-[min(86vw,30rem)] items-center gap-2 rounded-full border border-border bg-popover py-1.5 pr-3.5 pl-3 text-sm whitespace-nowrap text-popover-foreground shadow-md"
        >
          <component
            :is="iconFor[item.kind]"
            :class="['size-4 shrink-0', toneClass[item.kind]]"
            aria-hidden="true"
          />
          <span class="min-w-0 truncate" :title="item.title">{{ item.title }}</span>
        </div>
      </TransitionGroup>
    </div>

    <!-- Notifications — top right, card -->
    <div class="ds-note-region pointer-events-none">
      <TransitionGroup name="ds-note">
        <div
          v-for="item in notifications"
          :key="item.id"
          class="ds-note pointer-events-auto flex w-80 gap-3 rounded-xl border border-border bg-popover p-3.5 text-popover-foreground shadow-lg"
        >
          <component
            :is="iconFor[item.kind]"
            :class="['mt-0.5 size-[18px] shrink-0', toneClass[item.kind]]"
            aria-hidden="true"
          />
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium" :title="item.title">{{ item.title }}</p>
            <p
              v-if="item.description"
              class="mt-0.5 line-clamp-2 text-sm text-muted-foreground"
              :title="item.description"
            >
              {{ item.description }}
            </p>
          </div>
          <button
            type="button"
            class="-mt-1 -mr-1 flex size-6 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
            aria-label="关闭通知"
            @click="dismissNotify(item.id)"
          >
            <XIcon class="size-4" aria-hidden="true" />
          </button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
.ds-msg-region {
  position: fixed;
  top: 1rem;
  left: 0;
  right: 0;
  z-index: 90;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}
.ds-note-region {
  position: fixed;
  top: 1rem;
  right: 1rem;
  z-index: 90;
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
}

.ds-msg-enter-active,
.ds-msg-leave-active,
.ds-note-enter-active,
.ds-note-leave-active {
  transition:
    opacity 0.3s var(--ease-out-expo, ease-out),
    transform 0.3s var(--ease-out-expo, ease-out);
}
/* Surviving siblings slide to their new slot instead of snapping (fixes the
   multi-item exit jitter). */
.ds-msg-move,
.ds-note-move {
  transition: transform 0.34s var(--ease-out-expo, ease-out);
}

/* ── Messages (top-center) ──
   The region is full-width with centered items; a leaving pill goes out of flow
   (so siblings slide up at once) but stays centered via fit-content + auto
   margins — keeping the leave transform pure fade + rise + scale (clearly
   visible, no sideways re-centering jump). Single-line pill never collapses. */
.ds-msg-enter-from {
  opacity: 0;
  transform: translateY(-14px) scale(0.94);
}
.ds-msg-leave-active {
  position: absolute;
  left: 0;
  right: 0;
  width: fit-content;
  margin-inline: auto;
}
.ds-msg-leave-to {
  opacity: 0;
  transform: translateY(-12px) scale(0.94);
}

/* ── Notifications (top-right) ──
   Fixed-width card, anchored to the right edge while leaving. */
.ds-note-enter-from {
  opacity: 0;
  transform: translateX(16px) scale(0.98);
}
.ds-note-leave-active {
  position: absolute;
  right: 0;
}
.ds-note-leave-to {
  opacity: 0;
  transform: translateX(16px) scale(0.98);
}

@media (prefers-reduced-motion: reduce) {
  .ds-msg-enter-active,
  .ds-msg-leave-active,
  .ds-note-enter-active,
  .ds-note-leave-active,
  .ds-msg-move,
  .ds-note-move {
    transition: opacity 0.2s linear;
  }
  .ds-msg-enter-from,
  .ds-msg-leave-to,
  .ds-note-enter-from,
  .ds-note-leave-to {
    transform: none;
  }
}
</style>
