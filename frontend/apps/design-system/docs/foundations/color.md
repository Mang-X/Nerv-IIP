# 色彩与动态色

## 动态色（运行时品牌色）

品牌强调色不是写死的，而是**运行时可切换的动态色**，写入 `<html>` 的 `--nv-brand`。右上角的取色器即可切换；选择持久化在 `localStorage`。

12 个预设是一个**等亮度的 12 色相轮**（OKLCH），任意一个选出来都读作"经过校准的品牌色"，而不是随机色：

<div class="flex flex-wrap gap-2 my-6">
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.54 0.16 256)" title="blue"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.52 0.17 278)" title="indigo"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.55 0.17 300)" title="violet"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.56 0.19 340)" title="magenta"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.58 0.18 18)" title="rose"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.57 0.19 28)" title="red"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.66 0.16 52)" title="orange"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.72 0.13 68)" title="amber"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.7 0.15 130)" title="lime"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.6 0.12 160)" title="green"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.6 0.11 200)" title="teal"></span>
  <span class="size-9 rounded-full border border-border" style="background:oklch(0.65 0.12 224)" title="cyan"></span>
</div>

默认 `blue` = `oklch(0.54 0.16 256)`。

## 亮 / 暗

视觉基准是**深色**。亮色完整支持，通过 `<html>.dark` 类切换；所有语义令牌都有亮暗两组取值，组件无需关心当前模式。本文档站右上角的外观开关直接驱动它。

## 用色原则

- 大面积用中性表面（`--background` / `--card` / `--muted`），品牌色只用于**强调与可点击**。
- 状态色（成功 / 警告 / 危险）语义固定，不参与动态色切换。
- 对比度遵循可读性基线；强调文字用 `--*-strong` 变体保证对比。
