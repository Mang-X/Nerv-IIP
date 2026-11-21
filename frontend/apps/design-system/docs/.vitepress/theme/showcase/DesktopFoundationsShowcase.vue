<script setup lang="ts">
import { useThemeAccent } from '@nerv-iip/ui'
import { vReveal } from './showcaseReveal'

const { accent, setAccent, presets } = useThemeAccent()
const accentEntries = Object.entries(presets)
const accentNames: Record<string, string> = {
  blue: '锐蓝',
  violet: '品紫',
  rose: '绛红',
  amber: '琥珀',
  green: '翠绿',
  teal: '青碧',
}

// ---- Foundations data ----
const tokenGroups = [
  {
    title: '表面与文字',
    desc: '画布低于卡片一档，形成内嵌悬浮的层次。',
    items: [
      { name: '--background', cls: 'bg-background', ring: true, role: '页面画布' },
      { name: '--card', cls: 'bg-card', ring: true, role: '卡片 / 面板' },
      { name: '--muted', cls: 'bg-muted', ring: true, role: '次级表面' },
      { name: '--foreground', cls: 'bg-foreground', role: '正文文字' },
      { name: '--muted-foreground', cls: 'bg-muted-foreground', role: '次要文字' },
      { name: '--border', cls: 'bg-border', ring: true, role: '描边' },
    ],
  },
  {
    title: '语义状态',
    desc: '状态只走语义令牌，去饱和处理，专业不刺眼。',
    items: [
      { name: '--primary', cls: 'bg-primary', role: '主操作' },
      { name: '--nv-brand', cls: 'bg-brand', role: '动态强调' },
      { name: '--nv-success', cls: 'bg-success', role: '健康 / 启用' },
      { name: '--nv-warning', cls: 'bg-warning', role: '预警' },
      { name: '--destructive', cls: 'bg-destructive', role: '危险 / 错误' },
      { name: '--ring', cls: 'bg-ring', role: '聚焦环' },
    ],
  },
]

const radiusScale = [
  { name: 'sm', cls: 'rounded-sm', token: '--radius-sm' },
  { name: 'md', cls: 'rounded-md', token: '--radius-md' },
  { name: 'lg', cls: 'rounded-lg', token: '--radius-lg' },
  { name: 'xl', cls: 'rounded-xl', token: '--radius-xl' },
  { name: 'full', cls: 'rounded-full', token: '9999px' },
]
const shadowScale = [
  { name: 'xs', cls: 'shadow-xs' },
  { name: 'sm', cls: 'shadow-sm' },
  { name: 'md', cls: 'shadow-md' },
  { name: 'lg', cls: 'shadow-lg' },
]
const typeScale = [
  { label: '页面标题', cls: 'text-2xl font-semibold tracking-tight', sample: '数字工厂控制平面' },
  { label: '区块标题', cls: 'text-lg font-semibold', sample: '在制工单总览' },
  { label: '正文', cls: 'text-sm', sample: '默认表格与表单内容，密度优先于装饰。' },
  {
    label: '辅助 / 弱化',
    cls: 'text-sm text-muted-foreground',
    sample: '编号、时间戳、提示语走弱化色。',
  },
  {
    label: '等宽',
    cls: 'font-mono text-xs text-muted-foreground',
    sample: 'WO-2406-0413 · 8f3a-21c0',
  },
]

// ---- Foundations: border + spacing ----
const borderScale = [
  { name: 'hairline', cls: 'border', label: '1px · 默认描边' },
  { name: 'medium', cls: 'border-2', label: '2px · 强调 / 选中' },
  { name: 'focus', cls: 'border-2 border-brand', label: '品牌 · 聚焦' },
]
const spacingScale = [
  { step: '1', px: '4px' },
  { step: '2', px: '8px' },
  { step: '3', px: '12px' },
  { step: '4', px: '16px' },
  { step: '6', px: '24px' },
  { step: '8', px: '32px' },
]
</script>

<template>
  <section id="foundations" v-reveal class="scroll-mt-20">
    <div class="nv-eyebrow">
      <h2 class="text-xl font-semibold tracking-tight">设计基础</h2>
      <p class="mt-1.5 text-sm text-muted-foreground">
        颜色、品牌色板、圆角、阴影、排印 —— 全部来自单一 OKLCH 令牌源。
      </p>
    </div>

    <!-- 语义令牌 -->
    <div class="mt-6 grid gap-6 lg:grid-cols-2">
      <div
        v-for="group in tokenGroups"
        :key="group.title"
        class="rounded-xl border border-border bg-card p-5 shadow-xs"
      >
        <h3 class="text-sm font-semibold">{{ group.title }}</h3>
        <p class="mt-1 text-sm text-muted-foreground">{{ group.desc }}</p>
        <ul class="mt-4 grid grid-cols-3 gap-3">
          <li v-for="t in group.items" :key="t.name" class="min-w-0">
            <div
              class="h-12 w-full rounded-lg"
              :class="[t.cls, t.ring ? 'ring-1 ring-inset ring-border' : '']"
            />
            <p class="mt-2 truncate font-mono text-xs text-foreground">{{ t.name }}</p>
            <p class="truncate text-xs text-muted-foreground">{{ t.role }}</p>
          </li>
        </ul>
      </div>
    </div>

    <div class="mt-6 rounded-xl border border-border bg-card p-5 shadow-xs">
      <div class="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h3 class="text-sm font-semibold">动态品牌色板</h3>
          <p class="mt-1 text-sm text-muted-foreground">
            点击任意色号，整页
            <code class="font-mono text-xs">--nv-brand</code> 即时切换并持久化。
          </p>
        </div>
        <span class="text-xs text-muted-foreground">{{ accentEntries.length }} 色</span>
      </div>
      <div class="mt-4 grid grid-cols-3 gap-3 sm:grid-cols-6">
        <button
          v-for="[name, value] in accentEntries"
          :key="name"
          type="button"
          class="group/sw flex flex-col items-center gap-1.5 outline-none"
          :aria-label="`品牌色 ${accentNames[name] ?? name}`"
          :aria-pressed="accent === value"
          @click="setAccent(value)"
        >
          <span
            class="size-9 rounded-lg ring-offset-2 ring-offset-card transition-transform duration-150 ease-out-quart group-hover/sw:scale-110 group-focus-visible/sw:ring-2 group-focus-visible/sw:ring-ring"
            :class="accent === value ? 'ring-2 ring-foreground' : 'ring-1 ring-border'"
            :style="{ backgroundColor: value }"
          />
          <span class="text-[11px] text-muted-foreground">{{ accentNames[name] ?? name }}</span>
        </button>
      </div>
    </div>

    <!-- 圆角 + 阴影 -->
    <div class="mt-6 grid gap-6 lg:grid-cols-2">
      <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
        <h3 class="text-sm font-semibold">圆角</h3>
        <div class="mt-4 flex flex-wrap items-end gap-5">
          <div v-for="r in radiusScale" :key="r.name" class="flex flex-col items-center gap-2">
            <div class="size-14 border border-border bg-muted" :class="r.cls" />
            <span class="font-mono text-xs text-muted-foreground">{{ r.name }}</span>
          </div>
        </div>
      </div>
      <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
        <h3 class="text-sm font-semibold">阴影 · 高度</h3>
        <div class="mt-4 flex flex-wrap items-center gap-6 px-1 py-2">
          <div v-for="s in shadowScale" :key="s.name" class="flex flex-col items-center gap-2">
            <div class="size-14 rounded-lg bg-card" :class="s.cls" />
            <span class="font-mono text-xs text-muted-foreground">{{ s.name }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 边框 + 间距 -->
    <div class="mt-6 grid gap-6 lg:grid-cols-2">
      <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
        <h3 class="text-sm font-semibold">边框</h3>
        <div class="mt-4 flex flex-wrap items-end gap-5">
          <div v-for="b in borderScale" :key="b.name" class="flex flex-col items-center gap-2">
            <div class="size-14 rounded-lg border-border bg-muted" :class="b.cls" />
            <span class="font-mono text-xs text-muted-foreground">{{ b.name }}</span>
          </div>
        </div>
        <p class="mt-3 text-xs text-muted-foreground">
          发丝级 1px 描边为默认；2px 仅用于强调与聚焦。
        </p>
      </div>
      <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
        <h3 class="text-sm font-semibold">间距 · 4px 基准</h3>
        <ul class="mt-4 space-y-2">
          <li v-for="s in spacingScale" :key="s.step" class="flex items-center gap-3">
            <span class="w-8 font-mono text-xs text-muted-foreground">{{ s.step }}</span>
            <span class="h-3 rounded-sm bg-brand/70" :style="{ width: s.px }" />
            <span class="font-mono text-xs text-muted-foreground">{{ s.px }}</span>
          </li>
        </ul>
      </div>
    </div>

    <!-- 排印 -->
    <div
      class="mt-6 divide-y divide-border overflow-hidden rounded-xl border border-border bg-card"
    >
      <div
        v-for="row in typeScale"
        :key="row.label"
        class="flex flex-col gap-1 px-5 py-4 sm:flex-row sm:items-baseline sm:gap-6"
      >
        <span class="w-28 shrink-0 text-xs text-muted-foreground">{{ row.label }}</span>
        <span :class="row.cls">{{ row.sample }}</span>
      </div>
    </div>
  </section>
</template>

<style scoped src="./showcase-reveal.css"></style>
