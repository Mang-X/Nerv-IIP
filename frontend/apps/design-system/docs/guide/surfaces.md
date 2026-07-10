---
title: 三个表面
---

<script setup>
import { MonitorIcon, SmartphoneIcon, LayoutDashboardIcon, ArrowRightIcon } from 'lucide-vue-next'
</script>

# 三个表面

同一套令牌与设计语言，三种原生形态。

<div class="ds-surfaces">
  <a class="ds-surface" href="/components/desktop/">
    <span class="ds-surface-icon"><MonitorIcon /></span>
    <span class="ds-surface-name">桌面 PC</span>
    <span class="ds-surface-desc">控制台主表面：信息密度高、键鼠为主。语义令牌 + 12 色动态板，Pro 组件（DataTable、描述列表、时间线、图表）。克制动效，专业感优先。</span>
    <span class="ds-surface-go">桌面组件 <ArrowRightIcon /></span>
  </a>
  <a class="ds-surface" href="/components/mobile/">
    <span class="ds-surface-icon"><SmartphoneIcon /></span>
    <span class="ds-surface-name">移动 PDA</span>
    <span class="ds-surface-desc">车间手持终端：触控为主、单手操作、强光环境。原生质感控件 + 完整手势（侧滑 / 下拉刷新 / 抽屉），统一橡皮筋阻尼。安全区适配，≥44px 触控目标。</span>
    <span class="ds-surface-go">移动控件 <ArrowRightIcon /></span>
  </a>
  <a class="ds-surface" href="/components/board">
    <span class="ds-surface-icon"><LayoutDashboardIcon /></span>
    <span class="ds-surface-name">一体机看板</span>
    <span class="ds-surface-desc">工位大屏：远距离可读、触控操作、减少路径。工单大卡、报工步进、节拍图，为"少点几下完成报工"而设计。</span>
    <span class="ds-surface-go">看板组件 <ArrowRightIcon /></span>
  </a>
</div>

## 一致性原则

- **令牌共享**：颜色、间距、圆角、动效缓动全部来自同一套 `theme.css`。
- **手感原生**：每个表面遵循其平台的交互基线，不强行统一手势或尺寸。
- **组件分层**：桌面 / 一体机用 `@nerv-iip/ui`，移动用 `@nerv-iip/ui-mobile`，但视觉血统一致。

<style scoped>
.ds-surfaces {
  display: grid;
  gap: 1rem;
  margin: 1.5rem 0;
}
@media (min-width: 768px) {
  .ds-surfaces { grid-template-columns: repeat(3, 1fr); }
}
.ds-surface {
  display: flex;
  flex-direction: column;
  padding: 1.25rem;
  border-radius: 14px;
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--foreground) !important;
  text-decoration: none !important;
  font-weight: inherit;
  transition:
    border-color 0.2s var(--nv-ease-out-quart, ease-out),
    transform 0.2s var(--nv-ease-out-quart, ease-out),
    box-shadow 0.2s var(--nv-ease-out-quart, ease-out);
}
.ds-surface:hover {
  border-color: color-mix(in oklch, var(--nv-brand) 45%, var(--border));
  transform: translateY(-2px);
  box-shadow: 0 10px 30px -14px color-mix(in oklch, var(--nv-brand) 50%, black 40%);
}
.ds-surface-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 11px;
  background: color-mix(in oklch, var(--nv-brand) 12%, transparent);
  color: var(--nv-brand-strong);
}
.ds-surface-icon :deep(svg) { width: 1.35rem; height: 1.35rem; }
.ds-surface-name { margin-top: 0.875rem; font-size: 1rem; font-weight: 600; }
.ds-surface-desc {
  margin-top: 0.5rem;
  font-size: 0.875rem;
  line-height: 1.6;
  color: var(--muted-foreground);
}
.ds-surface-go {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--nv-brand-strong);
}
.ds-surface-go :deep(svg) {
  width: 0.9rem;
  height: 0.9rem;
  transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
}
.ds-surface:hover .ds-surface-go :deep(svg) { transform: translateX(2px); }
@media (prefers-reduced-motion: reduce) {
  .ds-surface, .ds-surface:hover { transform: none; transition: none; }
}
</style>
