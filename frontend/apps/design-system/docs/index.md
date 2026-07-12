---
layout: home

hero:
  name: Nerv-IIP 设计系统
  text: 数字工厂控制平面的统一设计语言
  tagline: 一套语义令牌与组件库，覆盖桌面 PC、移动 PDA、车间一体机、挂墙大屏四个表面。黑主题 · 动态色 · 亮暗自适应。
  actions:
    - theme: brand
      text: 阅读设计哲学
      link: /foundations/philosophy
    - theme: alt
      text: 浏览组件
      link: /components/overview
    - theme: alt
      text: 设计令牌
      link: /foundations/tokens
---

<script setup>
import {
  MonitorIcon, SmartphoneIcon, TabletIcon, PresentationIcon, ArrowRightIcon,
  PaletteIcon, GaugeIcon, LayersIcon, AccessibilityIcon,
} from 'lucide-vue-next'
</script>

<div class="nv-home">

<section class="nv-home-section">
  <p class="nv-home-eyebrow">四个表面</p>
  <h2 class="nv-home-h2">一套语言，四种原生形态</h2>
  <div class="nv-home-surfaces">
    <a class="nv-home-surface" href="/components/desktop/">
      <span class="nv-home-ic"><MonitorIcon /></span>
      <span class="nv-home-name">桌面 PC</span>
      <span class="nv-home-desc">语义令牌 · 12 色动态板 · Pro 组件 · DataTable · 描述 / 时间线 · 图表。紧凑 36–40px。</span>
      <span class="nv-home-go">桌面组件 <ArrowRightIcon /></span>
    </a>
    <a class="nv-home-surface" href="/components/mobile/">
      <span class="nv-home-ic"><SmartphoneIcon /></span>
      <span class="nv-home-name">移动 PDA</span>
      <span class="nv-home-desc">原生质感控件 · 手势(侧滑 / 下拉刷新 / 抽屉) · 宫格 / 悬浮按钮 · 玻璃覆盖层。40–48px。</span>
      <span class="nv-home-go">移动控件 <ArrowRightIcon /></span>
    </a>
    <a class="nv-home-surface" href="/components/touch/">
      <span class="nv-home-ic"><TabletIcon /></span>
      <span class="nv-home-name">一体机 / 工位</span>
      <span class="nv-home-desc">大触控 · 减少操作路径 · 工单大卡 · 报工步进 · 节拍图。56–72px。</span>
      <span class="nv-home-go">一体机组件 <ArrowRightIcon /></span>
    </a>
    <a class="nv-home-surface" href="/components/screen/">
      <span class="nv-home-ic"><PresentationIcon /></span>
      <span class="nv-home-name">大屏 / 控制室</span>
      <span class="nv-home-desc">独立 --nv-scr-* 工业蓝令牌 · OEE 核心指标 · 节拍甘特 · 告警表 · 舞台缩放。远读。</span>
      <span class="nv-home-go">大屏组件 <ArrowRightIcon /></span>
    </a>
  </div>
</section>

<section class="nv-home-section">
  <p class="nv-home-eyebrow">为什么是它</p>
  <h2 class="nv-home-h2">为生产动作而设计</h2>
  <div class="nv-home-features">
    <div class="nv-home-feat">
      <span class="nv-home-fic"><PaletteIcon /></span>
      <span class="nv-home-fname">动态品牌色</span>
      <span class="nv-home-fdesc">OKLCH 语义令牌，运行时切换 12 色等亮度品牌色，亮暗自适应。</span>
    </div>
    <div class="nv-home-feat">
      <span class="nv-home-fic"><GaugeIcon /></span>
      <span class="nv-home-fname">克制的工艺感</span>
      <span class="nv-home-fdesc">统一缓动令牌；拖拽跟手 + 橡皮筋回弹；覆盖层克制玻璃质感。</span>
    </div>
    <div class="nv-home-feat">
      <span class="nv-home-fic"><LayersIcon /></span>
      <span class="nv-home-fname">原版零改动</span>
      <span class="nv-home-fdesc">上游原语仅作内部基座，定制复制重建为 Pro / Mobile 层 + 令牌。</span>
    </div>
    <div class="nv-home-feat">
      <span class="nv-home-fic"><AccessibilityIcon /></span>
      <span class="nv-home-fname">可读可达</span>
      <span class="nv-home-fdesc">WCAG AA 对比、reduced-motion 降级、≥44px 触控目标。</span>
    </div>
  </div>
</section>

</div>

<style scoped>
.nv-home {
  max-width: 1152px;
  margin: 0 auto;
  padding: 1rem 1.5rem 4rem;
}
.nv-home-section { margin-top: 4rem; }
.nv-home-eyebrow {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--nv-brand-strong);
}
.nv-home-h2 {
  margin: 0.375rem 0 1.75rem;
  font-size: 1.6rem;
  font-weight: 600;
  letter-spacing: -0.02em;
  color: var(--foreground);
}
.nv-home-surfaces, .nv-home-features {
  display: grid;
  gap: 1rem;
}
@media (min-width: 768px) {
  .nv-home-surfaces { grid-template-columns: repeat(2, 1fr); }
  .nv-home-features { grid-template-columns: repeat(2, 1fr); }
}
@media (min-width: 1100px) {
  .nv-home-surfaces { grid-template-columns: repeat(4, 1fr); }
  .nv-home-features { grid-template-columns: repeat(4, 1fr); }
}
.nv-home-surface, .nv-home-feat {
  display: flex;
  flex-direction: column;
  padding: 1.4rem;
  border-radius: 14px;
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--foreground) !important;
  text-decoration: none !important;
  transition:
    border-color 0.2s var(--nv-ease-out-quart, ease-out),
    transform 0.2s var(--nv-ease-out-quart, ease-out),
    box-shadow 0.2s var(--nv-ease-out-quart, ease-out);
}
.nv-home-surface:hover {
  /* oklab (not oklch): oklch hue-interpolates the blue brand through purple when
     mixed with a neutral; oklab keeps the hue and just desaturates. */
  border-color: color-mix(in oklab, var(--nv-brand) 45%, var(--border));
  transform: translateY(-3px);
  box-shadow: 0 12px 32px -16px color-mix(in oklab, var(--nv-brand) 55%, black 40%);
}
.nv-home-ic, .nv-home-fic {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 11px;
  background: color-mix(in oklch, var(--nv-brand) 12%, transparent);
  color: var(--nv-brand-strong);
}
.nv-home-ic :deep(svg), .nv-home-fic :deep(svg) { width: 1.35rem; height: 1.35rem; }
.nv-home-name, .nv-home-fname {
  margin-top: 0.875rem;
  font-size: 1rem;
  font-weight: 600;
}
.nv-home-desc, .nv-home-fdesc {
  margin-top: 0.5rem;
  font-size: 0.875rem;
  line-height: 1.6;
  color: var(--muted-foreground);
}
.nv-home-go {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--nv-brand-strong);
}
.nv-home-go :deep(svg) {
  width: 0.9rem; height: 0.9rem;
  transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
}
.nv-home-surface:hover .nv-home-go :deep(svg) { transform: translateX(3px); }
@media (prefers-reduced-motion: reduce) {
  .nv-home-surface, .nv-home-surface:hover { transform: none; transition: none; }
}
</style>
