<script setup lang="ts">
import type { AlertDialogContentEmits, AlertDialogContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import {
  AlertDialogContent,
  AlertDialogOverlay,
  AlertDialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — alert-dialog content (does NOT touch原版 AlertDialog). Blurred overlay,
 * centered card with exponential scale-in. AlertDialog has no top-right close
 * affordance (base behavior preserved) — dismiss is via Cancel/Action only.
 *
 * 滚动归属与 `NvDialogContent` 同款:遮罩层滚动、本体不定高。原先同样是
 * `top-1/2 -translate-y-1/2` 且无 `max-height`/`overflow`,内容高过视口即上下对称
 * 裁切、确认按钮落到视口外(#1418)。确认类弹框够不着「确定」比普通弹框更致命。
 */
defineOptions({
  inheritAttrs: false,
})

const props = defineProps<AlertDialogContentProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<AlertDialogContentEmits>()

const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <AlertDialogPortal>
    <AlertDialogOverlay
      class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 grid place-items-center overflow-y-auto overscroll-contain bg-black/40 backdrop-blur-sm"
    >
      <AlertDialogContent
        data-slot="nv-alert-dialog-content"
        v-bind="{ ...$attrs, ...forwarded }"
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 relative z-50 my-8 grid w-[calc(100%-2rem)] max-w-md grid-cols-1 gap-4 [&>*]:min-w-0 rounded-xl border border-border bg-card p-6 text-card-foreground shadow-lg duration-200 outline-none',
            props.class,
          )
        "
      >
        <slot />
      </AlertDialogContent>
    </AlertDialogOverlay>
  </AlertDialogPortal>
</template>
