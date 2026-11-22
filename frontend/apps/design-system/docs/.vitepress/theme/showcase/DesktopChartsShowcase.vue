<script setup lang="ts">
import {
  NvAreaChart,
  NvBarChart,
  NvCard,
  NvDonutChart,
  NvLineChart,
  NvMetricCard,
  Progress,
  NvStatusBadge,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import { vReveal } from './showcaseReveal'

import DesktopTableShowcase from './DesktopTableShowcase.vue'

const proMetrics = [
  {
    label: '产能利用率',
    value: '87.4%',
    trend: { value: '+4.2%', direction: 'up' as const },
    hint: '近 7 日均值',
    series: [72, 74, 73, 78, 80, 79, 83, 85, 87],
  },
  {
    label: 'OEE 综合效率',
    value: '78.6%',
    trend: { value: '-1.1%', direction: 'down' as const },
    hint: '目标 82%',
    series: [82, 81, 83, 80, 79, 80, 78, 79, 78],
  },
  {
    label: '当班产出',
    value: '4,210',
    trend: { value: '+312', direction: 'up' as const },
    hint: '件 · 截至 16:00',
    series: [2100, 2400, 2800, 3000, 3300, 3600, 3900, 4050, 4210],
  },
  {
    label: '在线良率',
    value: '99.2%',
    trend: { value: '0.0%', direction: 'flat' as const },
    hint: '质检实时判定',
    series: [99.0, 99.3, 99.1, 99.2, 99.0, 99.4, 99.2, 99.1, 99.2],
  },
]
// ---- Charts ----
const outputSeries = [
  { label: '08:00', value: 420 },
  { label: '10:00', value: 680 },
  { label: '12:00', value: 1240 },
  { label: '14:00', value: 1880 },
  { label: '16:00', value: 2610 },
  { label: '18:00', value: 3180 },
  { label: '20:00', value: 4210 },
]
const planActual = [
  { day: '周一', plan: 900, actual: 860 },
  { day: '周二', plan: 950, actual: 980 },
  { day: '周三', plan: 1000, actual: 940 },
  { day: '周四', plan: 1000, actual: 1060 },
  { day: '周五', plan: 1100, actual: 1020 },
  { day: '周六', plan: 700, actual: 720 },
]
const planActualSeries = [
  { key: 'plan', label: '计划' },
  { key: 'actual', label: '实际' },
]
const outputByCenter = [
  { center: 'CNC-07', good: 412, scrap: 18 },
  { center: 'FORGE-02', good: 1200, scrap: 24 },
  { center: 'CNC-11', good: 96, scrap: 6 },
  { center: 'ASM-04', good: 188, scrap: 12 },
  { center: 'STAMP-01', good: 2740, scrap: 60 },
]
const outputByCenterSeries = [
  { key: 'good', label: '良品' },
  { key: 'scrap', label: '废品' },
]
const statusMix = [
  { label: '执行中', value: 38 },
  { label: '已完成', value: 52 },
  { label: '待处理', value: 22 },
  { label: '阻塞', value: 6 },
]

// ---- Workorders table ----
const workOrders = [
  {
    code: 'WO-2406-0413',
    product: '前桥壳体 A2',
    center: 'WC-CNC-07',
    qty: 480,
    done: 412,
    status: 'running',
  },
  {
    code: 'WO-2406-0419',
    product: '转向节 L',
    center: 'WC-FORGE-02',
    qty: 1200,
    done: 1200,
    status: 'completed',
  },
  {
    code: 'WO-2406-0421',
    product: '齿轮箱端盖',
    center: 'WC-CNC-11',
    qty: 320,
    done: 96,
    status: 'ready',
  },
  {
    code: 'WO-2406-0426',
    product: '液压阀体 V3',
    center: 'WC-ASM-04',
    qty: 640,
    done: 188,
    status: 'blocked',
  },
  {
    code: 'WO-2406-0430',
    product: '电机定子叠片',
    center: 'WC-STAMP-01',
    qty: 5000,
    done: 2740,
    status: 'pending',
  },
]

const progress = computed(
  () => (workOrder: { done: number; qty: number }) =>
    Math.round((workOrder.done / workOrder.qty) * 100),
)
</script>

<template>
  <section id="charts" v-reveal class="scroll-mt-20">
    <div class="nv-eyebrow">
      <h2 class="text-xl font-semibold tracking-tight">数据可视化</h2>
      <p class="mt-1.5 text-sm text-muted-foreground">
        基于 unovis：面积、折线、柱状、环形覆盖常见工厂场景；颜色走令牌，随品牌色重着色。
      </p>
    </div>

    <div class="mt-6 grid gap-4 lg:grid-cols-2">
      <NvCard class="p-6">
        <div class="flex items-center justify-between">
          <div>
            <h3 class="text-sm font-semibold">当班累计产出</h3>
            <p class="mt-0.5 text-xs text-muted-foreground">面积图 · 每 2 小时采样</p>
          </div>
          <NvStatusBadge value="running" pulse />
        </div>
        <NvAreaChart class="mt-4" :data="outputSeries" :height="220" value-suffix=" 件" />
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">计划 vs 实际产量</h3>
        <p class="mt-0.5 text-xs text-muted-foreground">多系列折线 · 本周</p>
        <NvLineChart
          class="mt-4"
          :data="planActual"
          x-key="day"
          :series="planActualSeries"
          :height="220"
          value-suffix=" 件"
        />
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">各工作中心产出</h3>
        <p class="mt-0.5 text-xs text-muted-foreground">分组柱状 · 良品 / 废品</p>
        <NvBarChart
          class="mt-4"
          :data="outputByCenter"
          x-key="center"
          :series="outputByCenterSeries"
          :height="220"
        />
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">工单状态构成</h3>
        <p class="mt-0.5 mb-4 text-xs text-muted-foreground">环形占比 · 实时</p>
        <NvDonutChart
          :data="statusMix"
          :height="180"
          central-label="118"
          central-sub-label="工单"
        />
      </NvCard>
    </div>

    <!-- 指标卡 -->
    <div class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <NvMetricCard
        v-for="(m, i) in proMetrics"
        :key="m.label"
        v-reveal="i"
        :label="m.label"
        :value="m.value"
        :trend="m.trend"
        :hint="m.hint"
        :series="m.series"
      />
    </div>

    <!-- 数据表 -->
    <div class="mt-4 overflow-hidden rounded-xl border border-border bg-card shadow-xs">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>工单号</TableHead>
            <TableHead>产品</TableHead>
            <TableHead>工作中心</TableHead>
            <TableHead class="text-right">进度</TableHead>
            <TableHead>状态</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="wo in workOrders" :key="wo.code">
            <TableCell class="font-mono text-xs">{{ wo.code }}</TableCell>
            <TableCell class="font-medium">{{ wo.product }}</TableCell>
            <TableCell class="font-mono text-xs text-muted-foreground">{{ wo.center }}</TableCell>
            <TableCell>
              <div class="flex items-center justify-end gap-2">
                <Progress :model-value="progress(wo)" class="h-1.5 w-24" />
                <span class="w-16 text-right font-mono text-xs text-muted-foreground tabular-nums">
                  {{ wo.done }}/{{ wo.qty }}
                </span>
              </div>
            </TableCell>
            <TableCell><NvStatusBadge :value="wo.status" /></TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <DesktopTableShowcase />
  </section>
</template>

<style scoped src="./showcase-reveal.css"></style>
