<script setup lang="ts">
import { Button, useColorMode } from '@nerv-iip/ui'
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'
import { computed, ref } from 'vue'
import { previewPlan } from './schedulingPreviewData'

// 开发预览(非产品页/不进路由):用样例数据展示排产工作台,供视觉确认与组件开发。
const model = computed(() => toModel(previewPlan))
const { isDark, toggle } = useColorMode()
const readOnly = ref(false)
</script>

<template>
  <div class="min-h-screen bg-background p-6 text-foreground">
    <div class="mx-auto flex max-w-[1400px] flex-col gap-4">
      <header class="flex flex-wrap items-center gap-3">
        <div class="mr-auto">
          <h1 class="text-2xl font-semibold tracking-tight">排产工作台 · 组件预览</h1>
          <p class="text-sm text-muted-foreground">@nerv-iip/scheduling · 样例数据 · 默认 NativeEngine 渲染</p>
        </div>
        <Button size="sm" variant="outline" @click="toggle()">{{ isDark ? '切到亮色' : '切到暗色' }}</Button>
        <Button size="sm" variant="outline" @click="readOnly = !readOnly">{{ readOnly ? '允许编辑' : '设为只读' }}</Button>
      </header>

      <div class="h-[calc(100vh-9rem)] min-h-[520px]">
        <SchedulingWorkbench :model="model" :read-only="readOnly" engine-kind="native" />
      </div>
    </div>
  </div>
</template>
