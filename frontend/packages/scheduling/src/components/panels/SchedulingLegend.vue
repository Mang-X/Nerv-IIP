<script setup lang="ts">
// 图例:讲清当前视图的视觉语言。分色与条形共用 --nerv-cat-* 全局变量,保证图例与条形一致。
// 视图感知:资源排产板隐藏甘特专属(计划基线/依赖/关键路径/里程碑),改讲齐套/换型/瓶颈。
withDefaults(defineProps<{ categories?: { key: string; label: string }[]; view?: 'order' | 'resource' }>(), {
  view: 'order',
})
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-5 gap-y-2 border-t border-border/50 bg-card/60 px-4 py-2 text-xs text-muted-foreground">
    <!-- 分色(与条形同色) -->
    <template v-if="categories?.length">
      <span v-for="c in categories" :key="c.key" class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px]" :style="{ background: `var(--nerv-cat-${c.key})` }"></span>
        {{ c.label }}
      </span>
      <span class="mx-1 h-3.5 w-px bg-border/60"></span>
    </template>

    <!-- 甘特专属:仅工单甘特 -->
    <template v-if="view === 'order'">
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] border border-dashed border-muted-foreground/50 bg-muted-foreground/15"></span>计划
      </span>
      <span class="inline-flex items-center gap-1.5">
        <svg width="20" height="10" viewBox="0 0 20 10" aria-hidden="true" class="text-muted-foreground">
          <path d="M1 5h13" fill="none" stroke="currentColor" stroke-width="1.4" />
          <path d="M12 2l4 3-4 3" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" />
        </svg>
        依赖
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-0 w-6 border-t-2 border-dashed border-warning"></span>关键路径
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="size-2.5 rotate-45 rounded-[2px] bg-brand"></span>里程碑
      </span>
    </template>

    <!-- 资源排产板专属:卡片状态语言(优先级 / 插单 / 齐套 / 换型 / 瓶颈) -->
    <template v-else>
      <span class="inline-flex items-center gap-1.5">
        <span class="rounded bg-destructive/15 px-1 py-px text-[0.58rem] font-bold text-destructive">高</span>优先级
      </span>
      <span class="inline-flex items-center gap-1" style="color: oklch(0.7 0.17 60)">
        <svg viewBox="0 0 24 24" width="12" height="12" aria-hidden="true"><path d="M13 2L4.5 13.5H11l-1 8.5L19.5 10H13l0-8z" fill="currentColor" /></svg>
        <span class="text-muted-foreground">插单</span>
      </span>
      <span class="inline-flex items-center gap-1">
        <span class="size-1.5 rounded-full" style="background: oklch(0.6 0.14 150)"></span>
        <span class="size-1.5 rounded-full" style="background: oklch(0.82 0.15 85)"></span>
        <span class="size-1.5 rounded-full" style="background: oklch(0.58 0.2 25)"></span>
        齐套 足 / 缺 / 危
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="rounded bg-foreground/10 px-1.5 py-px text-[0.58rem] font-semibold">换型</span>换型耗时
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="rounded-[3px] bg-destructive/15 px-1.5 py-px text-[0.58rem] font-bold text-destructive">瓶颈</span>资源过载
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] border-2 border-destructive bg-destructive/15"></span>冲突
      </span>
      <span class="inline-flex items-center gap-1.5">
        <svg viewBox="0 0 24 24" width="11" height="11" aria-hidden="true" class="text-foreground"><path d="M8 10V7a4 4 0 0 1 8 0v3" fill="none" stroke="currentColor" stroke-width="2" /><rect x="5.5" y="10" width="13" height="9.5" rx="2.2" fill="currentColor" /></svg>
        锁定
      </span>
      <!-- 资源时间块:斜纹块,各色一类 -->
      <span class="inline-flex items-center gap-1.5">
        <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: oklch(0.55 0.02 260)"></span>设备维护
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: var(--destructive)"></span>计划停机
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: oklch(0.58 0.13 250)"></span>换线窗口
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: oklch(0.7 0.15 60)"></span>换型窗口
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] bg-foreground/[0.05]"></span>非工作 / 夜班
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-3.5 w-0.5 rounded-full bg-brand"></span>现在
      </span>
    </template>

    <!-- 甘特视图保留通用尾项 -->
    <template v-if="view === 'order'">
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] border-2 border-destructive bg-destructive/25"></span>冲突
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] border border-dashed border-brand/70"></span>锁定
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-2.5 w-6 rounded-[3px] bg-muted"></span>非工作 / 夜班
      </span>
      <span class="inline-flex items-center gap-1.5">
        <span class="h-3.5 w-0.5 rounded-full bg-brand"></span>现在
      </span>
    </template>
  </div>
</template>

<style scoped>
.nerv-leg-hatch {
  background-color: color-mix(in srgb, var(--h) 12%, transparent);
  background-image: repeating-linear-gradient(
    -45deg,
    transparent 0,
    transparent 2px,
    color-mix(in srgb, var(--h) 50%, transparent) 2px,
    color-mix(in srgb, var(--h) 50%, transparent) 3px
  );
  border: 1px solid color-mix(in srgb, var(--h) 45%, transparent);
}
</style>
