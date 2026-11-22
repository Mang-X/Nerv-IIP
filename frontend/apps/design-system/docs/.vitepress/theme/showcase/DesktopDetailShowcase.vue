<script setup lang="ts">
import type { DescriptionItem, TimelineItem } from '@nerv-iip/ui'
import {
  NvButton,
  NvCard,
  NvDescriptions,
  nvMessage,
  NvPopconfirm,
  NvStatusBadge,
  NvTimeline,
  NvTooltip,
} from '@nerv-iip/ui'
import { FilePlus2Icon, PackageCheckIcon, PlayIcon, TriangleAlertIcon } from '@lucide/vue'
import { vReveal } from './showcaseReveal'

// ---- 描述列表 Descriptions 演示（工单详情）----
const woDescItems: DescriptionItem[] = [
  { key: 'code', label: '工单号', value: 'WO-2406-0413' },
  { label: '产品', value: '前桥壳体 A2' },
  { label: '工作中心', value: 'WC-CNC-07' },
  { label: '负责人', value: '张伟' },
  { label: '计划数量', value: '480 件' },
  { label: '已完成', value: '312 件' },
  { key: 'status', label: '状态', value: 'running' },
  { label: '优先级', value: '高' },
  { label: '交期', value: '2026-06-22' },
  { label: '备注', value: '首件需三坐标全尺寸检测，合格后方可批量。', span: 3 },
]
const descClampItems: DescriptionItem[] = [
  { label: '首件检验工艺要求', value: '三坐标全尺寸' },
  { label: '关联物料批次号', value: 'MTL-7782-0034' },
  { label: '客户与交付地点', value: '一汽解放 · 长春' },
  { label: '首检报告存储路径', value: 'first-article-v3.pdf' },
]

// ---- 时间线 Timeline 演示（工序流转日志）----
const woTimeline: TimelineItem[] = [
  {
    key: 'created',
    title: '工单创建',
    label: '06-17 08:12',
    description: '由 MES 排产自动下发，物料已预占。',
    tone: 'neutral',
    icon: FilePlus2Icon,
  },
  {
    key: 'kitted',
    title: '物料齐套',
    label: '06-17 08:40',
    description: '5 项物料从 WH-A 出库并送达线边仓。',
    tone: 'success',
    icon: PackageCheckIcon,
  },
  {
    key: 'started',
    title: '开工',
    label: '06-17 09:05',
    description: '张伟在 WC-CNC-07 扫码开工，节拍 42s/件。',
    tone: 'brand',
    icon: PlayIcon,
  },
  {
    key: 'warn',
    title: '首检预警',
    label: '06-17 09:21',
    description: '孔径 Φ12.02 接近上公差，已通知工艺复核。',
    tone: 'warning',
    icon: TriangleAlertIcon,
  },
]
</script>

<template>
  <section id="detail" v-reveal class="scroll-mt-20">
    <div class="nv-eyebrow">
      <h2 class="text-xl font-semibold tracking-tight">详情展示</h2>
      <p class="mt-1.5 text-sm text-muted-foreground">
        键值详情（Descriptions）与工序流转（Timeline）—— 工单 / 设备详情页核心。
      </p>
    </div>

    <div class="mt-6 grid gap-4 lg:grid-cols-3">
      <NvCard class="p-6 lg:col-span-2">
        <h3 class="text-sm font-semibold">工单详情 · Descriptions</h3>
        <p class="mt-0.5 mb-4 text-xs text-muted-foreground">键值详情列表 · 3 列网格</p>
        <NvDescriptions :items="woDescItems" :columns="3">
          <template #code="{ item }">
            <span class="font-mono text-xs">{{ item.value }}</span>
          </template>
          <template #status="{ item }">
            <NvStatusBadge :value="String(item.value)" :pulse="item.value === 'running'" />
          </template>
        </NvDescriptions>
      </NvCard>

      <NvCard class="p-6">
        <h3 class="text-sm font-semibold">工序日志 · Timeline</h3>
        <p class="mt-0.5 mb-4 text-xs text-muted-foreground">工序流转 · 进行中脉冲</p>
        <NvTimeline :items="woTimeline" pending pending-text="精加工中…" />
      </NvCard>
    </div>

    <!-- 带边框变体 -->
    <div class="mt-4">
      <h3 class="mb-3 text-sm font-semibold">描述列表 · 带边框记录</h3>
      <NvDescriptions :items="woDescItems" :columns="3" bordered>
        <template #code="{ item }">
          <span class="font-mono text-xs">{{ item.value }}</span>
        </template>
        <template #status="{ item }">
          <NvStatusBadge :value="String(item.value)" :pulse="item.value === 'running'" />
        </template>
      </NvDescriptions>
    </div>

    <!-- 标题超长省略 + NvTooltip -->
    <div class="mt-4">
      <h3 class="mb-1 text-sm font-semibold">描述列表 · 标题超长省略</h3>
      <p class="mb-3 text-xs text-muted-foreground">
        <code class="font-mono">ellipsis</code> + <code class="font-mono">label-width</code>
        ：标题单行截断，悬停弹 NvTooltip 看全文（内容由你用插槽自定）。
      </p>
      <NvDescriptions :items="descClampItems" :columns="2" bordered ellipsis label-width="6rem" />
    </div>

    <!-- 气泡确认 Popconfirm -->
    <div class="mt-4">
      <h3 class="mb-3 text-sm font-semibold">气泡确认 · Popconfirm</h3>
      <NvCard class="flex flex-wrap items-center gap-3 p-5">
        <NvPopconfirm
          title="确认删除该工单？"
          description="删除后不可恢复。"
          confirm-text="删除"
          @confirm="nvMessage.error('工单已删除')"
        >
          <NvButton variant="outline" size="sm">删除工单</NvButton>
        </NvPopconfirm>
        <NvPopconfirm
          title="确认下发到产线？"
          confirm-text="下发"
          confirm-tone="brand"
          @confirm="nvMessage.success('已下发排产')"
        >
          <NvButton variant="brand" size="sm">下发排产</NvButton>
        </NvPopconfirm>
        <span class="text-xs text-muted-foreground">行内危险操作 / 主操作二次确认</span>
      </NvCard>
    </div>
  </section>
</template>

<style scoped src="./showcase-reveal.css"></style>
