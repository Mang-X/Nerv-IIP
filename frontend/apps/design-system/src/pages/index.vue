<script setup lang="ts">
import { ThemePicker, ThemeToggle } from '@nerv-iip/ui'
import { ArrowRightIcon, LayoutDashboardIcon, MonitorIcon, SmartphoneIcon } from 'lucide-vue-next'

definePage({ meta: { title: '总览' } })

const surfaces = [
  {
    to: '/design-system',
    tag: '桌面 PC',
    name: '桌面设计系统',
    desc: '语义令牌 · 12 色板 · Pro 组件 · 图表 · DataTablePro · 描述/时间线 · 动效',
    icon: MonitorIcon,
  },
  {
    to: '/board',
    tag: '一体机看板',
    name: '车间工位看板',
    desc: '大屏触控 · 减少操作路径 · 工单大卡 · 报工步进 · 节拍图',
    icon: LayoutDashboardIcon,
  },
  {
    to: '/mobile',
    tag: '移动 PDA',
    name: '移动控件画廊',
    desc: '原生质感控件 · 手势 · 宫格 / 悬浮按钮 / 居中提示 · 设备外观框',
    icon: SmartphoneIcon,
  },
]
</script>

<template>
  <div class="min-h-dvh bg-background text-foreground">
    <header
      class="sticky top-0 z-10 flex items-center gap-3 border-b border-border/70 bg-background/80 px-6 py-3.5 backdrop-blur-xl"
    >
      <div
        class="flex size-7 items-center justify-center rounded-md bg-brand text-brand-foreground"
      >
        <span class="text-sm font-bold">N</span>
      </div>
      <span class="text-sm font-semibold">Nerv-IIP 设计系统</span>
      <span class="text-xs text-muted-foreground">v2</span>
      <div class="ms-auto flex items-center gap-1.5">
        <ThemePicker />
        <ThemeToggle />
      </div>
    </header>

    <main class="mx-auto max-w-5xl px-6 py-16">
      <h1 class="text-3xl font-semibold tracking-tight text-balance sm:text-4xl">
        数字工厂控制平面 · 设计系统
      </h1>
      <p class="mt-3 max-w-2xl text-sm text-muted-foreground">
        统一令牌与组件库，覆盖桌面、车间一体机、移动 PDA 三个表面。选择一个表面进入演示。
      </p>

      <div class="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <RouterLink v-for="s in surfaces" :key="s.to" :to="s.to" class="ds-hub-card group">
          <div class="flex items-center gap-3">
            <span class="ds-hub-icon"
              ><component :is="s.icon" class="size-5" aria-hidden="true"
            /></span>
            <span class="ds-hub-tag">{{ s.tag }}</span>
          </div>
          <h2 class="mt-4 text-base font-semibold">{{ s.name }}</h2>
          <p class="mt-1.5 text-sm leading-relaxed text-muted-foreground">{{ s.desc }}</p>
          <span class="mt-5 inline-flex items-center gap-1 text-sm font-medium text-brand-strong">
            进入
            <ArrowRightIcon
              class="size-4 transition-transform duration-200 group-hover:translate-x-0.5"
              aria-hidden="true"
            />
          </span>
        </RouterLink>
      </div>
    </main>
  </div>
</template>

<style scoped>
.ds-hub-card {
  display: flex;
  flex-direction: column;
  padding: 1.25rem;
  border-radius: 14px;
  border: 1px solid var(--border);
  background-color: var(--card);
  box-shadow: 0 1px 2px 0 color-mix(in oklch, black 5%, transparent);
  transition:
    border-color 0.18s var(--ease-out-quart, ease-out),
    box-shadow 0.18s var(--ease-out-quart, ease-out),
    transform 0.18s var(--ease-out-quart, ease-out);
}
.ds-hub-card:hover {
  border-color: color-mix(in oklch, var(--brand) 45%, var(--border));
  box-shadow: 0 8px 28px -12px color-mix(in oklch, var(--brand) 50%, black 30%);
  transform: translateY(-2px);
}
.ds-hub-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.25rem;
  height: 2.25rem;
  border-radius: 10px;
  background-color: color-mix(in oklch, var(--brand) 12%, transparent);
  color: var(--brand-strong);
}
.ds-hub-tag {
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--muted-foreground);
  text-transform: uppercase;
}
@media (prefers-reduced-motion: reduce) {
  .ds-hub-card {
    transition: none;
  }
  .ds-hub-card:hover {
    transform: none;
  }
}
</style>
