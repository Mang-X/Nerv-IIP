<script setup lang="ts">
import type { DialogContentEmits, DialogContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
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
import { NV_SHEET_BLOCK_SIZE, NV_SHEET_INLINE_SIZE, type NvSheetSize } from '.'

/**
 * Pro — sheet content (does NOT touch原版 Sheet). Keeps base's `side`
 * (top/right/bottom/left) variants with directional slide-in/out, a blurred
 * overlay, a `bg-card` panel, and a built-in close affordance.
 */
interface NvSheetContentProps extends DialogContentProps {
  class?: HTMLAttributes['class']
  side?: 'top' | 'right' | 'bottom' | 'left'
  showCloseButton?: boolean
  /** 尺寸档位，默认 `md`；表格类内容用 `xl`。见 `NvSheetSize`。 */
  size?: NvSheetSize
}

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(defineProps<NvSheetContentProps>(), {
  side: 'right',
  showCloseButton: true,
  size: 'md',
})
const emits = defineEmits<DialogContentEmits>()

const delegatedProps = reactiveOmit(props, 'class', 'side', 'showCloseButton', 'size')

const forwarded = useForwardPropsEmits(delegatedProps, emits)

const sizeClass = computed(
  () => `${NV_SHEET_INLINE_SIZE[props.size]} ${NV_SHEET_BLOCK_SIZE[props.size]}`,
)
</script>

<template>
  <DialogPortal>
    <DialogOverlay
      class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/40 backdrop-blur-sm"
    />
    <DialogContent
      data-slot="nv-sheet-content"
      :data-side="side"
      :class="
        cn(
          'bg-card text-card-foreground border-border fixed z-50 flex flex-col gap-4 bg-clip-padding text-sm shadow-lg transition duration-200 ease-in-out data-[side=bottom]:inset-x-0 data-[side=bottom]:bottom-0 data-[side=bottom]:h-auto data-[side=bottom]:border-t data-[side=left]:inset-y-0 data-[side=left]:left-0 data-[side=left]:h-full data-[side=left]:w-3/4 data-[side=left]:border-r data-[side=right]:inset-y-0 data-[side=right]:right-0 data-[side=right]:h-full data-[side=right]:w-3/4 data-[side=right]:border-l data-[side=top]:inset-x-0 data-[side=top]:top-0 data-[side=top]:h-auto data-[side=top]:border-b data-open:animate-in data-open:fade-in-0 data-[side=bottom]:data-open:slide-in-from-bottom-10 data-[side=left]:data-open:slide-in-from-left-10 data-[side=right]:data-open:slide-in-from-right-10 data-[side=top]:data-open:slide-in-from-top-10 data-closed:animate-out data-closed:fade-out-0 data-[side=bottom]:data-closed:slide-out-to-bottom-10 data-[side=left]:data-closed:slide-out-to-left-10 data-[side=right]:data-closed:slide-out-to-right-10 data-[side=top]:data-closed:slide-out-to-top-10',
          // 尺寸档位先于 props.class，调用点仍可用 sm:max-w-* 覆盖（tailwind-merge 后写优先）。
          sizeClass,
          // 抽屉内容默认可纵向滚动：此前 21 个调用点里有 17 个在手写 overflow-y-auto。
          'overflow-y-auto overscroll-contain',
          // 直接子元素一律 min-w-0：flex 子项的 `min-width:auto` 会让宽表格/多列网格按
          // **内容最小宽**把自己撑过抽屉边界（#1418：CAPA 详情抽屉内容排到 780px，
          // 而抽屉只有 512px，右侧 285px 直接溢出到视口外）。钉住 min-w-0 之后，超宽内容
          // 各自在自己的 overflow 容器里横向滚动（NvDataTable 本就自带 overflow-auto），
          // 而不是把整个抽屉顶破。
          '[&>*]:min-w-0',
          // 正文子元素补水平内边距：基类只有 `gap-4`，**一点水平 padding 都没有**，
          // 于是不自己写 `px-*` 的抽屉，内容左右两侧直接贴死在面板边缘——owner 第五轮
          // 亲验点名（工单紧急度解释抽屉里「计算时间」「保存优先级」与 CR/Slack 判定表
          // 全压在右缘上），而 #1421 当时只加了 overflow 与 min-w-0，没管内边距。
          //
          // 头/尾自带 `p-4` 且是 sticky、要整幅 `bg-card` 背景，必须排除：否则双重内边距，
          // 且背景跟着缩进后会在两侧露出底色。
          '[&>*:not([data-slot=nv-sheet-header]):not([data-slot=nv-sheet-footer])]:px-4',
          props.class,
        )
      "
      v-bind="{ ...$attrs, ...forwarded }"
    >
      <slot />

      <DialogClose
        v-if="showCloseButton"
        data-slot="nv-sheet-close"
        class="absolute top-4 right-4 flex size-7 items-center justify-center rounded-md text-muted-foreground opacity-70 transition-[color,opacity,background] hover:bg-muted hover:text-foreground hover:opacity-100 focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
        aria-label="关闭"
      >
        <XIcon class="size-4" aria-hidden="true" />
      </DialogClose>
    </DialogContent>
  </DialogPortal>
</template>
