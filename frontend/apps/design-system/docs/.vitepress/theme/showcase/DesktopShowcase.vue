<script setup lang="ts">
import type { CommandGroup, CommandItem } from '@nerv-iip/ui'
import {
  NvButton,
  NvCommand,
  nvMessage,
  NvNotifierHost,
  NvStatusDot,
  NvTooltipProvider,
  useThemeAccent,
} from '@nerv-iip/ui'
import {
  ActivityIcon,
  ArrowRightIcon,
  GaugeIcon,
  LayersIcon,
  PlusIcon,
  RocketIcon,
  SearchIcon,
} from '@lucide/vue'
import { ref } from 'vue'
import DesktopFoundationsShowcase from './DesktopFoundationsShowcase.vue'
import DesktopComponentsShowcase from './DesktopComponentsShowcase.vue'
import DesktopChartsShowcase from './DesktopChartsShowcase.vue'
import DesktopDetailShowcase from './DesktopDetailShowcase.vue'
import DesktopMotionShowcase from './DesktopMotionShowcase.vue'

const { presets } = useThemeAccent()
const accentEntries = Object.entries(presets)

// ---- ⌘K command palette ----
const cmdOpen = ref(false)
const cmdGroups: CommandGroup[] = [
  {
    label: '导航',
    items: [
      { id: 'instances', label: '实例总览', hint: 'G I', icon: LayersIcon },
      { id: 'orders', label: '工单工作台', hint: 'G O', icon: GaugeIcon },
      { id: 'iam', label: '用户与权限', icon: ActivityIcon, keywords: 'user role permission' },
    ],
  },
  {
    label: '快捷操作',
    items: [
      {
        id: 'new-wo',
        label: '新建工单',
        hint: '⌘N',
        icon: PlusIcon,
        keywords: 'create work order',
      },
      { id: 'dispatch', label: '派发到产线', icon: RocketIcon, keywords: 'dispatch line' },
      { id: 'search-material', label: '搜索物料编码', icon: SearchIcon, keywords: 'material' },
    ],
  },
]
function onCommandSelect(item: CommandItem) {
  nvMessage.success(`执行：${item.label}`)
}

const stats = [
  { k: '语义令牌', v: '32+' },
  { k: '彩色主题', v: `${accentEntries.length} 色` },
  { k: 'Pro 组件', v: '18' },
  { k: '图表类型', v: '4' },
]
</script>

<template>
  <NvTooltipProvider :delay-duration="200">
    <div class="ds min-h-screen bg-background text-foreground">
      <!-- 顶栏由文档站导航提供（原页面 header 已移除，命令面板触发改由 ⌘K 等） -->

      <!-- Hero -->
      <section class="relative overflow-hidden border-b border-border/70">
        <div class="nv-hero-glow" aria-hidden="true" />
        <div class="relative mx-auto max-w-6xl px-6 py-20">
          <p class="flex items-center gap-2 text-sm font-medium text-muted-foreground">
            <NvStatusDot tone="info" pulse /> 工业智能平台 · 控制平面组件库
          </p>
          <h1 class="mt-5 max-w-2xl text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
            克制、精确、可信赖的工厂界面语言
          </h1>
          <p class="mt-5 max-w-xl text-base text-muted-foreground text-pretty">
            近黑主色，去饱和语义色，亮暗双主题，12
            色动态品牌色运行时可切换。组件、图表、反馈与基础参数自成一套完整系统。
          </p>
          <div class="mt-8 flex flex-wrap items-center gap-3">
            <NvButton as="a" href="#components" variant="brand">
              浏览组件
              <template #trailing><ArrowRightIcon class="size-4" aria-hidden="true" /></template>
            </NvButton>
            <NvButton as="a" href="#foundations" variant="outline">设计基础</NvButton>
          </div>
          <dl
            class="mt-12 grid max-w-2xl grid-cols-2 gap-px overflow-hidden rounded-xl border border-border bg-border sm:grid-cols-4"
          >
            <div v-for="stat in stats" :key="stat.k" class="bg-card px-4 py-3.5">
              <dt class="text-sm text-muted-foreground">{{ stat.k }}</dt>
              <dd class="mt-0.5 text-lg font-semibold tabular-nums tracking-tight">{{ stat.v }}</dd>
            </div>
          </dl>
        </div>
      </section>

      <main class="mx-auto max-w-6xl space-y-20 px-6 py-20">
        <!-- ============ 基础 ============ -->
        <DesktopFoundationsShowcase />
        <DesktopComponentsShowcase @open-command="cmdOpen = true" />
        <DesktopChartsShowcase />
        <DesktopDetailShowcase />
        <DesktopMotionShowcase />
      </main>

      <footer class="border-t border-border/70">
        <div class="mx-auto flex max-w-6xl flex-col gap-1 px-6 py-10">
          <p class="text-sm font-medium">Nerv-IIP 设计系统 v2</p>
          <p class="text-sm text-muted-foreground">
            令牌单一来源于 <code class="font-mono text-xs">@nerv-iip/ui</code>，原版 shadcn
            零改动，定制走复制重建。
          </p>
        </div>
      </footer>

      <NvCommand v-model:open="cmdOpen" :groups="cmdGroups" @select="onCommandSelect" />
      <NvNotifierHost />
    </div>
  </NvTooltipProvider>
</template>

<style scoped>
.nv-hero-glow {
  position: absolute;
  inset: 0;
  background:
    radial-gradient(
      60% 70% at 15% 0%,
      color-mix(in oklch, var(--nv-brand) 14%, transparent),
      transparent 70%
    ),
    radial-gradient(
      50% 60% at 100% 10%,
      color-mix(in oklch, var(--nv-brand) 9%, transparent),
      transparent 65%
    );
  pointer-events: none;
}
</style>
