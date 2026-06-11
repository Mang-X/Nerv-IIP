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

const OWNERS = ['张伟', '李强', '王磊', '刘洋', '陈刚', '赵敏', '孙凯']
const PRIORITIES = ['high', 'medium', 'low'] as const
const STATUSES = [
  { label: '已完成', tone: 'success' as const, progress: 1 },
  { label: '进行中', tone: 'info' as const, progress: 0.55 },
  { label: '未开始', tone: 'neutral' as const, progress: 0 },
]
const WC_COLOR: Record<string, string> = {
  '激光切割-01': 'cut',
  '折弯-02': 'bend',
  '焊接-01': 'weld',
  '加工中心-03': 'mach',
}

const model = computed(() => {
  const m = toModel(previewPlan)
  let i = 0
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
    const st = STATUSES[i % STATUSES.length]
    t.owner = OWNERS[i % OWNERS.length]
    t.priority = PRIORITIES[i % PRIORITIES.length]
    t.status = { label: st.label, tone: st.tone }
    t.progress = st.progress
    t.colorKey = WC_COLOR[wc] ?? 'cut'
    // 计划基线:比实际早 1 小时(演示计划 vs 实际偏差)。
    t.plannedStartUtc = new Date(Date.parse(t.startUtc) - 3_600_000).toISOString()
    t.plannedEndUtc = new Date(Date.parse(t.endUtc) - 3_600_000).toISOString()
    i += 1
  }
  // 标准甘特里程碑:独立一行 + 菱形(零时长)。
  const milestone = (id: string, orderId: string, text: string, whenUtc: string, colorKey: string) => ({
    id,
    orderId,
    operationId: '',
    operationSequence: 99,
    parentId: `order:${orderId}`,
    type: 'operation' as const,
    text,
    startUtc: whenUtc,
    endUtc: whenUtc,
    isMilestone: true,
    locked: false,
    hasConflict: false,
    colorKey,
  })
  m.tasks.push(milestone('ms-weld', 'WO-2026-001', '冲焊完成', '2026-06-10T19:00:00.000Z', 'weld'))
  m.tasks.push(milestone('ms-final', 'WO-2026-003', '总装下线', '2026-06-11T04:00:00.000Z', 'mach'))

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
