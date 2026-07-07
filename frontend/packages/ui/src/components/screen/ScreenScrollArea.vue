<script setup lang="ts">
/**
 * Screen — scroll area. shadcn / reka-ui ScrollArea 的大屏重制版（shadcn 原版
 * 零改动，按定制规矩复制重建）：悬浮细滚动条 —— 无轨道底色、发丝级圆角 thumb、
 * hover / 滚动时才浮现，替代原生滚动条在深色挂墙屏上的粗灰观感。
 * ⚠️ 虚拟滚动容器（useVirtualList 需绑定原生滚动元素 containerProps）不适用本组件。
 */
import { ScrollAreaRoot, ScrollAreaScrollbar, ScrollAreaThumb, ScrollAreaViewport } from 'reka-ui'

withDefaults(
  defineProps<{
    /** 滚动条浮现时机：hover=悬停/滚动时（默认，挂墙常态无条干净）；always=常驻 */
    type?: 'auto' | 'always' | 'scroll' | 'hover'
  }>(),
  { type: 'hover' },
)
</script>

<template>
  <ScrollAreaRoot class="sb-sa" :type="type" :scroll-hide-delay="450">
    <ScrollAreaViewport class="sb-sa-vp">
      <slot />
    </ScrollAreaViewport>
    <ScrollAreaScrollbar class="sb-sa-bar" orientation="vertical">
      <ScrollAreaThumb class="sb-sa-thumb" />
    </ScrollAreaScrollbar>
  </ScrollAreaRoot>
</template>

<style scoped>
/* flex 链传高：调用方用 height / max-height / flex 约束根节点，viewport 都能
   拿到确定高度（height:100% 在 max-height 场景下无参照，会退化成"裁切不滚动"） */
.sb-sa {
  position: relative;
  overflow: hidden;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.sb-sa-vp {
  flex: 1;
  min-height: 0;
  width: 100%;
}
.sb-sa-bar {
  display: flex;
  touch-action: none;
  user-select: none;
  width: 8px;
  padding: 2px 1px;
  background: transparent;
  transition: opacity 0.22s var(--sb-ease);
}
.sb-sa-thumb {
  position: relative;
  flex: 1;
  min-height: 28px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.13);
  transition: background 0.18s var(--sb-ease);
}
.sb-sa-bar:hover .sb-sa-thumb,
.sb-sa-bar[data-state='visible']:active .sb-sa-thumb {
  background: rgba(135, 208, 255, 0.34);
}
@media (prefers-reduced-motion: reduce) {
  .sb-sa-bar,
  .sb-sa-thumb {
    transition: none;
  }
}
</style>
