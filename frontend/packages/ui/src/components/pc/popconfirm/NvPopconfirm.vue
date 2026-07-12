<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ref, watch } from 'vue'
import { TriangleAlertIcon } from 'lucide-vue-next'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import NvButton from '../button/NvButton.vue'

/**
 * Pro — an inline bubble confirmation for in-place dangerous actions (row delete,
 * dispatch, cancel…), distinct from a full modal. Anchors a small Popover to its
 * trigger slot with a caution icon, question, and cancel / confirm buttons.
 * Works self-managed or controlled via `v-model:open`; supports an async
 * `loading` confirm when controlled.
 */
const props = withDefaults(
  defineProps<{
    title?: string
    description?: string
    confirmText?: string
    cancelText?: string
    confirmTone?: 'brand' | 'danger'
    loading?: boolean
    /** Controlled open state (v-model:open); omit for self-managed. */
    open?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    title: '确认执行该操作？',
    confirmText: '确定',
    cancelText: '取消',
    confirmTone: 'danger',
    loading: false,
  },
)

const emit = defineEmits<{ confirm: []; cancel: []; 'update:open': [value: boolean] }>()

// Self-managed by default; `v-model:open` (props.open) syncs both ways when used.
const isOpen = ref(props.open ?? false)
watch(
  () => props.open,
  (v) => {
    if (v !== undefined) isOpen.value = v
  },
)
watch(isOpen, (v) => {
  if (props.open !== undefined) emit('update:open', v)
})

function onConfirm() {
  emit('confirm')
  isOpen.value = false
}
function onCancel() {
  emit('cancel')
  isOpen.value = false
}
</script>

<template>
  <Popover v-model:open="isOpen">
    <PopoverTrigger as-child>
      <slot />
    </PopoverTrigger>
    <PopoverContent align="center" :side-offset="8" :class="['w-64 gap-0 p-0', props.class]">
      <div class="flex gap-2.5 p-3">
        <span class="nv-pc-icon" aria-hidden="true">
          <slot name="icon"><TriangleAlertIcon class="size-4" /></slot>
        </span>
        <div class="min-w-0 flex-1">
          <p class="text-sm font-medium text-foreground">{{ title }}</p>
          <p v-if="description" class="mt-1 text-xs leading-relaxed text-muted-foreground">
            {{ description }}
          </p>
          <div class="mt-3 flex justify-end gap-2">
            <NvButton size="sm" variant="outline" @click="onCancel">{{ cancelText }}</NvButton>
            <NvButton
              size="sm"
              :variant="confirmTone === 'danger' ? 'destructive' : 'brand'"
              :loading="loading"
              @click="onConfirm"
            >
              {{ confirmText }}
            </NvButton>
          </div>
        </div>
      </div>
    </PopoverContent>
  </Popover>
</template>

<style scoped>
@layer nv-components {
  .nv-pc-icon {
    flex-shrink: 0;
    margin-top: 0.0625rem;
    color: var(--nv-warning-strong);
  }
}
</style>
