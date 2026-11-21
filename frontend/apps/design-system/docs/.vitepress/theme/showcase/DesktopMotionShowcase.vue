<script setup lang="ts">
import { NvCard } from '@nerv-iip/ui'
import { ActivityIcon, LayersIcon } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { vReveal } from './showcaseReveal'

const reduceMotion = ref(false)
onMounted(() => {
  reduceMotion.value =
    typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
})
</script>

<template>
  <section id="motion" v-reveal class="scroll-mt-20">
    <div class="nv-eyebrow">
      <h2 class="text-xl font-semibold tracking-tight">动效</h2>
      <p class="mt-1.5 text-sm text-muted-foreground">
        指数级 ease-out 缓动，150–320ms 区间，列表入场错峰；reduced-motion 下全部降级。
      </p>
    </div>
    <div class="mt-6 grid gap-4 sm:grid-cols-3">
      <NvCard
        v-for="(curve, i) in [
          { name: 'ease-out-quart', cls: 'ease-out-quart', use: '微交互 · 悬停聚焦' },
          { name: 'ease-out-expo', cls: 'ease-out-expo', use: '入场揭示 · 覆盖层' },
          { name: 'ease-in-out-quart', cls: 'ease-in-out-quart', use: '状态切换 · 位移' },
        ]"
        :key="curve.name"
        v-reveal="i"
        class="nv-motion-card group p-5"
      >
        <div class="flex items-center gap-2">
          <ActivityIcon class="size-4 text-brand" aria-hidden="true" />
          <span class="font-mono text-xs">{{ curve.name }}</span>
        </div>
        <div class="mt-4 h-1.5 w-full overflow-hidden rounded-full bg-muted">
          <span class="nv-motion-bar block h-full w-1/3 rounded-full bg-brand" :class="curve.cls" />
        </div>
        <p class="mt-3 text-sm text-muted-foreground">{{ curve.use }}</p>
      </NvCard>
    </div>
    <p class="mt-4 flex items-center gap-2 text-sm text-muted-foreground">
      <LayersIcon class="size-4" aria-hidden="true" />
      悬停卡片查看缓动；当前
      <span class="font-medium text-foreground">{{
        reduceMotion ? '已开启减弱动效' : '动效启用中'
      }}</span
      >。
    </p>
  </section>
</template>

<style scoped src="./showcase-reveal.css"></style>
<style scoped>
.nv-motion-bar {
  transform: translateX(0);
  transition: transform 1.1s var(--nv-ease-out-quart);
}
.nv-motion-card:hover .nv-motion-bar {
  transform: translateX(200%);
}
.nv-motion-card:hover .nv-motion-bar.ease-out-expo {
  transition-timing-function: var(--nv-ease-out-expo);
}
.nv-motion-card:hover .nv-motion-bar.ease-in-out-quart {
  transition-timing-function: var(--nv-ease-in-out-quart);
}

@media (prefers-reduced-motion: reduce) {
  .nv-motion-bar {
    transition: none;
  }
}
</style>
