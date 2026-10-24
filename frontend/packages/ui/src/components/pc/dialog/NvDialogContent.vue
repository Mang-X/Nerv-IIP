<script setup lang="ts">
import type { DialogContentEmits, DialogContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { XIcon } from '@lucide/vue'
import {
  DialogClose,
  DialogContent,
  DialogOverlay,
  DialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { cn } from '../../../lib/utils'
import { provide, toRef } from 'vue'
import { dialogScrollableKey } from './layout'

/**
 * 品牌对话框。scrollable 模式：Header / Body / Footer 三段，只有 Body 滚动。
 * form 仍拥有全部输入与提交按钮，操作区在滚动正文外正常占位。
 * 未启用时保持既有遮罩滚动，兼容旧组合。
 */
const props = withDefaults(
  defineProps<DialogContentProps & { class?: HTMLAttributes['class']; scrollable?: boolean }>(),
  { scrollable: false },
)
const emits = defineEmits<DialogContentEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class', 'scrollable'), emits)
provide(dialogScrollableKey, toRef(props, 'scrollable'))

/**
 * 遮罩层现在是滚动容器,点它的滚动条会落在弹框外侧、被判成 outside-click 而误关。
 * 命中点超出目标内容盒即视为点在滚动条上,放行不关闭(与原版 DialogScrollContent 同款)。
 */
function guardScrollbarDismiss(event: CustomEvent<{ originalEvent: PointerEvent }>) {
  const originalEvent = event.detail.originalEvent
  const target = originalEvent.target as HTMLElement | null
  if (!target) return
  if (originalEvent.offsetX > target.clientWidth || originalEvent.offsetY > target.clientHeight) {
    event.preventDefault()
  }
}
</script>

<template>
  <DialogPortal>
    <DialogOverlay
      class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 grid place-items-center overflow-y-auto overscroll-contain bg-black/40 backdrop-blur-sm"
    >
      <DialogContent
        data-slot="nv-dialog-content"
        :data-scrollable="props.scrollable ? '' : undefined"
        v-bind="forwarded"
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 relative z-50 my-8 grid w-[calc(100%-2rem)] max-w-md grid-cols-1 gap-4 [&>*]:min-w-0 rounded-xl border border-border bg-card p-6 text-card-foreground shadow-lg duration-200 outline-none',
            props.scrollable && 'flex max-h-[calc(100dvh-4rem)] flex-col gap-0 overflow-hidden p-0',
            props.class,
          )
        "
        @pointer-down-outside="guardScrollbarDismiss"
      >
        <slot />
        <DialogClose
          class="absolute top-4 right-4 flex size-7 items-center justify-center rounded-md text-muted-foreground opacity-70 transition-[color,opacity,background] hover:bg-muted hover:text-foreground hover:opacity-100 focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
          aria-label="关闭"
        >
          <XIcon class="size-4" aria-hidden="true" />
        </DialogClose>
      </DialogContent>
    </DialogOverlay>
  </DialogPortal>
</template>

<style>
@layer nv-components {
  /* form 保留原生提交语义；只有 NvDialogBody 滚动，不把操作区塞进滚动层。 */
  [data-slot='nv-dialog-content'][data-scrollable] > form {
    display: flex;
    min-height: 0;
    flex: 1 1 auto;
    flex-direction: column;
    overflow: hidden;
  }
}
</style>
