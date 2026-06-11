<script setup lang="ts">
// 图例:讲清甘特的视觉语言(状态/关系/时间),紧凑一行,弱化为辅助信息。
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-5 gap-y-2 border-t border-border/50 bg-card/60 px-4 py-2 text-xs text-muted-foreground">
    <span class="inline-flex items-center gap-1.5">
      <span class="h-2.5 w-6 rounded-[3px] bg-brand"></span>工序
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
  </div>
</template>
