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
 * 默认 `sticky bottom-0`:长表单弹框滚动时操作按钮常驻底部,不跟着内容滚出视口
 * (#1418)。负外边距把 `bg-card` 铺到弹框自身 `p-6` 的内边距边缘,滚动内容不会从
 * 按钮下方的缝隙里透出来;`rounded-b-xl` 保证盖住圆角处不出现直角色块。弹框不滚动
 * 时 sticky 与 static 渲染完全一致 —— 短弹框零视觉变化。
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
        'sticky bottom-0 z-10 -mx-6 -mb-6 flex flex-col-reverse gap-2 rounded-b-xl bg-card px-6 pt-3 pb-6 sm:flex-row sm:justify-end',
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
