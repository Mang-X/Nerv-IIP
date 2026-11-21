<script setup lang="ts">
import {
  NvBadge,
  NvButton,
  NvCard,
  NvCheckbox,
  NvDatePicker,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvInput,
  Label,
  NvRadioGroup,
  NvRadioGroupItem,
  NvSwitch,
  NvLoader,
  nvMessage,
  nvNotification,
  Progress,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Separator,
  Skeleton,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  NvTimePicker,
  NvTooltip,
  NvTooltipContent,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import { ActivityIcon, GaugeIcon, PlusIcon, SearchIcon } from '@lucide/vue'
import { ref } from 'vue'
import { vReveal } from './showcaseReveal'

// ---- Components data ----
const emit = defineEmits<{ openCommand: [] }>()

const agreed = ref(true)
const lineValue = ref('line-a')
const searchValue = ref('')
const switchOn = ref(true)
const radioValue = ref('std')
const planTime = ref('08:30')
const planDate = ref('2026-06-18')
const dialogOpen = ref(false)

const proLoading = ref(false)
function runProAction() {
  proLoading.value = true
  window.setTimeout(() => {
    proLoading.value = false
    nvNotification.success('工单已派发', {
      description: 'WO-2406-0431 已进入 A 线排队队列，物料已锁定。',
    })
  }, 1500)
}
const loaderVariants = [
  { v: 'ring' as const, name: '环形' },
  { v: 'dots' as const, name: '点阵' },
  { v: 'bars' as const, name: '柱条' },
  { v: 'pulse' as const, name: '脉冲' },
]

// ---- Feedback ----
function fireMessage(kind: 'info' | 'success' | 'warning' | 'error') {
  const map = {
    info: () => nvMessage.info('已同步 MES 网关'),
    success: () => nvMessage.success('保存成功'),
    warning: () => nvMessage.warning('库存接近下限'),
    error: () => nvMessage.error('网络连接中断'),
  }
  map[kind]()
}
function fireLongMessage() {
  nvMessage.info(
    'WO-2406-0413 前桥壳体 A2 已下发至 A 线排队队列，物料已锁定，预计 14:20 开工，请关注节拍与首检结果。',
  )
}
function fireBurst() {
  nvMessage.success('已保存草稿')
  window.setTimeout(() => nvMessage.info('已同步 MES 网关'), 140)
  window.setTimeout(() => nvMessage.warning('库存接近下限'), 280)
}
function fireNotification(kind: 'info' | 'success' | 'warning' | 'error') {
  const map = {
    info: () =>
      nvNotification.info('排产已更新', { description: '今日计划重排，受影响工单 6 张。' }),
    success: () =>
      nvNotification.success('保养工单已创建', { description: 'CNC-07 · 今晚 22:00 执行。' }),
    warning: () =>
      nvNotification.warning('B 线物料不足', {
        description: '液压阀体 V3 缺口 452 件，已转采购申请。',
      }),
    error: () =>
      nvNotification.error('派工失败', { description: '工作中心 WC-ASM-04 处于阻塞状态。' }),
  }
  map[kind]()
}
</script>

<template>
  <section id="components" v-reveal class="scroll-mt-20">
    <div class="nv-eyebrow">
      <div class="flex flex-wrap items-center gap-2">
        <h2 class="text-xl font-semibold tracking-tight">组件</h2>
        <NvBadge variant="brand">Pro</NvBadge>
      </div>
      <p class="mt-1.5 text-sm text-muted-foreground">
        复制重建于 shadcn 之上：分层表面、品牌聚焦、内建加载与状态反馈。
      </p>
    </div>

    <!-- 按钮 -->
    <div class="mt-6 space-y-5 rounded-xl border border-border bg-card p-6 shadow-xs">
      <div class="flex flex-wrap items-center gap-3">
        <NvButton variant="brand">主操作</NvButton>
        <NvButton variant="default">默认</NvButton>
        <NvButton variant="secondary">次要</NvButton>
        <NvButton variant="outline">描边</NvButton>
        <NvButton variant="ghost">幽灵</NvButton>
        <NvButton variant="destructive">危险</NvButton>
        <NvButton variant="link">链接</NvButton>
      </div>
      <Separator />
      <div class="flex flex-wrap items-center gap-3">
        <NvButton size="sm">小号</NvButton>
        <NvButton>默认</NvButton>
        <NvButton size="lg">大号</NvButton>
        <NvButton size="icon" aria-label="新建"><PlusIcon aria-hidden="true" /></NvButton>
        <NvButton disabled>禁用</NvButton>
        <NvButton variant="brand" :loading="proLoading" class="min-w-28" @click="runProAction">
          {{ proLoading ? '派发中' : '确认派工' }}
        </NvButton>
      </div>
    </div>

    <!-- 徽章 + 状态 + 表单 -->
    <div class="mt-4 grid gap-4 lg:grid-cols-2">
      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">徽章与状态</h3>
        <div class="mt-4 flex flex-wrap gap-2">
          <NvBadge variant="brand">品牌</NvBadge>
          <NvBadge variant="success">已完成</NvBadge>
          <NvBadge variant="warning">待处理</NvBadge>
          <NvBadge variant="danger">阻塞</NvBadge>
          <NvBadge variant="solid">主要</NvBadge>
          <NvBadge>中性</NvBadge>
        </div>
        <div class="mt-4 flex flex-wrap gap-2">
          <NvStatusBadge value="running" pulse />
          <NvStatusBadge value="ready" />
          <NvStatusBadge value="completed" />
          <NvStatusBadge value="pending" />
          <NvStatusBadge value="blocked" />
        </div>
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">表单控件</h3>
        <div class="mt-4 grid gap-4">
          <div class="grid gap-2">
            <Label for="nv-search">搜索</Label>
            <NvInput id="nv-search" v-model="searchValue" placeholder="搜索工单号 / 产品">
              <template #leading><SearchIcon aria-hidden="true" /></template>
              <template #trailing>
                <kbd
                  class="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground"
                  >⌘K</kbd
                >
              </template>
            </NvInput>
          </div>
          <div class="grid gap-2">
            <Label for="nv-line">目标产线</Label>
            <NvSelect v-model="lineValue">
              <NvSelectTrigger id="nv-line"
                ><NvSelectValue placeholder="选择产线"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="line-a">A 线 · 精密加工</NvSelectItem>
                <NvSelectItem value="line-b">B 线 · 锻压</NvSelectItem>
                <NvSelectItem value="line-c">C 线 · 总装</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </div>
          <div class="grid gap-2">
            <Label>计划开工日期</Label>
            <NvDatePicker v-model="planDate" placeholder="选择日期" />
          </div>
          <div class="grid gap-2">
            <Label>计划开工时间</Label>
            <NvTimePicker v-model="planTime" :minute-step="5" placeholder="选择时间" />
          </div>
          <label class="flex items-center gap-2.5 text-sm">
            <NvCheckbox v-model="agreed" />
            派发后立即锁定物料并生成领料单
          </label>
          <div class="flex items-center justify-between">
            <span class="text-sm">加急插单</span>
            <NvSwitch v-model="switchOn" />
          </div>
          <div class="grid gap-2">
            <span class="text-sm font-medium">质检策略</span>
            <NvRadioGroup v-model="radioValue">
              <NvRadioGroupItem value="std">标准全检</NvRadioGroupItem>
              <NvRadioGroupItem value="sample">抽样检验</NvRadioGroupItem>
              <NvRadioGroupItem value="exempt">免检放行</NvRadioGroupItem>
            </NvRadioGroup>
          </div>
        </div>
      </NvCard>
    </div>

    <!-- 标签页 + 加载态 -->
    <div class="mt-4 grid gap-4 lg:grid-cols-2">
      <NvCard class="p-6">
        <h3 class="mb-4 text-sm font-semibold">标签页</h3>
        <NvTabs default-value="overview">
          <NvTabsList>
            <NvTabsTrigger value="overview">概览</NvTabsTrigger>
            <NvTabsTrigger value="quality">质量</NvTabsTrigger>
            <NvTabsTrigger value="maint">维护</NvTabsTrigger>
          </NvTabsList>
          <NvTabsContent value="overview" class="pt-4">
            <p class="text-sm text-muted-foreground">当班共 12 条产线运行，2 条待料。</p>
            <div class="mt-4 space-y-2">
              <div class="flex items-center justify-between text-sm">
                <span>计划达成</span><span class="tabular-nums text-muted-foreground">68%</span>
              </div>
              <Progress :model-value="68" />
            </div>
          </NvTabsContent>
          <NvTabsContent value="quality" class="pt-4">
            <p class="text-sm text-muted-foreground">在线判定良率 99.2%，无重大不合格。</p>
          </NvTabsContent>
          <NvTabsContent value="maint" class="pt-4">
            <p class="text-sm text-muted-foreground">CNC-07 计划保养窗口：今晚 22:00。</p>
          </NvTabsContent>
        </NvTabs>
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">加载态</h3>
        <p class="mt-1 text-sm text-muted-foreground">
          四种克制形态，品牌着色，减弱动效下降级为静态。
        </p>
        <div class="mt-5 flex flex-wrap gap-8">
          <div v-for="l in loaderVariants" :key="l.v" class="flex flex-col items-center gap-2">
            <NvLoader :variant="l.v" size="lg" />
            <span class="font-mono text-xs text-muted-foreground">{{ l.name }}</span>
          </div>
        </div>
        <div class="mt-5 flex items-center gap-3">
          <Skeleton class="size-10 rounded-full" />
          <div class="flex-1 space-y-2">
            <Skeleton class="h-3 w-2/5" />
            <Skeleton class="h-3 w-3/5" />
          </div>
        </div>
      </NvCard>
    </div>

    <!-- 反馈：Message / Notification / Tooltip / ⌘K -->
    <div class="mt-4 grid gap-4 lg:grid-cols-2">
      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">轻提示 Message</h3>
        <p class="mt-1 text-sm text-muted-foreground">
          顶部居中、单行、短暂自动消失。超长文本省略，hover 看全文。对标传统组件库的 message。
        </p>
        <div class="mt-4 flex flex-wrap gap-2">
          <NvButton size="sm" variant="outline" @click="fireMessage('success')">成功</NvButton>
          <NvButton size="sm" variant="outline" @click="fireMessage('info')">信息</NvButton>
          <NvButton size="sm" variant="outline" @click="fireMessage('warning')">预警</NvButton>
          <NvButton size="sm" variant="outline" @click="fireMessage('error')">错误</NvButton>
          <NvButton size="sm" variant="outline" @click="fireLongMessage">长文本</NvButton>
          <NvButton size="sm" variant="brand" @click="fireBurst">连发 3 条</NvButton>
        </div>
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">通知 Notification</h3>
        <p class="mt-1 text-sm text-muted-foreground">
          右上角卡片，含标题、描述与关闭。对标 notification。
        </p>
        <div class="mt-4 flex flex-wrap gap-2">
          <NvButton size="sm" variant="outline" @click="fireNotification('success')">成功</NvButton>
          <NvButton size="sm" variant="outline" @click="fireNotification('info')">信息</NvButton>
          <NvButton size="sm" variant="outline" @click="fireNotification('warning')">预警</NvButton>
          <NvButton size="sm" variant="outline" @click="fireNotification('error')">错误</NvButton>
        </div>
        <div class="mt-4 flex items-center gap-3">
          <NvTooltip>
            <NvTooltipTrigger as-child>
              <NvButton variant="ghost" size="sm">
                <template #leading><ActivityIcon aria-hidden="true" /></template>
                悬停提示
              </NvButton>
            </NvTooltipTrigger>
            <NvTooltipContent>实时采集自 MES 网关</NvTooltipContent>
          </NvTooltip>
          <NvButton variant="ghost" size="sm" @click="emit('openCommand')">
            <template #leading><SearchIcon aria-hidden="true" /></template>
            命令面板 ⌘K
          </NvButton>
          <NvDialog v-model:open="dialogOpen">
            <NvDialogTrigger as-child>
              <NvButton variant="ghost" size="sm">
                <template #leading><GaugeIcon aria-hidden="true" /></template>
                打开对话框
              </NvButton>
            </NvDialogTrigger>
            <NvDialogContent>
              <NvDialogHeader>
                <NvDialogTitle>确认派发工单</NvDialogTitle>
                <NvDialogDescription>
                  WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
                </NvDialogDescription>
              </NvDialogHeader>
              <div class="rounded-lg border border-border bg-muted/40 p-4 text-sm">
                <div class="flex justify-between">
                  <span class="text-muted-foreground">数量</span
                  ><span class="tabular-nums">480</span>
                </div>
                <div class="mt-2 flex justify-between">
                  <span class="text-muted-foreground">预计工时</span
                  ><span class="tabular-nums">14.5 h</span>
                </div>
              </div>
              <NvDialogFooter>
                <NvDialogClose as-child><NvButton variant="ghost">取消</NvButton></NvDialogClose>
                <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
              </NvDialogFooter>
            </NvDialogContent>
          </NvDialog>
        </div>
      </NvCard>
    </div>
  </section>
</template>

<style scoped src="./showcase-reveal.css"></style>
