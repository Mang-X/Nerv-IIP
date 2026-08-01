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

/**
 * Pro — dialog content (does NOT touch原版 Dialog). Blurred overlay, centered
 * card with exponential scale-in, built-in close affordance.
 *
 * 滚动归属：**遮罩层滚动，弹框本体不定高**。此前本体用 `top-1/2 -translate-y-1/2`
 * 居中且既无 `max-height` 也无 `overflow`,内容一旦高过视口就上下对称裁切、底部
 * 操作按钮直接落到视口外够不着(#1418:ECO「发布变更」971px 弹框在 900px 视口
 * 上下各裁 35px,取消/发布两个按钮全在视口外)。这也是 9 个调用点各自手写
 * `max-h-[85vh] overflow-y-auto` 打补丁的原因。
 *
 * 改成遮罩层 `grid place-items-center overflow-y-auto` + 本体 `my-8`:网格行轨道
 * 按内容撑高,弹框矮时依旧居中,弹框高时行轨道长过遮罩、弹框贴着上边距排布,
 * 遮罩滚动即可读全 —— 不会出现 flex 居中那种「上半截永远滚不到」的经典陷阱。
 * 配套 `NvDialogFooter` 默认 `sticky bottom-0`,操作按钮常驻。
 */
const props = defineProps<DialogContentProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<DialogContentEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)

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
        v-bind="forwarded"
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 relative z-50 my-8 grid w-[calc(100%-2rem)] max-w-md grid-cols-1 gap-4 [&>*]:min-w-0 rounded-xl border border-border bg-card p-6 text-card-foreground shadow-lg duration-200 outline-none',
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
