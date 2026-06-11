<script setup lang="ts">
import { Button, useColorMode } from '@nerv-iip/ui'
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'
import { computed, ref } from 'vue'
import { previewPlan } from './schedulingPreviewData'

// 开发预览(非产品页/不进路由):用样例数据展示排产工作台,供视觉确认与组件开发。
// 真实 APS 契约只带工作中心;此处为演示「按 设备/班组/产线 切换泳道」补多维样例归属。
const WC_DIMS: Record<string, { device: [string, string]; team: [string, string]; line: [string, string] }> = {
  '激光切割-01': { device: ['DEV-L1', '激光机 L1'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金产线'] },
  '折弯-02': { device: ['DEV-B2', '折弯机 B2'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金产线'] },
  '焊接-01': { device: ['DEV-W1', '焊接机器人 W1'], team: ['T-B', '乙班'], line: ['LN-WELD', '焊装产线'] },
  '加工中心-03': { device: ['DEV-C3', 'CNC 加工中心 03'], team: ['T-B', '乙班'], line: ['LN-MACH', '机加产线'] },
}

const model = computed(() => {
  const m = toModel(previewPlan)
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = t.workCenterId ?? ''
    const x = WC_DIMS[wc]
    t.dimensions = {
      workCenter: { id: wc, label: wc },
      ...(x
        ? {
            device: { id: x.device[0], label: x.device[1] },
            team: { id: x.team[0], label: x.team[1] },
            line: { id: x.line[0], label: x.line[1] },
          }
        : {}),
    }
  }
  m.groupDimensions = [
    { key: 'workCenter', label: '工作中心' },
    { key: 'device', label: '设备' },
    { key: 'team', label: '班组' },
    { key: 'line', label: '产线' },
  ]
  return m
})
const { isDark, toggle } = useColorMode()
const readOnly = ref(false)
const defaultView = new URLSearchParams(window.location.search).get('view') === 'resource' ? 'resource' : 'order'
</script>

<template>
  <div class="min-h-screen bg-background p-6 text-foreground">
    <div class="mx-auto flex max-w-[1400px] flex-col gap-4">
      <header class="flex flex-wrap items-center gap-3">
        <div class="mr-auto">
          <h1 class="text-2xl font-semibold tracking-tight">排产工作台 · 组件预览</h1>
          <p class="text-sm text-muted-foreground">@nerv-iip/scheduling · 样例数据 · DHTMLX 试用引擎渲染</p>
        </div>
        <Button size="sm" variant="outline" @click="toggle()">{{ isDark ? '切到亮色' : '切到暗色' }}</Button>
        <Button size="sm" variant="outline" @click="readOnly = !readOnly">{{ readOnly ? '允许编辑' : '设为只读' }}</Button>
      </header>

      <div class="h-[calc(100vh-9rem)] min-h-[520px]">
        <SchedulingWorkbench :model="model" :read-only="readOnly" :default-view="defaultView" engine-kind="auto" />
      </div>
    </div>
  </div>
</template>
