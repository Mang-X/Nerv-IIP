<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { DialogClose } from 'reka-ui'
import { cn } from '../../../lib/utils'
import { NvButton } from '../button'

/**
 * Pro — dialog footer. Mirrors base `DialogFooter`'s API so it drops in: pass
 * `show-close-button` for an auto outline "关闭" button, or compose your own
 * actions in the default slot.
 *
 * 默认 `sticky bottom-0`，相对最近的滚动容器吸底，并在正常文档流中保留完整高度。
 * 不用负外边距抵消父级 padding：Footer 既可直接位于 Content，也可嵌入无 padding
 * 或局部滚动的 form。水平边界由父容器负责，字段与操作区间距由父级 gap 负责。
 * 自身上下 padding 留出按钮呼吸空间，不能用负底边距压缩末字段的滚动占位。
 */
const props = withDefaults(
  defineProps<{
    class?: HTMLAttributes['class']
    showCloseButton?: boolean
  }>(),
  {
    showCloseButton: false,
  },
)
</script>

<template>
  <div
    data-slot="nv-dialog-footer"
    :class="
      cn(
        'sticky bottom-0 z-10 flex flex-col-reverse gap-2 rounded-b-xl bg-card py-3 sm:flex-row sm:justify-end',
        props.class,
      )
    "
  >
    <slot />
    <DialogClose v-if="showCloseButton" as-child>
      <NvButton variant="outline">关闭</NvButton>
    </DialogClose>
  </div>
</template>
