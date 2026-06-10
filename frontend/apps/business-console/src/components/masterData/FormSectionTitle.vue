<script setup lang="ts">
// 表单分段标题：必须明显区别于字段标题（字段标题是 text-sm font-medium）。
// 用「主色短条 + 加粗 + 下边线 + 上间距」给表单清晰的视觉分组与层次引导，
// 避免"分段标题与字段标题一模一样、整片没有视觉引导"。所有主数据表单统一用它。
</script>

<template>
  <div class="mt-1 flex items-center gap-2 border-b border-border/60 pb-2">
    <span class="h-4 w-0.5 shrink-0 rounded-full bg-brand" aria-hidden="true" />
    <span class="text-base font-semibold tracking-wide text-foreground"><slot /></span>
  </div>
</template>
