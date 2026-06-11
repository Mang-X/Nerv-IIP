<script setup lang="ts">
import { Button, useColorMode } from '@nerv-iip/ui'
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'
import { computed, ref } from 'vue'
import { previewPlan } from './schedulingPreviewData'

// 开发预览(非产品页/不进路由):用样例数据展示排产工作台,供视觉确认与组件开发。
// 真实 APS 契约只带工作中心;此处为演示「按 设备/班组/产线 切换泳道」补多维样例归属。
// 层级:工作中心(切割/折弯/焊装/机加中心) > 设备(具体机台)。ID 保留用于匹配,仅改展示名。
// 工作中心 = 中心级;设备 = 机台级(激光切割机/数控折弯机/焊接机器人/CNC加工中心)。
const WC_LABEL: Record<string, string> = {
  '激光切割-01': '切割中心',
  '折弯-02': '折弯中心',
  '焊接-01': '焊装中心',
  '加工中心-03': '机加中心',
}
const WC_DIMS: Record<string, { device: [string, string]; team: [string, string]; line: [string, string] }> = {
  '激光切割-01': { device: ['DEV-L1', '激光切割机 L1'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '折弯-02': { device: ['DEV-B2', '数控折弯机 B2'], team: ['T-A', '甲班'], line: ['LN-SHEET', '钣金线'] },
  '焊接-01': { device: ['DEV-W1', '焊接机器人 W1'], team: ['T-B', '乙班'], line: ['LN-WELD', '焊装线'] },
  '加工中心-03': { device: ['DEV-C3', '数控机床 M3'], team: ['T-B', '乙班'], line: ['LN-MACH', '机加线'] },
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
const ORDER_PRODUCT: Record<string, string> = {
  'WO-2026-001': '前减振器总成',
  'WO-2026-002': '后桥壳体',
  'WO-2026-003': '转向节',
}
const QTY = [2000, 1500, 1000, 800, 500, 1200, 900]
const DUE = ['2026-06-11T18:00:00.000Z', '2026-06-12T12:00:00.000Z']
const KIT = [1, 0.85, 0.7]
const CHANGEOVER = [20, 30, 40]
// 资源 KPI:利用率 / OEE / 换型次数 / 待料风险
const RES_KPI: Record<string, [number, number, number, number]> = {
  '激光切割-01': [0.88, 0.78, 6, 2],
  '折弯-02': [0.38, 0.85, 3, 0],
  '焊接-01': [1.12, 0.72, 8, 3],
  '加工中心-03': [0.96, 0.77, 7, 1],
}

const model = computed(() => {
  const m = toModel(previewPlan)
  let i = 0
  for (const t of m.tasks) {
    if (t.type !== 'operation') continue
    const wc = t.workCenterId ?? ''
    const x = WC_DIMS[wc]
    t.dimensions = {
      workCenter: { id: wc, label: WC_LABEL[wc] ?? wc },
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
    // 工单卡片信息(排产板用)。
    t.product = ORDER_PRODUCT[t.orderId] ?? '通用件'
    t.quantity = QTY[i % QTY.length]
    t.dueUtc = DUE[i % DUE.length]
    t.kitting = KIT[i % KIT.length]
    t.changeoverMin = CHANGEOVER[i % CHANGEOVER.length]
    t.load = [0.9, 0.85, 0.95, 1.05, 0.8][i % 5]
    t.isRush = i % 4 === 0
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

  // 资源时间块(非工单):维护 / 停机 / 换线 / 换型 —— 落在对应资源泳道,斜纹块、不可拖拽。
  const block = (
    id: string,
    kind: 'maintenance' | 'downtime' | 'lineChange' | 'changeover',
    wc: string,
    startUtc: string,
    endUtc: string,
  ) => {
    const x = WC_DIMS[wc]
    return {
      id,
      orderId: '',
      operationId: '',
      operationSequence: 0,
      type: 'operation' as const,
      text: '',
      blockKind: kind,
      startUtc,
      endUtc,
      resourceId: wc,
      workCenterId: wc,
      dimensions: {
        workCenter: { id: wc, label: WC_LABEL[wc] ?? wc },
        ...(x
          ? {
              device: { id: x.device[0], label: x.device[1] },
              team: { id: x.team[0], label: x.team[1] },
              line: { id: x.line[0], label: x.line[1] },
            }
          : {}),
      },
      locked: true,
      hasConflict: false,
    }
  }
  m.tasks.push(block('blk-maint', 'maintenance', '焊接-01', '2026-06-11T03:00:00.000Z', '2026-06-11T04:30:00.000Z'))
  m.tasks.push(block('blk-down', 'downtime', '加工中心-03', '2026-06-11T08:00:00.000Z', '2026-06-11T10:00:00.000Z'))
  m.tasks.push(block('blk-line', 'lineChange', '激光切割-01', '2026-06-11T06:00:00.000Z', '2026-06-11T07:00:00.000Z'))
  m.tasks.push(block('blk-co', 'changeover', '折弯-02', '2026-06-10T22:00:00.000Z', '2026-06-10T22:40:00.000Z'))

  // 资源 KPI(排产板左侧泳道头)。
  for (const r of m.resources) {
    const k = RES_KPI[r.id]
    if (k) {
      r.utilization = k[0]
      r.oee = k[1]
      r.changeoverCount = k[2]
      r.materialRisk = k[3]
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
