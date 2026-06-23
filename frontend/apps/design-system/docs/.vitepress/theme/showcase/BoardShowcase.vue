<script setup lang="ts">
import {
  AreaChartPro,
  CardPro,
  messagePro,
  notificationPro,
  NotifierHost,
  Progress,
  QtyStepper,
  StationBar,
  StatTile,
  ThemeToggle,
  TouchButton,
  TouchSegmented,
} from '@nerv-iip/ui'
import { BellRingIcon, CheckCircleIcon, CircleStopIcon, PauseIcon } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

// 页面标题由 VitePress frontmatter 提供（原 definePage 已移除）

// 现场看板：尽量少的操作路径，触摸直达。
const now = ref('--:--:--')
let timer: ReturnType<typeof setInterval> | undefined
function tick() {
  now.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
}
onMounted(() => {
  tick()
  timer = setInterval(tick, 1000)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const qty = 480
const done = ref(412)
const reportQty = ref(12)
const progress = computed(() => Math.round((done.value / qty) * 100))
const running = ref(true)

function report() {
  done.value = Math.min(qty, done.value + reportQty.value)
  notificationPro.success('报工成功', {
    description: `本次 ${reportQty.value} 件，累计 ${done.value}/${qty}。`,
  })
  reportQty.value = 12
}
function pause() {
  running.value = !running.value
  messagePro.warning(running.value ? '已恢复运行' : '已暂停')
}
function call() {
  notificationPro.info('已呼叫班组长', { description: 'WC-CNC-07 请求支援，预计 3 分钟到位。' })
}
function finish() {
  notificationPro.success('工单完工', { description: 'WO-2406-0413 已提交质检。' })
}

const view = ref('queue')
const viewOptions = [
  { value: 'queue', label: '待加工' },
  { value: 'done', label: '已完成' },
]

const queue = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖', qty: 320 },
  { code: 'WO-2406-0426', product: '液压阀体 V3', qty: 640 },
  { code: 'WO-2406-0430', product: '电机定子叠片', qty: 5000 },
]
const finished = [
  { code: 'WO-2406-0408', product: '前桥壳体 A1', qty: 480 },
  { code: 'WO-2406-0405', product: '转向节 R', qty: 1200 },
]
const list = computed(() => (view.value === 'queue' ? queue : finished))

const takt = [
  { label: '1', value: 47 },
  { label: '2', value: 45 },
  { label: '3', value: 46 },
  { label: '4', value: 44 },
  { label: '5', value: 43 },
  { label: '6', value: 45 },
]
</script>

<template>
  <div class="ds-board min-h-screen bg-background p-5 text-foreground">
    <div class="mx-auto flex max-w-[1400px] flex-col gap-5">
      <StationBar
        station="WC-CNC-07 · 精密加工"
        :status-label="running ? '运行中' : '已暂停'"
        :tone="running ? 'success' : 'warning'"
        :pulse="running"
      >
        <template #right>
          <div class="text-sm text-muted-foreground">早班 · 张伟</div>
          <div class="font-mono text-2xl font-semibold tabular-nums">{{ now }}</div>
          <ThemeToggle />
        </template>
      </StationBar>

      <div class="grid gap-5 lg:grid-cols-3">
        <!-- 当前工单 -->
        <CardPro class="lg:col-span-2 p-6">
          <div class="flex items-start justify-between gap-4">
            <div>
              <p class="text-sm text-muted-foreground">当前工单</p>
              <p class="mt-1 font-mono text-xl font-semibold">WO-2406-0413</p>
              <p class="mt-0.5 text-lg">前桥壳体 A2</p>
            </div>
            <div class="text-right">
              <p class="text-sm text-muted-foreground">完成进度</p>
              <p class="mt-1 text-4xl font-semibold tabular-nums">
                {{ done }}<span class="text-xl text-muted-foreground">/{{ qty }}</span>
              </p>
            </div>
          </div>
          <Progress :model-value="progress" class="mt-4 h-2.5" />

          <div class="mt-6 flex flex-wrap items-center gap-4">
            <div>
              <p class="mb-1.5 text-sm text-muted-foreground">报工数量</p>
              <QtyStepper v-model="reportQty" :min="1" :max="qty - done" :step="1" />
            </div>
            <div class="flex flex-1 flex-wrap items-end gap-3">
              <TouchButton variant="success" size="xl" class="flex-1" @click="report">
                <template #leading><CheckCircleIcon aria-hidden="true" /></template>
                报工
              </TouchButton>
              <TouchButton variant="warning" size="xl" @click="pause">
                <template #leading><PauseIcon aria-hidden="true" /></template>
                {{ running ? '暂停' : '恢复' }}
              </TouchButton>
              <TouchButton variant="outline" size="xl" @click="call">
                <template #leading><BellRingIcon aria-hidden="true" /></template>
                呼叫
              </TouchButton>
              <TouchButton variant="brand" size="xl" @click="finish">
                <template #leading><CircleStopIcon aria-hidden="true" /></template>
                完工
              </TouchButton>
            </div>
          </div>
        </CardPro>

        <!-- 实时指标 -->
        <div class="grid grid-cols-2 gap-4">
          <StatTile label="今日已完成" :value="done" unit="件" tone="brand" />
          <StatTile label="当前节拍" value="45" unit="s/件" tone="neutral" />
          <StatTile label="在线良率" value="99.2" unit="%" tone="success" />
          <StatTile label="设备 OEE" value="78.6" unit="%" tone="warning" />
        </div>
      </div>

      <div class="grid gap-5 lg:grid-cols-3">
        <!-- 队列 -->
        <CardPro class="lg:col-span-2 p-6">
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-semibold">工单队列</h2>
            <TouchSegmented v-model="view" :options="viewOptions" />
          </div>
          <ul class="mt-4 divide-y divide-border">
            <li v-for="item in list" :key="item.code" class="flex min-h-16 items-center gap-4 py-3">
              <span class="font-mono text-base text-muted-foreground">{{ item.code }}</span>
              <span class="flex-1 text-lg font-medium">{{ item.product }}</span>
              <span class="text-lg tabular-nums text-muted-foreground">{{ item.qty }} 件</span>
            </li>
          </ul>
        </CardPro>

        <!-- 节拍趋势 -->
        <CardPro class="p-6">
          <h2 class="text-lg font-semibold">节拍趋势</h2>
          <p class="mt-0.5 text-sm text-muted-foreground">秒 / 件 · 近 6 件</p>
          <AreaChartPro class="mt-4" :data="takt" :height="160" value-suffix=" s" />
        </CardPro>
      </div>
    </div>

    <NotifierHost />
  </div>
</template>
