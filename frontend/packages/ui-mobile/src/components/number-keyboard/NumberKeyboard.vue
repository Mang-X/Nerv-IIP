<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { Delete } from 'lucide-vue-next'
import { computed, inject } from 'vue'
import { MOBILE_OVERLAY_TARGET } from '../../lib/overlay-target'
import { cn } from '../../lib/utils'

// Defaults to body (full-screen PDA); a host (e.g. docs phone sim) can scope it.
const overlayTarget = inject(MOBILE_OVERLAY_TARGET, 'body')

/**
 * NumberKeyboard — on-screen numeric keypad for PDA entry (Arco form). Bottom
 * fixed panel toggled by `v-model:show`, big touch keys for gloved hands. Edits a
 * string `v-model` and emits each key press. `extraKey` is the bottom-left key
 * (default '.', set to 'X' for 工号/批次 alnum or '' to hide). For 录入数量 / 工号 / 称重.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: string
    show?: boolean
    title?: string
    extraKey?: string
    maxlength?: number
    confirmText?: string
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: '', show: false, extraKey: '.', confirmText: '完成' },
)
const emit = defineEmits<{
  'update:modelValue': [value: string]
  'update:show': [value: boolean]
  press: [key: string]
  confirm: []
}>()

const keys = computed(() => ['1', '2', '3', '4', '5', '6', '7', '8', '9'])

function input(key: string) {
  emit('press', key)
  if (key === '.' && props.modelValue.includes('.')) return
  if (props.maxlength != null && props.modelValue.length >= props.maxlength) return
  emit('update:modelValue', props.modelValue + key)
}
function backspace() {
  emit('press', 'delete')
  emit('update:modelValue', props.modelValue.slice(0, -1))
}
function confirm() {
  emit('confirm')
  emit('update:show', false)
}
</script>

<template>
  <!-- `defer` lets the target resolve after the tree mounts, so a scoped target
       that is an ancestor (docs phone sim) is found instead of erroring. -->
  <Teleport defer :to="overlayTarget">
    <Transition name="nv-m-nk-fade">
      <div v-if="show" class="fixed inset-0 z-40 bg-black/30" @click="emit('update:show', false)" />
    </Transition>
    <Transition name="nv-m-nk-slide">
      <div
        v-if="show"
        data-slot="number-keyboard"
        :class="
          cn(
            'fixed inset-x-0 bottom-0 z-50 select-none rounded-t-2xl border-t border-border bg-card pb-safe shadow-[0_-8px_40px_-12px_rgb(0_0_0/0.45)]',
            props.class,
          )
        "
      >
        <div class="flex items-center justify-between px-4 py-2.5">
          <span class="text-sm text-muted-foreground">{{ title ?? '请输入' }}</span>
          <button
            type="button"
            class="nv-m-nk-key rounded-md px-2 py-1 text-[15px] font-medium text-brand"
            @click="confirm"
          >
            {{ confirmText }}
          </button>
        </div>
        <div class="grid grid-cols-4 gap-1.5 px-1.5 pb-2">
          <!-- digits 1-9 span the first three columns -->
          <button
            v-for="k in keys"
            :key="k"
            type="button"
            class="nv-m-nk-key col-span-1 grid h-14 place-items-center rounded-xl bg-muted text-2xl font-medium text-foreground tabular-nums"
            @click="input(k)"
          >
            {{ k }}
          </button>
          <!-- backspace, spanning the right column across the top two rows -->
          <button
            type="button"
            class="nv-m-nk-key col-start-4 row-start-1 row-span-2 grid h-auto place-items-center rounded-xl bg-muted text-foreground"
            aria-label="删除"
            @click="backspace"
          >
            <Delete class="size-6" aria-hidden="true" />
          </button>
          <!-- confirm, spanning the right column across the bottom two rows -->
          <button
            type="button"
            class="nv-m-nk-key col-start-4 row-start-3 row-span-2 grid h-auto place-items-center rounded-xl bg-brand text-base font-medium text-brand-foreground"
            @click="confirm"
          >
            {{ confirmText }}
          </button>
          <!-- extra key (decimal / alnum) -->
          <button
            v-if="extraKey"
            type="button"
            class="nv-m-nk-key col-span-1 grid h-14 place-items-center rounded-xl bg-muted text-2xl font-medium text-foreground"
            @click="input(extraKey)"
          >
            {{ extraKey }}
          </button>
          <!-- zero, widening to fill when there is no extra key -->
          <button
            type="button"
            class="nv-m-nk-key grid h-14 place-items-center rounded-xl bg-muted text-2xl font-medium text-foreground tabular-nums"
            :class="extraKey ? 'col-span-2' : 'col-span-3'"
            @click="input('0')"
          >
            0
          </button>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
@layer nv-components {
  .nv-m-nk-key {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .nv-m-nk-key:active {
    opacity: 0.6;
  }
  .nv-m-nk-fade-enter-active,
  .nv-m-nk-fade-leave-active {
    transition: opacity 0.25s var(--nv-ease-out-quart, ease-out);
  }
  .nv-m-nk-fade-enter-from,
  .nv-m-nk-fade-leave-to {
    opacity: 0;
  }
  .nv-m-nk-slide-enter-active,
  .nv-m-nk-slide-leave-active {
    transition: transform 0.3s var(--nv-ease-out-expo);
  }
  .nv-m-nk-slide-enter-from,
  .nv-m-nk-slide-leave-to {
    transform: translateY(100%);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-nk-fade-enter-active,
    .nv-m-nk-fade-leave-active,
    .nv-m-nk-slide-enter-active,
    .nv-m-nk-slide-leave-active {
      transition: none;
    }
  }
}
</style>
