# 设计令牌

所有视觉常量都收敛为**语义令牌**，唯一真源是 `@nerv-iip/ui` 的 `src/styles/theme.css`（Tailwind v4 `@theme inline` + OKLCH）。组件只引用语义名（如 `--brand`、`--muted-foreground`），不写死颜色——这样动态色、亮暗切换都能一处生效。

## 语义颜色（实时取色，跟随当前主题）

<div class="grid grid-cols-2 sm:grid-cols-4 gap-3 my-6">
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--background)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--background</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--card)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--card</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--muted)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--muted</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--brand)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--brand</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--foreground)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--foreground</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--muted-foreground)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--muted-foreground</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--border)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--border</div>
  </div>
  <div class="rounded-lg border border-border overflow-hidden">
    <div class="h-14" style="background:var(--destructive)"></div>
    <div class="px-2 py-1.5 text-xs text-muted-foreground bg-card">--destructive</div>
  </div>
</div>

> 切换右上角的外观（亮/暗）与动态色，上面的色块会实时跟随——证明组件只依赖语义令牌。

## 令牌族

| 族 | 示例 | 说明 |
|---|---|---|
| 表面 | `--background` `--card` `--popover` `--muted` | 从底到浮层的层级 |
| 文字 | `--foreground` `--muted-foreground` `--*-strong` | 含高对比强调变体 |
| 品牌 | `--brand` `--brand-strong` `--brand-foreground` | 运行时动态色 |
| 语义 | `--destructive` `--border` `--ring` `--accent` | 状态与边线 |
| 动效 | `--ease-out-quart / expo / in-out-quart` | 统一缓动，见[动效](/foundations/motion) |

## 为什么用 OKLCH

OKLCH 在感知上更均匀——同一亮度下切换色相（动态色 12 板）能保持一致的"品牌强度"，不会某些颜色突然显得过亮或过脏。
