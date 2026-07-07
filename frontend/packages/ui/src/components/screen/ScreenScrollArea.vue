<script setup lang="ts">
/**
 * Screen — scroll area. shadcn / reka-ui ScrollArea 的大屏重制版（shadcn 原版
 * 零改动，按定制规矩复制重建）：悬浮细滚动条 —— 无轨道底色、发丝级圆角 thumb、
 * hover / 滚动时才浮现；上/下缘渐隐 + 微呼吸箭头提示还有内容可滑（挂墙远视距
 * 也能一眼看出列表未到底）。
 * ⚠️ 虚拟滚动容器（useVirtualList 需绑定原生滚动元素 containerProps）不适用本组件。
 */
import { ScrollAreaRoot, ScrollAreaScrollbar, ScrollAreaThumb, ScrollAreaViewport } from 'reka-ui'
import { onBeforeUnmount, onMounted, ref } from 'vue'

withDefaults(
  defineProps<{
    /** 滚动条浮现时机：hover=悬停/滚动时（默认，挂墙常态无条干净）；always=常驻 */
    type?: 'auto' | 'always' | 'scroll' | 'hover'
  }>(),
  { type: 'hover' },
)

// —— 上/下可滑提示：监听 viewport 滚动 + 尺寸变化（数据轮询会改内容高） ——
const vpRef = ref<InstanceType<typeof ScrollAreaViewport> | null>(null)
const canUp = ref(false)
const canDown = ref(false)
let ro: ResizeObserver | null = null
let vpEl: HTMLElement | null = null

function update() {
  if (!vpEl) return
  canUp.value = vpEl.scrollTop > 2
  canDown.value = vpEl.scrollTop + vpEl.clientHeight < vpEl.scrollHeight - 2
}

onMounted(() => {
  // reka Viewport 是多根组件（div + style），$el 会落在占位节点上 —— 优先用它
  // expose 的 viewportElement，退化路径从组件根节点找 class。
  const inst = vpRef.value as unknown as { viewportElement?: HTMLElement; $el?: Node } | null
  vpEl =
    inst?.viewportElement ??
    ((inst?.$el instanceof HTMLElement && inst.$el.classList.contains('sb-sa-vp')
      ? inst.$el
      : (inst?.$el?.parentElement?.querySelector(':scope > .sb-sa-vp') as HTMLElement | null)) ??
      null)
  if (!vpEl) return
  vpEl.addEventListener('scroll', update, { passive: true })
  ro = new ResizeObserver(update)
  ro.observe(vpEl)
  if (vpEl.firstElementChild) ro.observe(vpEl.firstElementChild)
  update()
})
onBeforeUnmount(() => {
  vpEl?.removeEventListener('scroll', update)
  ro?.disconnect()
})
</script>

<template>
  <ScrollAreaRoot class="sb-sa" :type="type" :scroll-hide-delay="450">
    <ScrollAreaViewport ref="vpRef" class="sb-sa-vp">
      <slot />
    </ScrollAreaViewport>
    <ScrollAreaScrollbar class="sb-sa-bar" orientation="vertical">
      <ScrollAreaThumb class="sb-sa-thumb" />
    </ScrollAreaScrollbar>
    <div v-show="canUp" class="sb-sa-hint up" aria-hidden="true"><i /></div>
    <div v-show="canDown" class="sb-sa-hint down" aria-hidden="true"><i /></div>
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
/* reka viewport 内容层默认 display:table（横向滚动测量用）——垂直滚动场景下
   table 会让内容按固有宽撑开、flex/ellipsis 全失效（行宽溢出面板）。改回块级。 */
:deep(.sb-sa-vp > div) {
  display: block !important;
  min-width: 0 !important;
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
  z-index: 2;
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
/* 可滑提示：边缘渐隐遮罩 + 微呼吸人字箭头（隐式、不占布局、不截点击） */
.sb-sa-hint {
  position: absolute;
  left: 0;
  right: 8px;
  height: 22px;
  pointer-events: none;
  display: flex;
  justify-content: center;
  z-index: 1;
}
.sb-sa-hint.up {
  top: 0;
  background: linear-gradient(180deg, rgba(7, 11, 19, 0.82), transparent);
  align-items: flex-start;
}
.sb-sa-hint.down {
  bottom: 0;
  background: linear-gradient(0deg, rgba(7, 11, 19, 0.82), transparent);
  align-items: flex-end;
}
.sb-sa-hint i {
  width: 9px;
  height: 9px;
  margin: 2px 0;
  border-right: 1.5px solid rgba(160, 200, 245, 0.5);
  border-bottom: 1.5px solid rgba(160, 200, 245, 0.5);
  animation: sb-sa-breath 2.8s ease-in-out infinite;
}
.sb-sa-hint.up i {
  transform: rotate(-135deg);
}
.sb-sa-hint.down i {
  transform: rotate(45deg);
}
@keyframes sb-sa-breath {
  50% {
    opacity: 0.3;
  }
}
@media (prefers-reduced-motion: reduce) {
  .sb-sa-bar,
  .sb-sa-thumb {
    transition: none;
  }
  .sb-sa-hint i {
    animation: none;
  }
}
</style>
