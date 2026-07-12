---
title: 四个表面
---

<script setup>
import { MonitorIcon, SmartphoneIcon, TabletIcon, PresentationIcon, ArrowRightIcon } from 'lucide-vue-next'
</script>

# 四个表面

同一套令牌与设计语言，四种原生形态。表面决定组件目录、token 命名空间与触控尺寸（详见 [设计令牌](/foundations/tokens) 的四场景矩阵）；一件组件跨两个表面时**拆两件分别实现**，绝不"一件双态"（ADR 0020 §1.2）。

<div class="nv-surfaces">
  <a class="nv-surface" href="/components/desktop/">
    <span class="nv-surface-icon"><MonitorIcon /></span>
    <span class="nv-surface-name">桌面 PC</span>
    <span class="nv-surface-desc">控制台主表面：信息密度高、键鼠为主。紧凑 36–40px 触控目标，语义令牌 + 12 色动态板，Pro 组件（NvDataTable、描述列表、时间线、图表）。克制动效，专业感优先。</span>
    <span class="nv-surface-tag"><code>@nerv-iip/ui</code> · <code>--nv-*</code> · 36–40px</span>
    <span class="nv-surface-go">桌面组件 <ArrowRightIcon /></span>
  </a>
  <a class="nv-surface" href="/components/mobile/">
    <span class="nv-surface-icon"><SmartphoneIcon /></span>
    <span class="nv-surface-name">移动 PDA</span>
    <span class="nv-surface-desc">车间手持终端：触控为主、单手操作、强光环境。原生质感控件 + 完整手势（侧滑 / 下拉刷新 / 抽屉），统一橡皮筋阻尼。安全区适配，≥44px 触控目标。</span>
    <span class="nv-surface-tag"><code>@nerv-iip/ui-mobile</code> · <code>--nv-m-*</code> · 40–48px</span>
    <span class="nv-surface-go">移动控件 <ArrowRightIcon /></span>
  </a>
  <a class="nv-surface" href="/components/touch/">
    <span class="nv-surface-icon"><TabletIcon /></span>
    <span class="nv-surface-name">一体机 / 工位</span>
    <span class="nv-surface-desc">工位一体机：近距离大触控、减少路径。56–72px 大触控目标，工单大卡、报工步进、节拍图，为"少点几下完成报工"而设计。大量复用 PC 件（放大字号间距）。</span>
    <span class="nv-surface-tag"><code>@nerv-iip/ui · touch/</code> · <code>--nv-t-*</code> · 56–72px</span>
    <span class="nv-surface-go">一体机组件 <ArrowRightIcon /></span>
  </a>
  <a class="nv-surface" href="/components/screen/">
    <span class="nv-surface-icon"><PresentationIcon /></span>
    <span class="nv-surface-name">大屏 / 控制室</span>
    <span class="nv-surface-desc">挂墙大屏：远距离可读、几乎不触控。独立的近黑工业蓝令牌层（<code>--nv-scr-*</code>），发光克制、白色发丝线撑结构，舞台缩放适配任意分辨率。</span>
    <span class="nv-surface-tag"><code>@nerv-iip/ui · screen/</code> · <code>--nv-scr-*</code> · 远读</span>
    <span class="nv-surface-go">大屏组件 <ArrowRightIcon /></span>
  </a>
</div>

## 一致性原则

- **令牌共享，命名隔离**：PC/mobile/touch 共享 `--nv-*` 语义令牌；大屏用独立的 `--nv-scr-*` 工业蓝层。允许跨场景取值相同，但名称按命名空间隔离（ADR 0020 §3）。
- **手感原生**：每个表面遵循其平台的交互基线与触控尺寸，不强行统一手势或尺寸。
- **组件分层**：桌面 / 一体机 / 大屏用 `@nerv-iip/ui`（分属 `pro/blocks/layout` · `touch/` · `screen/`），移动用 `@nerv-iip/ui-mobile`，但视觉血统一致。

<style scoped>
.nv-surfaces {
  display: grid;
  gap: 1rem;
  margin: 1.5rem 0;
}
@media (min-width: 640px) {
  .nv-surfaces { grid-template-columns: repeat(2, 1fr); }
}
@media (min-width: 1100px) {
  .nv-surfaces { grid-template-columns: repeat(4, 1fr); }
}
.nv-surface {
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
.nv-surface:hover {
  /* oklab (not oklch): mixing the blue brand with a neutral in oklch drifts the
     hue through purple; oklab desaturates without shifting the hue. */
  border-color: color-mix(in oklab, var(--nv-brand) 45%, var(--border));
  transform: translateY(-2px);
  box-shadow: 0 10px 30px -14px color-mix(in oklab, var(--nv-brand) 50%, black 40%);
}
.nv-surface-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 11px;
  background: color-mix(in oklch, var(--nv-brand) 12%, transparent);
  color: var(--nv-brand-strong);
}
.nv-surface-icon :deep(svg) { width: 1.35rem; height: 1.35rem; }
.nv-surface-name { margin-top: 0.875rem; font-size: 1rem; font-weight: 600; }
.nv-surface-desc {
  margin-top: 0.5rem;
  font-size: 0.875rem;
  line-height: 1.6;
  color: var(--muted-foreground);
}
.nv-surface-tag {
  margin-top: 0.75rem;
  font-size: 0.75rem;
  line-height: 1.5;
  color: var(--muted-foreground);
}
.nv-surface-tag code {
  font-size: 0.7rem;
  padding: 0.05rem 0.3rem;
  border-radius: 5px;
  background: color-mix(in oklch, var(--muted) 60%, transparent);
}
.nv-surface-go {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: auto;
  padding-top: 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--nv-brand-strong);
}
.nv-surface-go :deep(svg) {
  width: 0.9rem;
  height: 0.9rem;
  transition: transform 0.2s var(--nv-ease-out-quart, ease-out);
}
.nv-surface:hover .nv-surface-go :deep(svg) { transform: translateX(2px); }
@media (prefers-reduced-motion: reduce) {
  .nv-surface, .nv-surface:hover { transform: none; transition: none; }
}
</style>
